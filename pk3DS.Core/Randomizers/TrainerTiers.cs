using System;

using pk3DS.Core.Structures;

namespace pk3DS.Core.Randomizers;

public static class TrainerTiers
{
    public static readonly int[] BossClasses_USUM =
    [
        031, 049, 050, 051, 141, 164,   // Island Kahuna
        080, 107, 109, 110, 191,        // Elite Four
        076,                            // Team Skull Boss
        071, 082, 220,                  // Aether President
    ];

    public enum Tier
    {
        Regular,
        Important,
        Boss,
    }

    public static bool IsBoss(TrainerData7 trainer) =>
        trainer != null && Array.IndexOf(BossClasses_USUM, trainer.TrainerClass) >= 0;

    public static bool IsImportant(int index) =>
        Array.IndexOf(pk3DS.Core.Legal.ImportantTrainers_USUM, index) >= 0;

    public static Tier Of(TrainerData7 trainer, int index)
    {
        if (IsBoss(trainer)) return Tier.Boss;
        if (IsImportant(index)) return Tier.Important;
        return Tier.Regular;
    }

    public static T Pick<T>(TrainerData7 trainer, int index, T boss, T important, T regular) =>
        Of(trainer, index) switch
        {
            Tier.Boss => boss,
            Tier.Important => important,
            _ => regular,
        };
}
