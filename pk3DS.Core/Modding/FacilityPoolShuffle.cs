#nullable enable

using System;
using System.Linq;

namespace pk3DS.Core.Modding;

public static class FacilityPoolShuffle
{
    public static int ShufflePwt(FacilityPools fp, Random rng)
    {
        var d = FacilityPools.Defs.First(x => x.Name == "PWT ids");
        int[] ids = fp.Read(d);
        for (int i = ids.Length - 1; i > 0; i--)   // Fisher-Yates
        {
            int j = rng.Next(i + 1);
            (ids[i], ids[j]) = (ids[j], ids[i]);
        }
        int changed = 0;
        for (int c = 0; c < ids.Length; c++)
        {
            if (fp.Get(d, c) == ids[c]) continue;
            fp.Set(d, c, ids[c]);
            changed++;
        }
        return changed;
    }
}
