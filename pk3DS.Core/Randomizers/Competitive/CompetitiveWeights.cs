using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace pk3DS.Core.Randomizers.Competitive;

public sealed class CompetitiveWeights
{
    // Manifesto 0.2 / 0.6 (CompetitiveRules keeps the documented defaults)
    public int ConstrainedPickAttempts { get; set; } = CompetitiveRules.ConstrainedPickAttempts;
    public double ShouldWeight { get; set; } = CompetitiveRules.ShouldWeight;
    public double WeakShouldWeight { get; set; } = CompetitiveRules.WeakShouldWeight;

    // Spec §1: speed benchmark (beat this Pokemon at the same level)
    public int SpeedBenchmarkBase { get; set; } = 100;
    public int SpeedBenchmarkIv { get; set; } = 31;
    public int SpeedBenchmarkEv { get; set; } = 252;
    public int SpeedBenchmarkNaturePercent { get; set; } = 110;

    // Spec §2: move scoring
    public double VariablePowerDefault { get; set; } = 80;
    public double StabMultiplier { get; set; } = 1.5;
    public double AdaptabilityMultiplier { get; set; } = 2.0;
    public double MultiHitAverage { get; set; } = 3.1;
    public double ChargeTurnFactor { get; set; } = 0.5;
    public double CoverageWeight { get; set; } = 0.5;
    public double CoverageNewSuperEffective { get; set; } = 1.0;
    public double CoverageResistedToNeutral { get; set; } = 0.5;
    public double StatusMoveBase { get; set; } = 60;
    public double WeakPriorityFactor { get; set; } = 0.1;
    public double SpreadMoveFactor { get; set; } = 1.5;

    // Spec §3: team synergy (penalties are negative and added)
    public double StackedWeakness3Penalty { get; set; } = -60;
    public double StackedWeakness2Penalty { get; set; } = -40;
    public double CoverStackedWeaknessImmune { get; set; } = 50;
    public double CoverStackedWeaknessResist { get; set; } = 35;
    public double CoverLoneWeaknessImmune { get; set; } = 25;
    public double CoverLoneWeaknessResist { get; set; } = 15;
    public double CoreAdvance { get; set; } = 50;
    public double CoreComplete { get; set; } = 100;
    public double OffensiveCoveragePerType { get; set; } = 20;
    public double RoleCoveragePerRole { get; set; } = 40;
    public double RedundantTypingPenalty { get; set; } = -30;
    public double SynergyAcceptScore { get; set; } = 100;

    // Allowed range per key; anything not listed is [0, +inf).
    private static readonly Dictionary<string, (double Min, double Max)> Limits = new()
    {
        [nameof(ConstrainedPickAttempts)] = (1, 1000),
        [nameof(SpeedBenchmarkBase)] = (1, 255),
        [nameof(SpeedBenchmarkIv)] = (0, 31),
        [nameof(SpeedBenchmarkEv)] = (0, 252),
        [nameof(SpeedBenchmarkNaturePercent)] = (90, 110),
        [nameof(StackedWeakness3Penalty)] = (double.NegativeInfinity, 0),
        [nameof(StackedWeakness2Penalty)] = (double.NegativeInfinity, 0),
        [nameof(RedundantTypingPenalty)] = (double.NegativeInfinity, 0),
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "competitive_weights.json");

    public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);

    public void Save(string path) => File.WriteAllText(path, ToJson());

    public static CompetitiveWeights Parse(string json, List<string> problems)
    {
        var w = new CompetitiveWeights();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, ReadOptions); }
        catch (JsonException ex) { problems.Add($"not valid JSON ({ex.Message}); using all defaults"); return w; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                problems.Add("the file isn't a JSON object; using all defaults");
                return w;
            }
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var prop = typeof(CompetitiveWeights).GetProperty(p.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null) { problems.Add($"{p.Name}: unknown key, ignored"); continue; }

                object fallback = prop.GetValue(w);
                if (p.Value.ValueKind != JsonValueKind.Number) { problems.Add($"{p.Name}: not a number, default {fallback} used"); continue; }

                double v = p.Value.GetDouble();
                if (!double.IsFinite(v)) { problems.Add($"{p.Name}: {v} is not a finite number, default {fallback} used"); continue; }
                var (min, max) = Limits.TryGetValue(prop.Name, out var l) ? l : (0, double.PositiveInfinity);
                if (v < min || v > max)
                {
                    string why = min == 0 && v < 0 ? "is negative"
                               : max == 0 && v > 0 ? "is positive (penalties must be 0 or below)"
                               : $"must be {min}-{max}";
                    problems.Add($"{p.Name}: {v} {why}, default {fallback} used");
                    continue;
                }
                if (prop.PropertyType == typeof(int))
                {
                    if (v != Math.Floor(v)) { problems.Add($"{p.Name}: {v} is not a whole number, default {fallback} used"); continue; }
                    prop.SetValue(w, (int)v);
                }
                else prop.SetValue(w, v);
            }
        }
        return w;
    }

    public static CompetitiveWeights Load(string path, List<string> problems)
    {
        try
        {
            if (!File.Exists(path))
            {
                var defaults = new CompetitiveWeights();
                defaults.Save(path);
                return defaults;
            }
            return Parse(File.ReadAllText(path), problems);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{Path.GetFileName(path)}: {ex.Message}; using all defaults");
            return new CompetitiveWeights();
        }
    }
}
