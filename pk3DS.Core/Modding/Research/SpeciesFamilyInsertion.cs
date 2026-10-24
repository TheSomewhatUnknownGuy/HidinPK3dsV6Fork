using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public sealed class SpeciesFamilyInsertResult
{
    public byte[][] Personal { get; set; } = [];
    public byte[][] Evolution { get; set; } = [];
    public byte[][] Levelup { get; set; } = [];
    public byte[][] Megaevo { get; set; } = [];
    public byte[][] EvolutionBase { get; set; } = [];
    public int InsertedAt { get; init; }
    public int InsertedCount { get; init; }
    public int PersonalCrossReferencesShifted { get; set; }
    public List<string> Notes { get; } = [];
    public string Describe() => string.Join(Environment.NewLine,
        new[] { $"inserted {InsertedCount} new base species at index {InsertedAt}, " +
                $"{PersonalCrossReferencesShifted} personal-table cross-reference(s) shifted to match" }
        .Concat(Notes));
}

public static class SpeciesFamilyInsertion
{
    public const int PersonalFormStatsIndexOffset = 0x1C;

    public static SpeciesFamilyInsertResult InsertBaseSpecies(
        byte[][] personalFiles, byte[][] evolutionFiles, byte[][] levelupFiles, byte[][] megaevoFiles,
        int insertAt, int count)
    {
        if (personalFiles == null) throw new ArgumentNullException(nameof(personalFiles));

        var result = new SpeciesFamilyInsertResult { InsertedAt = insertAt, InsertedCount = count };

        var p = SpeciesArraySplicer.Insert(personalFiles, insertAt, count,
            altFormBlockStart: insertAt, crossRefByteOffset: PersonalFormStatsIndexOffset, crossRefSize: 2);
        result.Personal = p.Files;
        result.PersonalCrossReferencesShifted = p.CrossReferencesShifted;

        if (evolutionFiles != null)
        {
            if (evolutionFiles.Length != personalFiles.Length)
                result.Notes.Add($"evolution has {evolutionFiles.Length} entries, personal has {personalFiles.Length} - " +
                                 "expected them to match 1:1; double-check before trusting this insertion");
            result.Evolution = SpeciesArraySplicer.Insert(evolutionFiles, insertAt, count).Files;
        }

        if (levelupFiles != null)
        {
            if (levelupFiles.Length != personalFiles.Length)
                result.Notes.Add($"levelup has {levelupFiles.Length} entries, personal has {personalFiles.Length} - " +
                                 "expected them to match 1:1; double-check before trusting this insertion");
            result.Levelup = SpeciesArraySplicer.Insert(levelupFiles, insertAt, count).Files;
        }

        if (megaevoFiles != null)
        {
            if (megaevoFiles.Length != personalFiles.Length)
                result.Notes.Add($"megaevo has {megaevoFiles.Length} entries, personal has {personalFiles.Length} - " +
                                 "expected them to match 1:1; double-check before trusting this insertion");
            result.Megaevo = SpeciesArraySplicer.Insert(megaevoFiles, insertAt, count).Files;
        }

        return result;
    }

    public static byte[][] AppendEvolutionBase(byte[][] evolutionBaseFiles, int count, int firstNewSpeciesId)
    {
        if (evolutionBaseFiles == null) throw new ArgumentNullException(nameof(evolutionBaseFiles));
        int oldLen = evolutionBaseFiles.Length;
        var result = new byte[oldLen + count][];
        Array.Copy(evolutionBaseFiles, result, oldLen);
        for (int i = 0; i < count; i++)
        {
            var entry = new byte[2];
            BitConverter.GetBytes((ushort)(firstNewSpeciesId + i)).CopyTo(entry, 0);
            result[oldLen + i] = entry;
        }
        return result;
    }
}
