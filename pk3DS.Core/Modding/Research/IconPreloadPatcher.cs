using System;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Raises the two ".word 1507" icon preload limits (code_map.csv "POKEMON ICONS", file 0x1D4C2C and
/// 0x1DF164, identical in US and UM). Both are LAST-member-inclusive bounds passed as r3 to the GARC
/// range-size/range-load calls (0x4B3CFC / 0x4B3B7C) at 0x2D4AB8 and 0x2DEFE8/0x2DF014; the buffer is
/// then sized from the GARC's own offsets, so the limit can grow with no fixed-size allocation to
/// overflow. Inclusive because the retail value 1153 is the Egg icon's own member, which must load.
/// </summary>
public static class IconPreloadPatcher
{
    public static readonly int[] LimitOffsets = [0x1D4C2C, 0x1DF164];

    /// <summary>Lowest value either word can plausibly hold (retail).</summary>
    private const uint RetailLimit = 1153;
    private const uint SaneMax = 0xFFFF;

    /// <summary>Current limit, or -1 when either word doesn't look like the documented preload bound.</summary>
    public static int Read(byte[] codeBin)
    {
        uint min = uint.MaxValue;
        foreach (int ofs in LimitOffsets)
        {
            if (ofs + 4 > codeBin.Length) return -1;
            uint v = BitConverter.ToUInt32(codeBin, ofs);
            if (v < RetailLimit || v > SaneMax) return -1;
            min = Math.Min(min, v);
        }
        return (int)min;
    }

    /// <summary>Ensures both limits are at least <paramref name="lastMember"/>. Returns an error, or null.</summary>
    public static string Raise(byte[] codeBin, int lastMember, out bool patched)
    {
        patched = false;
        if (Read(codeBin) < 0)
            return "icon preload limit words at 0x1D4C2C/0x1DF164 don't hold a plausible member count - refusing to touch them";
        if (lastMember > SaneMax) return $"icon member {lastMember} exceeds the preload word's sane range";

        foreach (int ofs in LimitOffsets)
        {
            if (BitConverter.ToUInt32(codeBin, ofs) >= (uint)lastMember) continue;
            BitConverter.GetBytes((uint)lastMember).CopyTo(codeBin, ofs);
            patched = true;
        }
        return null;
    }
}
