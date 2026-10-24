using System.Collections.Generic;

namespace pk3DS.Core.Randomizers.Competitive;

// Pokémon type constants matching the game's internal type IDs (Gen 6/7).
public static class PokemonTypes
{
    public const int Normal = 0, Fighting = 1, Flying = 2, Poison = 3, Ground = 4,
                     Rock = 5, Bug = 6, Ghost = 7, Steel = 8, Fire = 9,
                     Water = 10, Grass = 11, Electric = 12, Psychic = 13,
                     Ice = 14, Dragon = 15, Dark = 16, Fairy = 17;
}

// Gen 6+ type effectiveness chart (attacker type -> defender type -> multiplier).
// Only non-neutral (not 1x) matchups are stored; anything absent defaults to 1x.
public static class TypeEffectivenessChart
{
    private static readonly Dictionary<(int Atk, int Def), float> Chart = BuildChart();

    public static float GetEffectiveness(int attackType, int defendType)
    {
        return Chart.TryGetValue((attackType, defendType), out float mult) ? mult : 1f;
    }

    // Effectiveness of attackType against a Pokemon with the given (1 or 2) defending types.
    public static float GetEffectiveness(int attackType, int[] defendTypes)
    {
        if (defendTypes == null || defendTypes.Length == 0) return 1f;
        float mult = GetEffectiveness(attackType, defendTypes[0]);
        if (defendTypes.Length > 1 && defendTypes[1] != defendTypes[0])
            mult *= GetEffectiveness(attackType, defendTypes[1]);
        return mult;
    }

    private static Dictionary<(int, int), float> BuildChart()
    {
        var c = new Dictionary<(int, int), float>();
        void Set(int atk, int def, float mult) => c[(atk, def)] = mult;

        int Normal = PokemonTypes.Normal, Fighting = PokemonTypes.Fighting, Flying = PokemonTypes.Flying,
            Poison = PokemonTypes.Poison, Ground = PokemonTypes.Ground, Rock = PokemonTypes.Rock,
            Bug = PokemonTypes.Bug, Ghost = PokemonTypes.Ghost, Steel = PokemonTypes.Steel,
            Fire = PokemonTypes.Fire, Water = PokemonTypes.Water, Grass = PokemonTypes.Grass,
            Electric = PokemonTypes.Electric, Psychic = PokemonTypes.Psychic, Ice = PokemonTypes.Ice,
            Dragon = PokemonTypes.Dragon, Dark = PokemonTypes.Dark, Fairy = PokemonTypes.Fairy;

        Set(Normal, Rock, 0.5f); Set(Normal, Ghost, 0f); Set(Normal, Steel, 0.5f);

        Set(Fighting, Normal, 2f); Set(Fighting, Flying, 0.5f); Set(Fighting, Poison, 0.5f);
        Set(Fighting, Rock, 2f); Set(Fighting, Bug, 0.5f); Set(Fighting, Ghost, 0f);
        Set(Fighting, Steel, 2f); Set(Fighting, Psychic, 0.5f); Set(Fighting, Ice, 2f);
        Set(Fighting, Dark, 2f); Set(Fighting, Fairy, 0.5f);

        Set(Flying, Fighting, 2f); Set(Flying, Rock, 0.5f); Set(Flying, Bug, 2f);
        Set(Flying, Steel, 0.5f); Set(Flying, Grass, 2f); Set(Flying, Electric, 0.5f);

        Set(Poison, Poison, 0.5f); Set(Poison, Ground, 0.5f); Set(Poison, Rock, 0.5f);
        Set(Poison, Ghost, 0.5f); Set(Poison, Steel, 0f); Set(Poison, Grass, 2f); Set(Poison, Fairy, 2f);

        Set(Ground, Flying, 0f); Set(Ground, Poison, 2f); Set(Ground, Rock, 2f);
        Set(Ground, Bug, 0.5f); Set(Ground, Steel, 2f); Set(Ground, Fire, 2f);
        Set(Ground, Grass, 0.5f); Set(Ground, Electric, 2f);

        Set(Rock, Fighting, 0.5f); Set(Rock, Flying, 2f); Set(Rock, Ground, 0.5f);
        Set(Rock, Bug, 2f); Set(Rock, Steel, 0.5f); Set(Rock, Fire, 2f); Set(Rock, Ice, 2f);

        Set(Bug, Fighting, 0.5f); Set(Bug, Flying, 0.5f); Set(Bug, Poison, 0.5f);
        Set(Bug, Ghost, 0.5f); Set(Bug, Steel, 0.5f); Set(Bug, Fire, 0.5f); Set(Bug, Grass, 2f);
        Set(Bug, Psychic, 2f); Set(Bug, Dark, 2f); Set(Bug, Fairy, 0.5f);

        Set(Ghost, Normal, 0f); Set(Ghost, Ghost, 2f); Set(Ghost, Dark, 0.5f); Set(Ghost, Psychic, 2f);

        Set(Steel, Rock, 2f); Set(Steel, Steel, 0.5f); Set(Steel, Fire, 0.5f);
        Set(Steel, Water, 0.5f); Set(Steel, Electric, 0.5f); Set(Steel, Ice, 2f); Set(Steel, Fairy, 2f);

        Set(Fire, Rock, 0.5f); Set(Fire, Bug, 2f); Set(Fire, Steel, 2f); Set(Fire, Fire, 0.5f);
        Set(Fire, Water, 0.5f); Set(Fire, Grass, 2f); Set(Fire, Ice, 2f); Set(Fire, Dragon, 0.5f);

        Set(Water, Ground, 2f); Set(Water, Rock, 2f); Set(Water, Fire, 2f); Set(Water, Water, 0.5f);
        Set(Water, Grass, 0.5f); Set(Water, Dragon, 0.5f);

        Set(Grass, Flying, 0.5f); Set(Grass, Poison, 0.5f); Set(Grass, Ground, 2f); Set(Grass, Rock, 2f);
        Set(Grass, Bug, 0.5f); Set(Grass, Steel, 0.5f); Set(Grass, Fire, 0.5f); Set(Grass, Water, 2f);
        Set(Grass, Grass, 0.5f); Set(Grass, Dragon, 0.5f);

        Set(Electric, Flying, 2f); Set(Electric, Ground, 0f); Set(Electric, Water, 2f);
        Set(Electric, Grass, 0.5f); Set(Electric, Electric, 0.5f); Set(Electric, Dragon, 0.5f);

        Set(Psychic, Fighting, 2f); Set(Psychic, Poison, 2f); Set(Psychic, Steel, 0.5f);
        Set(Psychic, Psychic, 0.5f); Set(Psychic, Dark, 0f);

        Set(Ice, Flying, 2f); Set(Ice, Ground, 2f); Set(Ice, Steel, 0.5f); Set(Ice, Fire, 0.5f);
        Set(Ice, Water, 0.5f); Set(Ice, Grass, 2f); Set(Ice, Ice, 0.5f); Set(Ice, Dragon, 2f);

        Set(Dragon, Steel, 0.5f); Set(Dragon, Dragon, 2f); Set(Dragon, Fairy, 0f);

        Set(Dark, Fighting, 0.5f); Set(Dark, Ghost, 2f); Set(Dark, Psychic, 2f);
        Set(Dark, Dark, 0.5f); Set(Dark, Fairy, 0.5f);

        Set(Fairy, Fighting, 2f); Set(Fairy, Poison, 0.5f); Set(Fairy, Steel, 0.5f);
        Set(Fairy, Fire, 0.5f); Set(Fairy, Dragon, 2f); Set(Fairy, Dark, 2f);

        return c;
    }
}
