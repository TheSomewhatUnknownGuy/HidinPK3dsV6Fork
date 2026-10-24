#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace pk3DS.Core.Modding;

public enum FlagTrigger
{
    None = 0,

    Ability = 1,

    Item = 2,
}

public sealed class AbilityFlagBinding
{
    public int Bit { get; set; }

    public FlagTrigger Trigger { get; set; } = FlagTrigger.None;

    public int TriggerId { get; set; } = -1;

    public string TriggerName { get; set; } = "";

    public double Multiplier { get; set; } = 1.3;

    public string Label { get; set; } = "";

    public bool IsBound => Trigger != FlagTrigger.None && TriggerId >= 0;

    public int AbilityId
    {
        get => Trigger == FlagTrigger.Ability ? TriggerId : -1;
        set
        {
            if (value < 0) return;
            TriggerId = value;
            if (Trigger == FlagTrigger.None) Trigger = FlagTrigger.Ability;
        }
    }

    public string AbilityName
    {
        get => Trigger == FlagTrigger.Ability ? TriggerName : "";
        set { if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(TriggerName)) TriggerName = value; }
    }
}

public static class AbilityMoveFlags
{
    public const int FirstFreeBit = 17;

    public const int LastBit = 31;

    private static readonly Dictionary<int, AbilityFlagBinding> Bindings = [];
    private static string _path = "";
    private static bool _loaded;

    public static void SetStorePath(string path)
    {
        if (_path == path) return;
        _path = path;
        _loaded = false;
        Bindings.Clear();
    }

    public static IReadOnlyCollection<AbilityFlagBinding> All
    {
        get { EnsureLoaded(); return Bindings.Values; }
    }

    public static AbilityFlagBinding? Get(int bit)
    {
        EnsureLoaded();
        return Bindings.TryGetValue(bit, out var b) ? b : null;
    }

    public static bool IsFreeBit(int bit) => bit is >= FirstFreeBit and <= LastBit;

    public static void Set(int bit, FlagTrigger trigger, int triggerId, string triggerName, double multiplier, string label)
    {
        if (!IsFreeBit(bit))
            throw new ArgumentOutOfRangeException(nameof(bit),
                $"Only bits {FirstFreeBit}-{LastBit} are unused and safe to reassign.");

        EnsureLoaded();
        Bindings[bit] = new AbilityFlagBinding
        {
            Bit = bit,
            Trigger = triggerId >= 0 ? trigger : FlagTrigger.None,
            TriggerId = triggerId,
            TriggerName = triggerName ?? "",
            Multiplier = multiplier,
            Label = string.IsNullOrWhiteSpace(label)
                ? (triggerId >= 0 ? triggerName ?? "" : $"F{bit + 1}")
                : label,
        };
        Save();
    }

    public static void Clear(int bit)
    {
        EnsureLoaded();
        if (Bindings.Remove(bit)) Save();
    }

    public static string Describe(int bit)
    {
        var b = Get(bit);
        if (b == null) return $"F{bit + 1}";
        if (!b.IsBound) return string.IsNullOrWhiteSpace(b.Label) ? $"F{bit + 1}" : b.Label;
        string kind = b.Trigger == FlagTrigger.Item ? "item" : "ability";
        return $"{b.Label} ({kind}: {b.TriggerName} x{b.Multiplier:0.##})";
    }

    public static int BitFor(FlagTrigger trigger, int triggerId)
    {
        EnsureLoaded();
        foreach (var b in Bindings.Values)
        {
            if (b.Trigger == trigger && b.TriggerId == triggerId) return b.Bit;
        }
        return -1;
    }

    public static double GetMultiplier(uint moveFlags, int abilityId, int itemId = -1)
    {
        EnsureLoaded();
        double result = 1.0;
        foreach (var b in Bindings.Values)
        {
            if (!b.IsBound) continue;
            if ((moveFlags & (1u << b.Bit)) == 0) continue;

            bool applies = b.Trigger switch
            {
                FlagTrigger.Ability => b.TriggerId == abilityId,
                FlagTrigger.Item => itemId >= 0 && b.TriggerId == itemId,
                _ => false,
            };
            if (applies) result *= b.Multiplier;
        }
        return result;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Bindings.Clear();

        if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<AbilityFlagBinding>>(File.ReadAllText(_path));
            if (list == null) return;
            foreach (var b in list.Where(b => IsFreeBit(b.Bit)))
                Bindings[b.Bit] = b;
        }
        catch
        {
            // A damaged store must not stop the editor opening; it just starts empty.
        }
    }

    private static void Save()
    {
        if (string.IsNullOrEmpty(_path)) return;
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(
                Bindings.Values.OrderBy(b => b.Bit).ToList(),
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* the binding is still live in memory for this session */ }
    }
}
