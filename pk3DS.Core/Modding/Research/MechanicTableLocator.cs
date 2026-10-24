using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public sealed class LocatedMechanicTable
{
    public string Name { get; init; } = "";
    public CustomMechanicKind? Kind { get; init; }
    public uint TableOffset { get; init; }
    public int EntryCount { get; init; }
    public int Matched { get; init; }
    public int Compared { get; init; }
    public uint DocumentedOffset { get; init; }
    public long Shift => (long)TableOffset - DocumentedOffset;

    public double Confidence => Compared == 0 ? 0 : (double)Matched / Compared;

    public Dictionary<uint, string> Names { get; init; } = [];

    public override string ToString() =>
        $"{Name} @0x{TableOffset:X} ({EntryCount} entries, {Matched}/{Compared} id match, shift {(Shift >= 0 ? "+" : "-")}0x{Math.Abs(Shift):X})";
}

public static class MechanicTableLocator
{
    private const int FingerprintLength = 48;

    private const double AcceptThreshold = 0.75;

    public static List<LocatedMechanicTable> LocateAll(byte[] rom, ResearchDatabase db, ISet<uint> relocatedSlots = null)
    {
        var results = new List<LocatedMechanicTable>();
        if (rom == null || db == null) return results;

        foreach (var index in db.MechanicIndexes)
        {
            var found = Locate(rom, index, relocatedSlots);
            if (found != null) results.Add(found);
        }
        return results;
    }

    public static LocatedMechanicTable Locate(byte[] rom, ResearchMechanicIndex index, ISet<uint> relocatedSlots = null)
    {
        if (rom == null || index == null || index.Entries.Count == 0) return null;

        uint[] want = index.Fingerprint;
        int n = Math.Min(FingerprintLength, want.Length);
        if (n < 8) return null; // too short to identify anything reliably

        int need = (int)Math.Ceiling(n * AcceptThreshold);
        uint first = want[0];

        uint bestAt = 0;
        int bestMatched = -1;

        // Anchor on the first id to avoid scoring every 4-byte position in a 1.4 MB file.
        for (int at = 0; at + n * 8 <= rom.Length; at += 4)
        {
            if (BitConverter.ToUInt32(rom, at) != first) continue;

            int matched = 0;
            for (int k = 0; k < n; k++)
                if (BitConverter.ToUInt32(rom, at + k * 8) == want[k]) matched++;

            if (matched > bestMatched) { bestMatched = matched; bestAt = (uint)at; }
            if (matched == n) break; // exact - stop looking
        }

        if (bestMatched < need) return null;

        return new LocatedMechanicTable
        {
            Name = index.Name,
            Kind = index.Kind,
            TableOffset = bestAt,
            EntryCount = CountEntries(rom, bestAt, index, relocatedSlots),
            Matched = bestMatched,
            Compared = n,
            DocumentedOffset = index.DocumentedTableOffset,
            Names = BuildNameMap(index),
        };
    }

    private static int CountEntries(byte[] rom, uint tableOffset, ResearchMechanicIndex index, ISet<uint> relocatedSlots)
    {
        int max = Math.Min(Math.Max(index.Entries.Count * 4, 64), (rom.Length - (int)tableOffset) / 8);
        var seen = new HashSet<uint>();

        int count = 0;
        for (int k = 0; k < max; k++)
        {
            uint at = tableOffset + (uint)(k * 8);
            uint id = BitConverter.ToUInt32(rom, (int)at);

            // Ids are 16-bit in practice; anything larger is not an index entry.
            if (id > 0xFFFF) break;
            // An id that has already appeared means we've left the table.
            if (!seen.Add(id)) break;
            // When relocation data is available, a real entry always has its handler slot filled.
            if (relocatedSlots != null && !relocatedSlots.Contains(at + 4)) break;

            count++;
        }
        return count;
    }

    public static List<(uint Id, string Name, uint EntryOffset, uint Handler)> ReadEntries(
        byte[] rom, LocatedMechanicTable table, Func<uint, uint> resolveHandler = null)
    {
        var list = new List<(uint, string, uint, uint)>();
        if (rom == null || table == null) return list;

        for (int k = 0; k < table.EntryCount; k++)
        {
            uint at = table.TableOffset + (uint)(k * 8);
            if (at + 8 > rom.Length) break;

            uint id = BitConverter.ToUInt32(rom, (int)at);
            uint handler = resolveHandler?.Invoke(at + 4) ?? BitConverter.ToUInt32(rom, (int)at + 4);
            table.Names.TryGetValue(id, out string name);
            list.Add((id, name ?? "", at, handler));
        }
        return list;
    }

    private static Dictionary<uint, string> BuildNameMap(ResearchMechanicIndex index)
    {
        var map = new Dictionary<uint, string>();
        foreach (var e in index.Entries)
        {
            if (string.IsNullOrWhiteSpace(e.Name)) continue;
            map.TryAdd(e.Id, e.Name.Trim());
        }
        return map;
    }
}
