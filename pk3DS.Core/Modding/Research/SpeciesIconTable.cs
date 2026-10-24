using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public sealed class SpeciesIconTable
{
    public bool Success { get; private init; }
    public string Error { get; private init; } = "";

    public const int CompactTableFirstMember = 1372;

    public int CompactCount => _compact?.Count ?? 0;

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
