using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

/// <summary>One ability-ID bounds check found in code.bin.</summary>
public sealed class AbilityCeilingSite
{
    public int Offset { get; init; }
    public int Register { get; init; }
    public uint Ceiling { get; init; }
    public uint NewCeiling { get; set; }
    public bool Patched { get; set; }

    public override string ToString() =>
        $"0x{Offset:X7} CMP R{Register}, #0x{Ceiling:X}" + (Patched ? $" -> #0x{NewCeiling:X}" : "");
}

public sealed class AbilityCeilingResult
{
    public List<AbilityCeilingSite> Sites { get; } = [];
    public List<string> Notes { get; } = [];
    public uint AppliedCeiling { get; set; }
    public int PatchedCount => Sites.Count(s => s.Patched);
    public int AlreadySufficient => Sites.Count(s => !s.Patched && s.Ceiling >= AppliedCeiling && AppliedCeiling > 0);
    public bool Success => PatchedCount > 0 || AlreadySufficient > 0;

    public string Describe()
    {
        string headline = PatchedCount > 0
            ? $"ability ceiling raised to 0x{AppliedCeiling:X} ({AppliedCeiling}) at {PatchedCount} site(s):"
            : AlreadySufficient > 0
                ? $"ability ceiling already at or above 0x{AppliedCeiling:X} ({AppliedCeiling}) at {AlreadySufficient} site(s) - nothing to change:"
                : "no ability bounds check could be located:";
        var lines = new List<string> { headline };
        lines.AddRange(Sites.Select(s => "  " + s));
        lines.AddRange(Notes.Select(n => "  ! " + n));
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Raises the ability name-text bounds check in code.bin past whatever this build already allows.
/// <para>
/// Retail USUM only names abilities up to 0xEA (234). The community Expansion Pack's own documented
/// "ABILITY LIMITS" `code_map.csv` row (already confirmed applied in this session's reference ROM
/// via <see cref="ExpansionPatchApplier"/>) raises it to 0x140 (320) - separately, the "NINTH
/// ABILITY BIT" family of patches (also already applied) lets ability IDs themselves carry a 9th
/// bit, so IDs up to 511 can be stored and read back correctly even though only 320 currently have a
/// name-text slot reserved. This class only widens the name-text ceiling (the one bounds check this
/// session found and verified) - it does not touch the 9th-bit storage patches themselves, which
/// need no further widening on their own.
/// </para>
/// </summary>
public static class AbilityCeilingPatcher
{
    public const uint RetailCeiling = 0xEA;      // 234
    public const uint ExpansionCeiling = 0x140;  // 320

    /// <summary>US and UM addresses both listed; whichever doesn't match this build fails <see cref="TryReadCeiling"/> and is skipped.</summary>
    private static readonly int[] DocumentedSites = [0x2D30BC, 0x2D30C0];

    public static List<AbilityCeilingSite> Analyze(byte[] code)
    {
        var sites = new List<AbilityCeilingSite>();
        if (code == null) return sites;

        var taken = new HashSet<int>();
        foreach (int ofs in DocumentedSites)
        {
            if (ofs + 8 > code.Length || taken.Contains(ofs)) continue;
            if (!TryReadCeiling(code, ofs, out int rn, out uint imm)) continue;
            if (imm != RetailCeiling && imm != ExpansionCeiling && imm < RetailCeiling) continue;
            taken.Add(ofs);
            sites.Add(new AbilityCeilingSite { Offset = ofs, Register = rn, Ceiling = imm });
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

    /// <summary>Widens the ability name-text bounds check so <paramref name="requiredMaxAbilityId"/> gets a name slot.</summary>
    public static AbilityCeilingResult Raise(byte[] code, int requiredMaxAbilityId)
    {
        var result = new AbilityCeilingResult();
        if (code == null) { result.Notes.Add("no code supplied"); return result; }
        if (requiredMaxAbilityId < 0) { result.Notes.Add("required ability id must be non-negative"); return result; }

        uint needed = (uint)requiredMaxAbilityId + 1;
        uint ceiling = SmallestEncodableAtLeast(needed);
        if (ceiling == 0) { result.Notes.Add($"no ARM-encodable immediate at or above {needed}"); return result; }
        if (ceiling != needed)
            result.Notes.Add($"{needed} is not encodable as an ARM immediate; using {ceiling} (0x{ceiling:X}) instead");
        if (ceiling > 511)
            result.Notes.Add("note: ability IDs above 511 cannot be stored at all (the 9th-bit storage patch only reserves one extra bit) - a name slot beyond that would never be reachable");
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
            result.Notes.Add("no ability bounds checks were found at the documented offset - wrong build/version?");

        return result;
    }
}
