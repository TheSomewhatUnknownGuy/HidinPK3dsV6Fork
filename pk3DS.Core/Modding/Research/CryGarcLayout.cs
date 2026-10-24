using System;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Reads the current base/form boundary in the `cry` GARC (a/0/7/3) directly from code.bin's own
/// 136(+)-entry form-lookup table (see <see cref="CryTablePatcher"/>), instead of trusting a
/// hardcoded member index. `SpeciesInsertionOrchestrator` needs this because the boundary this
/// session found (member 5552, verified against the real ROM) is only correct the FIRST time a new
/// species is added - each additional distinct form cry the table gains pushes it forward, and
/// nothing else in the GARC records that shift. The table's own clip values are the one piece of
/// data that is always current: every real, non-sentinel entry's clip - relative to
/// <see cref="CryTablePatcher.CryBaseRegionStart"/>, NOT an absolute member index, confirmed by
/// dumping this ROM's real table (all 66 non-sentinel entries land at exactly relative 218-283,
/// i.e. absolute 5552-5617) - points at a form CWAR, so the lowest one in use is, by definition,
/// exactly where the form region starts right now.
/// </summary>
public static class CryGarcLayout
{
    /// <summary>
    /// Lowest real (non-sentinel) clip value referenced anywhere in the live code.bin form table,
    /// converted to an absolute `cry` GARC member index - the current base/form boundary. Falls
    /// back to <paramref name="fallback"/> (the original, byte-verified value for this ROM) if the
    /// table has no real entries to read yet.
    /// </summary>
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
