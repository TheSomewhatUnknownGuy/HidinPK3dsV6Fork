namespace pk3DS.Core.Modding.Research;

/// <summary>
/// The `partyicon`/`menuicon` GARCs (a257 / a/0/6/2 - the box/party-list and summary-screen icons,
/// a completely separate GARC from the 3D battle model in `models`/a094) are flat arrays of images,
/// but the "icon member" a given species occupies is NOT simply its species id for the stock range.
/// <para>
/// Corrected after a real, live mismatch the user caught (species 150/Mewtwo's slot actually held
/// Marowak's icon): for species &lt; <see cref="StockSpeciesCount"/>, member != species id at all
/// past the first Mega Evolution (Venusaur, dex 3) - it's a single flat, national-dex-ordered
/// catalog where each species contributes its base entry plus zero or more INTERLEAVED alt-form
/// entries (mega evolutions, Alolan forms, regional variants, costumes, gender differences...), so
/// every later species has drifted away from its own dex number by however many extra entries came
/// before it. There is no formula for this range - see <see cref="PartyIconCatalog"/>, which reads
/// the filename embedded in every entry's own header as ground truth instead of guessing.
/// </para>
/// <para>
/// Species &gt;= <see cref="StockSpeciesCount"/> (confirmed via those same embedded filenames -
/// every real 808-1025 species is tagged "poke_icon_exp_0NNN.tga", and every currently-unassigned
/// slot past that, reserved by the one-time 1507-member icon-cap raise workstream 1f, is tagged
/// "poke_icon_form_NNNN.tga") ARE a reliable formula: <see cref="ExpansionRegionMember"/>.
/// </para>
/// </summary>
public static class SpeciesIconLayout
{
    public const int StockSpeciesCount = 808;
    public const int ExpandedRegionBase = 1154;

    /// <summary>
    /// Reliable ONLY for species &gt;= <see cref="StockSpeciesCount"/> - confirmed live against every
    /// real 808-1025 species' own embedded "exp_0NNN" filename, and every reserved-but-unassigned
    /// slot past that. Do NOT use this for a stock species (id &lt; 808) - use
    /// <see cref="PartyIconCatalog"/>'s real, filename-derived lookup instead; member != id there.
    /// </summary>
    /// <summary>Last species the direct 1154+(id-808) formula serves (width 217); Gen 9 ends here.</summary>
    public const int DirectFormulaMaxSpecies = 1025;

    /// <summary>Size of the compact alt-form block (members 1372-1507) the remap steps over.</summary>
    public const int CompactBlockSize = 136;

    /// <summary>
    /// Icon member for species &gt;= 808, form 0, on a ROM whose lookup routine carries the remap
    /// <see cref="IconEggSentinelPatcher"/> installs: ids past 1025 skip the compact block and start
    /// at 1508, instead of landing on members 1372+ that other Pokemon's alt forms already own.
    /// </summary>
    public static int ExpansionRegionMember(int speciesId) => ExpansionRegionMember(speciesId, remapped: true);

    public static int ExpansionRegionMember(int speciesId, bool remapped)
    {
        int member = ExpandedRegionBase + (speciesId - StockSpeciesCount);
        return remapped && speciesId > DirectFormulaMaxSpecies ? member + CompactBlockSize : member;
    }

    /// <summary>
    /// Overwrites one icon member's partyicon/menuicon entries with a copy of another member's own
    /// entries - the same operation <see cref="SpeciesInsertionOrchestrator"/> runs automatically for
    /// a newly-inserted species' "Clone Model" choice, exposed standalone here so an EXISTING
    /// species (or any stock species whose icon should just be swapped directly) can get the same
    /// treatment without re-running the whole insertion pipeline. Operates on already-resolved
    /// MEMBER indices, not species ids - resolve those first (via <see cref="PartyIconCatalog"/> for
    /// an existing species, or <see cref="ExpansionRegionMember"/> for a not-yet-assigned reserved
    /// slot) since a plain species id is not enough information for the stock range. Pure - returns
    /// new arrays, touches nothing on disk; the caller saves them exactly like any other GARC edit in
    /// this codebase (GetGARCData("partyicon").Files = ...; .Save()).
    /// </summary>
    public static IconCopyResult CopyIconByMember(byte[][] partyIconFiles, byte[][] menuIconFiles, int fromMember, int toMember)
    {
        var r = new IconCopyResult { FromMember = fromMember, ToMember = toMember };

        if (partyIconFiles == null || menuIconFiles == null)
        { r.Error = "partyicon/menuicon data is missing"; return r; }
        if (fromMember < 0 || fromMember >= partyIconFiles.Length || fromMember >= menuIconFiles.Length)
        { r.Error = $"source icon member ({fromMember}) is out of range for this ROM's partyicon/menuicon data ({partyIconFiles.Length}/{menuIconFiles.Length} members)"; return r; }
        if (toMember < 0 || toMember >= partyIconFiles.Length || toMember >= menuIconFiles.Length)
        { r.Error = $"target icon member ({toMember}) is out of range for this ROM's partyicon/menuicon data ({partyIconFiles.Length}/{menuIconFiles.Length} members)"; return r; }

        r.PartyIconFiles = (byte[][])partyIconFiles.Clone();
        r.MenuIconFiles = (byte[][])menuIconFiles.Clone();
        r.PartyIconFiles[toMember] = (byte[])partyIconFiles[fromMember].Clone();
        r.MenuIconFiles[toMember] = (byte[])menuIconFiles[fromMember].Clone();
        r.Success = true;
        return r;
    }
}

public sealed class IconCopyResult
{
    public bool Success;
    public string Error = "";
    public byte[][] PartyIconFiles = [];
    public byte[][] MenuIconFiles = [];
    public int FromMember;
    public int ToMember;
}
