using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace pk3DS.Core.Modding.Research;

public sealed class InstallRecord
{
    public sealed class Placement
    {
        public string File { get; set; } = "";
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

    public IEnumerable<(uint Start, int Size)> PlacedIn(string file) =>
        Packages.Values.SelectMany(l => l)
            .Where(p => p.Size > 0 && p.File.Equals(file, StringComparison.OrdinalIgnoreCase))
            .Select(p => (p.Offset, p.Size));

    public bool OthersUse(string package, string file, string block, uint offset) =>
        Packages.Any(kv => !kv.Key.Equals(package, StringComparison.OrdinalIgnoreCase) &&
            kv.Value.Any(p => p.File.Equals(file, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(block) ? p.Block == "" && p.Offset == offset
                                             : p.Block.Equals(block, StringComparison.OrdinalIgnoreCase))));
}
