#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace pk3DS.Core.Modding.Insertion;

public sealed class FormInsertPlan
{
    public SpeciesTables After = new();
    public int[] OldToNew = [];
    public List<string> Log = [];
}

public static class FormPlanner
{
    private const int PersonalFormStatsIndexOffset = 0x1C;
    private const int PersonalFormeCountOffset = 0x20;
    private const int EggMoveFormTableIndexOffset = 0;
    private const int EggMoveHeaderLength = 4;
    private const int VanillaPersonalCeiling = 1153;
    private const int ExpansionPersonalCeiling = 1507;

    // The public eligibility signature has no code.bin argument, so its ceiling is the layout default.
    public static string? Eligibility(SpeciesTables t, int species, int count, int templateEntry, int maxSpecies, ModelsLayout models)
        => EligibilityAtCeiling(t, species, count, templateEntry, maxSpecies,
            maxSpecies >= 1025 ? ExpansionPersonalCeiling : VanillaPersonalCeiling, models);

    public static FormInsertPlan Insert(SpeciesTables before, int species, int count, int templateEntry, int maxSpecies,
        int ceiling, ModelsLayout models)
    {
        string? refusal = EligibilityAtCeiling(before, species, count, templateEntry, maxSpecies, ceiling, models);
        if (refusal != null) throw new ArgumentException(refusal, nameof(species));

        int insertionIndex = FindInsertionIndex(before, species, maxSpecies);
        int oldFormCount = before.Personal[species][PersonalFormeCountOffset];
        int oldPointer = ReadPointer(before.Personal[species]);
        int familyPointer = oldFormCount > 1 && oldPointer > 0 ? oldPointer : insertionIndex;
        int newFormCount = oldFormCount + count;

        SpeciesTables after = before.Clone();
        byte[][] oldEvolution = PadRows(after.Evolution, before.Personal.Length, RowLength(after.Evolution));
        byte[][] oldLevelup = PadRows(after.Levelup, before.Personal.Length, 0);
        byte[] personalTemplate = (byte[])before.Personal[templateEntry].Clone();
        byte[] evolutionTemplate = (byte[])oldEvolution[templateEntry].Clone();
        byte[] levelupTemplate = (byte[])oldLevelup[templateEntry].Clone();

        after.Personal = InsertRows(after.Personal, insertionIndex, count, personalTemplate);
        after.Evolution = InsertRows(oldEvolution, insertionIndex, count, evolutionTemplate);
        after.Levelup = InsertRows(oldLevelup, insertionIndex, count, levelupTemplate);
        if (after.Megaevo.Length > 0)
            after.Megaevo = InsertRows(after.Megaevo, insertionIndex, count, new byte[RowLength(after.Megaevo)]);

        for (int i = 0; i < newFormCount - 1; i++)
            SetFamilyMetadata(after.Personal[familyPointer + i], familyPointer, newFormCount);
        SetFamilyMetadata(after.Personal[species], familyPointer, newFormCount);

        RealignAllPointers(after.Personal, insertionIndex, count, species);

        var oldToNew = new int[before.Personal.Length];
        for (int i = 0; i < oldToNew.Length; i++) oldToNew[i] = i >= insertionIndex ? i + count : i;

        var log = new List<string>
        {
            $"inserted {count} form(s) for species {species} at personal index {insertionIndex}; family count {oldFormCount} -> {newFormCount}"
        };
        RelocateEggMoves(before, after, species, count, templateEntry, maxSpecies, log);

        return new FormInsertPlan
        {
            After = after,
            OldToNew = oldToNew,
            Log = log,
        };
    }

    internal static int FindInsertionIndex(SpeciesTables tables, int species, int maxSpecies)
    {
        int currentCount = tables.Personal[species][PersonalFormeCountOffset];
        int currentPointer = ReadPointer(tables.Personal[species]);
        if (currentCount > 1 && currentPointer > 0)
            return checked(currentPointer + currentCount - 1);

        for (int laterSpecies = species + 1; laterSpecies <= maxSpecies && laterSpecies < tables.Personal.Length; laterSpecies++)
        {
            byte[] entry = tables.Personal[laterSpecies];
            if (entry.Length <= PersonalFormStatsIndexOffset + 1) continue;
            int pointer = ReadPointer(entry);
            if (pointer > 0 && pointer < tables.Personal.Length) return pointer;
        }
        return tables.Personal.Length;
    }

    private static string? EligibilityAtCeiling(SpeciesTables? tables, int species, int count,
        int templateEntry, int maxSpecies, int ceiling, ModelsLayout models)
    {
        if (tables == null) return "species tables are missing";
        if (tables.Personal == null) return "personal table is missing";
        if (species < 1 || species > maxSpecies || species >= tables.Personal.Length)
            return $"species {species} is outside 1..{maxSpecies} or has no personal entry";
        if (templateEntry < 0 || templateEntry >= tables.Personal.Length)
            return $"template entry {templateEntry} is outside the personal table";
        if (count <= 0) return "form count must be positive";
        if (tables.Personal[species] == null || tables.Personal[species].Length <= PersonalFormeCountOffset)
            return $"species {species} has a short personal entry";
        if (tables.Personal[templateEntry] == null || tables.Personal[templateEntry].Length <= PersonalFormeCountOffset)
            return $"template entry {templateEntry} has a short personal entry";

        int currentCount = tables.Personal[species][PersonalFormeCountOffset];
        int pointer = ReadPointer(tables.Personal[species]);
        if (currentCount == 0) return $"species {species} has an invalid FormeCount of 0";
        if ((long)currentCount + count > byte.MaxValue)
            return $"FormeCount for species {species} would exceed 255";
        if (currentCount > 1 && pointer == 0)
            return $"species {species} is cosmetic-only (FormeCount {currentCount}, FormStatsIndex 0)";

        if (currentCount > 1)
        {
            long end = (long)pointer + currentCount - 2;
            if (pointer < maxSpecies + 1L || end >= tables.Personal.Length)
                return $"species {species} has an invalid FormStatsIndex family block at {pointer}";
            for (long index = pointer; index <= end; index++)
            {
                byte[] entry = tables.Personal[(int)index];
                if (entry == null || entry.Length <= PersonalFormeCountOffset)
                    return $"species {species} has a short form entry at {index}";
                if (ReadPointer(entry) != pointer || entry[PersonalFormeCountOffset] != currentCount)
                    return $"species {species} family entry {index} does not carry FormStatsIndex {pointer} and FormeCount {currentCount}";
            }
        }

        if (ceiling < 0 || (long)tables.Personal.Length + count > ceiling)
            return $"personal table would grow to {(long)tables.Personal.Length + count} entries, past the {ceiling} entry ceiling";
        return ModelsPlanner.FormEligibility(models, tables, species);
    }

    private static byte[][] PadRows(byte[][] rows, int targetCount, int newRowLength)
    {
        if (rows.Length >= targetCount) return rows;
        var padded = new byte[targetCount][];
        Array.Copy(rows, padded, rows.Length);
        for (int i = rows.Length; i < padded.Length; i++) padded[i] = new byte[newRowLength];
        return padded;
    }

    private static int RowLength(byte[][] rows) => rows.Length == 0 ? 0 : rows[0].Length;

    private static byte[][] InsertRows(byte[][] rows, int insertionIndex, int count, byte[] template)
    {
        if (insertionIndex < 0 || insertionIndex > rows.Length)
            throw new ArgumentOutOfRangeException(nameof(insertionIndex));
        var result = new byte[checked(rows.Length + count)][];
        Array.Copy(rows, 0, result, 0, insertionIndex);
        for (int i = 0; i < count; i++) result[insertionIndex + i] = (byte[])template.Clone();
        Array.Copy(rows, insertionIndex, result, insertionIndex + count, rows.Length - insertionIndex);
        return result;
    }

    private static void RelocateEggMoves(SpeciesTables before, SpeciesTables after, int species, int count,
        int templateEntry, int maxSpecies, List<string> log)
    {
        if (species >= before.EggMove.Length || before.EggMove[species].Length < sizeof(ushort)) return;

        byte[] speciesRow = before.EggMove[species];
        int oldBlockStart = ReadEggMoveIndex(speciesRow);
        if (oldBlockStart == species)
        {
            log.Add($"eggmove: species {species} has no form rows; unchanged");
            return;
        }

        int formCount = before.Personal[species][PersonalFormeCountOffset];
        if (formCount <= 1 || oldBlockStart < maxSpecies + 1) return;
        if (speciesRow.Length < EggMoveHeaderLength)
            throw new InvalidDataException($"eggmove row {species} is shorter than {EggMoveHeaderLength} bytes");
        long oldBlockEnd = (long)oldBlockStart + formCount - 2;
        if (oldBlockEnd >= before.EggMove.Length)
            throw new InvalidDataException($"eggmove block for species {species} ends at {oldBlockEnd}, past {before.EggMove.Length} rows");

        int newBlockStart = before.EggMove.Length;
        int delta = checked(newBlockStart - oldBlockStart);
        if (newBlockStart > ushort.MaxValue)
            throw new InvalidDataException($"eggmove block for species {species} would start at {newBlockStart}, past the 16-bit FormTableIndex limit");

        int templateRowIndex = ResolveEggMoveTemplateRow(before, templateEntry, maxSpecies);
        byte[] templateRow = (byte[])before.EggMove[templateRowIndex].Clone();
        int movedRows = formCount - 1;
        int newLength = checked(before.EggMove.Length + movedRows + count);
        Array.Resize(ref after.EggMove, newLength);

        for (int i = 0; i < movedRows; i++)
        {
            byte[] oldRow = before.EggMove[oldBlockStart + i];
            if (oldRow.Length < EggMoveHeaderLength)
                throw new InvalidDataException($"eggmove row {oldBlockStart + i} is shorter than {EggMoveHeaderLength} bytes");
            byte[] moved = (byte[])oldRow.Clone();
            int movedIndex = checked(ReadEggMoveIndex(moved) + delta);
            WriteEggMoveIndex(moved, movedIndex);
            after.EggMove[newBlockStart + i] = moved;
        }

        WriteEggMoveIndex(after.EggMove[species], newBlockStart);
        if (templateRow.Length < EggMoveHeaderLength)
            throw new InvalidDataException($"template eggmove row {templateRowIndex} is shorter than {EggMoveHeaderLength} bytes");
        for (int i = 0; i < count; i++)
        {
            byte[] inserted = (byte[])templateRow.Clone();
            WriteEggMoveIndex(inserted, newBlockStart);
            after.EggMove[newBlockStart + movedRows + i] = inserted;
        }

        log.Add($"eggmove: species {species} FormTableIndex {oldBlockStart} -> {newBlockStart}");
    }

    private static int ResolveEggMoveTemplateRow(SpeciesTables tables, int templateEntry, int maxSpecies)
    {
        int owner = templateEntry;
        int form = 0;
        if (templateEntry > maxSpecies)
        {
            owner = -1;
            for (int species = 1; species <= maxSpecies && species < tables.Personal.Length; species++)
            {
                byte[] entry = tables.Personal[species];
                if (entry.Length <= PersonalFormeCountOffset) continue;
                int count = entry[PersonalFormeCountOffset];
                int pointer = ReadPointer(entry);
                if (count <= 1 || pointer <= 0 || templateEntry < pointer || (long)templateEntry >= (long)pointer + count - 1)
                    continue;
                owner = species;
                form = templateEntry - pointer + 1;
                break;
            }
            if (owner < 0)
                throw new InvalidDataException($"template personal entry {templateEntry} has no owning species form block");
        }

        if (owner < 0 || owner >= tables.EggMove.Length || tables.EggMove[owner].Length < sizeof(ushort))
            throw new InvalidDataException($"template owner {owner} has no eggmove row");
        int formTableIndex = ReadEggMoveIndex(tables.EggMove[owner]);
        long rowIndex = form > 0 && formTableIndex >= maxSpecies + 1
            ? (long)formTableIndex + form - 1L
            : owner;
        if (rowIndex < 0 || rowIndex >= tables.EggMove.Length)
            throw new InvalidDataException($"template eggmove row {rowIndex} is outside the {tables.EggMove.Length}-row table");
        return (int)rowIndex;
    }

    private static int ReadEggMoveIndex(byte[] row) => BitConverter.ToUInt16(row, EggMoveFormTableIndexOffset);

    private static void WriteEggMoveIndex(byte[] row, int value)
        => BitConverter.TryWriteBytes(row.AsSpan(EggMoveFormTableIndexOffset), checked((ushort)value));

    private static void RealignAllPointers(byte[][] personal, int insertionIndex, int count, int excludeSpecies)
    {
        for (int i = 1; i < personal.Length; i++)
        {
            if (i == excludeSpecies || (i >= insertionIndex && i < insertionIndex + count)) continue;
            byte[] entry = personal[i];
            if (entry.Length <= PersonalFormStatsIndexOffset + 1) continue;
            int pointer = ReadPointer(entry);
            if (pointer == 0 || pointer < insertionIndex) continue;
            SetPointer(entry, checked(pointer + count));
        }
    }

    private static void SetFamilyMetadata(byte[] entry, int pointer, int count)
    {
        SetPointer(entry, pointer);
        entry[PersonalFormeCountOffset] = (byte)count;
    }

    private static int ReadPointer(byte[] entry) => BitConverter.ToUInt16(entry, PersonalFormStatsIndexOffset);

    private static void SetPointer(byte[] entry, int pointer)
        => BitConverter.TryWriteBytes(entry.AsSpan(PersonalFormStatsIndexOffset), checked((ushort)pointer));
}
