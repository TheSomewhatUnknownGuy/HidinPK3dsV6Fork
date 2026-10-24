using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

public sealed class TrainerCeilingSite
{
    public int Offset { get; init; }
    public int Register { get; init; }
    public uint Ceiling { get; init; }
    public uint NewCeiling { get; set; }
    public bool Patched { get; set; }

    public override string ToString() =>
        $"0x{Offset:X7} CMP R{Register}, #0x{Ceiling:X}" + (Patched ? $" -> #0x{NewCeiling:X}" : "");
}

public sealed class TrainerCeilingResult
{
    public List<TrainerCeilingSite> Sites { get; } = [];
    public List<string> Notes { get; } = [];
    public uint AppliedCeiling { get; set; }
    public int PatchedCount => Sites.Count(s => s.Patched);
    public int AlreadySufficient => Sites.Count(s => !s.Patched && s.Ceiling >= AppliedCeiling && AppliedCeiling > 0);
    public bool Success => PatchedCount > 0 || AlreadySufficient > 0;

    public string Describe()
    {
        string headline = PatchedCount > 0
            ? $"trainer ceiling raised to 0x{AppliedCeiling:X} ({AppliedCeiling}) at {PatchedCount} site(s):"
            : AlreadySufficient > 0
                ? $"trainer ceiling already at or above 0x{AppliedCeiling:X} ({AppliedCeiling}) at {AlreadySufficient} site(s) - nothing to change:"
                : "no trainer bounds check could be located:";
        var lines = new List<string> { headline };
        lines.AddRange(Sites.Select(s => "  " + s));
        lines.AddRange(Notes.Select(n => "  ! " + n));
        return string.Join(Environment.NewLine, lines);
    }
}

public static class TrainerCeilingPatcher
{
    public const uint RetailCeiling = 652;

    private static readonly int[] DocumentedSites =
    [
        // US / UM pairs for the 9 trainer bounds checks (UM is +4 relative to US)
        0x2D3158, 0x2D315C,
        0x330708, 0x33070C,
        0x3307C4, 0x3307C8,
        0x330F48, 0x330F4C,
        0x3317C0, 0x3317C4,
        0x3318B8, 0x3318BC,
        0x331990, 0x331994,
        0x331A40, 0x331A44,
        0x331AC4, 0x331AC8,
    ];

    public static List<TrainerCeilingSite> Analyze(byte[] code)
    {
        var sites = new List<TrainerCeilingSite>();
        if (code == null) return sites;

        foreach (int ofs in DocumentedSites)
        {
            if (ofs + 8 > code.Length) continue;
            if (!TryReadCeiling(code, ofs, out int rn, out uint imm)) continue;
            if (imm != RetailCeiling && imm < RetailCeiling) continue;
            sites.Add(new TrainerCeilingSite { Offset = ofs, Register = rn, Ceiling = imm });
        }

        return sites.OrderBy(s => s.Offset).ToList();
    }

    private static bool TryReadCeiling(byte[] code, int offset, out int rn, out uint imm)
    {
        rn = 0; imm = 0;
        uint w = BitConverter.ToUInt32(code, offset);
        if ((w & 0xFFF00000) != 0xE3500000) return false; // CMP Rn,#imm, cond=AL
        rn = (int)((w >> 16) & 0xF);
        if (rn == 15) return false;
        imm = ARMCodec.DecodeImm8r4((w >> 8) & 0xF, w & 0xFF);
        return true;
    }

    public static uint SmallestEncodableAtLeast(uint minimum)
    {
        for (uint v = minimum; v < minimum + 0x2000; v++)
        {
            var enc = ARMCodec.EncodeImm8r4(v);
            if (enc != null && ARMCodec.DecodeImm8r4(enc.Value.rot, enc.Value.imm8) == v) return v;
        }
        return 0;
    }

    public static TrainerCeilingResult Raise(byte[] code, int requiredMaxTrainerId)
    {
        var result = new TrainerCeilingResult();
        if (code == null) { result.Notes.Add("no code supplied"); return result; }
        if (requiredMaxTrainerId < 0) { result.Notes.Add("required trainer id must be non-negative"); return result; }

        uint needed = (uint)requiredMaxTrainerId + 1;
        uint ceiling = SmallestEncodableAtLeast(needed);
        if (ceiling == 0) { result.Notes.Add($"no ARM-encodable immediate at or above {needed}"); return result; }
        if (ceiling != needed)
            result.Notes.Add($"{needed} is not encodable as an ARM immediate; using {ceiling} (0x{ceiling:X}) instead");
        result.AppliedCeiling = ceiling;

        var encoded = ARMCodec.EncodeImm8r4(ceiling);
        if (encoded == null || ARMCodec.DecodeImm8r4(encoded.Value.rot, encoded.Value.imm8) != ceiling)
        {
            result.Notes.Add($"0x{ceiling:X} could not be encoded as an ARM immediate");
            return result;
        }

        foreach (var site in Analyze(code))
        {
            result.Sites.Add(site);

            if (site.Ceiling >= ceiling)
            {
                result.Notes.Add($"0x{site.Offset:X7} already allows up to {site.Ceiling - 1}; left unchanged");
                continue;
            }

            uint w = BitConverter.ToUInt32(code, site.Offset);
            uint rebuilt = (w & 0xFFFFF000) | ((encoded.Value.rot & 0xF) << 8) | (encoded.Value.imm8 & 0xFF);

            uint check = ARMCodec.DecodeImm8r4((rebuilt >> 8) & 0xF, rebuilt & 0xFF);
            if (check != ceiling)
            {
                result.Notes.Add($"0x{site.Offset:X7} SKIPPED: rebuilt instruction decodes to 0x{check:X}, not 0x{ceiling:X}");
                continue;
            }

            BitConverter.GetBytes(rebuilt).CopyTo(code, site.Offset);
            site.NewCeiling = ceiling;
            site.Patched = true;
        }

        if (result.Sites.Count == 0)
            result.Notes.Add("no trainer bounds checks were found - the binary may already be patched differently, or this build's layout differs");

        return result;
    }
}
