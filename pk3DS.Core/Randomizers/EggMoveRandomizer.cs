using System;
using pk3DS.Core.Structures;

namespace pk3DS.Core.Randomizers;

public class EggMoveRandomizer(GameConfig config, EggMoves[] sets) : IRandomizer
{
    /*
     * 3111 Egg Moves Learned by 290 Species (10.73 avg)
     * 18 is the most
     * 1000 moves learned were STAB (32.1%)
     */
    private readonly MoveRandomizer moverand = new(config);

    public bool Expand = true;
    public int ExpandTo = 18;
    public bool STAB { set => moverand.rSTAB = value; }
    public int[] BannedMoves { set => moverand.BannedMoves = value; }
    public decimal STABPercent { set => moverand.rSTABPercent = value; }

    public void Execute()
    {
        if (sets == null || sets.Length == 0 || sets[0] == null) return;

        if (sets[0] is EggMoves6)
        {
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i] != null)
                    Randomize(sets[i], i);
            }
        }
        else if (sets[0] is EggMoves7)
        {
            int max = Math.Min(config.MaxSpeciesID, Math.Min(sets.Length - 1, (config.Personal?.Table?.Length ?? 1) - 1));
            for (int i = 1; i <= max; i++)
            {
                if (sets[i] == null || config.Personal?[i] == null) continue;
                Randomize(sets[i], i);
                int formoff = ((EggMoves7)sets[i]).FormTableIndex;
                int count = config.Personal[i].FormeCount;
                if (formoff > 0)
                {
                    for (int j = 1; j < count; j++)
                    {
                        int targetIdx = formoff + j - 1;
                        if (targetIdx < sets.Length && sets[targetIdx] != null)
                            Randomize(sets[targetIdx], config.Personal.GetFormIndex(i, j));
                    }
                }
            }
        }
    }

    private void Randomize(EggMoves eggMoves, int index)
    {
        int count = Expand ? ExpandTo : eggMoves.Count;
        eggMoves.Moves = GetRandomMoves(count, index);
    }

    private int[] GetRandomMoves(int count, int index)
    {
        count = Expand ? ExpandTo : count;
        int[] moves = new int[count];
        moverand.GetRandomLearnset(index, count).CopyTo(moves, 0);
        return moves;
    }
}