using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.Randomizers.Competitive;

namespace pk3DS.Core.Randomizers;

public static class TypeRestrictions
{
    public const int TypeCount = 18;

    public static int[] TypesOf(GameConfig config, int species)
    {
        try
        {
            var p = config?.Personal?[species];
            if (p == null) return [];
            int t1 = p.Types[0];
            int t2 = p.Types.Length > 1 ? p.Types[1] : t1;
            return t1 == t2 ? [t1] : [t1, t2];
        }
        catch { return []; }
    }

    public static bool IsMonoType(GameConfig config, int species) => TypesOf(config, species).Length == 1;

    public static bool SharesType(GameConfig config, int a, int b)
    {
        var ta = TypesOf(config, a);
        var tb = TypesOf(config, b);
        return ta.Any(tb.Contains);
    }

    public static bool HasType(GameConfig config, int species, int type) => TypesOf(config, species).Contains(type);

    private static List<int> SuperEffectiveAgainst(int attacking)
    {
        var list = new List<int>();
        for (int d = 0; d < TypeCount; d++)
        {
            if (d == attacking) continue;
            if (TypeEffectivenessChart.GetEffectiveness(attacking, d) > 1f) list.Add(d);
        }
        return list;
    }

    public static List<int[]> FindTypeTriangles()
    {
        var found = new List<int[]>();
        var seen = new HashSet<string>();

        for (int one = 0; one < TypeCount; one++)
        {
            foreach (int two in SuperEffectiveAgainst(one))
            {
                foreach (int three in SuperEffectiveAgainst(two))
                {
                    if (three == one) continue;
                    if (TypeEffectivenessChart.GetEffectiveness(three, one) <= 1f) continue;

                    int[] triangle = [three, two, one];
                    if (seen.Add($"{three},{two},{one}")) found.Add(triangle);
                }
            }
        }
        return found;
    }

    public static int[] PickTypeTriangle(Random rand)
    {
        var all = FindTypeTriangles();
        return all.Count == 0
            ? [PokemonTypes.Fire, PokemonTypes.Water, PokemonTypes.Grass]
            : all[rand.Next(all.Count)];
    }

    public static bool AddsNewType(GameConfig config, IEnumerable<int> chosen, int candidate)
    {
        var have = new HashSet<int>();
        foreach (int s in chosen)
            foreach (int t in TypesOf(config, s)) have.Add(t);

        return TypesOf(config, candidate).Any(t => !have.Contains(t));
    }
}
