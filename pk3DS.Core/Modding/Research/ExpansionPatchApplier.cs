using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>One documented, fixed (non-ceiling) code_map.csv row checked against a live binary.</summary>
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

/// <summary>
/// Applies a documented `code_map.csv` section's fixed byte patches - the ones that don't scale
/// with a target count (unlike <see cref="SpeciesCeilingPatcher"/>/<see cref="ItemCeilingPatcher"/>),
/// just a one-time retail-bytes-to-current-bytes swap. Every row is byte-verified against the live
/// binary before being touched: if it already reads as the Expansion Pack's own current bytes,
/// nothing happens; if it still reads as retail, the row's current bytes are written; anything else
/// is left alone and reported rather than guessed at.
/// <para>
/// Serves workstream 1's "MODEL FLAGS" (the 0xCA0-&gt;0x1100 model-header offset move) and "POKEMON
/// ICONS" (the 1154+(species-808) icon-index rewrite at 0x20C8B4) needs - both are one-shot patches
/// a target ROM either already has or needs applying once, not something that scales with how many
/// species get inserted afterward.
/// </para>
/// </summary>
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
