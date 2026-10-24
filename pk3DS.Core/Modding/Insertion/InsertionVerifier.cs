#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;

namespace pk3DS.Core.Modding.Insertion;

public static class InsertionVerifier
{
    private const int PersonalFormStatsIndexOffset = 0x1C;
    private const int PersonalFormeCountOffset = 0x20;
    private const int EggMoveFormTableIndexOffset = 0;
    private const int EggMoveHeaderLength = 4;

    public static List<string> Census(SpeciesTables t, int maxSpecies)
    {
        int familyCount = 0;
        int alternateCount = 0;
        int cosmeticCount = 0;
        for (int species = 1; species <= maxSpecies && species < t.Personal.Length; species++)
        {
            byte[] entry = t.Personal[species];
            if (entry.Length <= PersonalFormeCountOffset) continue;
            int count = entry[PersonalFormeCountOffset];
            int pointer = ReadPointer(entry);
            if (count <= 1) continue;
            if (pointer > 0)
            {
                familyCount++;
                alternateCount += count - 1;
            }
            else
            {
                cosmeticCount++;
            }
        }

        RomKind kind = maxSpecies >= 1025 ? RomKind.Expansion : maxSpecies >= 800 ? RomKind.Vanilla : RomKind.Unknown;
        int ceiling = RomLayout.PersonalCeiling(null, kind);
        int evolutionBaseCount = t.EvolutionBase?.Length ?? 0;
        return
        [
            $"census personal: {t.Personal.Length} entries, max species {maxSpecies}",
            $"census forms: {familyCount} families, {alternateCount} alternate entries, {cosmeticCount} cosmetic-only species",
            $"census parallel: personal {t.Personal.Length}, levelup {t.Levelup.Length}, evolution {t.Evolution.Length}, megaevo {t.Megaevo.Length}",
            $"census eggmove: {t.EggMove.Length} entries",
            $"census evolutionbase: {evolutionBaseCount} entries, blob {t.BuildEvolutionBaseBlob().LongLength} B",
            $"census ceiling: {ceiling} entries, {Math.Max(0, ceiling - t.Personal.Length)} remaining",
        ];
    }

    public static List<string> Check(SpeciesTables before, SpeciesTables after,
        IReadOnlyList<(int Species, int Count, int Template)> forms, int maxSpecies,
        int speciesCount = 0, int[]? lowestEvolution = null)
    {
        var violations = new List<string>();
        if (before == null || after == null)
        {
            violations.Add("tables are missing");
            return violations;
        }

        CheckParallelCounts(after, violations);
        CheckMaster(after, violations);
        CheckFamilies(after, maxSpecies, violations);

        ExpectedInsertions expected = BuildExpected(before, forms, maxSpecies, speciesCount, violations);

        CheckEggMove(before, after, expected, maxSpecies, violations);
        CheckOldPersonalRows(before, after, expected, violations);
        CheckOldParallelRows(before.Levelup, after.Levelup, expected.OldToNew, "levelup", violations);
        CheckOldParallelRows(before.Evolution, after.Evolution, expected.OldToNew, "evolution", violations);
        CheckOldMegaevoRows(before.Megaevo, after.Megaevo, expected.OldToNew, violations);
        CheckInsertedRows(after, expected.Insertions, violations);
        CheckSpeciesInsertionRows(before, after, expected, violations);
        if (expected.SpeciesCount > 0)
        {
            CheckSpeciesEggMove(before, after, maxSpecies, expected.SpeciesCount, violations);
            CheckEvolutionBase(before, after, maxSpecies, speciesCount, lowestEvolution, violations);
        }
        return violations;
    }

    public static List<string> CheckModels(ModelsLayout beforeLayout, byte[] beforeFile0, ModelsPlan beforePlan,
        ModelsPlan afterPlan, int targetRecord, int targetCount, byte targetFlags,
        IReadOnlyList<int> sourceGroups, int? movedEggRecord = null)
    {
        var violations = new List<string>();
        if (beforeLayout == null || beforeFile0 == null || beforePlan == null || afterPlan == null || sourceGroups == null)
        {
            violations.Add("C12 models verification input is missing");
            return violations;
        }
        if (beforeLayout.Members != (long)beforePlan.BaseMembers
                + (long)beforePlan.GroupsAdded * ModelGroupInsertion.FilesPerGroup
            || beforePlan.Appended.Count != (long)beforePlan.GroupsAdded * ModelGroupInsertion.FilesPerGroup)
        {
            violations.Add("C12 baseline model members do not match its appended groups");
            return violations;
        }

        int addedGroups = sourceGroups.Count;
        int groupDelta = afterPlan.GroupsAdded - beforePlan.GroupsAdded;
        if (groupDelta != addedGroups)
            violations.Add($"C12 GroupsAdded increased by {groupDelta}, expected {addedGroups}");
        long expectedFile0Length = beforeFile0.LongLength + (long)addedGroups * sizeof(ushort);
        if (afterPlan.File0.LongLength != expectedFile0Length)
            violations.Add($"C12 file0 is {afterPlan.File0.LongLength} B, expected {expectedFile0Length} B");
        long expectedAppendCount = (long)beforePlan.Appended.Count + (long)addedGroups * ModelGroupInsertion.FilesPerGroup;
        if (afterPlan.Appended.Count != expectedAppendCount)
            violations.Add($"C12 appended members count {afterPlan.Appended.Count}, expected {expectedAppendCount}");

        long afterMembersLong = (long)beforeLayout.Members + (long)addedGroups * ModelGroupInsertion.FilesPerGroup;
        if (afterMembersLong > int.MaxValue)
        {
            violations.Add($"C12 model member count {afterMembersLong} is too large to verify");
            return violations;
        }
        ModelsLayout? afterLayout = ModelsLayout.Parse(afterPlan.File0, (int)afterMembersLong, out string parseError);
        if (afterLayout == null)
        {
            violations.Add("C12 resulting models layout is invalid: " + parseError);
            return violations;
        }
        if (afterLayout.Groups != beforeLayout.Groups + addedGroups)
            violations.Add($"C12 group count {afterLayout.Groups}, expected {beforeLayout.Groups + addedGroups}");

        var allowedRecords = new HashSet<int> { targetRecord };
        if (movedEggRecord is int movedRecord) allowedRecords.Add(movedRecord);
        for (int record = 0; record < beforeLayout.Records; record++)
        {
            if (allowedRecords.Contains(record)) continue;
            int offset = checked(record * ModelGroupInsertion.Table1Stride);
            if (!beforeFile0.AsSpan(offset, ModelGroupInsertion.Table1Stride)
                    .SequenceEqual(afterPlan.File0.AsSpan(offset, ModelGroupInsertion.Table1Stride)))
                violations.Add($"C12 pre-existing models record {record} changed");
        }
        if (movedEggRecord is int movedRecordIndex)
        {
            int sourceRecord = targetRecord;
            if (sourceRecord < 0 || sourceRecord >= beforeLayout.Records
                || movedRecordIndex < 0 || movedRecordIndex >= afterLayout.Records)
            {
                violations.Add("C12 moved Egg record is outside the models table");
            }
            else
            {
                int sourceOffset = checked(sourceRecord * ModelGroupInsertion.Table1Stride);
                int destinationOffset = checked(movedRecordIndex * ModelGroupInsertion.Table1Stride);
                if (!beforeFile0.AsSpan(sourceOffset, ModelGroupInsertion.Table1Stride)
                        .SequenceEqual(afterPlan.File0.AsSpan(destinationOffset, ModelGroupInsertion.Table1Stride)))
                    violations.Add($"C12 Egg record {sourceRecord} did not move to {movedRecordIndex}");
            }
        }

        if (targetRecord < 0 || targetRecord >= afterLayout.Records)
        {
            violations.Add($"C12 target models record {targetRecord} is outside the resulting table");
        }
        else
        {
            (ushort start, byte count, byte flags) = afterLayout.Record(targetRecord);
            if (start != beforeLayout.Groups || count != targetCount || flags != targetFlags)
                violations.Add($"C12 target record {targetRecord} is ({start},{count},{flags}), expected ({beforeLayout.Groups},{targetCount},{targetFlags})");
        }

        long oldTable2Length = (long)beforeLayout.Groups * sizeof(ushort);
        if (beforeLayout.Table2Offset + oldTable2Length > beforeFile0.LongLength
            || afterLayout.Table2Offset + oldTable2Length > afterPlan.File0.LongLength)
        {
            violations.Add("C12 old table-2 range is outside file0");
        }
        else if (!beforeFile0.AsSpan(beforeLayout.Table2Offset, checked((int)oldTable2Length))
                     .SequenceEqual(afterPlan.File0.AsSpan(afterLayout.Table2Offset, checked((int)oldTable2Length))))
        {
            violations.Add("C12 table-2 bytes for pre-existing groups changed");
        }

        int baseMembers = beforePlan.BaseMembers;
        for (int i = 0; i < addedGroups; i++)
        {
            int sourceGroup = sourceGroups[i];
            if (sourceGroup < 0 || sourceGroup >= beforeLayout.Groups)
            {
                violations.Add($"C12 source model group {sourceGroup} is outside 0..{beforeLayout.Groups - 1}");
                continue;
            }
            long sourceOffset = (long)beforeLayout.Table2Offset + (long)sourceGroup * sizeof(ushort);
            long targetOffset = (long)afterLayout.Table2Offset + (long)(beforeLayout.Groups + i) * sizeof(ushort);
            if (sourceOffset + sizeof(ushort) > beforeFile0.LongLength || targetOffset + sizeof(ushort) > afterPlan.File0.LongLength
                || !beforeFile0.AsSpan((int)sourceOffset, sizeof(ushort))
                    .SequenceEqual(afterPlan.File0.AsSpan((int)targetOffset, sizeof(ushort))))
                violations.Add($"C12 appended group {beforeLayout.Groups + i} table-2 entry differs from source group {sourceGroup}");

            for (int file = 0; file < ModelGroupInsertion.FilesPerGroup; file++)
            {
                long sourceMember = 1L + (long)sourceGroup * ModelGroupInsertion.FilesPerGroup + file;
                AppendedMember expectedMember;
                if (sourceMember < baseMembers)
                {
                    expectedMember = new AppendedMember(checked((int)sourceMember), null);
                }
                else
                {
                    long sourceAppendIndex = sourceMember - baseMembers;
                    if (sourceAppendIndex < 0 || sourceAppendIndex >= beforePlan.Appended.Count)
                    {
                        violations.Add($"C12 source group {sourceGroup} member {file} cannot be resolved");
                        continue;
                    }
                    expectedMember = beforePlan.Appended[(int)sourceAppendIndex];
                }

                int appendedIndex = checked(beforePlan.Appended.Count + i * ModelGroupInsertion.FilesPerGroup + file);
                if (appendedIndex >= afterPlan.Appended.Count || !SameMember(expectedMember, afterPlan.Appended[appendedIndex]))
                    violations.Add($"C12 appended group {beforeLayout.Groups + i} member {file} differs from source group {sourceGroup}");
            }
        }

        return violations;
    }

    private static bool SameMember(AppendedMember left, AppendedMember right)
    {
        if (left.FromBase != right.FromBase) return false;
        if (left.Bytes == null || right.Bytes == null) return left.Bytes == null && right.Bytes == null;
        return left.Bytes.AsSpan().SequenceEqual(right.Bytes);
    }

    private static void CheckParallelCounts(SpeciesTables tables, List<string> violations)
    {
        int personalCount = tables.Personal.Length;
        if (tables.Levelup.Length != personalCount)
            violations.Add($"C1 levelup row count {tables.Levelup.Length} does not match personal count {personalCount}");
        if (tables.Evolution.Length != personalCount)
            violations.Add($"C1 evolution row count {tables.Evolution.Length} does not match personal count {personalCount}");
        if (tables.Megaevo.Length != personalCount)
            violations.Add($"C1 megaevo row count {tables.Megaevo.Length} does not match personal count {personalCount}");
    }

    private static void CheckMaster(SpeciesTables tables, List<string> violations)
    {
        byte[] rebuilt = tables.BuildMaster();
        long expectedLength = tables.PersonalMasterTail.LongLength;
        foreach (byte[] entry in tables.Personal) expectedLength = checked(expectedLength + entry.LongLength);
        if (rebuilt.LongLength != expectedLength)
        {
            violations.Add($"C2 personal master rebuild length {rebuilt.LongLength} does not match entries plus tail length {expectedLength}");
            return;
        }

        if (expectedLength > int.MaxValue)
        {
            violations.Add($"C2 personal master rebuild is too large to verify: {expectedLength} B");
            return;
        }

        byte[] expected = new byte[(int)expectedLength];
        long offset = 0;
        foreach (byte[] entry in tables.Personal)
        {
            Array.Copy(entry, 0L, expected, offset, entry.LongLength);
            offset += entry.LongLength;
        }
        Array.Copy(tables.PersonalMasterTail, 0L, expected, offset, tables.PersonalMasterTail.LongLength);
        if (!rebuilt.AsSpan().SequenceEqual(expected))
            violations.Add("C2 personal master rebuild does not equal personal entries plus the preserved tail");
    }

    private static void CheckFamilies(SpeciesTables tables, int maxSpecies, List<string> violations)
    {
        var families = new List<(int Start, int End, int Species)>();
        for (int species = 1; species <= maxSpecies && species < tables.Personal.Length; species++)
        {
            byte[] entry = tables.Personal[species];
            if (entry.Length <= PersonalFormeCountOffset)
            {
                violations.Add($"FormStatsIndex personal entry {species} is too short for family metadata");
                continue;
            }

            int count = entry[PersonalFormeCountOffset];
            int pointer = ReadPointer(entry);
            if (count <= 1 || pointer == 0) continue;

            long endLong = (long)pointer + count - 2;
            if (pointer < maxSpecies + 1L)
                violations.Add($"FormStatsIndex family for species {species} starts at {pointer}, before alt entries {maxSpecies + 1}");
            if (endLong >= tables.Personal.Length)
            {
                violations.Add($"FormStatsIndex family for species {species} ends at {endLong}, beyond {tables.Personal.Length} personal entries");
                continue;
            }

            int end = (int)endLong;
            families.Add((pointer, end, species));
            for (int index = pointer; index <= end; index++)
            {
                byte[] form = tables.Personal[index];
                if (form.Length <= PersonalFormeCountOffset)
                {
                    violations.Add($"FormStatsIndex family for species {species} has a short entry at {index}");
                    continue;
                }
                if (ReadPointer(form) != pointer || form[PersonalFormeCountOffset] != count)
                    violations.Add($"FormStatsIndex family for species {species} has an inconsistent entry at {index}");
            }
        }

        families.Sort((left, right) => left.Start.CompareTo(right.Start));
        for (int i = 1; i < families.Count; i++)
        {
            if (families[i].Start <= families[i - 1].End)
                violations.Add($"FormStatsIndex families for species {families[i - 1].Species} and {families[i].Species} overlap");
        }
    }

    // Independent oracle: it tracks only FormStatsIndex and FormeCount per entry plus the row bytes, and never calls
    // FormPlanner, so a planner bug cannot make the expectation agree with it.
    private static ExpectedInsertions BuildExpected(SpeciesTables before, IReadOnlyList<(int Species, int Count, int Template)> forms,
        int maxSpecies, int speciesCount, List<string> violations)
    {
        var pointers = new List<int>();
        var counts = new List<int>();
        foreach (byte[] entry in before.Personal)
        {
            bool full = entry.Length > PersonalFormeCountOffset;
            pointers.Add(full ? ReadPointer(entry) : 0);
            counts.Add(full ? entry[PersonalFormeCountOffset] : 0);
        }
        List<byte[]> personalRows = [.. before.Personal];
        List<byte[]> evolution = [.. before.Evolution];
        List<byte[]> levelup = [.. before.Levelup];
        List<byte[]> eggMove = before.EggMove.Select(row => (byte[])row.Clone()).ToList();
        int[] oldToNew = Enumerable.Range(0, before.Personal.Length).ToArray();
        var insertions = new List<ExpectedInsertion>();
        var eggMoveRelocations = new List<ExpectedEggMoveRelocation>();
        int appendedEggMoveRows = 0;
        int speciesInsertAt = -1;

        if (speciesCount < 0 || speciesCount > ushort.MaxValue)
        {
            violations.Add($"C10 species count {speciesCount} is outside 0..{ushort.MaxValue}");
            speciesCount = 0;
        }
        if (speciesCount > 0)
        {
            long insertAtLong = (long)maxSpecies + 1;
            if (insertAtLong > pointers.Count || insertAtLong > int.MaxValue)
            {
                violations.Add($"C10 species insertion index {insertAtLong} is outside the personal table");
                speciesCount = 0;
            }
            else
            {
                speciesInsertAt = (int)insertAtLong;
                for (int i = 0; i < pointers.Count; i++)
                    if (pointers[i] >= speciesInsertAt) pointers[i] = checked(pointers[i] + speciesCount);
                for (int i = 0; i < oldToNew.Length; i++)
                    if (oldToNew[i] >= speciesInsertAt) oldToNew[i] = checked(oldToNew[i] + speciesCount);
                pointers.InsertRange(speciesInsertAt, Enumerable.Repeat(0, speciesCount));
                counts.InsertRange(speciesInsertAt, Enumerable.Repeat(0, speciesCount));
                if (speciesInsertAt <= evolution.Count)
                    evolution.InsertRange(speciesInsertAt, CreateZeroRows(evolution, speciesCount));
                else
                    violations.Add($"C10 species insertion index {speciesInsertAt} is outside the evolution table");
                if (speciesInsertAt <= levelup.Count)
                    levelup.InsertRange(speciesInsertAt, CreateZeroRows(levelup, speciesCount));
                else
                    violations.Add($"C10 species insertion index {speciesInsertAt} is outside the levelup table");
                eggMove = PlanExpectedSpeciesEggMove(eggMove, speciesInsertAt, speciesCount, violations);
            }
        }

        foreach ((int species, int count, int template) in forms)
        {
            if (species < 1 || species >= pointers.Count || template < 0 || template >= pointers.Count || count <= 0)
            {
                violations.Add($"form request could not be planned: species {species}, count {count}, template {template}");
                continue;
            }
            int oldCount = counts[species];
            if (oldCount + (long)count > byte.MaxValue)
            {
                violations.Add($"C7 FormeCount exceeds 255: species {species}");
                continue;
            }

            PlanExpectedEggMove(eggMove, pointers, counts, species, count, template, maxSpecies,
                eggMoveRelocations, ref appendedEggMoveRows, violations);

            int index = ExpectedIndex(pointers, counts, species, maxSpecies);
            int family = oldCount > 1 && pointers[species] > 0 ? pointers[species] : index;
            int newCount = oldCount + count;
            byte[] evolutionTemplate = GetInsertedTemplate(evolution, template);
            byte[] levelupTemplate = GetInsertedTemplate(levelup, template);
            byte[] personalTemplate = template < personalRows.Count ? personalRows[template] : [];

            foreach (ExpectedInsertion earlier in insertions)
            {
                if (earlier.Start >= index) earlier.Start += count;
                if (earlier.Species != species && earlier.FamilyPointer >= index) earlier.FamilyPointer += count;
            }
            for (int i = 0; i < oldToNew.Length; i++) if (oldToNew[i] >= index) oldToNew[i] += count;
            for (int i = 0; i < count; i++)
            {
                personalRows.Insert(index, personalTemplate);
                pointers.Insert(index, family);
                counts.Insert(index, newCount);
                if (index <= evolution.Count) evolution.Insert(index, evolutionTemplate);
                if (index <= levelup.Count) levelup.Insert(index, levelupTemplate);
            }
            for (int i = 1; i < pointers.Count; i++)
            {
                if (i == species || (i >= index && i < index + count)) continue;
                if (pointers[i] != 0 && pointers[i] >= index) pointers[i] += count;
            }
            for (int k = 0; k < newCount - 1; k++)
            {
                pointers[family + k] = family;
                counts[family + k] = newCount;
            }
            pointers[species] = family;
            counts[species] = newCount;
            int megaevoRowLength = before.Megaevo.Length == 0 ? 0 : before.Megaevo[0].Length;
            insertions.Add(new ExpectedInsertion(index, count, species, family, newCount,
                personalTemplate, evolutionTemplate, levelupTemplate, megaevoRowLength));
        }

        return new ExpectedInsertions(pointers.Count, oldToNew, insertions, eggMove.ToArray(),
            appendedEggMoveRows, eggMoveRelocations, speciesInsertAt, speciesCount);
    }

    private static List<byte[]> CreateZeroRows(List<byte[]> rows, int count)
    {
        int length = rows.Count == 0 ? 0 : rows[0].Length;
        var inserted = new List<byte[]>(count);
        for (int i = 0; i < count; i++) inserted.Add(new byte[length]);
        return inserted;
    }

    private static List<byte[]> PlanExpectedSpeciesEggMove(List<byte[]> rows, int insertAt, int count, List<string> violations)
    {
        if (insertAt > rows.Count)
        {
            violations.Add($"C10 species insertion index {insertAt} is outside the eggmove table");
            return rows;
        }
        int rowLength = rows.Count == 0 ? 0 : rows[0].Length;
        if (rowLength < EggMoveHeaderLength)
        {
            violations.Add($"C10 eggmove rows are shorter than {EggMoveHeaderLength} bytes");
            return rows;
        }

        var result = new List<byte[]>(checked(rows.Count + count));
        for (int i = 0; i < insertAt; i++) result.Add((byte[])rows[i].Clone());
        for (int i = 0; i < count; i++) result.Add(new byte[rowLength]);
        for (int i = insertAt; i < rows.Count; i++) result.Add((byte[])rows[i].Clone());

        for (int i = 0; i < result.Count; i++)
        {
            if (i >= insertAt && i < insertAt + count) continue;
            byte[] row = result[i];
            if (row.Length < sizeof(ushort)) continue;
            int value = ReadEggMoveIndex(row);
            if (value < insertAt) continue;
            long shifted = (long)value + count;
            if (shifted > ushort.MaxValue)
            {
                violations.Add($"C10 eggmove FormTableIndex {shifted} exceeds the 16-bit limit");
                continue;
            }
            WriteEggMoveIndex(row, (int)shifted);
        }
        for (int i = 0; i < count; i++)
        {
            byte[] row = result[insertAt + i];
            WriteEggMoveIndex(row, checked(insertAt + i));
            BitConverter.TryWriteBytes(row.AsSpan(sizeof(ushort), sizeof(ushort)), (ushort)0);
        }
        return result;
    }

    private static void PlanExpectedEggMove(List<byte[]> eggMove, List<int> pointers, List<int> counts,
        int species, int count, int templateEntry, int maxSpecies, List<ExpectedEggMoveRelocation> relocations,
        ref int appendedRows, List<string> violations)
    {
        if (species >= eggMove.Count || eggMove[species].Length < sizeof(ushort)) return;

        int oldBlockStart = ReadEggMoveIndex(eggMove[species]);
        if (oldBlockStart == species) return;
        int formCount = counts[species];
        if (formCount <= 1 || oldBlockStart < maxSpecies + 1) return;
        long oldBlockEnd = (long)oldBlockStart + formCount - 2;
        if (oldBlockEnd >= eggMove.Count)
        {
            violations.Add($"C9 eggmove block for species {species} ends at {oldBlockEnd}, past {eggMove.Count} rows");
            return;
        }

        int newBlockStart = eggMove.Count;
        if (newBlockStart > ushort.MaxValue)
        {
            violations.Add($"C9 eggmove block for species {species} would start at {newBlockStart}, past the 16-bit FormTableIndex limit");
            return;
        }

        int templateRowIndex = FindExpectedEggMoveTemplateRow(eggMove, pointers, counts, templateEntry, maxSpecies, out string templateError);
        if (templateRowIndex < 0)
        {
            violations.Add($"C9 {templateError}");
            return;
        }

        int rowCount = formCount - 1;
        var oldRows = new byte[rowCount][];
        for (int i = 0; i < rowCount; i++)
        {
            byte[] row = eggMove[oldBlockStart + i];
            if (row.Length < EggMoveHeaderLength)
            {
                violations.Add($"C9 eggmove row {oldBlockStart + i} is shorter than {EggMoveHeaderLength} bytes");
                return;
            }
            oldRows[i] = (byte[])row.Clone();
        }
        byte[] templateRow = (byte[])eggMove[templateRowIndex].Clone();
        if (templateRow.Length < EggMoveHeaderLength)
        {
            violations.Add($"C9 template eggmove row {templateRowIndex} is shorter than {EggMoveHeaderLength} bytes");
            return;
        }

        int delta = checked(newBlockStart - oldBlockStart);
        var movedRows = new byte[rowCount][];
        for (int i = 0; i < rowCount; i++)
        {
            movedRows[i] = (byte[])oldRows[i].Clone();
            long movedIndex = (long)ReadEggMoveIndex(oldRows[i]) + delta;
            if (movedIndex > ushort.MaxValue)
            {
                violations.Add($"C9 relocated eggmove row {oldBlockStart + i} has FormTableIndex {movedIndex}, past the 16-bit limit");
                return;
            }
            WriteEggMoveIndex(movedRows[i], (int)movedIndex);
        }

        byte[] updatedSpeciesRow = (byte[])eggMove[species].Clone();
        WriteEggMoveIndex(updatedSpeciesRow, newBlockStart);
        eggMove[species] = updatedSpeciesRow;
        eggMove.AddRange(movedRows);
        for (int i = 0; i < count; i++)
        {
            byte[] inserted = (byte[])templateRow.Clone();
            WriteEggMoveIndex(inserted, newBlockStart);
            eggMove.Add(inserted);
        }

        appendedRows = checked(appendedRows + rowCount + count);
        relocations.Add(new ExpectedEggMoveRelocation(oldBlockStart, newBlockStart, delta, oldRows));
    }

    private static int FindExpectedEggMoveTemplateRow(List<byte[]> eggMove, List<int> pointers,
        List<int> counts, int templateEntry, int maxSpecies, out string error)
    {
        error = "";
        int owner = templateEntry;
        int form = 0;
        if (templateEntry > maxSpecies)
        {
            owner = -1;
            for (int species = 1; species <= maxSpecies && species < pointers.Count; species++)
            {
                int count = counts[species];
                int pointer = pointers[species];
                if (count <= 1 || pointer <= 0 || templateEntry < pointer || (long)templateEntry >= (long)pointer + count - 1)
                    continue;
                owner = species;
                form = templateEntry - pointer + 1;
                break;
            }
            if (owner < 0)
            {
                error = $"template personal entry {templateEntry} has no owning species form block";
                return -1;
            }
        }

        if (owner < 0 || owner >= eggMove.Count || eggMove[owner].Length < sizeof(ushort))
        {
            error = $"template owner {owner} has no eggmove row";
            return -1;
        }
        int formTableIndex = ReadEggMoveIndex(eggMove[owner]);
        long rowIndex = form > 0 && formTableIndex >= maxSpecies + 1
            ? (long)formTableIndex + form - 1L
            : owner;
        if (rowIndex < 0 || rowIndex >= eggMove.Count)
        {
            error = $"template eggmove row {rowIndex} is outside the {eggMove.Count}-row table";
            return -1;
        }
        return (int)rowIndex;
    }

    private static int ExpectedIndex(List<int> pointers, List<int> counts, int species, int maxSpecies)
    {
        if (counts[species] > 1 && pointers[species] > 0) return pointers[species] + counts[species] - 1;
        for (int later = species + 1; later <= maxSpecies && later < pointers.Count; later++)
            if (pointers[later] > 0 && pointers[later] < pointers.Count) return pointers[later];
        return pointers.Count;
    }

    private static byte[] GetInsertedTemplate(List<byte[]> rows, int template)
    {
        if (template < rows.Count) return (byte[])rows[template].Clone();
        return new byte[rows.Count == 0 ? 0 : rows[0].Length];
    }

    private static ExpectedInsertion? FinalFor(List<ExpectedInsertion> insertions, int species)
        => insertions.LastOrDefault(i => i.Species == species);

    private static void CheckOldPersonalRows(SpeciesTables before, SpeciesTables after, ExpectedInsertions expected, List<string> violations)
    {
        if (after.Personal.Length != expected.PersonalCount)
            violations.Add($"C4 personal count {after.Personal.Length} does not match planned count {expected.PersonalCount}");

        for (int oldIndex = 0; oldIndex < before.Personal.Length; oldIndex++)
        {
            int newIndex = expected.OldToNew[oldIndex];
            if (newIndex < 0 || newIndex >= after.Personal.Length)
            {
                violations.Add($"C4 old personal entry {oldIndex} has no mapped entry at {newIndex}");
                continue;
            }

            byte[] oldEntry = before.Personal[oldIndex];
            byte[] actual = after.Personal[newIndex];
            if (actual.Length != oldEntry.Length)
            {
                violations.Add($"C4 personal entry {oldIndex} changed length at mapped entry {newIndex}");
                continue;
            }

            ExpectedInsertion? target = TargetFamily(before, oldIndex, expected.Insertions);
            // A pointer must follow its family through the row map. Only a no-form species gaining its first
            // block has no old pointer to map, so its new one comes from the insertion rule (C3 checks that block).
            int oldPointer = ReadPointer(oldEntry);
            int expectedPointer = oldPointer == 0 ? target?.FamilyPointer ?? 0
                : oldPointer < expected.OldToNew.Length ? expected.OldToNew[oldPointer] : -1;
            if (ReadPointer(actual) != expectedPointer)
                violations.Add($"C4 FormStatsIndex for old personal entry {oldIndex} is {ReadPointer(actual)}, not the mapped old family start {expectedPointer}");

            for (int offset = 0; offset < oldEntry.Length; offset++)
            {
                if (offset == PersonalFormStatsIndexOffset || offset == PersonalFormStatsIndexOffset + 1) continue;
                if (target != null && offset == PersonalFormeCountOffset)
                {
                    if (actual[offset] != target.NewCount)
                        violations.Add($"C4 FormeCount for target family entry {oldIndex} is {actual[offset]}, expected {target.NewCount}");
                }
                else if (actual[offset] != oldEntry[offset])
                {
                    violations.Add($"C4 old personal entry {oldIndex} changed byte 0x{offset:X} outside form metadata");
                    break;
                }
            }
        }
    }

    private static ExpectedInsertion? TargetFamily(SpeciesTables before, int oldIndex, List<ExpectedInsertion> insertions)
    {
        foreach (ExpectedInsertion insertion in insertions.AsEnumerable().Reverse())
        {
            if (oldIndex == insertion.Species) return insertion;
            byte[] baseEntry = before.Personal[insertion.Species];
            if (baseEntry.Length <= PersonalFormeCountOffset) continue;
            int count = baseEntry[PersonalFormeCountOffset];
            int pointer = ReadPointer(baseEntry);
            if (count > 1 && pointer > 0 && oldIndex >= pointer && (long)oldIndex < (long)pointer + count - 1)
                return insertion;
        }
        return null;
    }

    private static void CheckOldParallelRows(byte[][] before, byte[][] after, int[] oldToNew, string tableName, List<string> violations)
    {
        for (int oldIndex = 0; oldIndex < before.Length; oldIndex++)
        {
            if (oldIndex >= oldToNew.Length)
            {
                violations.Add($"C5 {tableName} old entry {oldIndex} has no personal index mapping");
                continue;
            }
            int newIndex = oldToNew[oldIndex];
            if (newIndex >= after.Length)
            {
                violations.Add($"C5 {tableName} old entry {oldIndex} has no mapped row at {newIndex}");
                continue;
            }
            if (!before[oldIndex].AsSpan().SequenceEqual(after[newIndex]))
                violations.Add($"C5 {tableName} row {oldIndex} differs at mapped row {newIndex}");
        }
    }

    private static void CheckOldMegaevoRows(byte[][] before, byte[][] after, int[] oldToNew, List<string> violations)
    {
        for (int oldIndex = 0; oldIndex < before.Length; oldIndex++)
        {
            if (oldIndex >= oldToNew.Length)
            {
                violations.Add($"C8 megaevo old entry {oldIndex} has no personal index mapping");
                continue;
            }
            int newIndex = oldToNew[oldIndex];
            if (newIndex < 0 || newIndex >= after.Length)
            {
                violations.Add($"C8 megaevo old entry {oldIndex} has no mapped row at {newIndex}");
                continue;
            }
            if (!before[oldIndex].AsSpan().SequenceEqual(after[newIndex]))
                violations.Add($"C8 megaevo row {oldIndex} differs at mapped row {newIndex}");
        }
    }

    private static void CheckEggMove(SpeciesTables before, SpeciesTables after, ExpectedInsertions expected,
        int maxSpecies, List<string> violations)
    {
        long expectedGrowth = (long)before.EggMove.Length + expected.AppendedEggMoveRows + expected.SpeciesCount;
        if (after.EggMove.Length != expectedGrowth)
            violations.Add($"C9 eggmove row count {after.EggMove.Length} does not equal independently planned count {expectedGrowth}");
        if (after.EggMove.Length != expected.EggMoveRows.Length)
            violations.Add($"C9 eggmove row count {after.EggMove.Length} does not match independently planned count {expected.EggMoveRows.Length}");

        for (int i = 0; i < expected.EggMoveRows.Length; i++)
        {
            if (i >= after.EggMove.Length)
            {
                violations.Add($"C9 expected eggmove row {i} is missing");
                continue;
            }
            if (!expected.EggMoveRows[i].AsSpan().SequenceEqual(after.EggMove[i]))
                violations.Add($"C9 eggmove row {i} differs from the independently planned row");
        }

        foreach (ExpectedEggMoveRelocation relocation in expected.EggMoveRelocations)
        {
            for (int i = 0; i < relocation.OldRows.Length; i++)
            {
                int newIndex = relocation.NewBlockStart + i;
                if (newIndex >= after.EggMove.Length)
                {
                    violations.Add($"C9 relocated eggmove row {relocation.OldBlockStart + i} is missing at {newIndex}");
                    continue;
                }

                byte[] oldRow = relocation.OldRows[i];
                byte[] actual = after.EggMove[newIndex];
                if (actual.Length < sizeof(ushort) || oldRow.Length != actual.Length)
                {
                    violations.Add($"C9 relocated eggmove row {relocation.OldBlockStart + i} has the wrong length at {newIndex}");
                    continue;
                }
                long movedIndex = (long)ReadEggMoveIndex(oldRow) + relocation.Delta;
                if (ReadEggMoveIndex(actual) != movedIndex)
                    violations.Add($"C9 relocated eggmove row {relocation.OldBlockStart + i} has FormTableIndex {ReadEggMoveIndex(actual)}, expected {movedIndex}");
                if (!oldRow.AsSpan(sizeof(ushort)).SequenceEqual(actual.AsSpan(sizeof(ushort))))
                    violations.Add($"C9 relocated eggmove row {relocation.OldBlockStart + i} changed bytes after its FormTableIndex");
            }
        }

        int oldBaseCount = Math.Min(before.EggMove.Length, maxSpecies + 1);
        for (int species = 1; species < oldBaseCount; species++)
        {
            byte[] oldRow = before.EggMove[species];
            if (oldRow.Length < sizeof(ushort) || ReadEggMoveIndex(oldRow) != species) continue;
            if (species >= after.EggMove.Length || after.EggMove[species].Length < sizeof(ushort)
                || ReadEggMoveIndex(after.EggMove[species]) != species)
                violations.Add($"C9 formless species {species} FormTableIndex no longer equals its id");
        }

        // The V and E baselines already have a few species whose blocks share rows (3 on V, 15 on E), so only
        // an overlap the insertion introduced is a violation.
        HashSet<string> baseline = EggMoveOverlaps(before, maxSpecies, null);
        EggMoveOverlaps(after, maxSpecies, violations, baseline);
    }

    private static HashSet<string> EggMoveOverlaps(SpeciesTables t, int maxSpecies, List<string>? violations, HashSet<string>? allowed = null)
    {
        var overlaps = new HashSet<string>();
        var blocks = new List<(long Start, long End, int Species)>();
        for (int species = 1; species <= maxSpecies && species < t.Personal.Length && species < t.EggMove.Length; species++)
        {
            byte[] personal = t.Personal[species];
            byte[] row = t.EggMove[species];
            if (personal.Length <= PersonalFormeCountOffset || row.Length < sizeof(ushort)) continue;
            int formCount = personal[PersonalFormeCountOffset];
            int start = ReadEggMoveIndex(row);
            if (formCount <= 1) continue;
            if (start < maxSpecies + 1)
            {
                if (start != species) violations?.Add($"C9 formless species {species} has FormTableIndex {start}, not its id");
                continue;
            }

            long end = (long)start + formCount - 2;
            if (end >= t.EggMove.Length)
            {
                violations?.Add($"C9 eggmove block for species {species} ends at {end}, past {t.EggMove.Length} rows");
                continue;
            }
            blocks.Add((start, end, species));
        }
        blocks.Sort((left, right) => left.Start.CompareTo(right.Start));
        for (int i = 1; i < blocks.Count; i++)
        {
            if (blocks[i].Start > blocks[i - 1].End) continue;
            string pair = $"{blocks[i - 1].Species},{blocks[i].Species}";
            overlaps.Add(pair);
            if (allowed != null && !allowed.Contains(pair))
                violations?.Add($"C9 eggmove blocks for species {blocks[i - 1].Species} and {blocks[i].Species} overlap");
        }
        return overlaps;
    }

    private static void CheckInsertedRows(SpeciesTables after, List<ExpectedInsertion> insertions, List<string> violations)
    {
        foreach (ExpectedInsertion insertion in insertions)
        {
            ExpectedInsertion final = FinalFor(insertions, insertion.Species)!;
            for (int i = 0; i < insertion.Count; i++)
            {
                int index = insertion.Start + i;
                if (index >= after.Personal.Length || index >= after.Evolution.Length || index >= after.Levelup.Length)
                {
                    violations.Add($"C6 inserted row {index} is missing from a parallel table");
                    continue;
                }
                byte[] row = after.Personal[index];
                if (!MatchesPersonalTemplate(insertion.PersonalTemplate, row))
                    violations.Add($"C6 inserted personal row {index} differs from its template outside FormStatsIndex/FormeCount metadata");
                else if (ReadPointer(row) != final.FamilyPointer || row[PersonalFormeCountOffset] != final.NewCount)
                    violations.Add($"C6 inserted personal row {index} carries FormStatsIndex {ReadPointer(row)} and FormeCount {row[PersonalFormeCountOffset]}, expected {final.FamilyPointer} and {final.NewCount}");
                if (!insertion.EvolutionTemplate.AsSpan().SequenceEqual(after.Evolution[index]))
                    violations.Add($"C6 inserted evolution row {index} differs from its template");
                if (!insertion.LevelupTemplate.AsSpan().SequenceEqual(after.Levelup[index]))
                    violations.Add($"C6 inserted levelup row {index} differs from its template");
                if (index >= after.Megaevo.Length)
                    violations.Add($"C8 inserted megaevo row {index} is missing");
                else
                {
                    byte[] megaevoRow = after.Megaevo[index];
                    if (megaevoRow.Length != insertion.MegaevoRowLength || megaevoRow.Any(value => value != 0))
                        violations.Add($"C8 inserted megaevo row {index} is not a zero row of length {insertion.MegaevoRowLength}");
                }
            }
        }
    }

    private static void CheckSpeciesInsertionRows(SpeciesTables before, SpeciesTables after,
        ExpectedInsertions expected, List<string> violations)
    {
        if (expected.SpeciesCount == 0 || expected.SpeciesInsertAt < 0) return;
        int insertAt = expected.SpeciesInsertAt;
        int count = expected.SpeciesCount;
        CheckZeroInsertedRows(before.Personal, after.Personal, insertAt, count, "personal", violations);
        CheckZeroInsertedRows(before.Evolution, after.Evolution, insertAt, count, "evolution", violations);
        CheckZeroInsertedRows(before.Levelup, after.Levelup, insertAt, count, "levelup", violations);
        CheckZeroInsertedRows(before.Megaevo, after.Megaevo, insertAt, count, "megaevo", violations);
    }

    private static void CheckZeroInsertedRows(byte[][] before, byte[][] after, int insertAt, int count,
        string tableName, List<string> violations)
    {
        int expectedLength = before.Length == 0 ? 0 : before[0].Length;
        for (int i = 0; i < count; i++)
        {
            int rowIndex = insertAt + i;
            if (rowIndex >= after.Length)
            {
                violations.Add($"C10 inserted {tableName} row {rowIndex} is missing");
                continue;
            }
            byte[] row = after[rowIndex];
            if (row.Length != expectedLength || row.Any(value => value != 0))
                violations.Add($"C10 inserted {tableName} row {rowIndex} is not a zero row of length {expectedLength}");
        }
    }

    private static void CheckSpeciesEggMove(SpeciesTables before, SpeciesTables after, int maxSpecies,
        int speciesCount, List<string> violations)
    {
        int insertAt = checked(maxSpecies + 1);
        long expectedCount = (long)before.EggMove.Length + speciesCount;
        if (after.EggMove.Length != expectedCount)
            violations.Add($"C10 eggmove row count {after.EggMove.Length} does not equal {before.EggMove.Length} old rows plus {speciesCount} new rows");

        for (int oldIndex = 0; oldIndex < before.EggMove.Length; oldIndex++)
        {
            int newIndex = oldIndex < insertAt ? oldIndex : checked(oldIndex + speciesCount);
            if (newIndex >= after.EggMove.Length)
            {
                violations.Add($"C10 old eggmove row {oldIndex} is missing at {newIndex}");
                continue;
            }
            byte[] oldRow = before.EggMove[oldIndex];
            byte[] actual = after.EggMove[newIndex];
            if (oldRow.Length != actual.Length)
            {
                violations.Add($"C10 old eggmove row {oldIndex} changed length at {newIndex}");
                continue;
            }
            if (oldRow.Length >= sizeof(ushort))
            {
                long expectedIndex = ReadEggMoveIndex(oldRow);
                if (expectedIndex >= insertAt) expectedIndex += speciesCount;
                if (expectedIndex > ushort.MaxValue || ReadEggMoveIndex(actual) != expectedIndex)
                    violations.Add($"C10 old eggmove row {oldIndex} has FormTableIndex {ReadEggMoveIndex(actual)}, expected {expectedIndex}");
                if (!oldRow.AsSpan(sizeof(ushort)).SequenceEqual(actual.AsSpan(sizeof(ushort))))
                    violations.Add($"C10 old eggmove row {oldIndex} changed bytes after FormTableIndex");
            }
            else if (!oldRow.AsSpan().SequenceEqual(actual))
            {
                violations.Add($"C10 short old eggmove row {oldIndex} changed");
            }
        }

        for (int i = 0; i < speciesCount; i++)
        {
            int speciesId = checked(insertAt + i);
            int rowIndex = checked(insertAt + i);
            if (rowIndex >= after.EggMove.Length)
            {
                violations.Add($"C10 new eggmove row for species {speciesId} is missing");
                continue;
            }
            byte[] row = after.EggMove[rowIndex];
            if (row.Length < EggMoveHeaderLength)
            {
                violations.Add($"C10 new eggmove row for species {speciesId} is shorter than {EggMoveHeaderLength} bytes");
                continue;
            }
            if (ReadEggMoveIndex(row) != speciesId || BitConverter.ToUInt16(row, sizeof(ushort)) != 0)
                violations.Add($"C10 new eggmove row for species {speciesId} is not [id, 0]");
            if (row.AsSpan(EggMoveHeaderLength).IndexOfAnyExcept((byte)0) >= 0)
                violations.Add($"C10 new eggmove row for species {speciesId} has nonzero trailing bytes");
        }
    }

    private static void CheckEvolutionBase(SpeciesTables before, SpeciesTables after, int maxSpecies,
        int speciesCount, int[]? lowestEvolution, List<string> violations)
    {
        if (before.EvolutionBase == null || after.EvolutionBase == null)
        {
            violations.Add("C11 evolutionbase table is missing");
            return;
        }

        long expectedCount = (long)maxSpecies + 1 + speciesCount;
        if (after.EvolutionBase.Length != expectedCount)
            violations.Add($"C11 evolutionbase has {after.EvolutionBase.Length} entries, expected {expectedCount}");
        if (before.EvolutionBase.Length != (long)maxSpecies + 1)
            violations.Add($"C11 baseline evolutionbase has {before.EvolutionBase.Length} entries, expected {maxSpecies + 1}");

        for (int i = 0; i < before.EvolutionBase.Length; i++)
        {
            if (i >= after.EvolutionBase.Length)
            {
                violations.Add($"C11 old evolutionbase entry {i} is missing");
                continue;
            }
            if (!before.EvolutionBase[i].AsSpan().SequenceEqual(after.EvolutionBase[i]))
                violations.Add($"C11 old evolutionbase entry {i} changed");
        }

        int newCount = Math.Min(speciesCount, Math.Max(0, after.EvolutionBase.Length - before.EvolutionBase.Length));
        for (int i = 0; i < newCount; i++)
        {
            int speciesId = checked(maxSpecies + 1 + i);
            int expected = lowestEvolution != null && i < lowestEvolution.Length && lowestEvolution[i] >= 0
                ? lowestEvolution[i]
                : speciesId;
            byte[] actual = after.EvolutionBase[before.EvolutionBase.Length + i];
            if (actual.Length != sizeof(ushort) || BitConverter.ToUInt16(actual) != expected)
                violations.Add($"C11 evolutionbase entry for species {speciesId} is not {expected}");
        }

        byte[] actualBlob = after.BuildEvolutionBaseBlob();
        long expectedBlobLength = 0;
        foreach (byte[] row in after.EvolutionBase) expectedBlobLength = checked(expectedBlobLength + row.LongLength);
        if (actualBlob.LongLength != expectedBlobLength)
            violations.Add($"C11 evolutionbase blob is {actualBlob.LongLength} B, expected concatenated length {expectedBlobLength} B");
    }

    private static bool MatchesPersonalTemplate(byte[] template, byte[] inserted)
    {
        if (template.Length != inserted.Length) return false;
        for (int offset = 0; offset < template.Length; offset++)
        {
            if (offset == PersonalFormStatsIndexOffset || offset == PersonalFormStatsIndexOffset + 1 || offset == PersonalFormeCountOffset) continue;
            if (template[offset] != inserted[offset]) return false;
        }
        return true;
    }

    private static int ReadPointer(byte[] entry) => BitConverter.ToUInt16(entry, PersonalFormStatsIndexOffset);
    private static int ReadEggMoveIndex(byte[] row) => BitConverter.ToUInt16(row, EggMoveFormTableIndexOffset);

    private static void WriteEggMoveIndex(byte[] row, int value)
        => BitConverter.TryWriteBytes(row.AsSpan(EggMoveFormTableIndexOffset), checked((ushort)value));

    private sealed class ExpectedInsertions(int personalCount, int[] oldToNew, List<ExpectedInsertion> insertions,
        byte[][] eggMoveRows, int appendedEggMoveRows, List<ExpectedEggMoveRelocation> eggMoveRelocations,
        int speciesInsertAt, int speciesCount)
    {
        public int PersonalCount { get; } = personalCount;
        public int[] OldToNew { get; } = oldToNew;
        public List<ExpectedInsertion> Insertions { get; } = insertions;
        public byte[][] EggMoveRows { get; } = eggMoveRows;
        public int AppendedEggMoveRows { get; } = appendedEggMoveRows;
        public List<ExpectedEggMoveRelocation> EggMoveRelocations { get; } = eggMoveRelocations;
        public int SpeciesInsertAt { get; } = speciesInsertAt;
        public int SpeciesCount { get; } = speciesCount;
    }

    private sealed class ExpectedInsertion(int start, int count, int species, int familyPointer, int newCount,
        byte[] personalTemplate, byte[] evolutionTemplate, byte[] levelupTemplate, int megaevoRowLength)
    {
        public int Start { get; set; } = start;
        public int Count { get; } = count;
        public int Species { get; } = species;
        public int FamilyPointer { get; set; } = familyPointer;
        public int NewCount { get; } = newCount;
        public byte[] PersonalTemplate { get; } = personalTemplate;
        public byte[] EvolutionTemplate { get; } = evolutionTemplate;
        public byte[] LevelupTemplate { get; } = levelupTemplate;
        public int MegaevoRowLength { get; } = megaevoRowLength;
    }

    private sealed record ExpectedEggMoveRelocation(int OldBlockStart, int NewBlockStart, int Delta, byte[][] OldRows);
}
