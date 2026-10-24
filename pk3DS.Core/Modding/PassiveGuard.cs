using System;

namespace pk3DS.Core.Modding;

public static class PassiveGuard
{
    public static bool Enabled { get; private set; }

    public static event Action<string> ForcedOff;

    public const string UhuruReason = "Passive abilities are not available on Uhuru: its trainer records already use bytes 0x0D and 0x16 (team level offset, colour), and its personal file layout is not safe to rewrite.";
    public const string ExpandedReason = "Passive abilities and Expanded Abilities (10-bit) both use personal byte 0x52 bits 4-6. Turn one off before enabling the other.";

    public static string TrySet(bool on, bool expandedAbilities)
    {
        if (!on) { Enabled = false; return null; }
        if (UhuruGuard.Active) return UhuruReason;
        if (expandedAbilities) return ExpandedReason;
        Enabled = true;
        return null;
    }

    public static string RefuseExpanded(bool wantExpanded) => wantExpanded && Enabled ? ExpandedReason : null;

    public static void EnforceOnOpen()
    {
        if (!Enabled || !UhuruGuard.Active) return;
        Enabled = false;
        ForcedOff?.Invoke(UhuruReason);
    }

    public const int MaxId = 345;

    public const int TmCap = 104;

    public const string TmCapReason = "Passive abilities use personal bytes 0x35-0x37, which are TM bits 104-127, so the TM list stops at 104 while they are on.";

    public static int[] Unpack(byte[] d, int[] lo, int hi, int shift)
    {
        var ids = new int[3];
        for (int i = 0; i < 3; i++) ids[i] = d[lo[i]] | (((d[hi] >> (shift + i)) & 1) << 8);
        return ids;
    }

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
