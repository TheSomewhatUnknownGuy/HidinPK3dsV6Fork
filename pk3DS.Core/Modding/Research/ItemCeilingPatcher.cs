using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

public sealed class ItemCeilingSite
{
    public int Offset { get; init; }
    public int Register { get; init; }
    public uint Ceiling { get; init; }
    public string FollowedBy { get; init; } = "";
    public string Reason { get; init; } = "";
    public uint NewCeiling { get; set; }
    public bool Patched { get; set; }

    public override string ToString() =>
        $"0x{Offset:X7} CMP R{Register}, #0x{Ceiling:X}" +
        (Patched ? $" -> #0x{NewCeiling:X}" : "") +
        (string.IsNullOrEmpty(FollowedBy) ? "" : $"   ({FollowedBy})") +
        (string.IsNullOrEmpty(Reason) ? "" : $"  [{Reason}]");
}

public sealed class ItemCeilingResult
{
    public List<ItemCeilingSite> Sites { get; } = [];
    public List<string> Notes { get; } = [];
    public uint AppliedCeiling { get; set; }
    public int PatchedCount => Sites.Count(s => s.Patched);

    public int AlreadySufficient =>
        Sites.Count(s => !s.Patched && s.Ceiling >= AppliedCeiling && AppliedCeiling > 0);

    public bool Success => PatchedCount > 0 || AlreadySufficient > 0;

    public string Describe()
    {
        string headline = PatchedCount > 0
            ? $"item ceiling raised to 0x{AppliedCeiling:X} ({AppliedCeiling}) at {PatchedCount} site(s):"
            : AlreadySufficient > 0
                ? $"item ceiling already at or above 0x{AppliedCeiling:X} ({AppliedCeiling}) at {AlreadySufficient} site(s) - nothing to change:"
                : "no item ceiling check could be located:";
        var lines = new List<string> { headline };
        lines.AddRange(Sites.Select(s => "  " + s));
        lines.AddRange(Notes.Select(n => "  ! " + n));
        return string.Join(Environment.NewLine, lines);
    }
}

public static class ItemCeilingPatcher
{
    public const uint RetailCeiling = 0x3C0;

    public const uint ExpansionCeiling = 0x400;

    private static readonly int[] DocumentedSites =
    [
        0x276B90,           // item records (same offset in both builds)
        0x2D2B60, 0x2D2B64, // item name text (US / UM)
        0x3BABFC, 0x3BAC04, // field held items (US / UM)
    ];

    public static List<ItemCeilingSite> Analyze(byte[] code)
    {
        var sites = new List<ItemCeilingSite>();
        if (code == null) return sites;

        var taken = new HashSet<int>();

        for (int i = 0; i + 8 <= code.Length; i += 4)
        {
            if (!TryReadCeiling(code, i, out int rn, out uint imm)) continue;
            if (imm != RetailCeiling) continue;
            taken.Add(i);
            bool added = IsInsideAddedCode((uint)i);
            sites.Add(Make(code, i, rn, imm,
                added ? "inside Expansion Pack added code - not a bounds check, left alone"
                      : "retail ceiling found by scan"));
        }

        foreach (int ofs in DocumentedSites)
        {
            if (ofs + 8 > code.Length || taken.Contains(ofs)) continue;
            if (!TryReadCeiling(code, ofs, out int rn, out uint imm)) continue;
            if (imm <= RetailCeiling) continue;
            taken.Add(ofs);
            sites.Add(Make(code, ofs, rn, imm,
                imm == ExpansionCeiling
                    ? "documented item-limit site"
                    : $"documented item-limit site, already raised to 0x{imm:X}"));
        }

        return sites.OrderBy(s => s.Offset).ToList();
    }

    private static bool IsInsideAddedCode(uint offset)
    {
        if (offset is >= 0x4B9B40 and < 0x4B9BA8) return true;

        try
        {
            foreach (var entry in ExpansionCodeMap.GetEntriesForFile("code.bin"))
            {
                if (entry.IsAddedSpace && entry.Contains(offset)) return true;
            }
        }
        catch { }
        return false;
    }

    private static bool TryReadCeiling(byte[] code, int offset, out int rn, out uint imm)
    {
        rn = 0; imm = 0;
        uint w = BitConverter.ToUInt32(code, offset);
        // Condition must be AL (0xE); a conditional CMP here is data disassembled as code.
        if ((w & 0xFFF00000) != 0xE3500000) return false;
        rn = (int)((w >> 16) & 0xF);
        if (rn == 15) return false; // comparing PC: data, not a real check
        imm = ARMCodec.DecodeImm8r4((w >> 8) & 0xF, w & 0xFF);
        return true;
    }

    private static ItemCeilingSite Make(byte[] code, int offset, int rn, uint imm, string why) => new()
    {
        Offset = offset,
        Register = rn,
        Ceiling = imm,
        Reason = why,
        FollowedBy = ARMCodec.DisassembleWord(BitConverter.ToUInt32(code, offset + 4), (uint)offset + 4),
    };

    public static (uint rot, uint imm8)? TryEncode(uint value)
    {
        var enc = ARMCodec.EncodeImm8r4(value);
        if (enc == null) return null;
        return ARMCodec.DecodeImm8r4(enc.Value.rot, enc.Value.imm8) == value ? enc : null;
    }

    public static uint SmallestEncodableAtLeast(uint minimum)
    {
        for (uint v = minimum; v < minimum + 0x2000; v++)
            if (TryEncode(v) != null) return v;
        return 0;
    }

    public static ItemCeilingResult Raise(byte[] code, int requiredMaxItemId)
    {
        var result = new ItemCeilingResult();
        if (code == null) { result.Notes.Add("no code supplied"); return result; }
        if (requiredMaxItemId < 0) { result.Notes.Add("required item id must be non-negative"); return result; }

        // The checks reject IDs >= ceiling, so the ceiling must exceed the highest ID in use.
        uint needed = (uint)requiredMaxItemId + 1;
        uint ceiling = SmallestEncodableAtLeast(needed);
        if (ceiling == 0)
        {
            result.Notes.Add($"no ARM-encodable immediate at or above {needed}");
            return result;
        }
        if (ceiling != needed)
            result.Notes.Add($"{needed} is not encodable as an ARM immediate; using {ceiling} (0x{ceiling:X}) instead");

        result.AppliedCeiling = ceiling;

        var encoded = TryEncode(ceiling);
        if (encoded == null) { result.Notes.Add($"0x{ceiling:X} could not be encoded as an ARM immediate"); return result; }

        foreach (var site in Analyze(code))
        {
            result.Sites.Add(site);

            if (IsInsideAddedCode((uint)site.Offset))
            {
                result.Notes.Add($"0x{site.Offset:X7} is inside Expansion Pack added code; not widened " +
                                 "(its comparison selects behaviour, it does not reject out-of-range IDs)");
                continue;
            }

            if (site.Ceiling >= ceiling)
            {
                result.Notes.Add($"0x{site.Offset:X7} already allows up to {site.Ceiling - 1}; left unchanged");
                continue;
            }

            uint w = BitConverter.ToUInt32(code, site.Offset);
            uint rebuilt = (w & 0xFFFFF000) | ((encoded.Value.rot & 0xF) << 8) | (encoded.Value.imm8 & 0xFF);

            // Re-decode the instruction we are about to write and confirm it still says what we
            // mean. A bad immediate here would silently turn a bounds check into nonsense.
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

        if (result.PatchedCount == 0 && result.Sites.Count == 0)
            result.Notes.Add("no item bounds checks were found; the binary may already use a different ceiling");

        return result;
    }
}
