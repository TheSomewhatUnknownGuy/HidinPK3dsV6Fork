using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Reads code.bin's OWN icon-lookup ARM routine's data tables directly, instead of guessing a
/// species/form -> icon-member mapping from filenames or ordinal position. Built after
/// <see cref="PartyIconCatalog"/>'s filename-derived, best-effort form ordering turned out wrong for
/// ~12% of stock species when cross-checked against this (Pikachu's costume forms came back in a
/// completely different order than the catalog's own member-index order suggested, for one) - the
/// user's "Gen 6/7 forms across the board... messed up" report was this exact class of bug.
/// <para>
/// The routine itself lives at code_map.csv's "POKEMON ICONS" section, offset 0x20C88C-0x20C957 (204
/// bytes, disassembled and traced this session): it takes (species, form, ...) and does, in order:
/// </para>
/// <para>
/// 1. A small early-exit guard for a couple of sentinel species ids near 1026/1027 (unrelated to
/// normal lookups, not modeled here). 2. A linear scan of a "compact added form icon" table - packed
/// 16-bit keys (species in the low 11 bits, form in the high 5 bits), each match returning a
/// SEQUENTIAL member starting at 1372 (the first of the 136 slots the one-time icon-cap raise,
/// workstream 1f, reserved) - this is what actually backs Gen 8/9 species' alt forms (e.g.
/// Dudunsparce's second segment, at real entry (982, form 1) -&gt; member 1498) AND extra alt forms
/// for a handful of stock species whose own per-species array (below) doesn't have room (Pikachu's
/// 9th costume form, entry (25, form 8) -&gt; member 1372). 3. If species &lt; 808 and not found
/// above: a direct, 16-byte-per-species table (one record per species 0-807) - <see cref="RecordCount"/>
/// real alt forms (0 for a species with none at all, in which case its base icon comes straight from
/// the record's own field-0/field-2 fallback slots instead of a sub-array) plus a pointer to a
/// per-form u16 member array, AUTHORITATIVE for every stock species' every real form - this is the
/// data <see cref="PartyIconCatalog"/> was approximating. 4. If species &gt;= 808 and form == 0: the
/// already-independently-verified 1154+(id-808) formula (<see cref="SpeciesIconLayout.ExpansionRegionMember"/>).
/// </para>
/// <para>
/// Verified against real ROM data: Venusaur/Charizard/Rattata/Raticate/Pikachu's stock-table entries
/// all read back exactly right (Mega Venusaur=member 4, Mega Charizard X/Y=8/9 in that order,
/// Alolan Rattata=member 25 vs its own Kantonian form=26, Pikachu's 8 costume forms in their real,
/// non-sequential member order); the compact table's 136 entries all decode to real, sensible
/// (species, form) pairs matching this ROM's actual Gen 8/9 alt-form species (Cramorant, Toxtricity,
/// Zacian, Calyrex, Dudunsparce, Tatsugiri, ...).
/// </para>
/// </summary>
public sealed class SpeciesIconTable
{
    public bool Success { get; private init; }
    public string Error { get; private init; } = "";

    /// <summary>First icon member the compact table assigns (to its entry 0) - derived from the ARM's own `mov r8,#0x480 / add r8,r8,#1 / add r7,r8,#0xdb` (1153 + 219 = 1372), confirmed against the real, byte-verified routine.</summary>
    public const int CompactTableFirstMember = 1372;

    /// <summary>Number of compact-table entries; members [1372, 1372+CompactCount) belong to them.</summary>
    public int CompactCount => _compact?.Count ?? 0;

    /// <summary>
    /// The (species, form) whose icon the game draws from <paramref name="member"/> via the compact
    /// table, or null. 1154+(id-808) reaches 1372 at id 1026, so every species inserted past Gen 9
    /// lands on one of these; writing there repaints an existing Pokemon's icon.
    /// </summary>
    public (int Species, int Form)? CompactOwnerOf(int member)
    {
        if (_compact == null) return null;
        foreach (var kv in _compact)
            if (kv.Value == member) return kv.Key;
        return null;
    }

    private readonly byte[] _codeBin;
    private readonly int _stockTableOffset;
    private readonly Dictionary<(int species, int form), int> _compact;

    /// <summary>Whether the routine carries IconEggSentinelPatcher's past-1025 remap.</summary>
    public bool Remapped { get; private init; }

    private SpeciesIconTable(byte[] codeBin, int stockTableOffset, Dictionary<(int species, int form), int> compact, bool success, string error)
    {
        _codeBin = codeBin;
        _stockTableOffset = stockTableOffset;
        _compact = compact ?? new();
        Success = success;
        Error = error;
    }

    public static SpeciesIconTable Analyze(byte[] codeBin, GameVersion version)
    {
        var rows = ExpansionCodeMap.GetEntriesForSection("POKEMON ICONS")
            .Where(e => version == GameVersion.Invalid || version == GameVersion.USUM ||
                        (version == GameVersion.US && e.ForUltraSun) || (version == GameVersion.UM && e.ForUltraMoon))
            .ToList();

        var routineRow = rows.FirstOrDefault(e => e.Purpose.Contains("look up normal species", StringComparison.OrdinalIgnoreCase));
        var pointerRow = rows.FirstOrDefault(e => e.Purpose.Contains("compact added form icon data", StringComparison.OrdinalIgnoreCase));
        if (routineRow == null || pointerRow == null)
            return Fail(codeBin, "code_map.csv doesn't document the POKEMON ICONS lookup routine (or this pk3DS build's copy of it is out of date) - can't locate the icon tables.");

        if (!routineRow.TryGetCurrentBytes(out var expectedRoutine) ||
            (int)(routineRow.EndOffset + 1) > codeBin.Length ||
            !RoutineMatches(codeBin, (int)routineRow.StartOffset, expectedRoutine))
        {
            return Fail(codeBin, $"code.bin doesn't match the documented icon-lookup routine at 0x{routineRow.StartOffset:X} - " +
                                  "this ROM's code.bin differs from what this was verified against, so the derived table locations can't be trusted.");
        }

        if (!pointerRow.TryGetCurrentBytes(out var expectedPointerBytes) || expectedPointerBytes.Length != 4 ||
            (int)(pointerRow.EndOffset + 1) > codeBin.Length ||
            !RegionEquals(codeBin, (int)pointerRow.StartOffset, expectedPointerBytes))
        {
            return Fail(codeBin, $"code.bin doesn't match the documented icon-table pointer at 0x{pointerRow.StartOffset:X}.");
        }

        uint compactTableVA = BitConverter.ToUInt32(codeBin, (int)pointerRow.StartOffset);
        long compactTableOffset = compactTableVA - 0x100000;
        if (compactTableOffset < 0 || compactTableOffset + 2 > codeBin.Length)
            return Fail(codeBin, $"the compact icon table's pointer (0x{compactTableVA:X}) doesn't land inside code.bin.");

        var compact = new Dictionary<(int species, int form), int>();
        int count = 0;
        for (long off = compactTableOffset; off + 2 <= codeBin.Length; off += 2, count++)
        {
            ushort raw = BitConverter.ToUInt16(codeBin, (int)off);
            if (raw == 0) break;
            int species = raw & 0x7FF;
            int form = raw >> 11;
            compact.TryAdd((species, form), CompactTableFirstMember + count); // linear scan: first match wins
            if (count > 4096) return Fail(codeBin, "the compact icon table never terminated within a sane bound - refusing to keep scanning.");
        }

        // Stock table sits right after the compact table's 0x0000 terminator, offset by the ARM's
        // own `add r1,r1,#0x5c` (92 bytes) - verified this session against real Venusaur/Charizard/
        // Rattata/Pikachu data.
        long stockOffset = compactTableOffset + (count * 2) + 2 + 0x5C;
        int stockTableEnd = (int)stockOffset + SpeciesIconLayout.StockSpeciesCount * 16;
        if (stockOffset < 0 || stockTableEnd > codeBin.Length)
            return Fail(codeBin, "the derived stock-species icon table location doesn't land inside code.bin.");

        return new SpeciesIconTable(codeBin, (int)stockOffset, compact, true, "") { Remapped = IconEggSentinelPatcher.IsRemapped(codeBin, (int)routineRow.StartOffset) };
    }

    private static SpeciesIconTable Fail(byte[] codeBin, string error) => new(codeBin, 0, null, false, error);

    // The Egg-sentinel SUB (+0x18) and the formula tail (+0x68..+0x77) are rewritten by
    // IconEggSentinelPatcher on every insertion past 1025; an exact match rejected those ROMs and
    // dropped the icon editor/insertion to the heuristic catalog.
    private static bool RoutineMatches(byte[] data, int offset, byte[] expected)
    {
        if (offset < 0 || offset + expected.Length > data.Length) return false;
        for (int i = 0; i < expected.Length; i++)
        {
            if ((i >= 0x18 && i < 0x1C) || (i >= 0x68 && i < 0x78)) continue;
            if (data[offset + i] != expected[i]) return false;
        }
        return true;
    }

    private static bool RegionEquals(byte[] data, int offset, byte[] expected)
    {
        if (offset < 0 || offset + expected.Length > data.Length) return false;
        for (int i = 0; i < expected.Length; i++)
            if (data[offset + i] != expected[i]) return false;
        return true;
    }

    private int RecordCount(int species) => BitConverter.ToUInt16(_codeBin, _stockTableOffset + species * 16 + 0xC);
    private uint SubPointerVA(int species) => BitConverter.ToUInt32(_codeBin, _stockTableOffset + species * 16 + 4);
    private ushort Field0(int species) => BitConverter.ToUInt16(_codeBin, _stockTableOffset + species * 16 + 0);

    private ushort StockFormMember(int species, int form)
    {
        int subOffset = (int)(SubPointerVA(species) - 0x100000) + form * 2;
        return BitConverter.ToUInt16(_codeBin, subOffset);
    }

    /// <summary>
    /// The real icon member for (speciesId, form) - form 0 is a species' own base/default
    /// appearance, form N (N&gt;=1) its Nth alt form in the SAME order the personal table's own
    /// FormeCount/form data already uses elsewhere in this codebase. Returns null when this ROM
    /// genuinely has no icon for that exact (species, form) pair (not every alt form has one).
    /// </summary>
    public int? ResolveMember(int speciesId, int form)
    {
        if (!Success) return null;

        // The routine scans the compact table BEFORE either species path (orr r3,r0,r4,lsl#11 ->
        // ldrh/cmp loop -> only on the 0x0000 terminator does it branch to the >=808 formula or the
        // stock table). Checking stock first sent any compact-covered (species, form) to the wrong
        // member, so an imported icon landed on a different Pokemon's slot.
        if (_compact.TryGetValue((speciesId, form), out int compactMember))
            return compactMember;

        if (speciesId < SpeciesIconLayout.StockSpeciesCount)
        {
            int count = RecordCount(speciesId);
            if (form < count)
                return StockFormMember(speciesId, form);
            if (form == 0 && count == 0)
                return Field0(speciesId);
            return null;
        }

        return form == 0 ? SpeciesIconLayout.ExpansionRegionMember(speciesId, Remapped) : null;
    }
}
