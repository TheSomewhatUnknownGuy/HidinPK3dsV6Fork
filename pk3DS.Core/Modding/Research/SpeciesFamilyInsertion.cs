using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>Result of inserting new base species across every per-species GARC at once.</summary>
public sealed class SpeciesFamilyInsertResult
{
    public byte[][] Personal { get; set; } = [];
    public byte[][] Evolution { get; set; } = [];
    public byte[][] Levelup { get; set; } = [];
    public byte[][] Megaevo { get; set; } = [];
    public byte[][] EvolutionBase { get; set; } = [];
    public int InsertedAt { get; init; }
    public int InsertedCount { get; init; }
    public int PersonalCrossReferencesShifted { get; set; }
    public List<string> Notes { get; } = [];
    public string Describe() => string.Join(Environment.NewLine,
        new[] { $"inserted {InsertedCount} new base species at index {InsertedAt}, " +
                $"{PersonalCrossReferencesShifted} personal-table cross-reference(s) shifted to match" }
        .Concat(Notes));
}

/// <summary>
/// Inserts new base species entries at matching indices across every GARC that lays its data out
/// one record per personal-table slot: `personal` (a017), `evolution`, `levelup`, `megaevo` - plus
/// `evolutionbase` (a018), which uses a *different*, simpler indexing space (see below) and is
/// handled separately.
/// <para>
/// Verified this session against a real ROM: `evolution`/`levelup`/`megaevo` all have exactly one
/// entry per `personal` slot (1330 real entries, matching `personal`'s 1330 real entries + 1 master
/// table blob) with no internal field that references another index - a pure parallel array, so
/// they only need the splice, never reindexing. `personal` itself is the one exception: its
/// `FormStatsIndex` field (offset 0x1C, u16 - `PersonalInfoXY.cs`/`PersonalInfoSM.cs`) points at
/// another slot in the very same array (that species' own first alt-form record) and must be
/// shifted for every entry whose stored value lands at or past the insertion point.
/// </para>
/// <para>
/// A real, initially-alarming discovery from this session, resolved rather than worked around:
/// the block usually thought of as "the alt-form data" (with sensible-looking `FormStatsIndex`
/// values in the 1100s) does not actually start immediately after the last base species - there is
/// a second, unlabelled region in between (in this ROM, roughly indices 1026-1133) whose entries
/// are self-referencing (`FormStatsIndex` equal to the entry's own index). What that region
/// represents semantically (a form of the newer 808+ species, a reserved block, something else)
/// was not conclusively identified - and doesn't need to be, because it turned out unnecessary:
/// since its own self-reference values are also &gt;= the insertion point, the exact same
/// generic "shift anything &gt;= the boundary" rule in <see cref="SpeciesArraySplicer"/> already
/// handles it correctly without needing to know what it is. `altFormBlockStart` is therefore set
/// to the insertion point itself (immediately after the last real base species), not to wherever
/// the "obviously alt-form-shaped" data happens to start.
/// </para>
/// <para>
/// <b>Caller's responsibility:</b> `personal`'s `Files` array ends with one oversized master-table
/// blob that is not a per-species record at all (`Main.cs`'s own `B_Personal_Click` already strips
/// it before editing and rebuilds it fresh afterward) - pass only the real per-species entries
/// here (i.e. `Files[..^1]`), and rebuild/append the master table the same established way once
/// this returns, exactly like the existing personal-stats save path already does.
/// </para>
/// </summary>
public static class SpeciesFamilyInsertion
{
    /// <summary>Byte offset of `PersonalInfoXY`/`PersonalInfoSM`'s `FormStatsIndex` field.</summary>
    public const int PersonalFormStatsIndexOffset = 0x1C;

    public static SpeciesFamilyInsertResult InsertBaseSpecies(
        byte[][] personalFiles, byte[][] evolutionFiles, byte[][] levelupFiles, byte[][] megaevoFiles,
        int insertAt, int count)
    {
        if (personalFiles == null) throw new ArgumentNullException(nameof(personalFiles));

        var result = new SpeciesFamilyInsertResult { InsertedAt = insertAt, InsertedCount = count };

        var p = SpeciesArraySplicer.Insert(personalFiles, insertAt, count,
            altFormBlockStart: insertAt, crossRefByteOffset: PersonalFormStatsIndexOffset, crossRefSize: 2);
        result.Personal = p.Files;
        result.PersonalCrossReferencesShifted = p.CrossReferencesShifted;

        if (evolutionFiles != null)
        {
            if (evolutionFiles.Length != personalFiles.Length)
                result.Notes.Add($"evolution has {evolutionFiles.Length} entries, personal has {personalFiles.Length} - " +
                                 "expected them to match 1:1; double-check before trusting this insertion");
            result.Evolution = SpeciesArraySplicer.Insert(evolutionFiles, insertAt, count).Files;
        }

        if (levelupFiles != null)
        {
            if (levelupFiles.Length != personalFiles.Length)
                result.Notes.Add($"levelup has {levelupFiles.Length} entries, personal has {personalFiles.Length} - " +
                                 "expected them to match 1:1; double-check before trusting this insertion");
            result.Levelup = SpeciesArraySplicer.Insert(levelupFiles, insertAt, count).Files;
        }

        if (megaevoFiles != null)
        {
            if (megaevoFiles.Length != personalFiles.Length)
                result.Notes.Add($"megaevo has {megaevoFiles.Length} entries, personal has {personalFiles.Length} - " +
                                 "expected them to match 1:1; double-check before trusting this insertion");
            result.Megaevo = SpeciesArraySplicer.Insert(megaevoFiles, insertAt, count).Files;
        }

        return result;
    }

    /// <summary>
    /// `evolutionbase` (a018) uses a *different* indexing space than the four GARCs above: one
    /// 2-byte entry per real species id (0..MaxSpeciesID), not one per personal-table slot (which
    /// also has alt-form entries interleaved in). New species get new, never-before-used ids
    /// appended at the end of that space - nothing existing needs to move or be reindexed, since
    /// nothing yet points at the new ids. Each new entry defaults to pointing at itself (the new
    /// species is its own lowest evolution) unless the caller already knows it evolves from
    /// something else.
    /// </summary>
    public static byte[][] AppendEvolutionBase(byte[][] evolutionBaseFiles, int count, int firstNewSpeciesId)
    {
        if (evolutionBaseFiles == null) throw new ArgumentNullException(nameof(evolutionBaseFiles));
        int oldLen = evolutionBaseFiles.Length;
        var result = new byte[oldLen + count][];
        Array.Copy(evolutionBaseFiles, result, oldLen);
        for (int i = 0; i < count; i++)
        {
            var entry = new byte[2];
            BitConverter.GetBytes((ushort)(firstNewSpeciesId + i)).CopyTo(entry, 0);
            result[oldLen + i] = entry;
        }
        return result;
    }
}
