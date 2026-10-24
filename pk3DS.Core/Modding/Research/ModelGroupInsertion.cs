using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

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

    public static int? GetGroupIndexForForm(byte[] file0, int totalGarcMemberCount, int speciesSlot, int form)
    {
        int recordCount = Table1RecordCount(file0, totalGarcMemberCount);
        if (speciesSlot < 0 || speciesSlot >= recordCount) return null;
        var (groupStart, count, flag) = ReadRecord(file0, speciesSlot);
        if (groupStart == 0 && count == 0 && flag == 0) return null;
        if (form < 0 || form >= count) return null;
        return groupStart + form;
    }

    public static int GetSettingsFileIndex(int groupIndex) => 1 + groupIndex * FilesPerGroup + (FilesPerGroup - 1);

    public static int FindNextFreeSpeciesSlot(byte[] file0, int totalGarcMemberCount)
    {
        int count = Table1RecordCount(file0, totalGarcMemberCount);
        for (int i = 0; i < count; i++)
            if (IsEmptySlot(file0, i))
                return i;
        return -1;
    }

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
