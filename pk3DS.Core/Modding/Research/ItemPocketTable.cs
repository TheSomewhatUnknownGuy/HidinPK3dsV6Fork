using System;
using System.IO;

namespace pk3DS.Core.Modding.Research;

public static class ItemPocketTable
{
    public const int ItemCount = 1024;
    public const int TableBytes = ItemCount / 2;

    public static readonly string[] PocketNames =
    [
        "General", "Medicine", "TM/HM", "Berry",
        "Key", "Z-Crystal (Bag)", "Roto Power", "Z-Crystal (Hold)",
        "8?", "9?", "10?", "11?", "12?", "13?", "14?", "15?",
    ];

    private static string Path(string romfs) => System.IO.Path.Combine(romfs, "a", "1", "0", "3");

    public static int Read(byte[] table, int itemId)
    {
        int bi = itemId / 2;
        if (bi >= table.Length) return -1;
        return (itemId % 2) == 0 ? (table[bi] >> 4) & 0xF : table[bi] & 0xF;
    }

    public static void Write(byte[] table, int itemId, int pocket)
    {
        int bi = itemId / 2;
        if (bi >= table.Length) return;
        pocket &= 0xF;
        table[bi] = (itemId % 2) == 0
            ? (byte)((table[bi] & 0x0F) | (pocket << 4))
            : (byte)((table[bi] & 0xF0) | pocket);
    }

    public static byte[] Load(string romfs)
    {
        string p = Path(romfs);
        if (!File.Exists(p)) return null;
        try
        {
            var garc = new pk3DS.Core.CTR.GARC.MemGARC(p);
            var files = garc.Files;
            return files is { Length: > 0 } ? files[0] : null;
        }
        catch { return null; }
    }

    public static bool Save(string romfs, byte[] table)
    {
        string p = Path(romfs);
        if (!File.Exists(p)) return false;
        try
        {
            var garc = new pk3DS.Core.CTR.GARC.MemGARC(p);
            var files = garc.Files;
            if (files is not { Length: > 0 }) return false;
            files[0] = table;
            garc.Files = files;              // setter repacks
            File.WriteAllBytes(p, garc.Data);
            return true;
        }
        catch { return false; }
    }
}
