using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

/// <summary>One entry in <see cref="PartyIconCatalog"/> - one `partyicon`/`menuicon` GARC member.</summary>
public sealed class PartyIconEntry
{
    public int Member;
    public string RawFilename = "";

    /// <summary>National dex id for a stock entry, or the real species id for an expansion-region entry. -1 if unresolvable (a reserved, not-yet-assigned future slot).</summary>
    public int SpeciesId = -1;

    /// <summary>The bit of the filename between the species number and the trailing "_m[_r]_NNN" suffix - "" for a plain base-form entry, otherwise e.g. "mega", "megax", "alola", "normal", "nushi", "satopika20".</summary>
    public string FormTag = "";

    public bool IsExpansionRegion;
    public bool IsReservedFutureSlot;
    public bool IsEgg;
}

/// <summary>
/// `partyicon` (a257) and `menuicon` (a/0/6/2) are NOT indexed by species id directly for stock
/// species (id &lt; 808) - confirmed live against this ROM's real data (a filename embedded in every
/// partyicon member's own custom header) after `SpeciesIconLayout`'s naive "member == species id"
/// assumption for the stock range turned out wrong (caught live: species 150/Mewtwo's slot actually
/// held Marowak's icon). The real layout is a single flat, NATIONAL-DEX-ORDERED catalog where each
/// species contributes one base entry plus zero or more interleaved alt-form entries (mega
/// evolutions, Alolan forms + their explicit "_normal" counterpart, regional Totem "_nushi" forms,
/// Pikachu's cosplay "_satopika*" costumes, mirrored/gendered "_m_r" frame variants, ...) - so every
/// species past the first Mega Evolution (Venusaur, dex 3) sits at a member index that has drifted
/// away from its own dex number by however many extra entries came before it. This catalog decodes
/// every member's embedded filename ONCE (see the "texture\0"-tagged custom header format this GARC
/// uses - PartyIconImage.HeaderLength bytes before the RGB5A1 pixel payload) and exposes the result
/// as real lookup data instead of a formula.
/// <para>
/// Species &gt;= 808 (this ROM's Expansion Pack region) ARE a reliable, verified formula instead -
/// every one of the real 808-1025 species is tagged "poke_icon_exp_0NNN.tga" matching
/// <see cref="SpeciesIconLayout.ExpansionRegionMember"/> exactly, and every currently-unassigned
/// slot past that (reserved by the one-time 1507-member icon-cap raise, workstream 1f) is tagged
/// "poke_icon_form_NNNN.tga" (just its own member number - no species assigned yet). This catalog
/// still records those for completeness, but callers should prefer the formula for that range.
/// </para>
/// </summary>
public static class PartyIconCatalog
{
    private static readonly Regex StockPattern = new(@"^poke_icon_(\d+)(?:_([a-z0-9]+))?_(m|f)(_r)?_\d+$", RegexOptions.IgnoreCase);
    private static readonly Regex ExpPattern = new(@"^poke_icon_exp_(\d+)$", RegexOptions.IgnoreCase);
    private static readonly Regex FormPattern = new(@"^poke_icon_form_(\d+)$", RegexOptions.IgnoreCase);
    private static readonly Regex EggPattern = new(@"^poke_icon_egg_", RegexOptions.IgnoreCase);

    /// <summary>Reads the embedded, null-terminated ASCII filename baked into a raw (possibly LZ11-compressed) partyicon/menuicon member, or null if this member is empty/unrecognized.</summary>
    public static string ReadEmbeddedFilename(byte[] rawMember)
    {
        if (rawMember == null || rawMember.Length == 0) return null;
        byte[] data = rawMember[0] == 0x11 ? LZSS.Decompress(rawMember) : rawMember;
        int start = PartyIconImage.FilenameOffset;
        if (start < 0 || start >= data.Length) return null;
        int end = start;
        while (end < data.Length && data[end] != 0) end++;
        if (end <= start) return null;
        string name = Encoding.ASCII.GetString(data, start, end - start);
        return name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    public static List<PartyIconEntry> Build(byte[][] partyIconFiles)
    {
        var list = new List<PartyIconEntry>(partyIconFiles.Length);
        for (int i = 0; i < partyIconFiles.Length; i++)
        {
            string name = ReadEmbeddedFilename(partyIconFiles[i]);
            var entry = new PartyIconEntry { Member = i, RawFilename = name ?? "" };
            if (string.IsNullOrEmpty(name)) { list.Add(entry); continue; }

            var expMatch = ExpPattern.Match(name);
            if (expMatch.Success)
            {
                entry.IsExpansionRegion = true;
                entry.SpeciesId = int.Parse(expMatch.Groups[1].Value);
                list.Add(entry);
                continue;
            }

            var formMatch = FormPattern.Match(name);
            if (formMatch.Success)
            {
                entry.IsExpansionRegion = true;
                entry.IsReservedFutureSlot = true;
                list.Add(entry);
                continue;
            }

            if (EggPattern.IsMatch(name))
            {
                entry.IsEgg = true;
                list.Add(entry);
                continue;
            }

            var stockMatch = StockPattern.Match(name);
            if (stockMatch.Success)
            {
                entry.SpeciesId = int.Parse(stockMatch.Groups[1].Value);
                entry.FormTag = stockMatch.Groups[2].Value;
            }
            list.Add(entry);
        }
        return list;
    }

    /// <summary>
    /// Groups a built catalog by resolved species id - stock range only (id &lt;
    /// <see cref="SpeciesIconLayout.StockSpeciesCount"/>). Species &gt;= that boundary are
    /// deliberately EXCLUDED here, even though some of them also have a real-looking entry earlier in
    /// the interleaved stock catalog (e.g. a second "poke_icon_808_m_001" sits at member 1014,
    /// alongside the real "poke_icon_exp_0808" the formula points at) - confirmed live, independently,
    /// that code.bin's own hardcoded icon-lookup ARM routine computes 1154+(id-808) at runtime, so
    /// that earlier stock-numbered duplicate is dead/unreachable data. Excluding it here means a
    /// caller who forgets to special-case the formula range gets a clean "not found" (null) instead
    /// of a plausible-looking but wrong answer - use <see cref="SpeciesIconLayout.ExpansionRegionMember"/>
    /// for id &gt;= <see cref="SpeciesIconLayout.StockSpeciesCount"/> instead.
    /// </summary>
    public static Dictionary<int, List<PartyIconEntry>> GroupBySpecies(this List<PartyIconEntry> entries) =>
        entries.Where(e => e.SpeciesId >= 0 && e.SpeciesId < SpeciesIconLayout.StockSpeciesCount)
               .GroupBy(e => e.SpeciesId)
               .ToDictionary(g => g.Key, g => g.ToList());

    /// <summary>
    /// Picks the single best "default" member for an EXISTING species out of however many variant
    /// entries it has (a species with mega evolutions/Alolan forms/costumes has several). Prefers,
    /// in order: a plain entry with no form tag at all; an entry explicitly tagged "normal" (the
    /// species' own original form, for species that also have an Alolan variant); otherwise the
    /// first entry found. Every real variant is available via <see cref="GroupBySpecies"/> for a UI
    /// to offer explicitly - this is only a reasonable single fallback default, not a claim that
    /// it's the only correct choice. Returns null for a species with no real icon yet (e.g. one
    /// about to be inserted) - use <see cref="SpeciesIconLayout.ExpansionRegionMember"/> for that.
    /// </summary>
    public static int? GetDefaultMember(this Dictionary<int, List<PartyIconEntry>> bySpecies, int speciesId)
    {
        if (!bySpecies.TryGetValue(speciesId, out var variants) || variants.Count == 0) return null;
        var plain = variants.FirstOrDefault(v => string.IsNullOrEmpty(v.FormTag));
        if (plain != null) return plain.Member;
        var normal = variants.FirstOrDefault(v => v.FormTag.Equals("normal", StringComparison.OrdinalIgnoreCase));
        if (normal != null) return normal.Member;
        return variants[0].Member;
    }
}
