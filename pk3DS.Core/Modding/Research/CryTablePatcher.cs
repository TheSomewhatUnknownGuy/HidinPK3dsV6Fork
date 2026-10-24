using System;
using System.Collections.Generic;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Inserts a new form-cry lookup record into the 136-entry table the USUM Expansion Pack's
/// cry-lookup routine walks at code.bin's real, byte-verified file offset 0x4B9CF8-0x4B9F14
/// (a 0xFFFF/0x8000 sentinel terminates it at 0x4B9F18 - confirmed this session: that's exactly
/// 0x4B9F18 + 4 - 0x4B9C80 = 0x29C = 668 bytes from the routine's start, matching
/// <c>code_map.csv</c>'s documented "POKEMON CRIES" section length exactly). Each record is 4
/// bytes: a u16 <c>species | (form &lt;&lt; 11)</c> key, then a u16 clip index - one entry per alt
/// FORM that has its own distinct cry (base-species cries are looked up by species id directly
/// elsewhere in the routine, not through this table at all).
/// <para>
/// <b>The clip index is relative to `cry` GARC member <see cref="CryBaseRegionStart"/> (5334, the
/// base-CWAR region's start), not an absolute member index</b> - confirmed by dumping every real
/// entry in this ROM's live table: all 66 non-sentinel clip values land at exactly 218-283, which
/// is only a sane range once treated as relative (218 + 5334 = 5552, matching the documented
/// form-CWAR region's real start byte-for-byte; as raw absolute indices, 218-283 would point deep
/// into the unrelated 0-5332 region instead). A clip value of exactly `0x8000` is a distinct
/// per-entry sentinel (70 of the 136 real entries use it) meaning "no distinct cry recorded for
/// this form - fall back to the routine's own hardcoded default", not a real reference at all -
/// this is separate from the table's own `0xFFFF`/`0x8000` end-of-table terminator, which also
/// happens to carry `0x8000` in the same field position. <see cref="InsertRecord"/> takes and
/// returns absolute `cry` GARC member indices and converts internally, so callers never have to
/// remember the relative encoding themselves.
/// </para>
/// <para>
/// Built on <see cref="CodeBinSegmentSplicer"/> since this is a mid-table insert (a new record has
/// to land immediately before the existing terminator, not at the end of the file) - byte-verified
/// this session that `.text`'s tail slack in this ROM is entirely consumed (0 free bytes between
/// the last packed routine and the page boundary), so <see cref="InsertRecord"/> will, in practice,
/// always need to grow `.text` by a page first the very first time it's called; after that, the
/// fresh page's slack comfortably absorbs roughly a thousand more records (0x1000 bytes / 4 bytes
/// each) before another grow is ever needed again.
/// </para>
/// </summary>
public static class CryTablePatcher
{
    /// <summary>File offset of the table's first record.</summary>
    public const int TableStart = 0x4B9CF8;

    /// <summary>File offset of the 4-byte 0xFFFF/0x8000 terminator record - new records are inserted here.</summary>
    public const int TerminatorOffset = 0x4B9F18;

    /// <summary>`cry` GARC member the table's clip index is relative to (the base-CWAR region's start).</summary>
    public const int CryBaseRegionStart = 5334;

    /// <summary>Per-entry sentinel meaning "no distinct cry recorded - use the routine's hardcoded default".</summary>
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

        /// <summary>See <see cref="CodeBinExpander.ExpandResult.UnconfirmedPointerCandidates"/> - same meaning, same caveat.</summary>
        public List<uint> UnconfirmedPointerCandidates = [];

        /// <summary>See <see cref="CodeBinExpander.ExpandResult.SkippedStringLikeCandidates"/> - same meaning.</summary>
        public List<uint> SkippedStringLikeCandidates = [];
    }

    /// <summary>
    /// Encodes the table's key field: species id in the low 11 bits, form number in bits 11+
    /// (matches the routine's own `CMP R6,#0x1F` / shift pattern found at the routine's start,
    /// i.e. up to 2047 species ids and up to 31 forms per species).
    /// </summary>
    public static ushort EncodeKey(int speciesId, int form) => (ushort)((speciesId & 0x7FF) | (form << 11));

    /// <summary>
    /// Inserts one new [speciesFormKey][clipIndex] record immediately before the table's
    /// terminator, growing `.text` by one page first (via <see cref="CodeBinExpander.ExpandText"/>)
    /// if the segment's existing tail slack can't absorb the 4 new bytes on its own.
    /// </summary>
    /// <param name="cryGarcMember">
    /// The ABSOLUTE `cry` GARC member holding this form's audio (e.g. the index a fresh
    /// <see cref="SpeciesArraySplicer"/> append just produced) - converted to the table's relative
    /// clip encoding internally. Must be &gt;= <see cref="CryBaseRegionStart"/>.
    /// </param>
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
