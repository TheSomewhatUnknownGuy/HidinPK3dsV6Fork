using System;
using System.Collections.Generic;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Inserts a small number of bytes at an arbitrary file offset INSIDE one already-loaded code.bin
/// segment (.text/.rodata/.data), consuming that segment's own existing tail slack (the gap
/// between its real content and its <c>NumMaxPages</c> allocation) rather than growing the file.
/// <para>
/// <see cref="CodeBinExpander"/> only ever grows a segment at its own OUTER boundary, shifting
/// every later segment along with it - fine for a whole-segment grow, but wrong for "insert one
/// new record before a terminator in the middle of a table", which is the shape workstream 1g
/// (Pokémon cries) needs: a new 4-byte record has to land immediately before the existing
/// 0xFFFF/0x8000 sentinel, not at the end of the file.
/// </para>
/// <para>
/// Real, byte-verified finding this session (Ultra Sun Concept's real <c>code.bin</c>/
/// <c>ExHeader.bin</c>): <c>.text</c>'s declared CodeSize (0x4B99F0) is smaller than the real,
/// functioning code already packed into that segment - the Expansion Pack's cry-lookup routine and
/// several others already run past that point, filling the segment's page allocation completely
/// full (zero bytes of true padding remain between the last instruction and the 0x4BA000 page
/// boundary). CodeSize is therefore stale/unenforced in this ROM; this class deliberately never
/// touches it, matching that already-working precedent instead of trying to "correct" it. A
/// practical consequence: in this ROM, <see cref="Insert"/> will only succeed once fresh slack has
/// been created via <see cref="CodeBinExpander.Expand"/> first - there is no free room to consume
/// today.
/// </para>
/// </summary>
public static class CodeBinSegmentSplicer
{
    private const uint LoadBase = 0x00100000;
    private const uint PageSize = 0x1000;

    public sealed class Result
    {
        public bool Success;
        public string Error = "";
        public byte[] CodeBin = [];
        public int RelocatedPointers;

        /// <summary>See <see cref="CodeBinExpander.ExpandResult.UnconfirmedPointerCandidates"/> - same meaning, same caveat.</summary>
        public List<uint> UnconfirmedPointerCandidates = [];

        /// <summary>See <see cref="CodeBinExpander.ExpandResult.SkippedStringLikeCandidates"/> - same meaning.</summary>
        public List<uint> SkippedStringLikeCandidates = [];
    }

    /// <summary>
    /// Inserts <paramref name="bytesToInsert"/> at file offset <paramref name="fileOffset"/>,
    /// shifting every byte from there to the end of the segment's own already-allocated page range
    /// forward by that many bytes. Fails (rather than silently touching the next segment) if the
    /// segment's tail doesn't already hold that many bytes of genuine filler (0xCC/0x00/0xFF) -
    /// grow the segment with <see cref="CodeBinExpander.Expand"/> first in that case, then retry.
    /// Every 4-byte-aligned pointer in the file that referenced something at-or-after
    /// <paramref name="fileOffset"/> is bumped by the insert length, via the same heuristic
    /// <see cref="CodeBinExpander"/> itself uses for a whole-segment grow.
    /// </summary>
    public static Result Insert(byte[] codeBin, byte[] exheader, CodeBinExpander.Segment segment,
        int fileOffset, byte[] bytesToInsert)
    {
        var r = new Result();
        if (codeBin == null) { r.Error = "codeBin is null"; return r; }
        if (bytesToInsert == null || bytesToInsert.Length == 0) { r.Error = "nothing to insert"; return r; }
        if (exheader == null || exheader.Length < 0x400) { r.Error = "exheader must be at least 0x400 bytes"; return r; }

        int addrOff, pagesOff;
        switch (segment)
        {
            case CodeBinExpander.Segment.Text: addrOff = 0x10; pagesOff = 0x14; break;
            case CodeBinExpander.Segment.Rodata: addrOff = 0x20; pagesOff = 0x24; break;
            case CodeBinExpander.Segment.Data: addrOff = 0x30; pagesOff = 0x34; break;
            default: r.Error = "unknown segment"; return r;
        }
        uint segAddr = BitConverter.ToUInt32(exheader, addrOff);
        uint segPages = BitConverter.ToUInt32(exheader, pagesOff);
        uint segFileStart = segAddr - LoadBase;
        uint segFileEnd = segFileStart + segPages * PageSize;
        int insertLen = bytesToInsert.Length;

        if (fileOffset < segFileStart || fileOffset > segFileEnd)
        { r.Error = $"file offset 0x{fileOffset:X} is outside this segment's range [0x{segFileStart:X}, 0x{segFileEnd:X})"; return r; }
        if (segFileEnd > (uint)codeBin.Length)
        { r.Error = $"segment's declared range extends past the end of code.bin (0x{segFileEnd:X} > 0x{codeBin.Length:X})"; return r; }

        int tailStart = (int)segFileEnd - insertLen;
        for (int i = 0; i < insertLen; i++)
        {
            byte b = codeBin[tailStart + i];
            if (b != 0xCC && b != 0x00 && b != 0xFF)
            {
                r.Error = $"segment has no free slack at its tail to consume (byte 0x{b:X2} at file offset " +
                          $"0x{tailStart + i:X} isn't filler) - grow this segment with CodeBinExpander.Expand first";
                return r;
            }
        }

        byte[] outCode = (byte[])codeBin.Clone();
        int moveLen = (int)segFileEnd - fileOffset - insertLen;
        Array.Copy(outCode, fileOffset, outCode, fileOffset + insertLen, moveLen);
        Array.Copy(bytesToInsert, 0, outCode, fileOffset, insertLen);

        int relocated = CodeBinExpander.RelocatePointers(outCode, exheader, (uint)fileOffset, (uint)insertLen, out var unconfirmed, out var skippedStringLike);

        r.Success = true;
        r.CodeBin = outCode;
        r.RelocatedPointers = relocated;
        r.UnconfirmedPointerCandidates = unconfirmed;
        r.SkippedStringLikeCandidates = skippedStringLike;
        return r;
    }
}
