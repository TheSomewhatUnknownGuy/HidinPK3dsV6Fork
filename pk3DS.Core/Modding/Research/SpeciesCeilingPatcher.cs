using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

public sealed class SpeciesCeilingSite
{
    public string Kind { get; init; } = "";
    public int Offset { get; init; }
    public uint OldTotal { get; init; }
    public uint NewTotal { get; set; }
    public bool Patched { get; set; }
    public string Purpose { get; init; } = "";

    internal int MainCmpRelOffset, SubRelOffset, TailCmpRelOffset;
    internal uint TailWidth;

    public override string ToString() =>
        $"0x{Offset:X7} [{Kind}] valid 0..{OldTotal - 1}" +
        (Patched ? $" -> 0..{NewTotal - 1}" : "") +
        (string.IsNullOrEmpty(Purpose) ? "" : $"  [{Purpose}]");
}

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

public static class SpeciesCeilingPatcher
{
    private const string CodeBin = "code.bin";
    private const string FinderStudio = "FinderStudioViewer.cro";

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

    private static bool TryReadCmpImm(uint w, out int rn, out uint imm, out int cond)
    {
        rn = 0; imm = 0; cond = (int)(w >> 28);
        if ((w & 0x0FF00000) != 0x03500000) return false;
        rn = (int)((w >> 16) & 0xF);
        imm = ARMCodec.DecodeImm8r4((w >> 8) & 0xF, w & 0xFF);
        return true;
    }

    private static bool TryReadSubImm(uint w, out int rn, out int rd, out uint imm)
    {
        rn = rd = 0; imm = 0;
        if ((w & 0x0FE00000) != 0x02400000) return false; // opcode SUB (0010), S=0, immediate form
        rn = (int)((w >> 16) & 0xF);
        rd = (int)((w >> 12) & 0xF);
        imm = ARMCodec.DecodeImm8r4((w >> 8) & 0xF, w & 0xFF);
        return true;
    }

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
