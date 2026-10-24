using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public sealed class ExpansionPatchSite
{
    public int Offset { get; init; }
    public string Purpose { get; init; } = "";
    public string State { get; set; } = ""; // "already-applied", "applied-now", "unrecognized"

    public override string ToString() => $"0x{Offset:X7} [{State}] {Purpose}";
}

public sealed class ExpansionPatchResult
{
    public List<ExpansionPatchSite> Sites { get; } = [];
    public List<string> Notes { get; } = [];
    public bool Success => Sites.Count > 0 && Sites.All(s => s.State != "unrecognized");
    public string Describe() => string.Join(Environment.NewLine,
        new[] { $"{Sites.Count} site(s):" }.Concat(Sites.Select(s => "  " + s)).Concat(Notes.Select(n => "  ! " + n)));
}

public static class ExpansionPatchApplier
{
    public static ExpansionPatchResult Ensure(byte[] fileData, string targetFile, string section, GameVersion version = GameVersion.Invalid)
    {
        var result = new ExpansionPatchResult();
        if (fileData == null) { result.Notes.Add("no file data supplied"); return result; }

        foreach (var entry in ExpansionCodeMap.GetEntriesForFile(targetFile, version))
        {
            if (!entry.Section.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            if (!entry.HasAddress) continue;
            if (!entry.TryGetCurrentBytes(out var current) || current.Length == 0) continue;

            int offset = (int)entry.StartOffset;
            if (offset + current.Length > fileData.Length)
            {
                result.Notes.Add($"0x{offset:X7} SKIPPED: row extends past the end of the supplied file (is this the right file/version?)");
                continue;
            }

            var live = fileData.Skip(offset).Take(current.Length).ToArray();
            var site = new ExpansionPatchSite { Offset = offset, Purpose = entry.Purpose };
            result.Sites.Add(site);

            if (live.SequenceEqual(current))
            {
                site.State = "already-applied";
                continue;
            }

            if (entry.TryGetRetailBytes(out var retail) && retail.Length == current.Length && live.SequenceEqual(retail))
            {
                Array.Copy(current, 0, fileData, offset, current.Length);
                site.State = "applied-now";
                continue;
            }

            site.State = "unrecognized";
            result.Notes.Add($"0x{offset:X7} SKIPPED: live bytes match neither the documented retail nor current state - " +
                              "left untouched rather than guessed at (a prior, different edit may already be here)");
        }

        if (result.Sites.Count == 0)
            result.Notes.Add($"no documented rows found for {targetFile} section \"{section}\"");

        return result;
    }
}
