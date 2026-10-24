#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public sealed class TemplateModel
{
    public CustomMechanicKind? Kind { get; init; }

    public string Name { get; init; } = "";

    public byte? Slot { get; init; }
}

public sealed class TimingSuggestion
{
    public byte? Timing { get; init; }

    public List<byte> Options { get; init; } = [];

    public string Source { get; init; } = "";

    public bool Certain => Timing.HasValue;
}

public static class TemplateTiming
{
    public static TimingSuggestion Suggest(FunctionTemplate template, BattleMechanicMap? map)
    {
        // A timing the engine settles rather than a stock element wins outright, and is the one
        // answer that does not need a ROM open to give.
        if (template.FixedTiming is { } fixedTiming)
        {
            return new TimingSuggestion
            {
                Timing = fixedTiming,
                Options = [fixedTiming],
                Source = $"the routine this calls runs at 0x{fixedTiming:X2}",
            };
        }

        var model = template.ModelledOn;

        if (map == null)
            return Recorded(model, "no ROM is loaded");

        if (model == null || string.IsNullOrWhiteSpace(model.Name))
            return FromUsage(template, map, "This shape is not modelled on one stock element");

        var kind = model.Kind ?? template.Kind;
        var found = map.Find(kind, model.Name);

        if (found == null)
        {
            var recorded = Recorded(model, $"'{model.Name}' is not in this ROM's {kind.ToString().ToLowerInvariant()} table");
            if (recorded.Certain) return recorded;
            return FromUsage(template, map,
                $"'{model.Name}' is not in this ROM's {kind.ToString().ToLowerInvariant()} table");
        }

        var slots = found.Slots.Select(s => s.Timing).Distinct().ToList();

        if (slots.Count == 0)
            return FromUsage(template, map, $"{model.Name} has no timing slots in this ROM");

        // A named slot only counts if the element really has it here.
        if (model.Slot is { } want && slots.Contains(want))
        {
            return new TimingSuggestion
            {
                Timing = want,
                Options = slots,
                Source = $"{model.Name} uses 0x{want:X2} for this in your ROM"
                       + (slots.Count > 1 ? $" (it also hooks {Join(slots.Where(s => s != want))})" : ""),
            };
        }

        if (slots.Count == 1)
        {
            return new TimingSuggestion
            {
                Timing = slots[0],
                Options = slots,
                Source = $"{model.Name} hooks exactly one timing in your ROM: 0x{slots[0]:X2}",
            };
        }

        return new TimingSuggestion
        {
            Options = slots,
            Source = $"{model.Name} hooks {slots.Count} timings in your ROM ({Join(slots)}) - pick the one you want",
        };
    }

    private static TimingSuggestion Recorded(TemplateModel? model, string why)
    {
        if (model?.Slot is not { } slot)
            return new TimingSuggestion { Source = $"The timing cannot be looked up: {why}." };

        return new TimingSuggestion
        {
            Timing = slot,
            Options = [slot],
            Source = $"0x{slot:X2}, recorded from {model.Name} (not confirmed here: {why})",
        };
    }

    private static TimingSuggestion FromUsage(FunctionTemplate template, BattleMechanicMap map, string why)
    {
        var ranked = map.OfKind(template.Kind)
            .SelectMany(m => m.Slots.Select(s => s.Timing))
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(8)
            .ToList();

        return new TimingSuggestion
        {
            Options = ranked,
            Source = ranked.Count == 0
                ? $"{why}, and no {template.Kind.ToString().ToLowerInvariant()} timings were found."
                : $"{why}. Most used by {template.Kind.ToString().ToLowerInvariant()}s here: {Join(ranked)}",
        };
    }

    private static string Join(IEnumerable<byte> timings) =>
        string.Join(", ", timings.Select(t => $"0x{t:X2}"));
}
