using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// What each installed package placed in the ROM, kept as <c>pk3ds-installed.json</c> beside the
/// romfs folder. Nothing else records install state, and two things need it:
/// <list type="bullet">
/// <item>the code-space allocator, which otherwise sees a placed block's zero-filled table as free
/// padding and hands it to the next package;</item>
/// <item>revert, which otherwise restores a hook that other installed packages still route through
/// (six bag-item packages share Bag.cro's <c>bag-item-effects</c> hook).</item>
/// </list>
/// Offsets are file offsets in the named file. ROMs patched before this record existed have no
/// entries, so callers must treat "not recorded" as "unknown", not "not installed".
/// </summary>
public sealed class InstallRecord
{
    public sealed class Placement
    {
        public string File { get; set; } = "";
        /// <summary>Block name, or empty for a patched site.</summary>
        public string Block { get; set; } = "";
        public uint Offset { get; set; }
        public int Size { get; set; }
    }

    public Dictionary<string, List<Placement>> Packages { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string PathFor(string romFsPath)
    {
        string dir = Path.GetDirectoryName(romFsPath.TrimEnd('\\', '/'));
        return Path.Combine(string.IsNullOrEmpty(dir) ? romFsPath : dir, "pk3ds-installed.json");
    }

    /// <summary>The record for this ROM, or an empty one if there is none (or it is unreadable).</summary>
    public static InstallRecord Load(string romFsPath)
    {
        try
        {
            string p = PathFor(romFsPath);
            if (System.IO.File.Exists(p))
            {
                var rec = JsonSerializer.Deserialize<InstallRecord>(System.IO.File.ReadAllText(p), Options);
                if (rec?.Packages != null)
                {
                    rec.Packages = new(rec.Packages, StringComparer.OrdinalIgnoreCase);
                    return rec;
                }
            }
        }
        catch (Exception) { /* a damaged record is treated as absent: callers already handle "unknown" */ }
        return new InstallRecord();
    }

    public void Save(string romFsPath) =>
        System.IO.File.WriteAllText(PathFor(romFsPath), JsonSerializer.Serialize(this, Options));

    public bool Knows(string package) => Packages.ContainsKey(package);

    /// <summary>Byte ranges already placed in <paramref name="file"/> by any installed package.</summary>
    public IEnumerable<(uint Start, int Size)> PlacedIn(string file) =>
        Packages.Values.SelectMany(l => l)
            .Where(p => p.Size > 0 && p.File.Equals(file, StringComparison.OrdinalIgnoreCase))
            .Select(p => (p.Offset, p.Size));

    /// <summary>Whether a package other than <paramref name="package"/> uses this block or site.</summary>
    public bool OthersUse(string package, string file, string block, uint offset) =>
        Packages.Any(kv => !kv.Key.Equals(package, StringComparison.OrdinalIgnoreCase) &&
            kv.Value.Any(p => p.File.Equals(file, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(block) ? p.Block == "" && p.Offset == offset
                                             : p.Block.Equals(block, StringComparison.OrdinalIgnoreCase))));
}
