using System;
using System.Linq;

using pk3DS.Core.Structures;

namespace pk3DS.Core.Randomizers;

public static class FirstRivalBattle
{
    public static readonly int[] TrainerIds = [491, 492, 493];

    public const byte Level = 5;

    public static bool Is(int index) => Array.IndexOf(TrainerIds, index) >= 0;

    public static bool Matches(int index, TrainerData7 trainer) =>
        Is(index) && trainer?.Pokemon is { Count: > 0 } &&
        trainer.Pokemon.Count <= 2 && trainer.Pokemon[0].Level <= 10;

    public static void Enforce(TrainerData7 trainer)
    {
        if (trainer?.Pokemon is not { Count: > 0 }) return;

        if (trainer.Pokemon.Count > 1)
            trainer.Pokemon.RemoveRange(1, trainer.Pokemon.Count - 1);

        trainer.Pokemon[0].Level = Level;
        trainer.NumPokemon = trainer.Pokemon.Count;

        // Singles regardless of the Battle Style setting: a doubles tutorial battle would ask the
        // player for a second Pokemon they do not have yet.
        trainer.Mode = BattleMode.Singles;
    }
}
