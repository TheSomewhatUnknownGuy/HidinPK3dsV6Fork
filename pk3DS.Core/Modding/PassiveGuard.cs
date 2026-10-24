using System;

namespace pk3DS.Core.Modding;

/// <summary>
/// Rom Nueva + passive abilities: personal bytes 0x35-0x37 (+ 0x52 bits 4-6) and trainer bytes 0x0D/0x0F/0x13 (+ 0x16 bits 0-2)
/// hold passive ability ids. A setting, not detection: the bytes are repurposed, so data alone cannot tell the mod's ROM from a stock one.
/// Pure on purpose (no GameConfig dependency, like <see cref="UhuruGuard"/>), so CompetitiveCheck can run it.
/// Change <see cref="Enabled"/> only through <see cref="TrySet"/>, so the exclusions hold.
/// </summary>
public static class PassiveGuard
{
    public static bool Enabled { get; private set; }

    /// <summary>Raised when opening a ROM switched passives off (Uhuru). The UI unticks its menu item and tells the user why.</summary>
    public static event Action<string> ForcedOff;

    public const string UhuruReason = "Passive abilities are not available on Uhuru: its trainer records already use bytes 0x0D and 0x16 (team level offset, colour), and its personal file layout is not safe to rewrite.";
    public const string ExpandedReason = "Passive abilities and Expanded Abilities (10-bit) both use personal byte 0x52 bits 4-6. Turn one off before enabling the other.";

    /// <summary>Turns passives on or off. Returns null on success, else the reason it was refused (state unchanged).</summary>
    public static string TrySet(bool on, bool expandedAbilities)
    {
        if (!on) { Enabled = false; return null; }
        if (UhuruGuard.Active) return UhuruReason;
        if (expandedAbilities) return ExpandedReason;
        Enabled = true;
        return null;
    }

    /// <summary>Reason Expanded Abilities cannot be turned on right now, or null.</summary>
    public static string RefuseExpanded(bool wantExpanded) => wantExpanded && Enabled ? ExpandedReason : null;

    /// <summary>Run on every ROM open, after Uhuru detection.</summary>
    public static void EnforceOnOpen()
    {
        if (!Enabled || !UhuruGuard.Active) return;
        Enabled = false;
        ForcedOff?.Invoke(UhuruReason);
    }

    /// <summary>Highest passive ability id the mod reads; above it the game ignores the slot.</summary>
    public const int MaxId = 345;

    /// <summary>TMs the personal table can hold while passives own bits 104-127 (bytes 0x35-0x37).</summary>
    public const int TmCap = 104;

    public const string TmCapReason = "Passive abilities use personal bytes 0x35-0x37, which are TM bits 104-127, so the TM list stops at 104 while they are on.";

    /// <summary>Ids 0..2 low bytes at <paramref name="lo"/>, bit 8 of each in <paramref name="hi"/> bits <paramref name="shift"/>..+2. 0 means none.</summary>
    public static int[] Unpack(byte[] d, int[] lo, int hi, int shift)
    {
        var ids = new int[3];
        for (int i = 0; i < 3; i++) ids[i] = d[lo[i]] | (((d[hi] >> (shift + i)) & 1) << 8);
        return ids;
    }

    /// <summary>Writes three ids, leaving every other bit of the high byte alone. Throws on a wrong count or an id outside 0..<see cref="MaxId"/>.</summary>
    public static void Pack(byte[] d, int[] lo, int hi, int shift, int[] ids)
    {
        if (ids?.Length != 3) throw new ArgumentException("Passives need exactly 3 ids.", nameof(ids));
        foreach (int id in ids)
            if (id is < 0 or > MaxId) throw new ArgumentOutOfRangeException(nameof(ids), id, $"Passive ability ids are 0..{MaxId}.");
        int bits = 0;
        for (int i = 0; i < 3; i++) { d[lo[i]] = (byte)ids[i]; bits |= ((ids[i] >> 8) & 1) << i; }
        d[hi] = (byte)((d[hi] & ~(7 << shift)) | (bits << shift));
    }
}
