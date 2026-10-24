using System;
using System.Linq;

namespace pk3DS.Core;

public static partial class Legal
{
    public static bool IsUsableMove(int id, string[] moveNames)
    {
        if (id <= 0 || moveNames == null || id >= moveNames.Length) return false;
        string name = moveNames[id];
        if (string.IsNullOrWhiteSpace(name) || name.Trim().All(c => c is '—' or '―' or '-')) return false;
        if (Array.IndexOf(Z_Moves, id) >= 0 || id is >= 743 and <= 774 or >= 921 and <= 953) return false;
        return !name.StartsWith("Max ", StringComparison.Ordinal) && !name.StartsWith("G-Max ", StringComparison.Ordinal);
    }
}
