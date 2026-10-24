#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace pk3DS.Core.Modding;

/// <summary>
/// Keeps code.bin / CRO / exheader edits away from Uhuru's own code (Pokemon Uhuru Gift v16.0.3).
/// <para>
/// Uhuru is the Expansion Pack patched in place, so an offset that is right on the Expansion is right on
/// Uhuru unless Uhuru changed the bytes there. The region table (<c>Resources/uhuru_regions.txt</c>, made by
/// <c>scripts/uhuru/regions.py</c>) lists every byte range where Uhuru differs from the Expansion.
/// </para>
/// <para>
/// Policy ("verified-only"): on an Uhuru ROM the only code writers allowed are patch packages, whose anchors
/// verify the original bytes, plus a before/after diff against this table. Every other code.bin / CRO /
/// exheader writer is refused through <see cref="RefusalFor"/>.
/// </para>
/// <para>
/// Accepted residual risk: zero-filled caves that Uhuru uses as data or BSS look unchanged to a byte diff, so
/// they are invisible here. The table is only valid for Uhuru v16.0.3. Detection reads RomFS (trainer GARCs),
/// so an ExeFS-only open cannot be recognised as Uhuru and its code.bin editors are not refused.
/// </para>
/// Pure on purpose (no GameConfig dependency): <c>GameConfig</c> sets <see cref="Active"/>.
/// </summary>
public static class UhuruGuard
{
    /// <summary>True while the open ROM is Uhuru. Assigned unconditionally when a ROM is opened.</summary>
    public static bool Active { get; set; }

    /// <summary>Pocket item ids Uhuru uses for its Tera items (kept out of recipe/randomizer writes).</summary>
    public static readonly HashSet<int> ProtectedPocketItems = [848, 859, .. Enumerable.Range(861, 18)];

    /// <summary>Battle-bag item ids Uhuru uses for its Tera items.</summary>
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

    /// <summary>Replaces the Expansion-to-Uhuru table from text (tests; also used to parse the embedded resource).</summary>
    public static void Load(string text) => _regions = Parse(text);

    /// <summary>Replaces the Vanilla-to-Expansion table (code.bin only) from text.</summary>
    public static void LoadExpansion(string text) => _expansion = Parse(text);

    /// <summary>Replaces the Uhuru cave table (zero runs its code reads or points at) from text.</summary>
    public static void LoadCaves(string text) => _caves = Parse(text);

    /// <summary>Finds an embedded table by resource-name suffix; an absent resource is an empty table.</summary>
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

    /// <summary>File name only, leading dot stripped (".code.bin" is "code.bin"), case-insensitive key.</summary>
    public static string Normalize(string fileName) => Path.GetFileName(fileName).TrimStart('.').ToLowerInvariant();

    /// <summary>The changed ranges for one file, or empty.</summary>
    public static IReadOnlyList<(uint Start, uint End)> Regions(string fileName)
        => Table().TryGetValue(Normalize(fileName), out var l) ? l : [];

    /// <summary>The ranges where the Expansion differs from Vanilla (code.bin only), or empty.</summary>
    public static IReadOnlyList<(uint Start, uint End)> ExpansionRegions(string fileName)
        => Embedded(ref _expansion, "expansion_regions.txt").TryGetValue(Normalize(fileName), out var l) ? l : [];

    /// <summary>Zero runs in Uhuru's .text that its own code reads or points at, or empty.</summary>
    public static IReadOnlyList<(uint Start, uint End)> Caves(string fileName)
        => Embedded(ref _caves, "uhuru_caves.txt").TryGetValue(Normalize(fileName), out var l) ? l : [];

    /// <summary>Regions plus caves as (offset, length) pairs, for allocators that take a keep-out list.</summary>
    public static List<(uint, int)> KeepOut(string fileName)
        => Regions(fileName).Concat(Caves(fileName)).Select(r => (r.Start, (int)(r.End - r.Start))).ToList();

    /// <summary>
    /// Null when no IPS record (widened by <paramref name="margin"/> on both sides) meets the Vanilla-to-Expansion,
    /// Expansion-to-Uhuru or cave tables; otherwise the first offending offset and which table. An IPS carries no
    /// original bytes, so these committed diffs are the only evidence that Uhuru equals Vanilla there. Pure: the
    /// caller checks <see cref="Active"/>.
    /// </summary>
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

    /// <summary>Uhuru-specific reason a patch package cannot be installed, or null. Name matched with spaces removed, case-insensitive.</summary>
    public static string? PackageRefusal(string packageName)
    {
        string n = packageName.Replace(" ", "");
        if (n.Equals("DynamaxCore", StringComparison.OrdinalIgnoreCase) || n.Equals("TeraShard", StringComparison.OrdinalIgnoreCase))
            return $"Uhuru already has Dynamax/Terastal; installing {packageName} would collide with it.";
        if (n.Equals("AbilityCycle", StringComparison.OrdinalIgnoreCase))
            return "Uhuru ships its own ability-capsule cycling at the same Bag.cro sites (0x9A48, 0x9A98, 0xDCF4, 0xDD18), so this package would overwrite it.";
        return null;
    }

    /// <summary>
    /// Null when going from <paramref name="before"/> to <paramref name="after"/> leaves Uhuru's code alone;
    /// otherwise a reason. A length change always counts, since it shifts everything behind it.
    /// </summary>
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

    /// <summary>
    /// The refusal for an in-place write that the caller has already diffed (the TM table). On Uhuru only a same-size
    /// code.bin edit that misses every region passes; anything else goes through <see cref="RefusalFor"/>, so exheader.bin
    /// and CROs stay refused. <paramref name="current"/> is the file's bytes now, or null when it does not exist.
    /// </summary>
    public static string? InPlaceRefusal(string path, byte[]? current, byte[] data)
    {
        if (!Active) return null;
        if (Normalize(path) != "code.bin" || current == null) return RefusalFor(path);
        return FirstOverlap("code.bin", current, data);
    }

    /// <summary>
    /// When Uhuru is active and <paramref name="path"/> is code.bin, a CRO or an exheader, the refusal message;
    /// otherwise null.
    /// </summary>
    public static string? RefusalFor(string path)
    {
        if (!Active || string.IsNullOrEmpty(path)) return null;
        string n = Normalize(path);
        if (n != "code.bin" && n != "exheader.bin" && !n.EndsWith(".cro", StringComparison.Ordinal)) return null;
        return $"Refused on Uhuru: this edit patches {Path.GetFileName(path)} without checking the original bytes, and Uhuru changed this game's code. Use a patch package (Research Center) instead.";
    }
}
