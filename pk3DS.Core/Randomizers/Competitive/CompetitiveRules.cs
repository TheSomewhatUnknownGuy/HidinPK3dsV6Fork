namespace pk3DS.Core.Randomizers.Competitive;

public static class CompetitiveRules
{
    public const int ConstrainedPickAttempts = 30;

    public const int ShouldWeight = 4;

    public const int StrongShouldWeight = 8;

    public const int WeakShouldWeight = 2;

    public static int Weigh(bool satisfies, int weight = ShouldWeight) => satisfies ? weight : 1;
}
