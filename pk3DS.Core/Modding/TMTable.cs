#nullable enable

using System;
using System.Collections.Generic;

namespace pk3DS.Core.Modding;

/// <summary>
/// Locates, reads and writes the USUM TM tables (move ids and item ids) in a decompressed code.bin.
/// <para>
/// Two layouts are recognised: the stock game (bound found from the CMP that guards the table lookup) and
/// pk3DS's own expanded-TM code block (see <c>TMExpansion</c>). Anything else
/// returns null from <see cref="Locate"/> with a reason; it never guesses.
/// </para>
/// Pure on purpose: only System, no GameConfig or WinForms.
/// </summary>
public sealed class TMTable
{
    public enum Kind { Stock, CustomBlock }

    private const int LoadAddress = 0x100000;
    private const int ItemDelta = 0x1FA; // item table sits this many bytes before the move table

    public Kind Layout { get; }
    /// <summary>Number of TMs (100 on stock).</summary>
    public int Count { get; }
    public int MoveOffset { get; }
    public int ItemOffset { get; }

    private TMTable(Kind layout, int count, int moveOffset, int itemOffset)
    {
        Layout = layout;
        Count = count;
        MoveOffset = moveOffset;
        ItemOffset = itemOffset;
    }

    /// <summary>The table, or null with the reason in <paramref name="error"/>.</summary>
    public static TMTable? Locate(byte[] code, out string error)
    {
        error = "";
        var custom = LocateCustom(code);
        if (custom != null) return custom;
        return LocateStock(code, out error);
    }

    public ushort[] ReadMoves(byte[] code) => Read(code, MoveOffset);
    public ushort[] ReadItems(byte[] code) => Read(code, ItemOffset);

    /// <summary>Overwrites exactly 2*Count bytes of each table.</summary>
    public void Write(byte[] code, ushort[] moves, ushort[] items)
    {
        if (moves.Length != Count) throw new ArgumentException($"expected {Count} moves, got {moves.Length}", nameof(moves));
        if (items.Length != Count) throw new ArgumentException($"expected {Count} items, got {items.Length}", nameof(items));
        for (int i = 0; i < Count; i++)
        {
            BitConverter.TryWriteBytes(code.AsSpan(MoveOffset + i * 2), moves[i]);
            BitConverter.TryWriteBytes(code.AsSpan(ItemOffset + i * 2), items[i]);
        }
    }

    private ushort[] Read(byte[] code, int ofs)
    {
        var r = new ushort[Count];
        for (int i = 0; i < r.Length; i++) r[i] = BitConverter.ToUInt16(code, ofs + i * 2);
        return r;
    }

    private static uint U32(byte[] c, int o) => BitConverter.ToUInt32(c, o);

    // push {r4,lr}; cmp r0,#?? ; ldrcc r4,[pc,#..]; movcs r0,#0  (bytes 4-5 are the count, masked)
    private static readonly byte[] CustomSig = [0x10, 0x40, 0x2D, 0xE9, 0, 0, 0x50, 0xE3, 0x0C, 0x40, 0x9F, 0x35, 0x00, 0x00, 0xA0, 0x23];
    private static readonly bool[] CustomMask = [true, true, true, true, false, false, true, true, true, true, true, true, true, true, true, true];

    private static TMTable? LocateCustom(byte[] code)
    {
        for (int i = 0; i + 64 <= code.Length; i += 4)
        {
            bool ok = true;
            for (int k = 0; k < CustomSig.Length && ok; k++)
                if (CustomMask[k] && code[i + k] != CustomSig[k]) ok = false;
            if (!ok) continue;
            int count = code[i + 4];
            long mo = (long)U32(code, i + 28) - LoadAddress;
            long io = (long)U32(code, i + 60) - LoadAddress;
            if (count == 0 || mo < 0 || io < 0 || mo + count * 2L > code.Length || io + count * 2L > code.Length) continue;
            return new TMTable(Kind.CustomBlock, count, (int)mo, (int)io);
        }
        return null;
    }

    private static TMTable? LocateStock(byte[] code, out string error)
    {
        error = "";
        // TM01 Work Up (526), TM02 Dragon Claw (337), TM03 Psyshock (473)
        byte[] sig = [0x0E, 0x02, 0x51, 0x01, 0xD9, 0x01];
        var hits = new List<int>();
        for (int i = 0; i + sig.Length <= code.Length; i++)
        {
            int k = 0;
            while (k < sig.Length && code[i + k] == sig[k]) k++;
            if (k == sig.Length) hits.Add(i);
        }
        if (hits.Count != 1) { error = $"TM move signature found {hits.Count} times (need exactly 1)"; return null; }
        int moveOfs = hits[0];
        int itemOfs = moveOfs - ItemDelta;
        if (itemOfs < 0) { error = "item table would start before the file"; return null; }

        var counts = new SortedSet<int>();
        foreach (int lit in LiteralsEqual(code, (uint)(moveOfs + LoadAddress)))
            foreach (int ldr in LdrsTo(code, lit))
                for (int back = 1; back <= 4 && ldr - back * 4 >= 0; back++)
                {
                    uint w = U32(code, ldr - back * 4);
                    if ((w & 0x0FF0F000) == 0x03500000 && (w & 0xF00) == 0) counts.Add((int)(w & 0xFF));
                }
        if (counts.Count == 0) { error = "TM table bound not found"; return null; }
        if (counts.Count > 1) { error = $"TM bound sites disagree: {string.Join(", ", counts)}"; return null; }
        int count = counts.Min;
        if (count == 0 || moveOfs + count * 2 > code.Length) { error = $"TM bound {count} does not fit the file"; return null; }
        if (!LiteralsEqual(code, (uint)(itemOfs + LoadAddress)).GetEnumerator().MoveNext())
        { error = "TM item table is not referenced by any literal"; return null; }
        return new TMTable(Kind.Stock, count, moveOfs, itemOfs);
    }

    /// <summary>File offsets of 4-aligned words equal to <paramref name="value"/>.</summary>
    private static IEnumerable<int> LiteralsEqual(byte[] c, uint value)
    {
        for (int i = 0; i + 4 <= c.Length; i += 4)
            if (U32(c, i) == value) yield return i;
    }

    /// <summary>File offsets of ldr rX,[pc,#imm] instructions (any condition) whose target is <paramref name="lit"/>.</summary>
    private static IEnumerable<int> LdrsTo(byte[] c, int lit)
    {
        // The literal is within 4095 bytes + 8 of its ldr.
        for (int a = Math.Max(0, lit - 4103) & ~3; a <= lit && a + 4 <= c.Length; a += 4)
        {
            uint w = U32(c, a);
            if ((w & 0x0F7F0000) != 0x051F0000) continue;
            int imm = (int)(w & 0xFFF);
            int target = a + 8 + ((w & 0x00800000) != 0 ? imm : -imm);
            if (target == lit) yield return a;
        }
    }
}
