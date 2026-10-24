using System;
using System.Linq;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

public sealed class IconEggSentinelResult
{
    public bool Success;
    public string Error = "";

    public uint OldSentinel;
    public uint NewSentinel;
    public bool SentinelPatched;

    public uint OldFormulaWidth;
    public uint NewFormulaWidth;
    public bool FormulaWidthPatched;

    public bool Patched => SentinelPatched || FormulaWidthPatched;

    public string Describe()
    {
        if (!Success) return $"icon Egg-sentinel/formula-width NOT patched: {Error}";
        var parts = new System.Collections.Generic.List<string>();
        parts.Add(SentinelPatched
            ? $"Egg-sentinel raised from species id {OldSentinel} to {NewSentinel}"
            : $"Egg-sentinel already covers species id {NewSentinel - 1}");
        parts.Add(FormulaWidthPatched
            ? $"expansion formula remapped: species past {SpeciesIconLayout.DirectFormulaMaxSpecies} now use icon members from " +
              $"{SpeciesIconLayout.ExpansionRegionMember(SpeciesIconLayout.DirectFormulaMaxSpecies + 1)} (width was {OldFormulaWidth})"
            : $"expansion formula already serves this species (width {NewFormulaWidth}, remap handles ids past {SpeciesIconLayout.DirectFormulaMaxSpecies})");
        return "icon lookup: " + string.Join("; ", parts);
    }
}

public static class IconEggSentinelPatcher
{
    private const int FirstSubRelOffset = 0x18; // sub ip, r0, #firstImm   (relative to the routine's own start)
    private const int SecondSubRelOffset = 0x1C; // sub ip, ip, #tailWidth (kept fixed - see Raise)
    private const int WidthCmpRelOffset = 0x68;  // cmp r2, #formulaWidth  (species-808 must be <= this or the formula returns member 0)
    public const int FormulaBaseSpecies = 808;

    public static bool TryFindVerifiedRoutine(byte[] codeBin, out int routineStart, out string error)
    {
        routineStart = 0; error = "";
        var candidates = ExpansionCodeMap.GetEntriesForSection("POKEMON ICONS")
            .Where(e => e.Purpose.Contains("look up normal species", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        { error = "code_map.csv doesn't document the POKEMON ICONS lookup routine (or this pk3DS build's copy of it is out of date)"; return false; }

        foreach (var row in candidates)
        {
            if (!row.HasAddress || !row.TryGetCurrentBytes(out var expected) || expected.Length == 0) continue;
            int offset = (int)row.StartOffset;
            if (offset + expected.Length > codeBin.Length) continue;

            bool matches = true;
            for (int i = 0; i < expected.Length && matches; i++)
            {
                if (i >= FirstSubRelOffset && i < FirstSubRelOffset + 4) continue; // may already be patched
                if (i >= WidthCmpRelOffset && i < WidthCmpRelOffset + 16) continue; // width cmp + remap tail
                if (codeBin[offset + i] != expected[i]) matches = false;
            }
            if (!matches) continue;

            routineStart = offset;
            return true;
        }

        error = "code.bin doesn't byte-match the documented icon-lookup routine at any known offset - this ROM's code.bin differs from what this was verified against";
        return false;
    }

    public static IconEggSentinelResult Raise(byte[] codeBin, int requiredMaxSpeciesId)
    {
        var result = new IconEggSentinelResult();
        if (codeBin == null) { result.Error = "no code.bin supplied"; return result; }
        if (requiredMaxSpeciesId < 0) { result.Error = "required species id must be non-negative"; return result; }

        if (!TryFindVerifiedRoutine(codeBin, out int routineStart, out string findError))
        { result.Error = findError; return result; }

        // --- 1. The Egg sentinel (two chained SUB immediates) ---
        int firstOffset = routineStart + FirstSubRelOffset;
        int secondOffset = routineStart + SecondSubRelOffset;
        if (secondOffset + 4 > codeBin.Length)
        { result.Error = "routine body is shorter than expected - refusing to touch it"; return result; }

        uint firstWord = BitConverter.ToUInt32(codeBin, firstOffset);
        uint secondWord = BitConverter.ToUInt32(codeBin, secondOffset);
        if ((firstWord & 0x0FE00000) != 0x02400000 || (secondWord & 0x0FE00000) != 0x02400000)
        { result.Error = "the two instructions at the expected sentinel offsets aren't plain SUB-immediate as expected - refusing to patch"; return result; }

        uint firstImm = ARMCodec.DecodeImm8r4((firstWord >> 8) & 0xF, firstWord & 0xFF);
        uint tailWidth = ARMCodec.DecodeImm8r4((secondWord >> 8) & 0xF, secondWord & 0xFF);
        uint oldSentinel = firstImm + tailWidth;
        result.OldSentinel = oldSentinel;

        uint neededSentinel = (uint)requiredMaxSpeciesId + 1;
        if (neededSentinel <= oldSentinel)
        {
            result.NewSentinel = oldSentinel;
        }
        else
        {
            uint desiredFirstImm = neededSentinel - tailWidth;
            uint actualFirstImm = ItemCeilingPatcher.SmallestEncodableAtLeast(desiredFirstImm);
            if (actualFirstImm == 0)
            { result.Error = $"no ARM-encodable immediate at or above {desiredFirstImm} was found near the requested species id (sentinel)"; return result; }

            var enc = ItemCeilingPatcher.TryEncode(actualFirstImm);
            if (enc == null)
            { result.Error = $"failed to re-encode {actualFirstImm} as an ARM immediate (sentinel)"; return result; }

            uint newFirstWord = (firstWord & 0xFFFFF000) | ((enc.Value.rot & 0xF) << 8) | (enc.Value.imm8 & 0xFF);
            if (ARMCodec.DecodeImm8r4((newFirstWord >> 8) & 0xF, newFirstWord & 0xFF) != actualFirstImm)
            { result.Error = "rebuilt sentinel instruction did not decode back to the intended value - refusing to write it"; return result; }

            BitConverter.GetBytes(newFirstWord).CopyTo(codeBin, firstOffset);
            result.SentinelPatched = true;
            result.NewSentinel = actualFirstImm + tailWidth;
        }

        // --- 2. The expansion formula: remap past the compact block instead of widening ---
        // Widening `cmp r2,#217` sent ids 1026+ to 1154+(id-808) = 1372+, the members the compact
        // alt-form table already owns (1372 = Pikachu costume 8), so an inserted species shared and
        // then overwrote another Pokemon's icon. Keep 217 as the split point and rewrite the three
        // conditional words after it so ids past 1025 step over the compact block:
        //   cmp r2,#217 ; addhi r2,r2,#136 ; add r0,r8,r2 ; add r0,r0,#1
        // Same word count, no new code space; the old `movhi r0,#0` upper bound goes away because
        // the species ceiling already bounds the id.
        if (!InstallRemap(codeBin, routineStart, requiredMaxSpeciesId, result)) return result;

        result.Success = true;
        return result;
    }

    private const uint WidthCmpWord = 0xE35200D9;              // cmp   r2, #217
    private const uint RetailTailAddLs1 = 0x90880002;          // addls r0, r8, r2
    private const uint RetailTailAddLs2 = 0x92800001;          // addls r0, r0, #1
    private const uint RetailTailMovHi = 0x83A00000;           // movhi r0, #0
    private static readonly uint RemapSkipWord = 0x82822000u | SpeciesIconLayout.CompactBlockSize; // addhi r2, r2, #136
    private const uint RemapAddWord = 0xE0880002;              // add   r0, r8, r2
    private const uint RemapIncWord = 0xE2800001;              // add   r0, r0, #1

    public static bool IsRemapped(byte[] codeBin, int routineStart) =>
        routineStart + WidthCmpRelOffset + 16 <= codeBin.Length &&
        BitConverter.ToUInt32(codeBin, routineStart + WidthCmpRelOffset + 4) == RemapSkipWord &&
        BitConverter.ToUInt32(codeBin, routineStart + WidthCmpRelOffset + 8) == RemapAddWord &&
        BitConverter.ToUInt32(codeBin, routineStart + WidthCmpRelOffset + 12) == RemapIncWord;

    public static bool? IsRemapped(byte[] codeBin) =>
        TryFindVerifiedRoutine(codeBin, out int start, out _) ? IsRemapped(codeBin, start) : null;

    private static bool InstallRemap(byte[] codeBin, int routineStart, int requiredMaxSpeciesId, IconEggSentinelResult result)
    {
        int at = routineStart + WidthCmpRelOffset;
        uint cmp = BitConverter.ToUInt32(codeBin, at);
        result.OldFormulaWidth = ARMCodec.DecodeImm8r4((cmp >> 8) & 0xF, cmp & 0xFF);
        result.NewFormulaWidth = result.OldFormulaWidth;

        if (IsRemapped(codeBin, routineStart))
        {
            result.NewFormulaWidth = SpeciesIconLayout.DirectFormulaMaxSpecies - FormulaBaseSpecies;
            return true;
        }
        if (requiredMaxSpeciesId <= SpeciesIconLayout.DirectFormulaMaxSpecies)
            return true; // the stock 217 width already covers it; nothing to rewrite

        uint w1 = BitConverter.ToUInt32(codeBin, at + 4), w2 = BitConverter.ToUInt32(codeBin, at + 8), w3 = BitConverter.ToUInt32(codeBin, at + 12);
        if ((cmp & 0xFFFFF000) != 0xE3520000 || w1 != RetailTailAddLs1 || w2 != RetailTailAddLs2 || w3 != RetailTailMovHi)
        {
            result.Error = $"icon formula tail at 0x{at:X} is not the documented cmp/addls/addls/movhi sequence " +
                           $"({cmp:X8} {w1:X8} {w2:X8} {w3:X8}) - refusing to rewrite it";
            return false;
        }

        // A ROM widened by an older pk3DS build keeps working: restoring 217 is what makes the
        // remap apply, and species it already served move to their collision-free members.
        BitConverter.GetBytes(WidthCmpWord).CopyTo(codeBin, at);
        BitConverter.GetBytes(RemapSkipWord).CopyTo(codeBin, at + 4);
        BitConverter.GetBytes(RemapAddWord).CopyTo(codeBin, at + 8);
        BitConverter.GetBytes(RemapIncWord).CopyTo(codeBin, at + 12);
        result.NewFormulaWidth = SpeciesIconLayout.DirectFormulaMaxSpecies - FormulaBaseSpecies;
        result.FormulaWidthPatched = true;
        return true;
    }
}
