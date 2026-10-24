using System;
using System.Collections.Generic;

namespace pk3DS.Core.Modding.Research;

public static class CodeBinExpander
{
    private const uint LoadBase = 0x00100000;
    private const uint PageSize = 0x1000;
    private const uint MediaUnit = 0x200;

    public enum Segment { Text, Rodata, Data }

    public sealed class ExpandResult
    {
        public bool Success;
        public string Error = "";
        public byte[] CodeBin = [];
        public byte[] ExHeader = [];
        public byte[] NcchHeader0 = [];
        public uint InsertionPointFileOffset;
        public int RelocatedPointers;

        public List<uint> UnconfirmedPointerCandidates = [];

        public List<uint> SkippedStringLikeCandidates = [];
    }

    private static uint ReadU32(byte[] d, int off) => BitConverter.ToUInt32(d, off);
    private static void WriteU32(byte[] d, int off, uint value) => BitConverter.GetBytes(value).CopyTo(d, off);
    private static void AddU32(byte[] d, int off, uint delta) => WriteU32(d, off, ReadU32(d, off) + delta);

    public static ExpandResult Expand(byte[] codeBin, byte[] exheaderIn, byte[] ncch0In, Segment segment, int pages, string journalBasePath = null)
    {
        var r = new ExpandResult();
        if (pages <= 0) { r.Error = "pages must be positive"; return r; }
        if (exheaderIn == null || exheaderIn.Length < 0x400) { r.Error = "exheader must be at least 0x400 bytes (the SCI)"; return r; }

        byte[] exheader = (byte[])exheaderIn.Clone();
        byte[] ncch0 = ncch0In != null ? (byte[])ncch0In.Clone() : null;
        uint bytesToAdd = (uint)pages * PageSize;

        uint segAddr, segPages;
        int addrOff, pagesOff, sizeOff;
        switch (segment)
        {
            case Segment.Text: addrOff = 0x10; pagesOff = 0x14; sizeOff = 0x18; break;
            case Segment.Rodata: addrOff = 0x20; pagesOff = 0x24; sizeOff = 0x28; break;
            case Segment.Data: addrOff = 0x30; pagesOff = 0x34; sizeOff = 0x38; break;
            default: r.Error = "unknown segment"; return r;
        }
        segAddr = ReadU32(exheader, addrOff);
        segPages = ReadU32(exheader, pagesOff);

        uint insertionPoint = segAddr + segPages * PageSize - LoadBase;
        if (insertionPoint > (uint)codeBin.Length) { r.Error = $"computed insertion point 0x{insertionPoint:X} is past the end of code.bin (0x{codeBin.Length:X})"; return r; }

        // Grow the segment's own declared size. .rodata has no separate "size" update in the
        // source tool (only .text/.data are ever chosen there) - still safe to update uniformly
        // since CodeSize <= NumMaxPages*PageSize must hold regardless of which segment grows.
        AddU32(exheader, sizeOff, bytesToAdd);
        AddU32(exheader, pagesOff, bytesToAdd / PageSize);

        byte[] outCode = new byte[codeBin.Length + (int)bytesToAdd];
        Array.Copy(codeBin, 0, outCode, 0, (int)insertionPoint);
        for (int i = 0; i < bytesToAdd; i++) outCode[(int)insertionPoint + i] = 0xCC;
        Array.Copy(codeBin, (int)insertionPoint, outCode, (int)(insertionPoint + bytesToAdd), codeBin.Length - (int)insertionPoint);

        // Any segment whose declared load address sits at-or-after the insertion point shifts
        // forward by the same amount (its file content just moved; keep its virtual address
        // matching where the shifted bytes now decompress/map to).
        uint rodataAddr = ReadU32(exheader, 0x20);
        uint dataAddr = ReadU32(exheader, 0x30);
        if (insertionPoint + LoadBase <= rodataAddr && segment != Segment.Rodata)
            AddU32(exheader, 0x20, bytesToAdd);
        if (insertionPoint + LoadBase <= ReadU32(exheader, 0x30) && segment != Segment.Data)
            AddU32(exheader, 0x30, bytesToAdd);

        if (ncch0 != null && ncch0.Length >= 0x1B4)
        {
            AddU32(ncch0, 0x1A4, bytesToAdd / MediaUnit); // ExeFS size, media units
            AddU32(ncch0, 0x1B0, bytesToAdd / MediaUnit); // RomFS offset, media units
        }

        int relocated = 0;
        List<uint> unconfirmed = [];
        List<uint> skippedStringLike = [];
        if (segment != Segment.Data || insertionPoint < (uint)codeBin.Length)
        {
            // Data-segment pure end-of-file appends need no relocation pass at all (nothing
            // existing could have pointed past its own old end) - the loop below is a no-op for
            // that case since insertionPoint == old file length, but is kept unconditional for any
            // caller that passes a non-default insertion point in the future.
            relocated = RelocatePointers(outCode, exheader, insertionPoint, bytesToAdd, out unconfirmed, out skippedStringLike, includeBss: true);
        }

        if (!string.IsNullOrEmpty(journalBasePath))
        {
            CodeBinExpansionJournal.RecordExpansion(journalBasePath, segment, insertionPoint, bytesToAdd);
        }

        r.Success = true;
        r.CodeBin = outCode;
        r.ExHeader = exheader;
        r.NcchHeader0 = ncch0 ?? [];
        r.InsertionPointFileOffset = insertionPoint;
        r.RelocatedPointers = relocated;
        r.UnconfirmedPointerCandidates = unconfirmed;
        r.SkippedStringLikeCandidates = skippedStringLike;
        return r;
    }

    internal static int RelocatePointers(byte[] file, byte[] exheader, uint insertionPoint, uint bytesToAdd,
        out List<uint> unconfirmedCandidates, out List<uint> skippedStringLikeCandidates, bool includeBss = false)
    {
        uint codeFileStart = ReadU32(exheader, 0x20) - LoadBase; // rodata start, post-shift
        uint dataLoadedStart = ReadU32(exheader, 0x30);
        uint dataPages = ReadU32(exheader, 0x34);
        uint fileLoadedEnd = dataLoadedStart + dataPages * PageSize;
        uint fileFileEnd = fileLoadedEnd - LoadBase;
        uint bssEnd = includeBss ? fileLoadedEnd - bytesToAdd + ReadU32(exheader, 0x3C) : fileLoadedEnd;
        uint valueEnd = Math.Max(fileLoadedEnd, bssEnd);

        var confirmedLiteralTargets = FindConfirmedLiteralTargetVAs(file, Math.Min(codeFileStart, (uint)file.Length));
        unconfirmedCandidates = [];
        skippedStringLikeCandidates = [];

        int count = 0;
        for (uint offset = 0; offset + 4 <= (uint)file.Length; offset += 4)
        {
            uint value = ReadU32(file, (int)offset);
            if (value < LoadBase) continue;
            if (value < insertionPoint + LoadBase) continue;
            if (value > valueEnd) continue;
            if (value % 4 != 0) continue;

            // BSS occupies loaded memory but has no corresponding bytes in code.bin.
            if (!includeBss || value < fileLoadedEnd - bytesToAdd)
            {
                uint targetFileOff = value - LoadBase;
                if (targetFileOff + 4 > (uint)file.Length) continue;
            }

            if (offset < codeFileStart)
            {
                if (confirmedLiteralTargets.Contains(offset + LoadBase))
                {
                    AddU32(file, (int)offset, bytesToAdd);
                    count++;
                }
                else
                {
                    unconfirmedCandidates.Add(offset);
                }
            }
            else if (offset < fileFileEnd)
            {
                if (LooksLikeUtf16String(file, offset, codeFileStart, fileFileEnd))
                {
                    skippedStringLikeCandidates.Add(offset);
                }
                else
                {
                    AddU32(file, (int)offset, bytesToAdd);
                    count++;
                }
            }
        }
        return count;
    }

    private static bool LooksLikeUtf16String(byte[] file, uint offset, uint regionStart, uint regionEnd)
    {
        int start = (int)Math.Max(regionStart, offset - 8);
        int end = (int)Math.Min(regionEnd - 1, offset + 8);
        int printablePairs = 0, totalPairs = 0;
        for (int b = start; b + 1 <= end; b += 2)
        {
            totalPairs++;
            byte lo = file[b], hi = file[b + 1];
            if (hi == 0x00 && lo is >= 0x20 and < 0x7F) printablePairs++;
        }
        return totalPairs > 0 && printablePairs >= totalPairs * 3 / 4;
    }

    private static HashSet<uint> FindConfirmedLiteralTargetVAs(byte[] file, uint textEnd)
    {
        var targets = new HashSet<uint>();

        for (uint off = 0; off + 4 <= textEnd; off += 4)
        {
            uint word = ReadU32(file, (int)off);
            if ((word & 0x0F7F0000) != 0x051F0000) continue; // ARM LDR Rd,[PC,#imm] (any Rd, cond)
            uint imm12 = word & 0xFFF;
            bool up = (word & (1 << 23)) != 0;
            uint pc = off + LoadBase + 8; // ARM PC = this instruction's VA + 8
            targets.Add(up ? pc + imm12 : pc - imm12);
        }

        for (uint off = 0; off + 2 <= textEnd; off += 2)
        {
            ushort half = BitConverter.ToUInt16(file, (int)off);
            if ((half & 0xF800) != 0x4800) continue; // THUMB LDR Rd,[PC,#imm] (any Rd)
            uint imm8 = (uint)(half & 0xFF);
            uint pc = ((off + LoadBase + 4) & ~3u); // THUMB PC = this instruction's VA + 4, word-aligned
            targets.Add(pc + imm8 * 4);
        }

        return targets;
    }

    public static ExpandResult ExpandData(byte[] codeBin, byte[] exheader, byte[] ncch0, int pages)
        => Expand(codeBin, exheader, ncch0, Segment.Data, pages);

    public static ExpandResult ExpandText(byte[] codeBin, byte[] exheader, byte[] ncch0, int pages)
        => Expand(codeBin, exheader, ncch0, Segment.Text, pages);
}
