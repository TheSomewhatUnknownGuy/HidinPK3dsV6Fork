using System;
using System.Linq;

namespace pk3DS.Core;

public static partial class Legal
{
    /// <summary>
    /// Whether a randomizer may hand out this move: it has a name that isn't a dash placeholder ("———", Korean "―――――"),
    /// and it isn't a Z-move, a Max Move or a G-Max move (Uhuru numbers those 743-774 and 921-953, and leaves 954-1022 unnamed).
    /// The id ranges catch Max/G-Max moves whatever the text language; the Expansion leaves 743-774 as placeholders and stops at 920,
    /// and vanilla stops at 728, so the ranges change nothing on those ROMs.
    /// </summary>
    public static bool IsUsableMove(int id, string[] moveNames)
    {
        if (id <= 0 || moveNames == null || id >= moveNames.Length) return false;
        string name = moveNames[id];
        if (string.IsNullOrWhiteSpace(name) || name.Trim().All(c => c is '—' or '―' or '-')) return false;
        if (Array.IndexOf(Z_Moves, id) >= 0 || id is >= 743 and <= 774 or >= 921 and <= 953) return false;
        return !name.StartsWith("Max ", StringComparison.Ordinal) && !name.StartsWith("G-Max ", StringComparison.Ordinal);
    }
}
