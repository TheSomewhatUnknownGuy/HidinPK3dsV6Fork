using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace pk3DS.Core.Modding.Research;

public enum CustomMechanicKind { Move = 0, Ability = 1, Item = 2 }

public sealed class CustomFunctionDefinition
{
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public ResearchTarget Target { get; set; } = ResearchTarget.BattleCro;

    public CustomMechanicKind Mechanic { get; set; } = CustomMechanicKind.Ability;

    public int MechanicIndex { get; set; } = -1;

    public string MechanicName { get; set; }

    public byte Timing { get; set; }

    public List<string> Assembly { get; set; } = [];

    public string HexCode { get; set; }

    public List<CustomStorageRequest> Storage { get; set; } = [];

    public string ReuseFunctionNamed { get; set; }

    public bool AllowTableExpansion { get; set; } = true;

    public byte[] ResolveCodeFrom(string assemblyText, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(assemblyText)) { error = "No assembly to build."; return null; }
        try
        {
            var bytes = pk3DS.Core.CTR.ARMCodec.Assemble(assemblyText);
            if (bytes == null || bytes.Length == 0) { error = "Assembly produced no output."; return null; }
            return bytes;
        }
        catch (Exception ex) { error = $"Assembly failed: {ex.Message}"; return null; }
    }

    public byte[] ResolveCode(out string error)
    {
        error = null;

        if (!string.IsNullOrWhiteSpace(HexCode))
        {
            string hex = HexCode.Replace(" ", "").Replace("-", "").Replace("\n", "").Replace("\r", "");
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
            if (hex.Length == 0 || hex.Length % 2 != 0 || !hex.All(Uri.IsHexDigit))
            { error = "HexCode is not a valid hex byte string."; return null; }
            try { return Convert.FromHexString(hex); }
            catch (Exception ex) { error = $"HexCode could not be parsed: {ex.Message}"; return null; }
        }

        if (Assembly is { Count: > 0 })
        {
            string text = string.Join(Environment.NewLine, Assembly);
            try
            {
                var bytes = pk3DS.Core.CTR.ARMCodec.Assemble(text);
                if (bytes == null || bytes.Length == 0)
                { error = "Assembly produced no output."; return null; }
                return bytes;
            }
            catch (Exception ex) { error = $"Assembly failed: {ex.Message}"; return null; }
        }

        error = "Definition supplies neither Assembly, HexCode, nor ReuseFunctionNamed.";
        return null;
    }

    #region Serialization

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static CustomFunctionDefinition FromJson(string json) =>
        JsonSerializer.Deserialize<CustomFunctionDefinition>(json, Options);

    public static List<CustomFunctionDefinition> LoadFolder(string folder, IList<string> diagnostics = null)
    {
        var list = new List<CustomFunctionDefinition>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return list;

        foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                string text = File.ReadAllText(file);
                // A file may hold one definition or an array of them.
                if (text.TrimStart().StartsWith("["))
                {
                    var many = JsonSerializer.Deserialize<List<CustomFunctionDefinition>>(text, Options);
                    if (many != null) list.AddRange(many);
                }
                else
                {
                    var one = FromJson(text);
                    if (one != null) list.Add(one);
                }
            }
            catch (Exception ex)
            {
                diagnostics?.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return list;
    }

    public void Save(string path) => File.WriteAllText(path, ToJson());

    #endregion
}

public sealed class CustomStorageRequest
{
    public string Name { get; set; } = "";
    public int Size { get; set; } = 1;
    public bool PerPokemon { get; set; }
}
