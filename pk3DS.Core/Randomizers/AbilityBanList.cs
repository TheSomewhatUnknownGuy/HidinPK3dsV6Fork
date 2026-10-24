#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Randomizers;

public sealed class AbilityBanList
{
    public static readonly string[] TrappingAbilities =
        ["Shadow Tag", "Arena Trap", "Magnet Pull"];

    public static readonly string[] NegativeAbilities =
    [
        "Truant", "Slow Start", "Defeatist", "Stall", "Normalize",
        "Klutz", "Comatose",
    ];

    public static readonly string[] BadAbilities =
    [
        "Honey Gather", "Illuminate", "Run Away", "Stench", "Pickup",
        "Ball Fetch", "Friend Guard", "Healer", "Telepathy", "Rivalry",
        "Anticipation", "Forewarn", "Frisk", "Keen Eye", "Minus", "Plus",
    ];

    private readonly HashSet<int> _banned = [];

    public IReadOnlySet<int> BannedIds => _banned;

    public int Count => _banned.Count;

    public bool IsBanned(int abilityId) => _banned.Contains(abilityId);

    public bool IsBanned(string? name) =>
        !string.IsNullOrEmpty(name) && _names.Contains(name);

    private readonly HashSet<string> _names = new(Competitive.GameNameComparer.Instance);

    public AbilityBanList(string[]? abilityNames, UniversalSettings? settings)
    {
        if (abilityNames == null || abilityNames.Length == 0 || settings == null) return;

        void Ban(IEnumerable<string> names)
        {
            foreach (string n in names)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                _names.Add(n);
                int id = Array.FindIndex(abilityNames, a => a != null && Competitive.GameNameComparer.Instance.Equals(a, n));
                if (id > 0) _banned.Add(id);
            }
        }

        if (!settings.AllowWonderGuard) Ban(["Wonder Guard"]);
        if (settings.BanTrappingAbilities) Ban(TrappingAbilities);
        if (settings.BanNegativeAbilities) Ban(NegativeAbilities);
        if (settings.BanBadAbilities) Ban(BadAbilities);
        if (settings.BannedAbilityNames is { Count: > 0 }) Ban(settings.BannedAbilityNames);
    }

    public List<T> Filter<T>(List<T> candidates, Func<T, int> abilityIdOf)
    {
        if (_banned.Count == 0 || candidates.Count == 0) return candidates;
        var kept = candidates.Where(c => !_banned.Contains(abilityIdOf(c))).ToList();
        return kept.Count > 0 ? kept : candidates;
    }
}
