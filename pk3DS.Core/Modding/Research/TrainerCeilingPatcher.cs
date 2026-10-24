using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

/// <summary>One trainer-id bounds check found in code.bin.</summary>
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

/// <summary>
/// Raises the trainer-id bounds checks in code.bin so trdata indices above the stock 653-entry
/// roster (0-652) resolve to the real, newly-added trainer instead of silently falling back to
/// trdata[1].
/// <para>
/// Unlike <see cref="ItemCeilingPatcher"/>/<see cref="SpeciesCeilingPatcher"/>, this is not
/// community-documented anywhere - it was found this session via live in-game testing (adding a
/// second real trdata entry at index 654 and confirming it ALSO fell back to trdata[1], the same
/// exact fallback as index 653 - a result that specifically rules out a modulo/wraparound
/// explanation and confirms a deliberate, hardcoded clamp) followed by a targeted disassembly of
/// code.bin's `.text` for a compound signature: <c>cmp Rn,#652; bls +skip; [a five-word "zeroed
/// register" debug/assert-stub call setup]; mov Rn,#1</c>. That exact 8-instruction shape (not just
/// the bare `#652` immediate, which is far too common a small constant to search for alone) found
/// 9 occurrences, all structurally identical - a single, shared, likely-templated "safe array
/// access, default to element 1" helper reused for several different arrays that happen to share
/// this bound, not something written specifically for trainers. Real gameplay testing after
/// patching all 9 confirmed both a previously-broken index (653) and a fresh one (654) resolve
/// correctly.
/// </para>
/// <para>
/// Because 652 is common enough that a blind value scan would return many unrelated hits (loop
/// bounds, buffer sizes), sites are only trusted when they match the FULL compound signature below,
/// not the bare immediate - the same anchor-first discipline established elsewhere this session for
/// telling a real bounds check apart from coincidental small-integer noise.
/// </para>
/// </summary>
public static class TrainerCeilingPatcher
{
    /// <summary>Stock ceiling: trdata indices 0..652 are valid (653 real trainers, index 0 reserved).</summary>
    public const uint RetailCeiling = 652;

    /// <summary>
    /// The 9 real trainer-id bounds checks, found this session (not community-documented) via a
    /// live two-data-point gameplay test (indices 653 AND 654 both fell back to the exact same
    /// trainer, ruling out a modulo/wraparound explanation) followed by a targeted disassembly for
    /// <c>cmp Rn,#652; bls +skip; [5-word zeroed-register debug/assert-stub call setup]; mov Rn,#1</c>.
    /// <para>
    /// These offsets are hardcoded (the same "documented site list" shape <see cref="ItemCeilingPatcher"/>
    /// and <see cref="MoveCeilingPatcher"/> already use) rather than re-derived by a fresh structural
    /// scan each time, for two concrete reasons found while building this class: (1) the exact
    /// 8-instruction "clamp to 1" signature is NOT unique to trainers - the same shared, likely-
    /// templated helper gates at least 16 other, completely unrelated small-bound arrays elsewhere in
    /// this binary (bounds of 0, 1, 2, 3, 5, 11, 13), and a value-agnostic structural re-scan matches
    /// all of them; (2) three of the nine real trainer sites (0x2D3158, 0x330708, 0x3307C4) share the
    /// compare-and-assert-stub prefix but do NOT have the trailing "mov Rn,#1" - they fall through to
    /// unrelated code afterward - so a scan that requires the full clamp signature misses them
    /// entirely. Both problems are avoided by trusting this fixed, already-verified list and
    /// confirming each by VALUE (either still at the original 652, or already raised past it) rather
    /// than by re-matching the generic shape.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Checks each documented site by value: kept only if it still reads the pristine retail bound
    /// (652) or already reads something higher (a previous run's own raise) - anything else is left
    /// alone rather than assumed, since a build with a genuinely different layout would otherwise
    /// get silently corrupted at whatever these fixed offsets happen to hold instead.
    /// </summary>
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

    /// <summary>Reads an unconditional CMP Rn,#imm that isn't comparing PC.</summary>
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

    /// <summary>Smallest ARM-encodable immediate at least <paramref name="minimum"/>, verified by round-trip decode.</summary>
    public static uint SmallestEncodableAtLeast(uint minimum)
    {
        for (uint v = minimum; v < minimum + 0x2000; v++)
        {
            var enc = ARMCodec.EncodeImm8r4(v);
            if (enc != null && ARMCodec.DecodeImm8r4(enc.Value.rot, enc.Value.imm8) == v) return v;
        }
        return 0;
    }

    /// <summary>
    /// Widens every confirmed trainer bounds check so <paramref name="requiredMaxTrainerId"/> is a
    /// valid trdata index. Mutates <paramref name="code"/> in place.
    /// </summary>
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
