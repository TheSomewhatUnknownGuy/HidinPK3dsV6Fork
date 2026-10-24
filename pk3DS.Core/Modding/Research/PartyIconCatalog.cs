using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

public sealed class PartyIconEntry
{
    public int Member;
    public string RawFilename = "";

    public int SpeciesId = -1;

    public string FormTag = "";

    public bool IsExpansionRegion;
    public bool IsReservedFutureSlot;
    public bool IsEgg;
}

public static class PartyIconCatalog
{
    private static readonly Regex StockPattern = new(@"^poke_icon_(\d+)(?:_([a-z0-9]+))?_(m|f)(_r)?_\d+$", RegexOptions.IgnoreCase);
    private static readonly Regex ExpPattern = new(@"^poke_icon_exp_(\d+)$", RegexOptions.IgnoreCase);
    private static readonly Regex FormPattern = new(@"^poke_icon_form_(\d+)$", RegexOptions.IgnoreCase);
    private static readonly Regex EggPattern = new(@"^poke_icon_egg_", RegexOptions.IgnoreCase);

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

    public static Dictionary<int, List<PartyIconEntry>> GroupBySpecies(this List<PartyIconEntry> entries) =>
        entries.Where(e => e.SpeciesId >= 0 && e.SpeciesId < SpeciesIconLayout.StockSpeciesCount)
               .GroupBy(e => e.SpeciesId)
               .ToDictionary(g => g.Key, g => g.ToList());

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
