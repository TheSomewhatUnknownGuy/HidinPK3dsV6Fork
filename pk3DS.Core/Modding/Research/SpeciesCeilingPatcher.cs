using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

/// <summary>One species-count ceiling site found in a code binary.</summary>
public sealed class SpeciesCeilingSite
{
    /// <summary>"word" (a plain data constant) or "range" (the two-stage CMP construct below).</summary>
    public string Kind { get; init; } = "";
    public int Offset { get; init; }
    public uint OldTotal { get; init; }
    public uint NewTotal { get; set; }
    public bool Patched { get; set; }
    public string Purpose { get; init; } = "";

    /// <summary>Only set for Kind == "range": where the main-cutoff CMP/SUBcc/tail-CMP triplet sits
    /// relative to <see cref="Offset"/>, so <see cref="SpeciesCeilingPatcher"/> can rewrite exactly
    /// those three words without re-scanning.</summary>
    internal int MainCmpRelOffset, SubRelOffset, TailCmpRelOffset;
    internal uint TailWidth;

    public override string ToString() =>
        $"0x{Offset:X7} [{Kind}] valid 0..{OldTotal - 1}" +
        (Patched ? $" -> 0..{NewTotal - 1}" : "") +
        (string.IsNullOrEmpty(Purpose) ? "" : $"  [{Purpose}]");
}

/// <summary>Outcome of a species-ceiling raise.</summary>
public sealed class SpeciesCeilingResult
{
    public List<SpeciesCeilingSite> Sites { get; } = [];
    public List<string> Notes { get; } = [];
    public uint AppliedTotal { get; set; }
    public int PatchedCount => Sites.Count(s => s.Patched);
    public bool Success => PatchedCount > 0;

    public string Describe()
    {
        string headline = PatchedCount > 0
            ? $"species ceiling raised to {AppliedTotal} valid ids (0..{AppliedTotal - 1}) at {PatchedCount} site(s):"
            : "no species ceiling site could be patched:";
        var lines = new List<string> { headline };
        lines.AddRange(Sites.Select(s => "  " + s));
        lines.AddRange(Notes.Select(n => "  ! " + n));
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Raises the species-count ceiling in a Gen 7 code binary (and, for the model-group count, in
/// FinderStudioViewer.cro) so species ids above the currently-applied Expansion Pack limit are
/// accepted.
/// <para>
/// This does not scan the binary blind the way <see cref="ItemCeilingPatcher"/> can for the item
/// ceiling - the species checks use a two-stage "main range + small tail range" construct (the
/// Expansion Pack's own rewrite of a pre-existing retail idiom that handled the alt-forme id
/// block), and 1024/2-style small constants are far too common to attribute to species by value
/// alone. Every site here is instead read from `code_map.csv` via <see cref="ExpansionCodeMap"/>
/// (the "SPECIES LIMITS" section) - real, already-verified before/after byte pairs, not a guess -
/// and byte-verified against the live binary before anything is touched.
/// </para>
/// <para>
/// Two site shapes exist in the map:
/// </para>
/// <list type="bullet">
/// <item>Plain 32-bit words (the two name-lookup bounds at 0x21C948/0x21C9CC, and the model-group
/// count in FinderStudioViewer.cro) - trivial to raise, just overwrite the literal.</item>
/// <item>A three-instruction range check - `CMP Rn, #mainCutoff` immediately followed (within a
/// handful of bytes, verified rather than assumed at a fixed distance) by `SUBcc Rd, Rn,
/// #mainCutoff` and `CMPcc Rd, #tailWidth`, where cc is HS (unsigned higher-or-same) in every
/// documented site - total valid ids = mainCutoff + tailWidth. Raising the ceiling grows
/// mainCutoff by the requested amount and leaves tailWidth exactly as found, so the *current*
/// tail width is read from the live bytes rather than assumed, keeping this correct even if a
/// future Expansion Pack build changes it.</item>
/// </list>
/// <para>
/// The redundant, already-NOP'd duplicate checks the map also documents (`mov r0, r0` in place of
/// a second `cmp Rn, #0x328`) are intentionally left alone - they were retired because an earlier
/// check in the same function already covers them, not because they still gate anything.
/// </para>
/// </summary>
public static class SpeciesCeilingPatcher
{
    private const string CodeBin = "code.bin";
    private const string FinderStudio = "FinderStudioViewer.cro";

    /// <summary>Finds every species-ceiling site the code map documents, byte-verified against
    /// <paramref name="code"/> (must already be in the "current"/Expansion Pack state - retail-state
    /// binaries are out of scope here, matching how this ROM's own build already carries the
    /// one-time 807-&gt;1026 raise).</summary>
    public static List<SpeciesCeilingSite> Analyze(byte[] code, GameVersion version = GameVersion.Invalid)
    {
        var sites = new List<SpeciesCeilingSite>();
        if (code == null) return sites;

        foreach (var entry in ExpansionCodeMap.GetEntriesForFile(CodeBin, version))
        {
            if (!entry.Section.Equals("SPECIES LIMITS", StringComparison.OrdinalIgnoreCase)) continue;
            if (!entry.HasAddress) continue;
            if (!entry.TryGetCurrentBytes(out var currentBytes) || currentBytes.Length == 0) continue;

            int offset = (int)entry.StartOffset;
            if (offset + currentBytes.Length > code.Length) continue;

            var liveSlice = code.Skip(offset).Take(currentBytes.Length).ToArray();
            if (!liveSlice.SequenceEqual(currentBytes)) continue; // not in the expected state - leave for the caller to report

            if (currentBytes.Length == 4)
            {
                uint word = BitConverter.ToUInt32(currentBytes, 0);
                // A plain data word, not an instruction: skip anything that decodes as a real ARM
                // opcode (the NOP'd duplicate-check sites are 4 bytes too, but are "mov r0, r0").
                if (IsMovR0R0(word)) continue;
                sites.Add(new SpeciesCeilingSite
                {
                    Kind = "word",
                    Offset = offset,
                    OldTotal = word, // raw stored value - see Raise() for why this is shifted by a
                                      // delta rather than assumed to equal any particular formula
                    Purpose = entry.Purpose,
                });
                continue;
            }

            if (TryFindRangeCheck(currentBytes, out int mainRel, out int subRel, out int tailRel, out uint mainCutoff, out uint tailWidth))
            {
                sites.Add(new SpeciesCeilingSite
                {
                    Kind = "range",
                    Offset = offset,
                    OldTotal = mainCutoff + tailWidth,
                    Purpose = entry.Purpose,
                    MainCmpRelOffset = mainRel,
                    SubRelOffset = subRel,
                    TailCmpRelOffset = tailRel,
                    TailWidth = tailWidth,
                });
            }
        }

        return sites.OrderBy(s => s.Offset).ToList();
    }

    /// <summary>
    /// FinderStudioViewer.cro's model-group entry count needs to grow in lockstep with the species
    /// ceiling, or newly added species have data but Refresh/photo mode won't know their model
    /// group exists. Kept as its own pass over its own file/array - never conflated with code.bin's
    /// offsets, which are a different binary and could coincidentally share a numeric offset.
    /// </summary>
    public static List<SpeciesCeilingSite> AnalyzeFinderStudio(byte[] finderStudioCro, GameVersion version = GameVersion.Invalid)
    {
        var sites = new List<SpeciesCeilingSite>();
        if (finderStudioCro == null) return sites;

        foreach (var entry in ExpansionCodeMap.GetEntriesForFile(FinderStudio, version))
        {
            if (!entry.Purpose.Contains("model group", StringComparison.OrdinalIgnoreCase)) continue;
            if (!entry.HasAddress || !entry.TryGetCurrentBytes(out var bytes) || bytes.Length != 4) continue;
            int offset = (int)entry.StartOffset;
            if (offset + 4 > finderStudioCro.Length) continue;
            var liveSlice = finderStudioCro.Skip(offset).Take(4).ToArray();
            if (!liveSlice.SequenceEqual(bytes)) continue;

            sites.Add(new SpeciesCeilingSite
            {
                Kind = "word",
                Offset = offset,
                OldTotal = BitConverter.ToUInt32(bytes, 0),
                Purpose = entry.Purpose,
            });
        }

        return sites;
    }

    private static bool IsMovR0R0(uint w) => w == 0xE1A00000;

    /// <summary>
    /// Looks for `CMP Rn,#a` (any condition) followed, within the next 3 words, by `SUBcc Rd,Rn,#a`
    /// (the same immediate, plain SUB - no flags) and then, within the next 3 words after that, by
    /// `CMPcc Rd,#b`. Every documented site uses HS for both conditional instructions and keeps the
    /// pair adjacent, but the search stays a little loose (rather than a fixed relative offset) so a
    /// harmless instruction-order shuffle in a future build doesn't silently stop this from finding
    /// the real triplet.
    /// </summary>
    private static bool TryFindRangeCheck(byte[] bytes, out int mainRel, out int subRel, out int tailRel, out uint mainCutoff, out uint tailWidth)
    {
        mainRel = subRel = tailRel = 0; mainCutoff = tailWidth = 0;

        for (int i = 0; i + 4 <= bytes.Length; i += 4)
        {
            uint w = BitConverter.ToUInt32(bytes, i);
            if (!TryReadCmpImm(w, out int rn, out uint imm, out _)) continue;
            if (rn == 15) continue;

            for (int j = i + 4; j - i <= 12 && j + 4 <= bytes.Length; j += 4)
            {
                uint sw = BitConverter.ToUInt32(bytes, j);
                if (!TryReadSubImm(sw, out int subRn, out int subRd, out uint subImm)) continue;
                if (subRn != rn || subImm != imm) continue;

                for (int k = j + 4; k - j <= 12 && k + 4 <= bytes.Length; k += 4)
                {
                    uint tw = BitConverter.ToUInt32(bytes, k);
                    if (!TryReadCmpImm(tw, out int tailRn, out uint tailImm, out _)) continue;
                    if (tailRn != subRd) continue;

                    mainRel = i; subRel = j; tailRel = k;
                    mainCutoff = imm; tailWidth = tailImm;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Reads `CMP Rn,#imm` for any condition code (bits 27-20 fixed, condition free).
    /// Bits 19-16 are Rn - a real register field, not part of the fixed opcode, so the mask must
    /// stop at bit 20 (0x0FF00000), not swallow the register nibble too.</summary>
    private static bool TryReadCmpImm(uint w, out int rn, out uint imm, out int cond)
    {
        rn = 0; imm = 0; cond = (int)(w >> 28);
        if ((w & 0x0FF00000) != 0x03500000) return false;
        rn = (int)((w >> 16) & 0xF);
        imm = ARMCodec.DecodeImm8r4((w >> 8) & 0xF, w & 0xFF);
        return true;
    }

    /// <summary>Reads `SUBcc Rd,Rn,#imm` (S=0, no flags) for any condition code.</summary>
    private static bool TryReadSubImm(uint w, out int rn, out int rd, out uint imm)
    {
        rn = rd = 0; imm = 0;
        if ((w & 0x0FE00000) != 0x02400000) return false; // opcode SUB (0010), S=0, immediate form
        rn = (int)((w >> 16) & 0xF);
        rd = (int)((w >> 12) & 0xF);
        imm = ARMCodec.DecodeImm8r4((w >> 8) & 0xF, w & 0xFF);
        return true;
    }

    /// <summary>
    /// Raises every located site so <paramref name="requiredMaxSpeciesId"/> is valid. Mutates
    /// <paramref name="code"/> (code.bin) in place. Sites already covering the requested id are
    /// left untouched.
    /// <para>
    /// The "range" and "word" site kinds don't provably share the same total-valid-count formula
    /// (the range construct's own documented purpose describes "0 through 1025" - 1026 values -
    /// while the paired word constant is 1027; whether that's a genuine +1 offset for a distinct
    /// reason, such as the word specifically being the Egg's own pseudo-id one slot further out,
    /// or something else isn't worth guessing at). Rather than assume a formula, every site is
    /// shifted by the same delta relative to its own current value - computed once from a "range"
    /// site as the reference - which preserves whatever relationship already exists between sites
    /// exactly, without needing to know what that relationship means.
    /// </para>
    /// </summary>
    public static SpeciesCeilingResult Raise(byte[] code, int requiredMaxSpeciesId, GameVersion version = GameVersion.Invalid)
    {
        var result = new SpeciesCeilingResult();
        if (code == null) { result.Notes.Add("no code.bin supplied"); return result; }
        if (requiredMaxSpeciesId < 0) { result.Notes.Add("required species id must be non-negative"); return result; }

        var sites = Analyze(code, version);
        var referenceSite = sites.FirstOrDefault(s => s.Kind == "range");
        if (referenceSite == null)
        {
            result.Notes.Add("no 'range' site found to use as a reference point - refusing to guess a delta from 'word' sites alone");
            result.Sites.AddRange(sites);
            return result;
        }

        uint neededTotal = (uint)requiredMaxSpeciesId + 1;
        long naiveDelta = (long)neededTotal - referenceSite.OldTotal;

        if (naiveDelta <= 0)
        {
            result.AppliedTotal = referenceSite.OldTotal;
            result.Notes.Add($"every site already covers up to id {referenceSite.OldTotal - 1} (reference site 0x{referenceSite.Offset:X7}); nothing to raise");
            result.Sites.AddRange(sites);
            return result;
        }

        // The reference site's main cutoff must land on an ARM-encodable immediate; if the exact
        // requested total doesn't, round up to the smallest one that does and apply *that* actual
        // delta everywhere, so every site - word and range alike - ends up agreeing on the same
        // real total instead of drifting apart because one site's rounding differed from another's.
        uint refOldMainCutoff = referenceSite.OldTotal - referenceSite.TailWidth;
        uint desiredMainCutoff = refOldMainCutoff + (uint)naiveDelta;
        uint actualMainCutoff = ItemCeilingPatcher.SmallestEncodableAtLeast(desiredMainCutoff);
        if (actualMainCutoff == 0)
        {
            result.Notes.Add($"no ARM-encodable immediate at or above {desiredMainCutoff} was found near the requested species id");
            result.Sites.AddRange(sites);
            return result;
        }
        if (actualMainCutoff != desiredMainCutoff)
            result.Notes.Add($"main cutoff {desiredMainCutoff} is not ARM-immediate-encodable; using {actualMainCutoff} instead " +
                              $"(species id {requiredMaxSpeciesId} is still covered - the applied total ends up slightly higher)");

        long delta = actualMainCutoff - refOldMainCutoff;
        result.AppliedTotal = (uint)(referenceSite.OldTotal + delta);

        foreach (var site in sites)
        {
            result.Sites.Add(site);
            uint newTotal = (uint)(site.OldTotal + delta);

            if (site.Kind == "word")
            {
                BitConverter.GetBytes(newTotal).CopyTo(code, site.Offset);
                site.NewTotal = newTotal;
                site.Patched = true;
                continue;
            }

            // "range": grow mainCutoff by the same delta, keep tailWidth exactly as found.
            uint oldMainCutoff = site.OldTotal - site.TailWidth;
            uint newMainCutoff = oldMainCutoff + (uint)delta;

            var mainEnc = ItemCeilingPatcher.TryEncode(newMainCutoff);
            if (mainEnc == null)
            {
                result.Notes.Add($"0x{site.Offset:X7} SKIPPED: new main cutoff {newMainCutoff} (0x{newMainCutoff:X}) is not ARM-immediate-encodable");
                continue;
            }

            int mo = site.Offset + site.MainCmpRelOffset;
            int so = site.Offset + site.SubRelOffset;
            uint mainWord = BitConverter.ToUInt32(code, mo);
            uint subWord = BitConverter.ToUInt32(code, so);

            uint newMainWord = (mainWord & 0xFFFFF000) | ((mainEnc.Value.rot & 0xF) << 8) | (mainEnc.Value.imm8 & 0xFF);
            uint newSubWord = (subWord & 0xFFFFF000) | ((mainEnc.Value.rot & 0xF) << 8) | (mainEnc.Value.imm8 & 0xFF);

            if (ARMCodec.DecodeImm8r4((newMainWord >> 8) & 0xF, newMainWord & 0xFF) != newMainCutoff ||
                ARMCodec.DecodeImm8r4((newSubWord >> 8) & 0xF, newSubWord & 0xFF) != newMainCutoff)
            {
                result.Notes.Add($"0x{site.Offset:X7} SKIPPED: rebuilt instructions did not decode back to {newMainCutoff}");
                continue;
            }

            BitConverter.GetBytes(newMainWord).CopyTo(code, mo);
            BitConverter.GetBytes(newSubWord).CopyTo(code, so);
            site.NewTotal = newTotal;
            site.Patched = true;
        }

        if (result.Sites.Count == 0)
            result.Notes.Add("no documented species ceiling sites matched the live binary - it may not be in the expected Expansion Pack state");

        return result;
    }

    /// <summary>
    /// Sets FinderStudioViewer.cro's model-group entry count to an explicit new total. Unlike the
    /// species ceiling, this can't be inferred from a target species id - it depends on how many
    /// model groups the newly inserted species/forms actually add (see the class doc comment) - so
    /// the caller supplies the real value once it knows it, rather than this method guessing.
    /// </summary>
    public static SpeciesCeilingResult RaiseFinderStudioModelGroupCount(byte[] finderStudioCro, uint newModelGroupCount, GameVersion version = GameVersion.Invalid)
    {
        var result = new SpeciesCeilingResult { AppliedTotal = newModelGroupCount };
        foreach (var site in AnalyzeFinderStudio(finderStudioCro, version))
        {
            result.Sites.Add(site);
            uint oldCount = site.OldTotal; // the stored word is the raw count directly (confirmed:
                                            // the documented raise is "1172 -> 1678", both plain)
            if (oldCount >= newModelGroupCount)
            {
                result.Notes.Add($"FinderStudioViewer.cro 0x{site.Offset:X7} already has {oldCount} model group(s); left unchanged");
                continue;
            }
            BitConverter.GetBytes(newModelGroupCount).CopyTo(finderStudioCro, site.Offset);
            site.NewTotal = newModelGroupCount;
            site.Patched = true;
        }
        if (result.Sites.Count == 0)
            result.Notes.Add("no FinderStudioViewer.cro model-group-count site matched the live file");
        return result;
    }
}
