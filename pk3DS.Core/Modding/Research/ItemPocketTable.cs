using System;
using System.IO;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Reads and writes the Bag pocket assignment table in RomFS <c>a/1/0/3</c>.
/// <para>
/// This - not <c>Item.PocketField</c> - is what the Bag UI actually reads to decide which pocket an
/// item is filed under. File[0] of that GARC is a flat nibble array covering item ids 0..1023 in
/// exactly 512 bytes (one nibble per item, no header): <c>byte = id / 2</c>, and, confirmed
/// empirically against ids whose real pocket is unambiguous, an <b>even</b> id occupies the
/// <b>high</b> nibble while an <b>odd</b> id occupies the <b>low</b> one.
/// </para>
/// <para>
/// That convention was verified rather than assumed, because getting it backwards is silently
/// self-concealing: within a contiguous run of same-pocket items (a TM block, the berries) both
/// conventions read the same value, and they diverge only at the run's edges. The checks that pin
/// it down are ids 17-30 (all Medicine, uniformly nibble 1 only under this convention), ids 149-162
/// (all Berries, uniformly 3), and Pikashunium Z at id 836 (a Z-Crystal, 7 - the other convention
/// reads 4, i.e. Key, for it).
/// </para>
/// <para>
/// The table's 512-byte length is not arbitrary and must not be grown: a bag slot stores its item
/// id in a 10-bit packed field (see the guard in <see cref="PatchPackageInstaller"/>), so 1023 is
/// the highest id the game can represent at all and 1024 nibbles already covers every id that can
/// exist.
/// </para>
/// </summary>
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

    /// <summary>Loads file[0] of a/1/0/3, or null when the GARC is not present/readable.</summary>
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

    /// <summary>Writes file[0] back, repacking the GARC. Returns false if it could not be saved.</summary>
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
