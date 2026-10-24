using System;
using System.Collections.Generic;

namespace pk3DS.Core.Modding.Research;

public static class CryTablePatcher
{
    public const int TableStart = 0x4B9CF8;

    public const int TerminatorOffset = 0x4B9F18;

    public const int CryBaseRegionStart = 5334;

    public const ushort NoDistinctCry = 0x8000;

    public sealed class InsertResult
    {
        public bool Success;
        public string Error = "";
        public byte[] CodeBin = [];
        public byte[] ExHeader = [];
        public byte[] NcchHeader0 = [];
        public int RelocatedPointers;
        public bool GrewTextSegment;

        public List<uint> UnconfirmedPointerCandidates = [];

        public List<uint> SkippedStringLikeCandidates = [];
    }

    public static ushort EncodeKey(int speciesId, int form) => (ushort)((speciesId & 0x7FF) | (form << 11));

    public static InsertResult InsertRecord(byte[] codeBin, byte[] exheader, byte[] ncch0,
        int speciesId, int form, int cryGarcMember)
    {
        if (cryGarcMember < CryBaseRegionStart)
            return new InsertResult { Error = $"cry GARC member {cryGarcMember} is before the base region start " +
                                                $"({CryBaseRegionStart}) - it can't be represented as a relative clip index" };
        ushort clipIndex = (ushort)(cryGarcMember - CryBaseRegionStart);
        if (clipIndex == NoDistinctCry)
            return new InsertResult { Error = $"cry GARC member {cryGarcMember} encodes to the reserved " +
                                                $"'no distinct cry' sentinel (0x{NoDistinctCry:X}) - pick a different member" };

        byte[] record = new byte[4];
        BitConverter.GetBytes(EncodeKey(speciesId, form)).CopyTo(record, 0);
        BitConverter.GetBytes(clipIndex).CopyTo(record, 2);

        var probe = CodeBinSegmentSplicer.Insert(codeBin, exheader, CodeBinExpander.Segment.Text, TerminatorOffset, record);
        bool grew = false;
        byte[] workingExheader = exheader;
        byte[] workingNcch0 = ncch0;

        List<uint> unconfirmedFromGrow = [];
        List<uint> skippedFromGrow = [];
        if (!probe.Success)
        {
            var expand = CodeBinExpander.ExpandText(codeBin, exheader, ncch0, pages: 1);
            if (!expand.Success)
                return new InsertResult { Error = $"no room, and growing .text failed: {expand.Error}" };

            codeBin = expand.CodeBin;
            workingExheader = expand.ExHeader;
            workingNcch0 = expand.NcchHeader0;
            grew = true;
            unconfirmedFromGrow = expand.UnconfirmedPointerCandidates;
            skippedFromGrow = expand.SkippedStringLikeCandidates;

            probe = CodeBinSegmentSplicer.Insert(codeBin, workingExheader, CodeBinExpander.Segment.Text, TerminatorOffset, record);
            if (!probe.Success)
                return new InsertResult { Error = $"still no room after growing .text by a page: {probe.Error}" };
        }

        return new InsertResult
        {
            Success = true,
            CodeBin = probe.CodeBin,
            ExHeader = workingExheader,
            NcchHeader0 = workingNcch0 ?? [],
            RelocatedPointers = probe.RelocatedPointers,
            GrewTextSegment = grew,
            UnconfirmedPointerCandidates = [.. unconfirmedFromGrow, .. probe.UnconfirmedPointerCandidates],
            SkippedStringLikeCandidates = [.. skippedFromGrow, .. probe.SkippedStringLikeCandidates],
        };
    }
}
