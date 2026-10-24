using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace pk3DS.Core.Modding.Research;

public sealed class PatchParameter
{
    public string Description { get; set; } = "";

    public string Type { get; set; } = "number";

    public string Default { get; set; }

    public string ExpectName { get; set; }

    public bool AllowUnnamed { get; set; } = true;
}

public sealed class PatchDataTable
{
    public string Target { get; set; } = "Battle.cro";
    public string Block { get; set; } = "";
    public string Offset { get; set; } = "0x0";
    public int ElementSize { get; set; } = 2;
    public string Values { get; set; } = "";
    public bool Terminate { get; set; } = true;
    public int Capacity { get; set; }

    public uint OffsetValue => Convert.ToUInt32(Offset.Trim(), Offset.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
}

public sealed class PatchItemData
{
    public string CloneFrom { get; set; }
    public string Name { get; set; }
    public string HeldEffect { get; set; }
    public string HeldArgument { get; set; }
    public string FlingPower { get; set; }
    public string Price { get; set; }
    public string Pocket { get; set; }

    public string ItemKind { get; set; }

    public string EffectField { get; set; }

    public Dictionary<string, string> Fields { get; set; } = [];
}

public sealed class PatchPackage
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";

    public List<string> Requires { get; set; } = [];

    public Dictionary<string, PatchParameter> Parameters { get; set; } = [];

    public List<PortedMechanic> Mechanics { get; set; } = [];
    public List<PortedBlock> Blocks { get; set; } = [];
    public List<PortedSitePatch> SitePatches { get; set; } = [];
    public Dictionary<string, CroSitePort> OtherCros { get; set; } = [];
    public CodeBinPort CodeBin { get; set; }

    public List<PatchDataTable> DataTables { get; set; } = [];

    public Dictionary<string, PatchItemData> ItemData { get; set; } = [];
    public Dictionary<string, string> ItemNames { get; set; } = [];
    public Dictionary<string, string> ItemNamesPlural { get; set; } = [];
    public Dictionary<string, string> ItemNamesPluralAlt { get; set; } = [];

    public Dictionary<string, string> ItemDescriptions { get; set; } = [];

    public Dictionary<string, Dictionary<string, string>> GameTextLines { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static PatchPackage FromJson(string json) => JsonSerializer.Deserialize<PatchPackage>(json, Options);
    public static PatchPackage Load(string path) => FromJson(File.ReadAllText(path));
    public void Save(string path) => File.WriteAllText(path, ToJson());

    public static List<PatchPackage> LoadFolder(string folder, IList<string> problems = null)
    {
        var list = new List<PatchPackage>();
        if (!Directory.Exists(folder)) return list;
        foreach (string f in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f))
        {
            try
            {
                var p = Load(f);
                if (p != null && !string.IsNullOrWhiteSpace(p.Name)) list.Add(p);
            }
            catch (Exception ex) { problems?.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
        }
        return list;
    }

    public static List<PatchPackage> InDependencyOrder(IEnumerable<PatchPackage> packages, IList<string> problems = null)
    {
        var byName = new Dictionary<string, PatchPackage>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<PatchPackage>();
        foreach (var p in packages)
        {
            if (byName.TryAdd(p.Name ?? "", p)) continue;
            duplicates.Add(p);
            problems?.Add($"more than one package is named '{p.Name}'; only the first is used");
        }
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<PatchPackage>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(PatchPackage p)
        {
            if (done.Contains(p.Name)) return;
            if (!visiting.Add(p.Name))
            { problems?.Add($"'{p.Name}' takes part in a requirement cycle"); return; }

            foreach (string need in p.Requires ?? [])
            {
                if (byName.TryGetValue(need, out var dep)) Visit(dep);
                else problems?.Add($"'{p.Name}' requires '{need}', which is not present");
            }
            visiting.Remove(p.Name);
            done.Add(p.Name);
            order.Add(p);
        }

        // The duplicates are skipped, not visited: they carry the same name as one already in the
        // order, so visiting them would add a second copy of the same install to the run.
        foreach (var p in packages)
            if (!duplicates.Contains(p)) Visit(p);
        return order;
    }
}

public static class PatchParameters
{
    private static readonly Regex Ref = new(@"\$\{([A-Za-z0-9_]+)(?:\[(\d+)\])?\}", RegexOptions.Compiled);

    public static Dictionary<string, string> Resolve(
        PatchPackage package, IDictionary<string, string> supplied, IList<string> problems = null)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, param) in package.Parameters ?? [])
        {
            if (supplied != null && supplied.TryGetValue(key, out string given) && !string.IsNullOrWhiteSpace(given))
            { values[key] = given.Trim(); continue; }

            if (!string.IsNullOrWhiteSpace(param.Default)) { values[key] = param.Default.Trim(); continue; }
            problems?.Add($"{package.Name}: '{key}' has no value and no default ({param.Description})");
        }
        return values;
    }

    public static string Expand(string text, IDictionary<string, string> values, IList<string> problems = null)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Ref.Replace(text, m =>
        {
            string key = m.Groups[1].Value;
            if (!values.TryGetValue(key, out string v))
            {
                problems?.Add($"no value for '{key}'");
                return m.Value;
            }

            if (!m.Groups[2].Success) return v;

            // Indexed reference into a comma-separated list parameter.
            int idx = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var parts = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (idx >= 0 && idx < parts.Length) return parts[idx];

            problems?.Add($"'{key}' has {parts.Length} item(s), so [{idx}] does not exist");
            return m.Value;
        });
    }

    public static bool TryNumber(string text, IDictionary<string, string> values, out uint result, IList<string> problems = null)
    {
        result = 0;
        string s = Expand(text, values, problems)?.Trim();
        if (string.IsNullOrEmpty(s)) return false;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        return uint.TryParse(s, out result);
    }

    public static PatchPackage Bind(PatchPackage package, IDictionary<string, string> values, IList<string> problems = null)
    {
        // Round-tripping through JSON is the simplest deep copy that also catches a package whose
        // own serialisation is broken, before any of it reaches a binary.
        var copy = PatchPackage.FromJson(package.ToJson());

        foreach (var m in copy.Mechanics)
        {
            m.Name = Expand(m.Name, values, problems);
            if (TryNumber(m.IdText ?? m.Id.ToString(), values, out uint id, problems)) m.Id = id;
            foreach (var s in m.Slots)
            {
                s.Timing = Expand(s.Timing, values, problems);
                s.SourceBase = Expand(s.SourceBase, values, problems);
                if (s.Reuse != null)
                {
                    s.Reuse.Timing = Expand(s.Reuse.Timing, values, problems);
                    if (TryNumber(s.Reuse.IdText ?? s.Reuse.Id.ToString(), values, out uint rid, problems)) s.Reuse.Id = rid;
                }
            }
        }
        foreach (var b in copy.Blocks) { b.Name = Expand(b.Name, values, problems); b.SourceBase = Expand(b.SourceBase, values, problems); }
        foreach (var p in copy.SitePatches)
        {
            p.Offset = Expand(p.Offset, values, problems);
            p.Original = Expand(p.Original, values, problems);
            p.Patched = Expand(p.Patched, values, problems);
            p.HookTarget = Expand(p.HookTarget, values, problems);
        }

        foreach (var (croName, croPort) in copy.OtherCros ?? [])
        {
            foreach (var b in croPort.Blocks ?? [])
            {
                b.Name = Expand(b.Name, values, problems);
                b.SourceBase = Expand(b.SourceBase, values, problems);
                b.Code = Expand(b.Code, values, problems);
            }
            foreach (var p in croPort.SitePatches ?? [])
            {
                p.Offset = Expand(p.Offset, values, problems);
                p.Original = Expand(p.Original, values, problems);
                p.Patched = Expand(p.Patched, values, problems);
                p.HookTarget = Expand(p.HookTarget, values, problems);
            }
        }

        foreach (var t in copy.DataTables ?? [])
        {
            t.Block = Expand(t.Block, values, problems);
            t.Offset = Expand(t.Offset, values, problems);
            t.Values = Expand(t.Values, values, problems);
        }

        copy.ItemNames = (copy.ItemNames ?? []).ToDictionary(
            kv => Expand(kv.Key, values, problems), kv => Expand(kv.Value, values, problems));
        copy.ItemNamesPlural = (copy.ItemNamesPlural ?? []).ToDictionary(
            kv => Expand(kv.Key, values, problems), kv => Expand(kv.Value, values, problems));
        copy.ItemNamesPluralAlt = (copy.ItemNamesPluralAlt ?? []).ToDictionary(
            kv => Expand(kv.Key, values, problems), kv => Expand(kv.Value, values, problems));
        copy.ItemDescriptions = (copy.ItemDescriptions ?? []).ToDictionary(
            kv => Expand(kv.Key, values, problems), kv => Expand(kv.Value, values, problems));
        copy.ItemData = (copy.ItemData ?? []).ToDictionary(
            kv => Expand(kv.Key, values, problems),
            kv => new PatchItemData
            {
                CloneFrom = Expand(kv.Value.CloneFrom, values, problems),
                Name = Expand(kv.Value.Name, values, problems),
                HeldEffect = Expand(kv.Value.HeldEffect, values, problems),
                HeldArgument = Expand(kv.Value.HeldArgument, values, problems),
                FlingPower = Expand(kv.Value.FlingPower, values, problems),
                Price = Expand(kv.Value.Price, values, problems),
                Pocket = Expand(kv.Value.Pocket, values, problems),
                ItemKind = Expand(kv.Value.ItemKind, values, problems),
                EffectField = Expand(kv.Value.EffectField, values, problems),
                Fields = (kv.Value.Fields ?? []).ToDictionary(
                    f => f.Key, f => Expand(f.Value, values, problems)),
            });

        return copy;
    }
}
