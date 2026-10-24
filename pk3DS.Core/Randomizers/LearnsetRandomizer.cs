using pk3DS.Core.Structures;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Randomizers;

// https://twitter.com/Drayano60/status/807297858244411397
// ORAS: 10682 moves learned on levelup/birth.
// 5593 are STAB. 52.3% are STAB.
// Steelix learns the most @ 25 (so many level 1)!
// Move relearner ingame glitch fixed (52 tested), but keep below 75
public class LearnsetRandomizer : IRandomizer
{
    private readonly MoveRandomizer moverand;
    private readonly GameConfig Config;
    private readonly Learnset[] Learnsets;

    public LearnsetRandomizer(GameConfig config, Learnset[] sets)
    {
        Config = config;
        moverand = new MoveRandomizer(config);
        Learnsets = sets;
        STABPercent = 52.3m;
    }

    public bool Expand = true;
    public int ExpandTo = 25;
    public bool Spread = true;
    public int SpreadTo = 75;
    public bool STABFirst = true;
    public bool Learn4Level1 = false;
    /// <summary>How many leading moves <see cref="Learn4Level1"/> puts at level 1.</summary>
    public int Level1Count = 4;
    public bool OrderByPower = true;

    public bool STAB { set => moverand.rSTAB = value; }
    public IList<int> BannedMoves { set => moverand.BannedMoves = value; }
    public decimal STABPercent { set => moverand.rSTABPercent = value; }

    public void Execute()
    {
        for (var i = 0; i < Learnsets.Length; i++)
            Randomize(Learnsets[i], i);
    }

    private void Randomize(Learnset set, int index)
    {
        int[] moves = GetRandomMoves(set.Count, index);
        int[] levels = GetRandomLevels(set, moves.Length);

        if (Learn4Level1)
        {
            for (int i = 0; i < Math.Min(Level1Count, levels.Length); ++i)
                levels[i] = 1;
        }

        set.Moves = moves;
        set.Levels = levels;
    }

    private int[] GetRandomLevels(Learnset set, int count)
    {
        int[] levels = new int[count];
        if (count == 0)
            return levels;
        if (Spread)
        {
            levels[0] = 1;
            decimal increment = SpreadTo / (decimal)count;
            for (int i = 1; i < count; i++)
                levels[i] = Math.Clamp((int)Math.Round(i * increment), levels[i - 1], 100);
            return levels;
        }
        if (levels.Length == count && levels.Length == set.Levels.Length)
            return set.Levels; // don't modify

        var exist = set.Levels;
        int lastlevel = Math.Max(1, exist.LastOrDefault());
        exist.CopyTo(levels, 0);
        for (int i = exist.Length; i < levels.Length; i++)
            levels[i] = Math.Min(100, lastlevel + (i - exist.Length + 1));

        return levels;
    }

    private int[] GetRandomMoves(int count, int index)
    {
        count = Expand ? ExpandTo : count;

        int[] moves = new int[count];
        if (count == 0)
            return moves;
        var rand = moverand.GetRandomLearnset(index, count - 1);
        // The first move comes from a separate short list, so it could repeat one of the others
        // (about 40% of Fire learnsets listed Ember twice). Redraw it; a tiny pool may still repeat.
        for (int tries = 0; tries < 20 && (tries == 0 || Array.IndexOf(rand, moves[0]) >= 0); tries++)
            moves[0] = STABFirst ? moverand.GetRandomFirstMove(index) : MoveRandomizer.GetRandomFirstMoveAny();

        // STAB Moves (if requested) come first; randomize the order of moves
        Util.Shuffle(rand);
        if (OrderByPower)
            moverand.ReorderMovesPower(rand);
        rand.CopyTo(moves, 1);
        return moves;
    }

    public int[] GetHighPoweredMoves(int species, int form, int count = 4)
    {
        int index = Config.Personal.GetFormIndex(species, form);
        if (index < 0 || index >= Learnsets.Length || Learnsets[index]?.Moves == null)
            return new int[count];

        var moves = Learnsets[index].Moves
            .Where(move => move > 0 && Config?.Moves != null && move < Config.Moves.Length && Config.Moves[move] != null)
            .OrderByDescending(move => Config.Moves[move].Power)
            .Distinct()
            .Take(count)
            .ToArray();
        Array.Resize(ref moves, count);
        return moves;
    }

    public int[] GetCurrentMoves(int species, int form, int level, int count = 4)
    {
        int i = Config.Personal.GetFormIndex(species, form);
        if (i < 0 || i >= Learnsets.Length || Learnsets[i] == null)
            return new int[count];

        var moves = Learnsets[i].GetEncounterMoves(level);
        Array.Resize(ref moves, count);
        return moves;
    }
}