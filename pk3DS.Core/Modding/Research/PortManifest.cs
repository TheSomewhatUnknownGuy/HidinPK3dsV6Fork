using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace pk3DS.Core.Modding.Research;

public sealed class PortedSlot
{
    public string Timing { get; set; } = "0x00";

    public string Code { get; set; }

    public string SourceBase { get; set; }

    public PortedReuse Reuse { get; set; }

    public byte TimingByte => (byte)Convert.ToInt32(Timing, 16);
    public uint SourceBaseValue => string.IsNullOrEmpty(SourceBase) ? 0 : Convert.ToUInt32(SourceBase, 16);
    public byte[] CodeBytes => string.IsNullOrEmpty(Code) ? null : Convert.FromHexString(Code);
}

public sealed class PortedReuse
{
    public string Kind { get; set; } = "";
    public uint Id { get; set; }
    public string IdText { get; set; }
    public string Timing { get; set; }
    public string Name { get; set; }
}

public sealed class PortedMechanic
{
    public string Kind { get; set; } = "Move";
    public uint Id { get; set; }
    public string IdText { get; set; }
    public string Name { get; set; } = "";
    public List<PortedSlot> Slots { get; set; } = [];

    public CustomMechanicKind KindValue => Enum.Parse<CustomMechanicKind>(Kind, true);
}

public sealed class PortedSitePatch
{
    public string Offset { get; set; } = "0x0";
    public string Original { get; set; } = "";
    public string Patched { get; set; } = "";

    public bool IsHook { get; set; }

    public string HookTarget { get; set; }

    [JsonIgnore]
    public int FallbackBranchOffset { get; set; } = -1;

    [JsonPropertyName("fallbackBranchOffset")]
    public string FallbackBranchOffsetStr
    {
        get => FallbackBranchOffset >= 0 ? $"0x{FallbackBranchOffset:X}" : null;
        set => FallbackBranchOffset = string.IsNullOrWhiteSpace(value) ? -1 : (int)Convert.ToUInt32(value.Trim(), value.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
    }

    public uint OffsetValue => Convert.ToUInt32(Offset, 16);
    public byte[] OriginalBytes => Convert.FromHexString(Original);
    public byte[] PatchedBytes => Convert.FromHexString(Patched);
}

public sealed class PortedBlock
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string SourceBase { get; set; } = "0x0";

    public byte[] CodeBytes => Convert.FromHexString(Code);
    public uint SourceBaseValue => Convert.ToUInt32(SourceBase, 16);
}

public sealed class PortManifest
{
    public string Description { get; set; } = "";

    [JsonPropertyName("battleCroMechanics")]
    public List<PortedMechanic> Mechanics { get; set; } = [];

    [JsonPropertyName("battleCroBlocks")]
    public List<PortedBlock> Blocks { get; set; } = [];

    [JsonPropertyName("battleCroSitePatches")]
    public List<PortedSitePatch> SitePatches { get; set; } = [];

    [JsonPropertyName("otherCros")]
    public Dictionary<string, CroSitePort> OtherCros { get; set; } = [];

    public static readonly uint[] StructuralOffsets = [0x000000, 0x0000B8, 0x00012C, 0x000090];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static PortManifest FromJson(string json) => JsonSerializer.Deserialize<PortManifest>(json, Options);
    public static PortManifest Load(string path) => FromJson(File.ReadAllText(path));
    public void Save(string path) => File.WriteAllText(path, ToJson());

    public int RemoveStructural()
    {
        int before = SitePatches.Count;
        SitePatches = SitePatches
            .Where(p => !StructuralOffsets.Contains(p.OffsetValue) && p.OriginalBytes.Length <= 64)
            .ToList();
        return before - SitePatches.Count;
    }
}
