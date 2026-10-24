#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace pk3DS.Core.Modding;

public static class UhuruGuard
{
    public static bool Active { get; set; }

    public static readonly HashSet<int> ProtectedPocketItems = [848, 859, .. Enumerable.Range(861, 18)];

    public static readonly HashSet<int> ProtectedBattleBagItems = [848, 896, 897, .. Enumerable.Range(899, 5)];

    private static Dictionary<string, List<(uint Start, uint End)>>? _regions, _expansion, _caves;

    private static Dictionary<string, List<(uint, uint)>> Parse(string text)
    {
        var d = new Dictionary<string, List<(uint, uint)>>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            string[] p = line.Split('\t');
            if (p.Length != 3) continue;
            uint s = uint.Parse(p[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            uint e = uint.Parse(p[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            string key = Normalize(p[0]);
            if (!d.TryGetValue(key, out var list)) d[key] = list = [];
            list.Add((s, e));
        }
        return d;
    }

    public static void Load(string text) => _regions = Parse(text);

    public static void LoadExpansion(string text) => _expansion = Parse(text);

    public static void LoadCaves(string text) => _caves = Parse(text);

    private static Dictionary<string, List<(uint Start, uint End)>> Embedded(ref Dictionary<string, List<(uint Start, uint End)>>? slot, string suffix)
    {
        if (slot != null) return slot;
        var asm = typeof(UhuruGuard).Assembly;
        string? name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        using var s = name == null ? null : asm.GetManifestResourceStream(name);
        if (s == null) return slot = [];
        using var r = new StreamReader(s);
        return slot = Parse(r.ReadToEnd());
    }

    private static Dictionary<string, List<(uint Start, uint End)>> Table() => Embedded(ref _regions, "uhuru_regions.txt");

    public static string Normalize(string fileName) => Path.GetFileName(fileName).TrimStart('.').ToLowerInvariant();

    public static IReadOnlyList<(uint Start, uint End)> Regions(string fileName)
        => Table().TryGetValue(Normalize(fileName), out var l) ? l : [];

    public static IReadOnlyList<(uint Start, uint End)> ExpansionRegions(string fileName)
        => Embedded(ref _expansion, "expansion_regions.txt").TryGetValue(Normalize(fileName), out var l) ? l : [];

    public static IReadOnlyList<(uint Start, uint End)> Caves(string fileName)
        => Embedded(ref _caves, "uhuru_caves.txt").TryGetValue(Normalize(fileName), out var l) ? l : [];

    public static List<(uint, int)> KeepOut(string fileName)
        => Regions(fileName).Concat(Caves(fileName)).Select(r => (r.Start, (int)(r.End - r.Start))).ToList();

    public static string? IpsRefusal(IEnumerable<(int Offset, int Length)> records, int margin = 16)
    {
        foreach (var (off, len) in records)
        {
            long lo = (long)off - margin, hi = (long)off + len + margin;
            foreach (var (table, ranges) in new[] { ("Expansion", ExpansionRegions("code.bin")), ("Uhuru", Regions("code.bin")), ("Uhuru cave", Caves("code.bin")) })
                foreach (var (s, e) in ranges)
                    if (lo < e && hi > s)
                        return $"IPS record at 0x{off:X} (+{len}) is within {margin} bytes of {table} code 0x{s:X}-0x{e:X}, so Uhuru's code.bin may differ from Vanilla there.";
        }
        return null;
    }

    public static string? PackageRefusal(string packageName)
    {
        string n = packageName.Replace(" ", "");
        if (n.Equals("DynamaxCore", StringComparison.OrdinalIgnoreCase) || n.Equals("TeraShard", StringComparison.OrdinalIgnoreCase))
            return $"Uhuru already has Dynamax/Terastal; installing {packageName} would collide with it.";
        if (n.Equals("AbilityCycle", StringComparison.OrdinalIgnoreCase))
            return "Uhuru ships its own ability-capsule cycling at the same Bag.cro sites (0x9A48, 0x9A98, 0xDCF4, 0xDD18), so this package would overwrite it.";
        return null;
    }

    public static string? FirstOverlap(string fileName, byte[] before, byte[] after)
    {
        if (before.Length != after.Length)
            return $"{fileName}: size changed ({before.Length} to {after.Length} bytes)";
        var regions = Regions(fileName);
        if (regions.Count == 0 && Table().Count == 0)
            return $"{fileName}: Uhuru region table is missing, so the edit cannot be checked";
        foreach (var (start, end) in regions)
        {
            long e = Math.Min(end, (long)before.Length);
            for (long i = start; i < e; i++)
                if (before[i] != after[i])
                    return $"{fileName}: changes byte 0x{i:X} inside Uhuru's own code (region 0x{start:X}-0x{end:X})";
        }
        foreach (var (start, end) in Caves(fileName))
        {
            long e = Math.Min(end, (long)before.Length);
            for (long i = start; i < e; i++)
                if (before[i] != after[i])
                    return $"{fileName}: changes byte 0x{i:X} inside a cave Uhuru's code reads or points at (0x{start:X}-0x{end:X})";
        }
        return null;
    }

    public static string? InPlaceRefusal(string path, byte[]? current, byte[] data)
    {
        if (!Active) return null;
        if (Normalize(path) != "code.bin" || current == null) return RefusalFor(path);
        return FirstOverlap("code.bin", current, data);
    }

    public static string? RefusalFor(string path)
    {
        if (!Active || string.IsNullOrEmpty(path)) return null;
        string n = Normalize(path);
        if (n != "code.bin" && n != "exheader.bin" && !n.EndsWith(".cro", StringComparison.Ordinal)) return null;
        return $"Refused on Uhuru: this edit patches {Path.GetFileName(path)} without checking the original bytes, and Uhuru changed this game's code. Use a patch package (Research Center) instead.";
    }
}
