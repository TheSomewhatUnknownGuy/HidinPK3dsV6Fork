using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Randomizers.Competitive;

public enum BulkGoal { Mixed, Physical, Special }

public sealed record SpreadRequest(int[] Base, int[] Ivs, int Level, int AttackStat, int UnusedAttackStat,
    int SpeedTarget, bool SlowSpeed, BulkGoal Goal,
    int HazardHpModulus = 0, bool DenyDownloadSpA = false, bool SpeedCreep = false);

public static class CompetitiveMath
{
    public const int HP = 0, ATK = 1, DEF = 2, SPA = 3, SPD = 4, SPE = 5;

    // The stat each nature row (raised) and column (lowered) affects, in the game's nature order.
    private static readonly int[] NatureStat = [ATK, DEF, SPE, SPA, SPD];

    public static int[] ToEvOrder(int[] personalStats) =>
        [personalStats[0], personalStats[1], personalStats[2], personalStats[4], personalStats[5], personalStats[3]];

    public static byte MakeNature(int plusStat, int minusStat) =>
        (byte)(Array.IndexOf(NatureStat, plusStat) * 5 + Array.IndexOf(NatureStat, minusStat));

    public static int NaturePercent(byte nature, int stat)
    {
        int plus = NatureStat[nature / 5], minus = NatureStat[nature % 5];
        if (plus == minus) return 100;
        return stat == plus ? 110 : stat == minus ? 90 : 100;
    }

    public static int Stat(int stat, int baseStat, int iv, int ev, int level, int naturePercent)
    {
        int core = (2 * baseStat + iv + ev / 4) * level / 100;
        return stat == HP ? core + level + 10 : (core + 5) * naturePercent / 100;
    }

    public static int MinEvFor(int stat, int baseStat, int iv, int level, int naturePercent, int target)
    {
        for (int ev = 0; ev <= 252; ev += 4)
            if (Stat(stat, baseStat, iv, ev, level, naturePercent) >= target) return ev;
        return -1;
    }

    public static int Trim(int stat, int baseStat, int iv, int ev, int level, int naturePercent) =>
        MinEvFor(stat, baseStat, iv, level, naturePercent, Stat(stat, baseStat, iv, ev, level, naturePercent));

    public static (int[] Evs, byte Nature) OptimizeSpread(SpreadRequest r)
    {
        int[] bestEvs = null;
        byte bestNature = 0;
        double[] bestKey = null;

        for (byte n = 0; n < 25; n++)
        {
            int plus = NatureStat[n / 5], minus = NatureStat[n % 5];
            if (r.SlowSpeed && (minus != SPE || plus == SPE)) continue;

            var evs = new int[6];
            bool meets = false;
            if (r.SpeedTarget > 0)
            {
                int spe = MinEvFor(SPE, r.Base[SPE], r.Ivs[SPE], r.Level, NaturePercent(n, SPE), r.SpeedTarget + 1);
                meets = spe >= 0;
                evs[SPE] = Math.Max(0, spe); // out of reach: invest nothing rather than chase it
            }
            if (r.AttackStat >= 0)
                evs[r.AttackStat] = Trim(r.AttackStat, r.Base[r.AttackStat], r.Ivs[r.AttackStat], 252, r.Level, NaturePercent(n, r.AttackStat));

            FillBulk(r, n, evs);
            if (r.DenyDownloadSpA) DenyDownload(r, n, evs);
            for (int s = 0; s < 6; s++)
                evs[s] = Trim(s, r.Base[s], r.Ivs[s], evs[s], r.Level, NaturePercent(n, s));

            int attack = r.AttackStat < 0 ? 0 : StatOf(r, n, r.AttackStat, evs);
            var (goal, mixed) = Bulk(r.Goal, StatOf(r, n, HP, evs), StatOf(r, n, DEF, evs), StatOf(r, n, SPD, evs));
            double minusKey = plus != minus && minus == r.UnusedAttackStat ? 1 : 0;
            double[] key = r.SpeedCreep
                ? [attack, meets ? 1 : 0, meets ? -evs[SPE] : 0, goal, mixed, minusKey]
                : [meets ? 1 : 0, meets ? -evs[SPE] : 0, attack, goal, mixed, minusKey];

            if (bestKey == null || Compare(key, bestKey) > 0) { bestKey = key; bestEvs = evs; bestNature = n; }
        }
        return (bestEvs, bestNature);
    }

    private static int StatOf(SpreadRequest r, byte nature, int s, int[] evs) =>
        Stat(s, r.Base[s], r.Ivs[s], evs[s], r.Level, NaturePercent(nature, s));

    // The role's bulk objective, plus the mixed objective as its tie-break.
    private static (double Goal, double Mixed) Bulk(BulkGoal g, double hp, double def, double spd)
    {
        double mixed = hp * def * spd / (def + spd);
        return (g switch { BulkGoal.Physical => hp * def, BulkGoal.Special => hp * spd, _ => mixed }, mixed);
    }

    // Spends what 510 leaves (in steps of 4) on HP, Def and SpD: every HP x Def split, with SpD taking
    // the remainder. Stealth Rock parity filters HP values, unless no split passes.
    private static void FillBulk(SpreadRequest r, byte nature, int[] evs)
    {
        int budget = (510 - evs.Sum()) / 4 * 4;
        int[] Table(int s)
        {
            var t = new int[64];
            for (int i = 0; i < 64; i++) t[i] = Stat(s, r.Base[s], r.Ivs[s], i * 4, r.Level, NaturePercent(nature, s));
            return t;
        }
        int[] hpT = Table(HP), defT = Table(DEF), spdT = Table(SPD);

        for (int pass = 0; pass < 2; pass++)
        {
            int mod = pass == 0 ? r.HazardHpModulus : 0;
            double bestGoal = -1, bestMixed = -1;
            int bh = -1, bd = 0, bs = 0;
            for (int h = 0; h <= Math.Min(252, budget); h += 4)
            {
                if (mod > 0 && hpT[h / 4] % mod == 0) continue;
                for (int d = 0; d <= Math.Min(252, budget - h); d += 4)
                {
                    int s = Math.Min(252, budget - h - d);
                    var (goal, mixed) = Bulk(r.Goal, hpT[h / 4], defT[d / 4], spdT[s / 4]);
                    if (goal > bestGoal || (goal == bestGoal && mixed > bestMixed))
                    {
                        bestGoal = goal; bestMixed = mixed; bh = h; bd = d; bs = s;
                    }
                }
            }
            if (bh < 0) continue; // every HP value hit the parity filter; search again without it
            evs[HP] = bh; evs[DEF] = bd; evs[SPD] = bs;
            return;
        }
    }

    // Download raises the foe's Atk when our Def < SpD, else its SpA. The old engine spent 4 EVs on
    // this; keep the rule at no more than 8, taking unspent EVs first and then Def's.
    private static void DenyDownload(SpreadRequest r, byte nature, int[] evs)
    {
        if (StatOf(r, nature, SPD, evs) > StatOf(r, nature, DEF, evs)) return;
        for (int move = 4; move <= 8; move += 4)
        {
            var t = (int[])evs.Clone();
            if (t[SPD] + move > 252) return;
            int fromFree = Math.Min(move, (510 - t.Sum()) / 4 * 4);
            int fromDef = move - fromFree;
            if (fromDef > t[DEF]) return;
            t[DEF] -= fromDef; t[SPD] += move;
            if (StatOf(r, nature, SPD, t) > StatOf(r, nature, DEF, t)) { Array.Copy(t, evs, 6); return; }
        }
    }

    public static double ExpectedDamage(int power, int accuracy, int hitMin, int hitMax,
        bool stab, bool adaptability, double attackStat, bool chargeTurn, CompetitiveWeights w)
    {
        double p = power <= 1 ? w.VariablePowerDefault : power;
        double s = !stab ? 1 : adaptability ? w.AdaptabilityMultiplier : w.StabMultiplier;
        // The accuracy byte is 0 or above 100 for moves that never miss.
        double a = accuracy == 0 || accuracy > 100 ? 1 : accuracy / 100.0;
        double h = hitMax <= 1 ? 1 : hitMin == 2 && hitMax == 5 ? w.MultiHitAverage : (hitMin + hitMax) / 2.0;
        return p * s * a * attackStat * h * (chargeTurn ? w.ChargeTurnFactor : 1);
    }

    public static double StatusScore(double strongestStat, double listWeight, CompetitiveWeights w) =>
        w.StatusMoveBase * strongestStat * listWeight;

    public static double CoverageGain(int moveType, IReadOnlyCollection<int> setAttackTypes, CompetitiveWeights w)
    {
        double gain = 0;
        for (int t = 0; t < 18; t++)
        {
            float best = setAttackTypes.Count == 0 ? 1f : setAttackTypes.Max(st => TypeEffectivenessChart.GetEffectiveness(st, t));
            float mine = TypeEffectivenessChart.GetEffectiveness(moveType, t);
            if (mine > 1f && best <= 1f) gain += w.CoverageNewSuperEffective;
            else if (mine >= 1f && best < 1f) gain += w.CoverageResistedToNeutral;
        }
        return gain;
    }

    public static double Score(double damage, double coverageGain, double listWeight, CompetitiveWeights w) =>
        damage * (1 + w.CoverageWeight * coverageGain) * listWeight;

    public static int WeightedIndex(IReadOnlyList<double> weights, Random rnd)
    {
        double total = weights.Sum(x => x > 0 ? x : 0);
        if (total <= 0) return rnd.Next(weights.Count);
        double roll = rnd.NextDouble() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            roll -= weights[i] > 0 ? weights[i] : 0;
            if (roll < 0) return i;
        }
        // Floating-point leftovers land on the last item that can actually be picked.
        for (int i = weights.Count - 1; ; i--)
            if (weights[i] > 0) return i;
    }

    private static int Compare(double[] a, double[] b)
    {
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        return 0;
    }
}
