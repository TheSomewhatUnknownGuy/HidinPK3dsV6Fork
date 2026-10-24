using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace pk3DS.Core.Randomizers.Competitive;

/// <summary>Team jobs the synergy score rewards filling (v2 spec §3).</summary>
[Flags]
public enum TeamRoles { None = 0, Hazards = 1, HazardRemoval = 2, Pivot = 4, SpeedControl = 8 }

/// <summary>
/// How much a candidate species adds to a trainer's team so far (v2 spec §3). Pure: depends only on
/// TypeChart.cs and CompetitiveWeights, so tests/CompetitiveCheck compiles it on its own.
/// </summary>
public static class TeamSynergy
{
    public static double Score(int[] candidateTypes, TeamRoles candidateRoles,
        IReadOnlyList<int[]> teamTypes, TeamRoles teamRoles, int[][] cores, CompetitiveWeights w)
    {
        if (candidateTypes == null || candidateTypes.Length == 0) return 0;
        var team = teamTypes.Where(t => t is { Length: > 0 }).ToList();
        var teamTypeSet = team.SelectMany(t => t).ToHashSet();

        double score = Defensive(candidateTypes, team, w) + Cores(candidateTypes, teamTypeSet, cores, w);

        // Offensive coverage: a type no teammate's STAB hits super-effectively, but this candidate's does
        for (int t = 0; t < 18; t++)
        {
            bool teamHits = teamTypeSet.Any(a => TypeEffectivenessChart.GetEffectiveness(a, t) > 1f);
            bool candidateHits = candidateTypes.Any(a => TypeEffectivenessChart.GetEffectiveness(a, t) > 1f);
            if (!teamHits && candidateHits) score += w.OffensiveCoveragePerType;
        }

        score += w.RoleCoveragePerRole * BitOperations.PopCount((uint)(candidateRoles & ~teamRoles));

        // Mono types are stored as [t, t], so compare as sets. Penalties are negative.
        if (team.Any(t => t.ToHashSet().SetEquals(candidateTypes))) score += w.RedundantTypingPenalty;
        return score;
    }

    // Weakness stacking and covering (formerly TeamGenerator.ScoreDefensiveSynergy)
    private static double Defensive(int[] candidateTypes, List<int[]> team, CompetitiveWeights w)
    {
        double score = 0;
        for (int attackType = 0; attackType <= 17; attackType++)
        {
            float candMult = TypeEffectivenessChart.GetEffectiveness(attackType, candidateTypes);
            int weakCount = 0, resistCount = 0;
            foreach (var member in team)
            {
                float eff = TypeEffectivenessChart.GetEffectiveness(attackType, member);
                if (eff > 1f) weakCount++;
                else if (eff < 1f) resistCount++;
            }

            if (weakCount >= 2 && candMult > 1f)
                score += weakCount >= 3 ? w.StackedWeakness3Penalty : w.StackedWeakness2Penalty;
            else if (weakCount >= 2 && candMult < 1f)
                score += candMult == 0f ? w.CoverStackedWeaknessImmune : w.CoverStackedWeaknessResist;
            else if (weakCount == 1 && resistCount == 0 && candMult < 1f)
                score += candMult == 0f ? w.CoverLoneWeaknessImmune : w.CoverLoneWeaknessResist;
        }
        return score;
    }

    // Advancing or completing a three-type core (formerly TeamGenerator.ScoreCoreAffinity)
    private static double Cores(int[] candidateTypes, HashSet<int> teamTypes, int[][] cores, CompetitiveWeights w)
    {
        if (teamTypes.Count == 0) return 0;
        double score = 0;
        foreach (var core in cores)
        {
            int present = core.Count(teamTypes.Contains);
            if (present > 0 && present < core.Length && candidateTypes.Any(t => core.Contains(t) && !teamTypes.Contains(t)))
                score += present >= 2 ? w.CoreComplete : w.CoreAdvance;
        }
        return score;
    }
}
