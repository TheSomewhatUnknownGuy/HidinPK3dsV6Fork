using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Inserts a new species (or a new alt-form group for an existing species) into the `models`
/// GARC (a094) - workstream 1b, the last of the four the user asked to have "figured out"
/// alongside 1c/1e (personal/evolution/levelup/megaevo splicing) and 1g (cries).
/// <para>
/// The real, byte-verified structure of this GARC (Ultra Sun Concept, confirmed this session -
/// not guessed, and not simply taken on faith from the user's own worked example, though that
/// example is what unlocked the rest): file[0] holds two back-to-back tables, and every other
/// member is raw per-model-group asset data.
/// </para>
/// <para>
/// <b>Table 1</b> - file[0] bytes [0x0, 0x1100): 1088 four-byte records, one per species SLOT
/// (the exact same indexing space `personal`/`evolution`/`levelup`/`megaevo` already use -
/// confirmed this session: this ROM has exactly 1026 real records here, and the remaining 62 are
/// an untouched, contiguous run of all-zero placeholders at positions 1026-1087, landing on
/// precisely the same insertion boundary <see cref="SpeciesFamilyInsertion"/> already uses).
/// Each record is <c>[u16 groupStart][u8 groupCount][u8 flag]</c>: `groupStart` is that species'
/// first entry in the completely separate MODEL GROUP numbering table 2 indexes into (not a
/// species id, and not required to relate to this record's own array position at all beyond an
/// early-game coincidence for original, never-touched retail species); `groupCount` is how many
/// consecutive model groups the species occupies (1 for a plain species, more for gender
/// differences/alt forms); `flag` is a bitmask - bit0 always set, bit1 = has a gender/female
/// difference, bit2 = has additional forms (01/03/05/07, matching the user's own worked
/// Palafin example: flag 05 = "+forms").
/// </para>
/// <para>
/// <b>Table 2</b> - file[0] bytes [0x1100, end): one 2-byte entry per MODEL GROUP index (1678 of
/// them in this ROM, 0-1677 - confirmed to exactly equal both file[0]'s remaining length /2 and
/// the highest `groupStart+groupCount` found anywhere in table 1, and matching
/// `code_map.csv`'s own documented "1172-&gt;1678" FinderStudioViewer model-group-count raise).
/// Each entry is <c>[byte shareBitmask][byte sourceForm]</c>: bit0/1/2 of the bitmask mean
/// "borrow this group's animations/textures/model (respectively) from `sourceForm`", a form
/// number relative to the OWNING species' own group range (`groupStart + sourceForm`) - `00 00`
/// means the group has fully independent assets of its own, borrowing nothing (confirmed against
/// the user's real Palafin example: group 1541's bytes are exactly `00 00`, at file offset
/// `0x1100 + 1541*2 = 0x1D0A`, matching what the user reported byte-for-byte).
/// </para>
/// <para>
/// <b>GARC member layout</b> - file[0] is member 0; every model group's own 9 files then follow
/// sequentially with no gaps: group N occupies members <c>[1 + N*9, 1 + N*9 + 8]</c> (confirmed
/// this session: 1678 groups * 9 files + 1 metadata file = 15103, exactly this ROM's real total
/// member count, verified precisely at groups 0/1/2, Palafin's real groups 1541-1542, AND the
/// very last group 1677 - which lands exactly on the GARC's final member with zero slack). A
/// brand-new group is a pure append at the true end of this array - nothing before it moves.
/// </para>
/// </summary>
public static class ModelGroupInsertion
{
    public const int Table1Stride = 4;
    public const int FilesPerGroup = 9;

    [Flags]
    public enum FormFlags : byte
    {
        Base = 0x01,
        GenderDifference = 0x02,
        HasForms = 0x04,
    }

    [Flags]
    public enum BorrowBitmask : byte
    {
        None = 0x00,
        Animations = 0x01,
        Textures = 0x02,
        Model = 0x04,
        All = Animations | Textures | Model,
    }

    public sealed class InsertResult
    {
        public bool Success;
        public string Error = "";
        public byte[] File0 = [];
        public byte[][] Files = [];
        public int SpeciesSlot;
        public int AssignedGroupStart;
        public int GroupCount;
        public List<string> Notes { get; } = [];
        public string Describe() =>
            $"species slot {SpeciesSlot}: assigned model group(s) {AssignedGroupStart}-{AssignedGroupStart + GroupCount - 1}, " +
            $"{Files.Length} total GARC members (was {Files.Length - GroupCount * FilesPerGroup})" +
            (Notes.Count == 0 ? "" : "\n" + string.Join("\n", Notes));
    }

    /// <summary>
    /// Table 1's real current record count, derived from file0's own length and the GARC's real
    /// total member count - NOT the fixed 0x1100 (1088 records) this used to hardcode. That was
    /// only ever true for the one ROM state this was first verified against; a ROM whose table 1
    /// has since been grown (see <see cref="GrowTable1"/>), or a different "Concept" ROM build that
    /// never matched that exact byte count in the first place (confirmed live: a real ROM with 1330
    /// personal-table entries whose table 1 still only had 1088 records), needs the real value
    /// computed instead of assumed. `numGroups` (from the member count, not file0) times 2 bytes is
    /// table 2's real length; whatever's left in file0 before that is table 1.
    /// </summary>
    public static int Table1RecordCount(byte[] file0, int totalGarcMemberCount)
    {
        int numGroups = (totalGarcMemberCount - 1) / FilesPerGroup;
        int table2ByteLength = numGroups * 2;
        return (file0.Length - table2ByteLength) / Table1Stride;
    }

    private static (ushort groupStart, byte count, byte flag) ReadRecord(byte[] file0, int slot)
    {
        int off = slot * Table1Stride;
        return (BitConverter.ToUInt16(file0, off), file0[off + 2], file0[off + 3]);
    }

    private static bool IsEmptySlot(byte[] file0, int slot)
    {
        var (groupStart, count, flag) = ReadRecord(file0, slot);
        return groupStart == 0 && count == 0 && flag == 0;
    }

    /// <summary>The model group index for a species' Nth form (0 = base), or null if that species has no record or that form is out of range - the same table-1 lookup <see cref="SpeciesInsertionOrchestrator"/> already does inline, exposed here so other editors (e.g. a model-scale editor) don't need to re-derive it.</summary>
    public static int? GetGroupIndexForForm(byte[] file0, int totalGarcMemberCount, int speciesSlot, int form)
    {
        int recordCount = Table1RecordCount(file0, totalGarcMemberCount);
        if (speciesSlot < 0 || speciesSlot >= recordCount) return null;
        var (groupStart, count, flag) = ReadRecord(file0, speciesSlot);
        if (groupStart == 0 && count == 0 && flag == 0) return null;
        if (form < 0 || form >= count) return null;
        return groupStart + form;
    }

    /// <summary>The GARC member index of a model group's 9th (last, "settings") file.</summary>
    public static int GetSettingsFileIndex(int groupIndex) => 1 + groupIndex * FilesPerGroup + (FilesPerGroup - 1);

    /// <summary>
    /// First species slot in table 1 whose record is still all-zero. Callers doing a coordinated
    /// insertion across every per-species GARC (see <see cref="SpeciesFamilyInsertion"/>) should
    /// use the SAME slot index there, not just whatever this returns independently - they only
    /// happen to agree today because this ROM has never had a species inserted anywhere yet.
    /// </summary>
    public static int FindNextFreeSpeciesSlot(byte[] file0, int totalGarcMemberCount)
    {
        int count = Table1RecordCount(file0, totalGarcMemberCount);
        for (int i = 0; i < count; i++)
            if (IsEmptySlot(file0, i))
                return i;
        return -1;
    }

    /// <summary>
    /// One past the highest model group index anywhere in use (`groupStart + groupCount`, maxed
    /// over every non-empty table-1 record) - the first group index safe to hand out next.
    /// </summary>
    public static int ComputeNextFreeGroupIndex(byte[] file0, int totalGarcMemberCount)
    {
        int count = Table1RecordCount(file0, totalGarcMemberCount);
        int max = 0;
        for (int i = 0; i < count; i++)
        {
            var (groupStart, groupCount, flag) = ReadRecord(file0, i);
            if (groupStart == 0 && groupCount == 0 && flag == 0) continue;
            max = Math.Max(max, groupStart + groupCount);
        }
        return max;
    }

    /// <summary>
    /// Extends table 1 by <paramref name="additionalSlots"/> more all-zero (empty) species-slot
    /// records, for a ROM whose personal/evolution/levelup/megaevo tables have grown further than
    /// table 1 has room to address at all (confirmed live: a "Concept" ROM with 1330 real
    /// personal-table entries - including every stock Mega Evolution/regional form, which occupy
    /// slots 1026 onward - but table 1 only ever had 1088 records, so no slot past 1087 could be
    /// referenced from the models side no matter what "free slot" search was used). Table 2's own
    /// bytes are copied through completely unchanged, just moved later in the file to make room -
    /// nothing else in the GARC needs to shift, since GARC members are addressed by their own
    /// offset+length table, not fixed absolute positions.
    /// </summary>
    public static byte[] GrowTable1(byte[] file0, int totalGarcMemberCount, int additionalSlots)
    {
        if (additionalSlots <= 0) throw new ArgumentException("additionalSlots must be positive", nameof(additionalSlots));
        int numGroups = (totalGarcMemberCount - 1) / FilesPerGroup;
        int table2ByteLength = numGroups * 2;
        int oldTable1Length = file0.Length - table2ByteLength;

        byte[] newFile0 = new byte[file0.Length + additionalSlots * Table1Stride];
        Array.Copy(file0, 0, newFile0, 0, oldTable1Length); // old table 1 records, unchanged
        // the additionalSlots * Table1Stride bytes in between default to 0 - new empty records
        Array.Copy(file0, oldTable1Length, newFile0, oldTable1Length + additionalSlots * Table1Stride, table2ByteLength); // table 2, unchanged content, moved later
        return newFile0;
    }

    /// <summary>
    /// Inserts a brand-new species at table-1 slot <paramref name="speciesSlot"/> (must currently
    /// be an empty/all-zero record - get one from <see cref="FindNextFreeSpeciesSlot"/>, or pass
    /// the same slot <see cref="SpeciesFamilyInsertion"/> used so every per-species GARC agrees),
    /// assigning it <paramref name="groupCount"/> new model groups starting at the next free group
    /// index. Each new group's table-2 sharing descriptor defaults to fully independent (`00 00`)
    /// unless <paramref name="borrowFrom"/> supplies a per-group override. Each new group's 9 GARC
    /// members default to empty placeholders unless <paramref name="templateGroupFiles"/> supplies
    /// real content to clone (e.g. an existing species' own 9 files, per the user's "clone an
    /// existing species' model as a placeholder" request) - pass one 9-file template per group, or
    /// a single 9-file template to reuse for every new group.
    /// </summary>
    public static InsertResult InsertNewSpecies(byte[] file0, byte[][] modelFiles, int speciesSlot,
        int groupCount, FormFlags flags, (BorrowBitmask bitmask, byte sourceForm)[] borrowFrom = null,
        byte[][][] templateGroupFiles = null)
    {
        var r = new InsertResult { SpeciesSlot = speciesSlot, GroupCount = groupCount };
        if (file0 == null) { r.Error = "file0 is null"; return r; }
        if (modelFiles == null) { r.Error = "modelFiles is null"; return r; }
        if (groupCount <= 0) { r.Error = "groupCount must be positive"; return r; }
        int recordCount = Table1RecordCount(file0, modelFiles.Length);
        if (speciesSlot < 0 || speciesSlot >= recordCount)
        { r.Error = $"speciesSlot {speciesSlot} is outside table 1's range [0, {recordCount}) - grow table 1 first (see GrowTable1)"; return r; }
        if (!IsEmptySlot(file0, speciesSlot))
        { r.Error = $"table-1 slot {speciesSlot} is already in use - pick an empty slot (see FindNextFreeSpeciesSlot)"; return r; }
        if (borrowFrom != null && borrowFrom.Length != groupCount)
        { r.Error = $"borrowFrom has {borrowFrom.Length} entries, expected {groupCount} (one per new group)"; return r; }
        if (templateGroupFiles != null && templateGroupFiles.Length != groupCount)
        { r.Error = $"templateGroupFiles has {templateGroupFiles.Length} entries, expected {groupCount} (one per new group)"; return r; }

        int newGroupStart = ComputeNextFreeGroupIndex(file0, modelFiles.Length);
        int table2Start = recordCount * Table1Stride;
        int expectedFile0Table2End = table2Start + newGroupStart * 2;
        if (expectedFile0Table2End != file0.Length)
            r.Notes.Add($"table 2's current end (0x{file0.Length:X}) doesn't exactly match the next free group index " +
                        $"(expected end 0x{expectedFile0Table2End:X}) - proceeding anyway, but double-check this ROM's " +
                        "table 2 for pre-existing slack/gaps before trusting this result blindly");

        byte[] newFile0 = new byte[file0.Length + groupCount * 2];
        Array.Copy(file0, newFile0, file0.Length);

        int recOff = speciesSlot * Table1Stride;
        BitConverter.GetBytes((ushort)newGroupStart).CopyTo(newFile0, recOff);
        newFile0[recOff + 2] = (byte)groupCount;
        newFile0[recOff + 3] = (byte)flags;

        for (int g = 0; g < groupCount; g++)
        {
            int off = file0.Length + g * 2;
            byte bitmask = 0, sourceForm = 0;
            if (borrowFrom != null) (bitmask, sourceForm) = ((byte)borrowFrom[g].bitmask, borrowFrom[g].sourceForm);
            newFile0[off] = bitmask;
            newFile0[off + 1] = sourceForm;
        }

        var newFiles = new byte[modelFiles.Length + groupCount * FilesPerGroup][];
        Array.Copy(modelFiles, newFiles, modelFiles.Length);
        // `Files[0]` must be the METADATA file, not the plain old member-0 copy `Array.Copy` just
        // made - a caller that saves `Files` straight back to the GARC without separately
        // remembering to overwrite index 0 with `File0` would silently drop the new table-1/
        // table-2 data while keeping the newly-appended member files, a real, easy-to-miss
        // corruption risk. `Files` is always self-consistent on its own.
        newFiles[0] = newFile0;
        for (int g = 0; g < groupCount; g++)
        {
            byte[][] template = templateGroupFiles != null ? templateGroupFiles[g] : null;
            for (int f = 0; f < FilesPerGroup; f++)
            {
                int idx = modelFiles.Length + g * FilesPerGroup + f;
                newFiles[idx] = template != null && f < template.Length && template[f] != null
                    ? (byte[])template[f].Clone()
                    : [];
            }
        }

        r.Success = true;
        r.File0 = newFile0;
        r.Files = newFiles;
        r.AssignedGroupStart = newGroupStart;
        return r;
    }
}
