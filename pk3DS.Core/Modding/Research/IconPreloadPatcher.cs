using System;

namespace pk3DS.Core.Modding.Research;

public static class IconPreloadPatcher
{
    public static readonly int[] LimitOffsets = [0x1D4C2C, 0x1DF164];

    private const uint RetailLimit = 1153;
    private const uint SaneMax = 0xFFFF;

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
