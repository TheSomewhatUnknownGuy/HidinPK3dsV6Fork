#nullable enable
using System;
using System.Linq;

namespace pk3DS.Core.Modding;

public static class TMCompat
{
    private const int BitsOffset = 0x28;

    public static int[] FromSlots(ushort[] before, ushort[] after)
    {
        var r = new int[after.Length];
        for (int i = 0; i < after.Length; i++)
            r[i] = i < before.Length && before[i] == after[i] ? i : Array.IndexOf(before, after[i]);
        return r;
    }

    public static string? PassiveRefusal(ushort[] before, ushort[] after)
    {
        if (!PassiveGuard.Enabled) return null;
        var from = FromSlots(before, after);
        for (int i = 0; i < after.Length; i++)
        {
            bool touchesPassives = i >= PassiveGuard.TmCap ? i >= before.Length || after[i] != before[i] : from[i] >= PassiveGuard.TmCap;
            if (touchesPassives) return $"{PassiveGuard.TmCapReason} TM{i + 1:00} cannot be changed or take a move from a slot above {PassiveGuard.TmCap}.";
        }
        return null;
    }

    public static int Remap(byte[][] garcFiles, int[] fromSlot)
    {
        int len = garcFiles[0].Length;
        var entries = garcFiles.Select((f, i) => (f, i)).Where(x => x.f.Length == len).ToList();
        int changed = 0;
        foreach (var (f, _) in entries)
        {
            var old = f.AsSpan(BitsOffset, 16).ToArray();
            bool Get(int s) => (old[s >> 3] & (1 << (s & 7))) != 0;
            int slots = PassiveGuard.Enabled ? PassiveGuard.TmCap : 128; // bytes 0x35-0x37 are passives then
            for (int s = 0; s < fromSlot.Length && s < slots; s++)
            {
                bool v = fromSlot[s] < 0 ? Get(s) : Get(fromSlot[s]);
                if (v) f[BitsOffset + (s >> 3)] |= (byte)(1 << (s & 7)); else f[BitsOffset + (s >> 3)] &= (byte)~(1 << (s & 7));
            }
            if (!f.AsSpan(BitsOffset, 16).SequenceEqual(old)) changed++;
        }
        SyncMaster(garcFiles);
        return changed;
    }

    public static void SyncMaster(byte[][] garcFiles)
    {
        int len = garcFiles[0].Length, last = garcFiles.Length - 1;
        var entries = garcFiles.Take(last).Where(f => f.Length == len).ToList();
        if (garcFiles[last].Length < entries.Count * len)
            garcFiles[last] = new byte[entries.Count * len];
        for (int j = 0; j < entries.Count; j++)
            Array.Copy(entries[j], 0, garcFiles[last], j * len, len);
    }
}
