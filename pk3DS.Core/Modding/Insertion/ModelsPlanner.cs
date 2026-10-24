#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;

namespace pk3DS.Core.Modding.Insertion;

public sealed class ModelsLayout
{
    private byte[] _file0 = [];

    public int Members, Records, Groups, Table2Offset, Used, FirstEmpty, LastRecord;

    public static ModelsLayout? Parse(byte[] file0, int members, out string error)
    {
        error = "";
        if (file0 == null)
        {
            error = "models file0 is missing";
            return null;
        }
        if (members <= 0)
        {
            error = $"models member count {members} must include file0";
            return null;
        }
        if ((members - 1) % ModelGroupInsertion.FilesPerGroup != 0)
        {
            error = $"models member count {members}: (members - 1) mod {ModelGroupInsertion.FilesPerGroup} = {(members - 1) % ModelGroupInsertion.FilesPerGroup}";
            return null;
        }

        int groups = (members - 1) / ModelGroupInsertion.FilesPerGroup;
        long table2Length = (long)groups * sizeof(ushort);
        if (table2Length > file0.LongLength)
        {
            error = $"models file0 has {file0.LongLength} B, shorter than table 2 ({table2Length} B)";
            return null;
        }
        long table1Length = file0.LongLength - table2Length;
        if (table1Length % ModelGroupInsertion.Table1Stride != 0)
        {
            error = $"models file0 has {table1Length} table-1 bytes, not divisible by {ModelGroupInsertion.Table1Stride}";
            return null;
        }
        long recordCountLong = table1Length / ModelGroupInsertion.Table1Stride;
        if (recordCountLong > int.MaxValue || table1Length > int.MaxValue)
        {
            error = "models table 1 is too large to index";
            return null;
        }

        int records = (int)recordCountLong;
        int table2Offset = checked(records * ModelGroupInsertion.Table1Stride);
        if (table2Offset != table1Length || (long)table2Offset + table2Length != file0.LongLength)
        {
            error = "models table 2 does not start at 4 x records or ends outside file0";
            return null;
        }

        var ranges = new List<(int Start, int End, int Record)>();
        int used = 0;
        int firstEmpty = -1;
        int lastRecord = -1;
        for (int record = 0; record < records; record++)
        {
            int offset = checked(record * ModelGroupInsertion.Table1Stride);
            ushort start = BitConverter.ToUInt16(file0, offset);
            byte count = file0[offset + 2];
            byte flags = file0[offset + 3];
            if (start == 0 && count == 0 && flags == 0)
            {
                if (firstEmpty < 0) firstEmpty = record;
                continue;
            }
            if (count == 0)
            {
                error = $"models record {record} has groupStart {start} and flags {flags}, but groupCount is zero";
                return null;
            }

            long end = (long)start + count;
            if (end > groups)
            {
                error = $"models record {record} range [{start}, {end}) ends past {groups} groups";
                return null;
            }
            ranges.Add((start, (int)end, record));
            used++;
            lastRecord = record;
        }

        ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
        for (int i = 1; i < ranges.Count; i++)
        {
            if (ranges[i].Start < ranges[i - 1].End)
            {
                error = $"models group ranges for records {ranges[i - 1].Record} and {ranges[i].Record} overlap";
                return null;
            }
        }

        return new ModelsLayout
        {
            _file0 = (byte[])file0.Clone(),
            Members = members,
            Records = records,
            Groups = groups,
            Table2Offset = table2Offset,
            Used = used,
            FirstEmpty = firstEmpty,
            LastRecord = lastRecord,
        };
    }

    public (ushort Start, byte Count, byte Flags) Record(int index)
    {
        if (index < 0 || index >= Records) throw new ArgumentOutOfRangeException(nameof(index));
        int offset = checked(index * ModelGroupInsertion.Table1Stride);
        return (BitConverter.ToUInt16(_file0, offset), _file0[offset + 2], _file0[offset + 3]);
    }

    public int GroupOf(int species, int form)
    {
        if (species <= 0) throw new ArgumentOutOfRangeException(nameof(species));
        if (form < 0) throw new ArgumentOutOfRangeException(nameof(form));
        (ushort start, byte count, byte flags) = Record(species - 1);
        int genderOffset = (flags & (byte)ModelGroupInsertion.FormFlags.GenderDifference) != 0 && form > 0 ? 1 : 0;
        if (form + genderOffset >= count) throw new ArgumentOutOfRangeException(nameof(form), $"species {species} has {count} model groups");
        return checked(start + form + genderOffset);
    }
}

public sealed class ModelsPlan
{
    public byte[] File0 = [];
    public List<AppendedMember> Appended = [];
    public int GroupsAdded;
    public int BaseMembers;
    public int BaseGroups;
}

public static class ModelsPlanner
{
    private const int PersonalFormStatsIndexOffset = 0x1C;
    private const int PersonalFormeCountOffset = 0x20;

    public static ModelsPlan Start(ModelsLayout l, byte[] file0)
    {
        if (l == null) throw new ArgumentNullException(nameof(l));
        if (file0 == null) throw new ArgumentNullException(nameof(file0));
        long expectedLength = (long)l.Table2Offset + (long)l.Groups * sizeof(ushort);
        if (expectedLength != file0.LongLength)
            throw new ArgumentException($"models file0 is {file0.LongLength} B; layout expects {expectedLength} B", nameof(file0));
        return new ModelsPlan
        {
            File0 = (byte[])file0.Clone(),
            BaseMembers = l.Members,
            BaseGroups = l.Groups,
        };
    }

    public static string? AddFormGroups(ModelsPlan p, ModelsLayout l, int species, int count, int templateGroup)
    {
        if (p == null || l == null) return "models plan or layout is missing";
        if (species <= 0 || species > l.Records) return $"species {species} has no models record";
        if (count <= 0) return "form count must be positive";
        int record = species - 1;
        if (!TryCurrentRecord(p, l, record, out (ushort Start, byte Count, byte Flags) current, out int groups, out string error)) return error;
        if (current.Count == 0) return $"species {species} has an empty models record";
        if ((long)current.Count + count > byte.MaxValue)
            return $"species {species} model group count {(long)current.Count + count} exceeds 255";
        if (templateGroup < 0 || templateGroup >= groups) return $"template model group {templateGroup} is outside 0..{groups - 1}";

        var sourceGroups = new List<int>(current.Count + count);
        for (int i = 0; i < current.Count; i++) sourceGroups.Add(current.Start + i);
        for (int i = 0; i < count; i++) sourceGroups.Add(templateGroup);
        byte flags = current.Flags < 0x05 ? checked((byte)(current.Flags + 0x04)) : current.Flags;
        return AppendGroups(p, l, record, checked(current.Count + count), flags, sourceGroups, null);
    }

    public static string? AddSpecies(ModelsPlan p, ModelsLayout l, int record, int groupCount,
        byte flags, int templateGroup, int recordShift)
    {
        if (p == null || l == null) return "models plan or layout is missing";
        if (record < 0 || record >= l.Records) return $"models record {record} is outside 0..{l.Records - 1}";
        if (groupCount <= 0 || groupCount > byte.MaxValue) return $"species model group count {groupCount} is outside 1..255";
        if (recordShift is not (0 or -1)) return $"model record shift {recordShift} must be 0 or -1";
        if (templateGroup < 0) return $"template model group {templateGroup} is negative";
        if (!TryCurrentRecord(p, l, record, out _, out int groups, out string error)) return error;
        if (templateGroup >= groups) return $"template model group {templateGroup} is outside 0..{groups - 1}";

        int targetRecord = record + recordShift;
        if (targetRecord < 0 || targetRecord >= l.Records) return $"new models record {targetRecord} is outside the table";
        if (!IsEmptyRecord(p.File0, record)) return $"models record {record} is already in use";
        if (recordShift < 0 && IsEmptyRecord(p.File0, targetRecord))
            return $"models record {targetRecord} is empty; there is no Egg record to move";

        var sourceGroups = new List<int>(groupCount);
        for (int i = 0; i < groupCount; i++) sourceGroups.Add(templateGroup);
        return AppendGroups(p, l, targetRecord, groupCount, flags, sourceGroups,
            recordShift < 0 ? (targetRecord, record) : null);
    }

    public static string? ResolveSpeciesTemplateGroup(ModelsLayout l, int cloneModelFromSlot,
        int recordShift, out int templateGroup)
    {
        templateGroup = -1;
        if (l == null) return "models layout is missing";
        if (cloneModelFromSlot <= 0) return $"clone model species id {cloneModelFromSlot} must be positive";
        if (recordShift is not (0 or -1)) return $"model record shift {recordShift} must be 0 or -1";

        // GroupOf accepts a one-based species id. Add one to preserve today's direct record-slot
        // read at shift 0; shift -1 opts into the species-1 record interpretation.
        int lookupSpecies = checked(cloneModelFromSlot + 1 + recordShift);
        try
        {
            templateGroup = l.GroupOf(lookupSpecies, 0);
            return null;
        }
        catch (ArgumentOutOfRangeException e)
        {
            return $"clone model species {cloneModelFromSlot} has no base group at shift {recordShift}: {e.Message}";
        }
    }

    public static string? FormEligibility(ModelsLayout l, SpeciesTables t, int species)
    {
        if (l == null || t == null) return "models or species tables are missing";
        if (species < 1 || species >= t.Personal.Length)
            return $"species {species} has no personal entry for models eligibility";
        if (species - 1 >= l.Records) return $"species {species} has no models record";
        byte[] personal = t.Personal[species];
        if (personal.Length <= PersonalFormeCountOffset) return $"species {species} has a short personal entry";
        int forms = personal[PersonalFormeCountOffset];
        int pointer = personal.Length >= PersonalFormStatsIndexOffset + sizeof(ushort)
            ? BitConverter.ToUInt16(personal, PersonalFormStatsIndexOffset)
            : 0;
        if (forms > 1 && pointer == 0)
            return $"species {species} is cosmetic-only (FormeCount {forms}, FormStatsIndex 0)";
        (ushort _, byte groups, byte flags) = l.Record(species - 1);
        int genderGroups = (flags & (byte)ModelGroupInsertion.FormFlags.GenderDifference) != 0 ? 1 : 0;
        int expected = forms + genderGroups;
        if (groups != expected)
            return $"species {species} has groups {groups}, forms {forms}, flag {flags}; expected {expected} groups";
        return null;
    }

    private static string? AppendGroups(ModelsPlan p, ModelsLayout l, int targetRecord, int targetCount,
        byte targetFlags, List<int> sourceGroups, (int EggRecord, int NewEggRecord)? moveRecord)
    {
        if (!TryCurrentRecord(p, l, targetRecord, out _, out int groups, out string error))
        {
            // AddSpecies with recordShift -1 targets the occupied Egg record; that is the one exception.
            if (moveRecord == null || moveRecord.Value.EggRecord != targetRecord) return error;
            if (!TryCurrentShape(p, l, out groups, out error)) return error;
        }
        long newGroupTotal = (long)groups + sourceGroups.Count;
        if (groups > ushort.MaxValue || newGroupTotal > ushort.MaxValue + 1L)
            return $"appended model groups would exceed the 16-bit group index limit ({newGroupTotal})";
        long expectedFile0Length = (long)l.Table2Offset + (long)groups * sizeof(ushort);
        if (expectedFile0Length != p.File0.LongLength)
            return $"models file0 is {p.File0.LongLength} B; current layout expects {expectedFile0Length} B";
        if (p.Appended.Count != (long)p.GroupsAdded * ModelGroupInsertion.FilesPerGroup)
            return "models appended-member list does not match GroupsAdded";
        if (sourceGroups.Count == 0) return "at least one model group must be appended";
        if (targetRecord < 0 || (long)targetRecord * ModelGroupInsertion.Table1Stride + ModelGroupInsertion.Table1Stride > p.File0.LongLength)
            return $"target models record {targetRecord} is outside file0";

        var sourceMembers = new List<AppendedMember>(checked(sourceGroups.Count * ModelGroupInsertion.FilesPerGroup));
        var descriptors = new byte[sourceGroups.Count * sizeof(ushort)];
        for (int i = 0; i < sourceGroups.Count; i++)
        {
            int sourceGroup = sourceGroups[i];
            if (sourceGroup < 0 || sourceGroup >= groups) return $"source model group {sourceGroup} is outside 0..{groups - 1}";
            long descriptorOffset = (long)l.Table2Offset + (long)sourceGroup * sizeof(ushort);
            if (descriptorOffset + sizeof(ushort) > p.File0.LongLength)
                return $"table-2 descriptor for model group {sourceGroup} is outside file0";
            Array.Copy(p.File0, (int)descriptorOffset, descriptors, i * sizeof(ushort), sizeof(ushort));
            for (int file = 0; file < ModelGroupInsertion.FilesPerGroup; file++)
            {
                long sourceMember = 1L + (long)sourceGroup * ModelGroupInsertion.FilesPerGroup + file;
                long currentMembers = (long)l.Members + (long)p.GroupsAdded * ModelGroupInsertion.FilesPerGroup;
                if (sourceMember >= currentMembers) return $"model group {sourceGroup} member {file} is outside {currentMembers} members";
                if (!TryResolveMember(p, sourceMember, out AppendedMember member))
                    return $"model group {sourceGroup} member {file} cannot be resolved to a base member";
                sourceMembers.Add(member);
            }
        }

        long addedGroupsTotal = (long)p.GroupsAdded + sourceGroups.Count;
        long newFile0Length = p.File0.LongLength + (long)sourceGroups.Count * sizeof(ushort);
        if (addedGroupsTotal > int.MaxValue / ModelGroupInsertion.FilesPerGroup || newFile0Length > int.MaxValue)
            return "models plan is too large to index";
        int newStart = groups;
        byte[] newFile0 = new byte[(int)newFile0Length];
        Array.Copy(p.File0, newFile0, p.File0.Length);
        Array.Copy(descriptors, 0, newFile0, p.File0.Length, descriptors.Length);
        if (moveRecord is { } move)
            Array.Copy(p.File0, move.EggRecord * ModelGroupInsertion.Table1Stride,
                newFile0, move.NewEggRecord * ModelGroupInsertion.Table1Stride, ModelGroupInsertion.Table1Stride);

        int recordOffset = checked(targetRecord * ModelGroupInsertion.Table1Stride);
        BitConverter.TryWriteBytes(newFile0.AsSpan(recordOffset, sizeof(ushort)), checked((ushort)newStart));
        newFile0[recordOffset + 2] = checked((byte)targetCount);
        newFile0[recordOffset + 3] = targetFlags;

        p.File0 = newFile0;
        p.Appended.AddRange(sourceMembers);
        p.GroupsAdded = checked((int)addedGroupsTotal);
        return null;
    }

    private static bool TryCurrentRecord(ModelsPlan p, ModelsLayout l, int record,
        out (ushort Start, byte Count, byte Flags) current, out int groups, out string error)
    {
        current = default;
        if (!TryCurrentShape(p, l, out groups, out error)) return false;
        if (record < 0 || record >= l.Records)
        {
            error = $"models record {record} is outside 0..{l.Records - 1}";
            return false;
        }
        int offset = checked(record * ModelGroupInsertion.Table1Stride);
        current = (BitConverter.ToUInt16(p.File0, offset), p.File0[offset + 2], p.File0[offset + 3]);
        error = "";
        return true;
    }

    private static bool TryCurrentShape(ModelsPlan p, ModelsLayout l, out int groups, out string error)
    {
        long currentGroups = (long)l.Groups + p.GroupsAdded;
        long expectedLength = (long)l.Table2Offset + currentGroups * sizeof(ushort);
        if (currentGroups > int.MaxValue || expectedLength != p.File0.LongLength
            || p.BaseGroups != l.Groups || p.BaseMembers != l.Members)
        {
            groups = 0;
            error = "models plan does not match its starting layout";
            return false;
        }
        groups = (int)currentGroups;
        error = "";
        return true;
    }

    private static bool TryResolveMember(ModelsPlan p, long memberIndex, out AppendedMember member)
    {
        if (memberIndex < p.BaseMembers)
        {
            member = new AppendedMember(checked((int)memberIndex), null);
            return true;
        }
        long appendedIndex = memberIndex - p.BaseMembers;
        if (appendedIndex < 0 || appendedIndex >= p.Appended.Count)
        {
            member = default;
            return false;
        }
        member = p.Appended[(int)appendedIndex];
        return true;
    }

    private static bool IsEmptyRecord(byte[] file0, int record)
    {
        int offset = checked(record * ModelGroupInsertion.Table1Stride);
        return file0[offset] == 0 && file0[offset + 1] == 0 && file0[offset + 2] == 0 && file0[offset + 3] == 0;
    }
}
