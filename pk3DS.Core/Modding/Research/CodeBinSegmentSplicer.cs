using System;
using System.Collections.Generic;

namespace pk3DS.Core.Modding.Research;

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

        public List<uint> UnconfirmedPointerCandidates = [];

        public List<uint> SkippedStringLikeCandidates = [];
    }

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
