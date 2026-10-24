using System;
using System.Collections.Generic;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Grows code.bin by inserting new, 0xCC-filled space into one of its three loaded segments
/// (.text/.code, .rodata, .data) and updating the matching ExHeader (SCI CodeSetInfo) fields so
/// the new size is self-consistent.
/// <para>
/// This is a direct C# port of the algorithm in ABZB/CRO-Expander's <c>exefs_expander.py</c>
/// (fetched and read in full, not guessed from the tool's name) - not an independent invention.
/// The three ExHeader CodeSetInfo blocks this reads/writes (each 12 bytes: Address, NumMaxPages,
/// CodeSize) sit at file offsets 0x10 (.text), 0x20 (.rodata), 0x30 (.data) inside the 0x400-byte
/// SCI, matching the public, documented 3DS ExHeader layout - confirmed against the real
/// `ExHeader.bin` in this session's reference ROM (byte-for-byte: Address/NumMaxPages/CodeSize for
/// all three segments summed exactly to that ROM's real code.bin file size, with zero slack).
/// </para>
/// <para>
/// <b>Open question, not yet resolved:</b> this project's actual distribution model (see
/// `Test File For US/README.txt`) ships only `code.bin` itself via Luma3DS/Citra/Azahar
/// LayeredFS "game patching" - it does NOT ship `ExHeader.bin`/`HeaderNCCH0.bin` at all, and the
/// running game/emulator loads those from the base, unmodified, installed title. Whether
/// LayeredFS game-patching actually re-derives the loaded memory layout from the replacement
/// code.bin's own size (as is reportedly common for other 3DS ROM hacks that grow code.bin this
/// way) or whether it strictly trusts the base title's original, unmodifiable ExHeader is NOT
/// verified here - it needs a real Citra/Luma test with an actually-grown code.bin before this is
/// trusted for anything beyond a scratch copy. The ExHeader/NCCH outputs are still produced (they
/// match the source tool's own approach and cost nothing to keep around), but they may turn out to
/// be unnecessary, or insufficient on their own, for this project's specific LayeredFS setup.
/// </para>
/// </summary>
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

        /// <summary>
        /// File offsets in `.text` that fell inside the shifted address range and looked
        /// pointer-shaped, but weren't backed by an actual `LDR [PC,#imm]` literal-pool reference
        /// anywhere - see <see cref="RelocatePointers"/>'s doc comment. NOT touched, since a THUMB
        /// instruction pair can be byte-for-byte indistinguishable from a real pointer here; review
        /// these manually before assuming they don't matter.
        /// </summary>
        public List<uint> UnconfirmedPointerCandidates = [];

        /// <summary>
        /// File offsets in `.rodata`/`.data` that looked pointer-shaped but were skipped because
        /// they positively look like embedded UTF-16LE string data instead - see
        /// <see cref="RelocatePointers"/>'s doc comment. Reported for transparency; unlike
        /// <see cref="UnconfirmedPointerCandidates"/> these were a deliberate, positive exclusion,
        /// not something left ambiguous.
        /// </summary>
        public List<uint> SkippedStringLikeCandidates = [];
    }

    private static uint ReadU32(byte[] d, int off) => BitConverter.ToUInt32(d, off);
    private static void WriteU32(byte[] d, int off, uint value) => BitConverter.GetBytes(value).CopyTo(d, off);
    private static void AddU32(byte[] d, int off, uint delta) => WriteU32(d, off, ReadU32(d, off) + delta);

    /// <summary>
    /// Appends <paramref name="pages"/> pages (0x1000 bytes each) of 0xCC to the given segment.
    /// For <see cref="Segment.Data"/> (the last segment - .text, .rodata, .data are laid out back
    /// to back with no gaps, confirmed this session against a real ROM), this is a plain, safe
    /// append at the true end of the file: no existing bytes move, so no pointer relocation is
    /// needed at all. For <see cref="Segment.Text"/>/<see cref="Segment.Rodata"/>, new space is
    /// spliced in at the end of that segment's already-allocated pages, which shifts every segment
    /// after it forward in the file - both the physical bytes and (for the CodeSetInfo entries
    /// declared to live at-or-after the insertion point) their declared load addresses, and every
    /// absolute pointer embedded in the binary that referenced something at-or-after the insertion
    /// point needs the same shift. That relocation pass is a heuristic (same one the source tool
    /// uses) - verify a sample of what it changes before trusting the result.
    /// </summary>
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

    /// <summary>
    /// Scans every 4-byte-aligned word in the (already-expanded) binary for values that look like
    /// absolute pointers into the shifted region, and adds <paramref name="bytesToAdd"/> to each
    /// one this trusts.
    /// <para>
    /// <b>Inside `.text` this is NOT a blind "any 4-byte-aligned word in range" scan</b> - this
    /// codebase's own real ROM data confirmed why that's unsafe: `.text` mixes 4-byte ARM and
    /// 2-byte THUMB instructions, and a THUMB instruction pair can land on the exact same 4-byte
    /// boundary this scan reads and be byte-for-byte indistinguishable from a real pointer (THUMB's
    /// extremely common `STR`/`LDR Rd,[Rn,#imm]` family encodes as 0x5xxx-0x6xxx, which is exactly
    /// the address range many real `.rodata`/`.data` pointers fall in for this ROM - not a rare
    /// coincidence, a structural collision). A `.text` candidate is only relocated if its own file
    /// position is confirmed as a real literal-pool slot: some instruction elsewhere - checked in
    /// both ARM and THUMB `LDR Rd,[PC,#imm]` encodings - actually computes a PC-relative address
    /// landing exactly there. Anything pointer-shaped with no such confirming reference is left
    /// untouched and reported via <see cref="ExpandResult.UnconfirmedPointerCandidates"/>.
    /// </para>
    /// <para>
    /// <b>`.rodata`/`.data` uses the opposite default, deliberately.</b> The original ported
    /// heuristic here ("does the target look like a function's opening push instruction") was
    /// tested against this ROM's real data and found to confirm essentially nothing - 5 out of
    /// 9008 real candidates in one real scenario - while a direct inspection of what it rejected
    /// turned up a genuine, structurally unambiguous pointer array (consecutive entries incrementing
    /// by exactly 4 bytes, e.g. 0x...D834/D838/D83C/D840, sitting beside small paired id fields -
    /// the classic shape of a reflection/debug metadata table) that a real, unmodified game system
    /// could plausibly walk by index rather than through any single `LDR [PC,#imm]` this scan could
    /// ever find. There is no ARM/THUMB code-vs-data ambiguity in pure data, so the failure mode
    /// this scan actually has to guard against here is different: embedded UTF-16LE text (error
    /// strings, debug names) whose byte pattern can also coincidentally fall in a valid address
    /// range - confirmed as the dominant real false-positive class this session (in the same real
    /// scenario, 974 of 9008 candidates decoded directly to readable string fragments). So instead
    /// of requiring positive proof a candidate is a pointer (impossible in general, per the user who
    /// first raised this), a `.rodata`/`.data` candidate is relocated UNLESS it positively looks like
    /// string data (<see cref="LooksLikeUtf16String"/>) - trading a small, low-probability risk of
    /// "correcting" some unrelated large numeric field that happens to fall in the address range,
    /// for not silently leaving real, structurally-confirmed pointer tables stale. Skipped
    /// string-like candidates are reported via <see cref="ExpandResult.SkippedStringLikeCandidates"/>
    /// for transparency, not because they need review the way `.text`'s unconfirmed list does.
    /// </para>
    /// <para>
    /// Exposed for <see cref="CodeBinSegmentSplicer"/>, which reuses this exact logic for a
    /// mid-segment splice (shift every pointer at-or-after some offset forward by some amount) -
    /// the scan-the-whole-file/gate-by-segment logic is identical whether the shift came from
    /// growing a segment at its own boundary or from consuming existing tail slack inside one.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// True if the 16 bytes centred on <paramref name="offset"/> are at least 75% plausible
    /// UTF-16LE characters (a null high byte paired with a printable-ASCII low byte) - the pattern
    /// confirmed this session to explain the overwhelming majority of non-pointer words that
    /// otherwise look address-shaped in `.rodata`/`.data` (embedded debug/error strings).
    /// </summary>
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

    /// <summary>
    /// Every virtual address actually read from via a real `LDR Rd,[PC,#imm]` literal-pool load
    /// found in <paramref name="file"/>[0, <paramref name="textEnd"/>) - checked in both ARM
    /// (4-byte-aligned, mask <c>(word &amp; 0x0F7F0000) == 0x051F0000</c>, i.e. single data
    /// transfer with immediate offset, pre-indexed, no writeback, load, Rn=PC) and THUMB
    /// (2-byte-aligned, mask <c>(halfword &amp; 0xF800) == 0x4800</c>) encodings, since either can
    /// appear anywhere in `.text` and this deliberately doesn't try to track which regions are
    /// really which mode - it only needs to know whether SOMETHING, in EITHER mode, treats a given
    /// address as a literal to load, not whether that something is itself real, executed code.
    /// </summary>
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

    /// <summary>
    /// The safe, common case: append <paramref name="pages"/> pages to .data (the last segment),
    /// a pure end-of-file grow with no relocation risk. Use this unless new space is specifically
    /// needed for executable code ahead of .rodata/.data.
    /// </summary>
    public static ExpandResult ExpandData(byte[] codeBin, byte[] exheader, byte[] ncch0, int pages)
        => Expand(codeBin, exheader, ncch0, Segment.Data, pages);

    /// <summary>The harder case: insert new space into .text, shifting .rodata and .data after it.</summary>
    public static ExpandResult ExpandText(byte[] codeBin, byte[] exheader, byte[] ncch0, int pages)
        => Expand(codeBin, exheader, ncch0, Segment.Text, pages);
}
