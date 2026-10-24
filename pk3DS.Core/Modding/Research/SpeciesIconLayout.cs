namespace pk3DS.Core.Modding.Research;

public static class SpeciesIconLayout
{
    public const int StockSpeciesCount = 808;
    public const int ExpandedRegionBase = 1154;

    public const int DirectFormulaMaxSpecies = 1025;

    public const int CompactBlockSize = 136;

    public static int ExpansionRegionMember(int speciesId) => ExpansionRegionMember(speciesId, remapped: true);

    public static int ExpansionRegionMember(int speciesId, bool remapped)
    {
        int member = ExpandedRegionBase + (speciesId - StockSpeciesCount);
        return remapped && speciesId > DirectFormulaMaxSpecies ? member + CompactBlockSize : member;
    }

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
