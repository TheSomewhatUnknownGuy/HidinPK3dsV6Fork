#nullable enable

using System;
using System.Collections.Generic;

namespace pk3DS.Core.Randomizers;

public sealed class ZCrystalFilter
{
    private readonly HashSet<int> _banned = [];

    public IReadOnlySet<int> BannedIds => _banned;

    public int Count => _banned.Count;

    public bool IsZCrystal(int itemId) => _banned.Contains(itemId);

    public static bool IsZCrystalName(string? name)
    {
        string n = (name ?? "").Trim();
        if (n.Length < 5) return false;
        return n.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            && n.Contains("ium", StringComparison.OrdinalIgnoreCase);
    }

    public ZCrystalFilter(string[]? itemNames)
    {
        if (itemNames == null) return;
        for (int i = 1; i < itemNames.Length; i++)
            if (IsZCrystalName(itemNames[i]))
                _banned.Add(i);
    }

    public int PickAllowed(Func<int> roll, int attempts = 64)
    {
        int last = roll();
        for (int i = 0; i < attempts && IsZCrystal(last); i++) last = roll();
        return last;
    }
}
