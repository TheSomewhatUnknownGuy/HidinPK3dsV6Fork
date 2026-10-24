using System;

namespace pk3DS.Core.Modding.Research;

public static class CryGarcLayout
{
    public static int ComputeCurrentBaseFormBoundary(byte[] codeBin, int fallback = 5552)
    {
        if (codeBin == null || CryTablePatcher.TableStart + 4 > codeBin.Length) return fallback;

        int minRelative = int.MaxValue;
        for (int off = CryTablePatcher.TableStart; off + 4 <= codeBin.Length; off += 4)
        {
            ushort key = BitConverter.ToUInt16(codeBin, off);
            ushort clip = BitConverter.ToUInt16(codeBin, off + 2);
            if (key == 0xFFFF && clip == CryTablePatcher.NoDistinctCry) break; // end-of-table terminator
            if (clip == CryTablePatcher.NoDistinctCry) continue; // per-entry "no distinct cry" sentinel, not a real reference
            if (clip < minRelative) minRelative = clip;
        }
        return minRelative == int.MaxValue ? fallback : CryTablePatcher.CryBaseRegionStart + minRelative;
    }
}
