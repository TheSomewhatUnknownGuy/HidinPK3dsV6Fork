#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using pk3DS.Core.Modding.Research;

namespace pk3DS.Core.Modding.Insertion;

public sealed class SpeciesInsertPlan
{
    public SpeciesTables After = new();
    public int[] NewSpeciesIds = [];
    public List<string> Log = [];
}

public static class SpeciesPlanner
{
    private const int EggMoveHeaderLength = 4;

    public static SpeciesInsertPlan InsertBaseSpecies(SpeciesTables before, int maxSpecies, int count, int[] lowestEvolution)
    {
        if (before == null) throw new ArgumentNullException(nameof(before));
        if (before.Personal == null || before.Evolution == null || before.Levelup == null || before.Megaevo == null || before.EggMove == null)
            throw new InvalidDataException("one or more species tables are missing");
        if (before.EvolutionBase == null)
            throw new InvalidDataException("evolutionbase table is missing");
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "must insert at least one species");
        if (maxSpecies < 0) throw new ArgumentOutOfRangeException(nameof(maxSpecies), "must not be negative");
        if (lowestEvolution == null) throw new ArgumentNullException(nameof(lowestEvolution));
        if (lowestEvolution.Length != count)
            throw new ArgumentException($"lowestEvolution has {lowestEvolution.Length} values, expected {count}", nameof(lowestEvolution));

        long firstNewSpeciesId = (long)maxSpecies + 1;
        long lastNewSpeciesId = (long)maxSpecies + count;
        if (lastNewSpeciesId > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(count), $"new species id {lastNewSpeciesId} exceeds the 16-bit table limit");
        int insertAt = checked((int)firstNewSpeciesId);
        if (insertAt > before.Personal.Length || insertAt > before.Evolution.Length
            || insertAt > before.Levelup.Length || insertAt > before.Megaevo.Length)
            throw new InvalidDataException($"species insertion index {insertAt} is outside one or more parallel tables");
        if (before.EvolutionBase.Length != insertAt)
            throw new InvalidDataException($"evolutionbase has {before.EvolutionBase.Length} entries; expected {insertAt} entries for species 0..{maxSpecies}");
        if (before.EggMove.Length == 0 || before.EggMove[0].Length < EggMoveHeaderLength)
            throw new InvalidDataException($"eggmove rows must be at least {EggMoveHeaderLength} bytes to add a species row");
        for (int i = 0; i < before.EvolutionBase.Length; i++)
            if (before.EvolutionBase[i].Length != sizeof(ushort))
                throw new InvalidDataException($"evolutionbase entry {i} is {before.EvolutionBase[i].Length} B; expected 2 B");
        for (int i = 0; i < count; i++)
        {
            int lowest = lowestEvolution[i];
            if (lowest < -1 || lowest > lastNewSpeciesId)
                throw new ArgumentOutOfRangeException(nameof(lowestEvolution), $"lowest evolution id {lowest} is outside -1..{lastNewSpeciesId} (an existing species or one inserted now)");
        }

        SpeciesFamilyInsertResult family = SpeciesFamilyInsertion.InsertBaseSpecies(
            before.Personal, before.Evolution, before.Levelup, before.Megaevo, insertAt, count);
        SpliceResult eggMove = SpeciesArraySplicer.Insert(before.EggMove, insertAt, count,
            template: null, altFormBlockStart: insertAt, crossRefByteOffset: 0, crossRefSize: sizeof(ushort));

        SpeciesTables after = before.Clone();
        after.Personal = family.Personal;
        after.Evolution = family.Evolution;
        after.Levelup = family.Levelup;
        after.Megaevo = family.Megaevo;
        after.EggMove = eggMove.Files;
        after.EvolutionBase = SpeciesFamilyInsertion.AppendEvolutionBase(before.EvolutionBase, count, insertAt);

        var ids = new int[count];
        for (int i = 0; i < count; i++)
        {
            int speciesId = checked(insertAt + i);
            ids[i] = speciesId;
            byte[] eggMoveRow = after.EggMove[insertAt + i];
            BitConverter.TryWriteBytes(eggMoveRow.AsSpan(0, sizeof(ushort)), checked((ushort)speciesId));
            BitConverter.TryWriteBytes(eggMoveRow.AsSpan(sizeof(ushort), sizeof(ushort)), (ushort)0);

            int evolutionBaseValue = lowestEvolution[i] < 0 ? speciesId : lowestEvolution[i];
            BitConverter.TryWriteBytes(after.EvolutionBase[before.EvolutionBase.Length + i], checked((ushort)evolutionBaseValue));
        }

        var log = new List<string>
        {
            family.Describe(),
            $"eggmove: inserted {count} base row(s) at {insertAt}; shifted {eggMove.CrossReferencesShifted} FormTableIndex value(s)",
            $"evolutionbase: appended {count} row(s); blob {before.BuildEvolutionBaseBlob().LongLength} -> {after.BuildEvolutionBaseBlob().LongLength} B",
        };
        return new SpeciesInsertPlan { After = after, NewSpeciesIds = ids, Log = log };
    }
}
