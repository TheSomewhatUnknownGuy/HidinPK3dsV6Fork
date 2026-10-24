#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding;

/// <summary>
/// Grows the USUM TM table from the stock 100 to up to <see cref="MaxCount"/> entries, all or nothing.
/// <para>
/// The five routines and both new tables go into a block of code.bin's .text that the caller reserved (the room), and
/// the stock code that reads the table is hooked with branches to them. Pure: returns a new buffer, never writes a
/// file, and never searches for space itself (CodeSpaceAllocator drags in the Research code, which this file avoids).
/// Ported from the WinForms-bound ResearchEngine patch it replaced, which has since been removed.
/// </para>
/// </summary>
public static class TMExpansion
{
    /// <summary>Largest TM count this patch allows; with <see cref="CodeBytes"/> it sets the space budget.</summary>
    public const int MaxCount = 128;

    /// <summary>The largest TM count allowed right now: <see cref="MaxCount"/>, or <see cref="PassiveGuard.TmCap"/> while passive abilities own personal bytes 0x35-0x37.</summary>
    public static int EffectiveMax => PassiveGuard.Enabled ? PassiveGuard.TmCap : MaxCount;

    /// <summary>Bytes of routine code: 68 + 68 + 32 + 32 + 88.</summary>
    public const int CodeBytes = 288;

    private const int LoadAddress = 0x100000;

    // Hook sites, with the same bytes and masks as ResearchEngine (a 0x00 mask byte is a wildcard).
    internal static readonly byte[] IsTmHmSig = [0xA4, 0x20, 0x9F, 0xE5, 0x64, 0xC0, 0xA0, 0xE3, 0x00, 0x10, 0xA0, 0xE3, 0x04, 0x40, 0x2D, 0xE5];
    internal static readonly byte[] IsTmHmMask = [0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
    internal static readonly byte[] ItemToOrderSig = [0xA4, 0x20, 0x9F, 0xE5, 0x00, 0x10, 0xA0, 0xE1, 0x64, 0xC0, 0xA0, 0xE3, 0x00, 0x00, 0xA0, 0xE3, 0x04, 0x40, 0x2D, 0xE5];
    internal static readonly byte[] ItemToOrderMask = [0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
    internal static readonly byte[] ItemToMoveSig = [0x04, 0x40, 0x2D, 0xE5, 0xAC, 0x40, 0x9F, 0xE5, 0xAC, 0x20, 0x9F, 0xE5];
    internal static readonly byte[] ItemToMoveMask = [0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
    internal static readonly byte[] VanillaOrderSig = [0x10, 0x40, 0x2D, 0xE9, 0x00, 0x00, 0x50, 0xE3, 0x00, 0x40, 0xA0, 0xE1, 0x04, 0x00, 0x00, 0x3A, 0x00, 0x30, 0xA0, 0xE3];
    internal static readonly byte[] VanillaOrderMask = [0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

    /// <summary>
    /// The code with TMs 1..<paramref name="newCount"/> installed, or null with the reason in <paramref name="error"/>.
    /// <paramref name="items"/> and <paramref name="moves"/> need one entry per TM, and their first stock entries must
    /// match the current table. <paramref name="room"/> is the free (file offset, length) block the caller reserved; it must be
    /// 4-aligned and hold <see cref="CodeBytes"/> + 4 * newCount bytes. The input is never modified.
    /// </summary>
    public static byte[]? Expand(byte[] code, int newCount, ushort[] items, ushort[] moves, (int Offset, int Length) room, out string error)
    {
        error = "";
        if (newCount > EffectiveMax && PassiveGuard.Enabled) { error = PassiveGuard.TmCapReason; return null; }
        var t = TMTable.Locate(code, out error);
        if (t == null) return null;
        if (t.Layout != TMTable.Kind.Stock) { error = $"already expanded to {t.Count}; re-expanding is not supported"; return null; }
        if (newCount <= t.Count || newCount > MaxCount) { error = $"new count must be {t.Count + 1}..{EffectiveMax}"; return null; }
        if (items.Length != newCount || moves.Length != newCount) { error = "items and moves need one entry per TM"; return null; }
        if (!t.ReadMoves(code).AsSpan().SequenceEqual(moves.AsSpan(0, t.Count)) || !t.ReadItems(code).AsSpan().SequenceEqual(items.AsSpan(0, t.Count)))
        { error = "the first TMs must match the current table"; return null; }

        // Find every site before writing anything, so a refusal leaves no trace.
        var c = (byte[])code.Clone();
        int stockMoveVA = t.MoveOffset + LoadAddress, stockItemVA = t.ItemOffset + LoadAddress;
        int siteIsTmHm = FindMasked(c, IsTmHmSig, IsTmHmMask, 0);
        int siteItemToOrder = FindMasked(c, ItemToOrderSig, ItemToOrderMask, 0);
        int siteItemToMove = FindMasked(c, ItemToMoveSig, ItemToMoveMask, 0);
        var siteToMove = new List<int>();
        var siteToItem = new List<int>();
        for (int o = FindMasked(c, VanillaOrderSig, VanillaOrderMask, 0); o >= 0; o = FindMasked(c, VanillaOrderSig, VanillaOrderMask, o + 20))
        {
            if (o + 0x48 > c.Length) continue;
            uint lit = BitConverter.ToUInt32(c, o + 0x44);
            if (lit == (uint)stockMoveVA) siteToMove.Add(o);
            else if (lit == (uint)stockItemVA) siteToItem.Add(o);
        }
        int fallback = siteItemToMove < 0 ? -1 : ZCrystalFallback(c, siteItemToMove);
        string? missing =
            siteIsTmHm < 0 ? "isTmHm" :
            siteItemToOrder < 0 ? "itemToOrder" :
            siteItemToMove < 0 ? "itemToMove" :
            fallback < 0 ? "itemToMove's cmp r1,#100" :
            siteToMove.Count != 1 ? "orderToMove (need exactly one)" :
            siteToItem.Count != 1 ? "orderToItem (need exactly one)" : null;
        if (missing != null) { error = $"hook site not found: {missing}"; return null; }

        int need = CodeBytes + 4 * newCount;
        if (room.Length < need) { error = $"not enough executable padding in .text: needs {need} bytes, {room.Length} available"; return null; }
        if (room.Offset < 0 || room.Offset % 4 != 0 || room.Offset + need > c.Length) { error = $"the room at 0x{room.Offset:X} is not a 4-aligned block inside code.bin"; return null; }
        int at = room.Offset;

        // Layout: [routines][item table][move table], starting at the padding run. The move table is only
        // halfword-aligned when newCount is odd, which is all ldrh needs.
        int codeVA = at + LoadAddress;
        int newItemVA = codeVA + CodeBytes;
        int newMoveVA = newItemVA + 2 * newCount;
        byte[] p1 = IsTmHmRoutine(newItemVA, newCount);
        byte[] p2 = ItemToOrderRoutine(newItemVA, newCount);
        byte[] p3 = OrderToMoveRoutine(newMoveVA, newCount);
        byte[] p4 = OrderToItemRoutine(newItemVA, newCount);
        byte[] p5 = ItemToMoveRoutine(newItemVA, newMoveVA, newCount, fallback);
        int e1 = codeVA, e2 = e1 + p1.Length, e3 = e2 + p2.Length, e4 = e3 + p3.Length, e5 = e4 + p4.Length;
        byte[] blob = [.. p1, .. p2, .. p3, .. p4, .. p5];
        blob.CopyTo(c, at);
        for (int i = 0; i < newCount; i++)
        {
            BitConverter.TryWriteBytes(c.AsSpan(at + CodeBytes + 2 * i), items[i]);
            BitConverter.TryWriteBytes(c.AsSpan(at + CodeBytes + 2 * newCount + 2 * i), moves[i]);
        }

        Branch(c, siteIsTmHm, e1);
        Branch(c, siteItemToOrder, e2);
        Branch(c, siteToMove[0], e3);
        Branch(c, siteToItem[0], e4);
        Branch(c, siteItemToMove, e5);

        var check = TMTable.Locate(c, out string checkErr);
        if (check == null || check.Layout != TMTable.Kind.CustomBlock || check.Count != newCount
            || !check.ReadMoves(c).AsSpan().SequenceEqual(moves) || !check.ReadItems(c).AsSpan().SequenceEqual(items))
        { error = "internal check failed: the expanded table does not read back" + (checkErr.Length > 0 ? $" ({checkErr})" : ""); return null; }
        return c;
    }

    /// <summary>The lowest <paramref name="need"/> item ids in 1..1023 that <paramref name="isUsable"/> accepts, or none if there are not enough.</summary>
    public static int[] PickItemIds(int need, Func<int, bool> isUsable)
    {
        var ids = Enumerable.Range(1, 1023).Where(isUsable).Take(need).ToArray();
        return ids.Length == need ? ids : [];
    }

    /// <summary>First 4-aligned offset at or after <paramref name="from"/> where the masked pattern matches, or -1.</summary>
    private static int FindMasked(byte[] c, byte[] sig, byte[] mask, int from)
    {
        for (int i = from; i + sig.Length <= c.Length; i += 4)
        {
            int k = 0;
            while (k < sig.Length && (c[i + k] & mask[k]) == (sig[k] & mask[k])) k++;
            if (k == sig.Length) return i;
        }
        return -1;
    }

    /// <summary>Load address of the word after the cmp r1,#100 (0x64 0x00 0x51 0xE3) inside itemToMove, or -1.</summary>
    private static int ZCrystalFallback(byte[] c, int site)
    {
        for (int z = site + 4; z < site + 0x100 && z + 4 <= c.Length; z += 4)
            if (c[z] == 0x64 && c[z + 1] == 0x00 && c[z + 2] == 0x51 && c[z + 3] == 0xE3) return z + 8 + LoadAddress; // skip cmp and bcc
        return -1;
    }

    /// <summary>Overwrites the word at file offset <paramref name="at"/> with a branch to load address <paramref name="target"/>.</summary>
    private static void Branch(byte[] c, int at, int target)
    {
        uint word = 0xEA000000 | (uint)(((target - (at + LoadAddress + 8)) >> 2) & 0xFFFFFF);
        BitConverter.TryWriteBytes(c.AsSpan(at), word);
    }

    // 1. IsTMHM_Assm
    private static byte[] IsTmHmRoutine(int itemTableVA, int count) => [
        0x70, 0x40, 0x2D, 0xE9, // push {r4, r5, r6, lr}
        0x30, 0x40, 0x9F, 0xE5, // ldr r4, [pc, #48] -> itemTableRAM
        0x00, 0x10, 0xA0, 0xE3, // mov r1, #0
        0x2C, 0x60, 0x9F, 0xE5, // ldr r6, [pc, #44] -> limit
        0x81, 0x20, 0x84, 0xE0, // Loop: add r2, r4, r1, lsl #1
        0xB0, 0x20, 0xD2, 0xE1, // ldrh r2, [r2]
        0x00, 0x00, 0x52, 0xE1, // cmp r2, r0
        0x04, 0x00, 0x00, 0x0A, // beq Match
        0x01, 0x10, 0x81, 0xE2, // add r1, r1, #1
        0x06, 0x00, 0x51, 0xE1, // cmp r1, r6
        0xF8, 0xFF, 0xFF, 0x3A, // bcc Loop
        0x00, 0x00, 0xA0, 0xE3, // mov r0, #0
        0x70, 0x80, 0xBD, 0xE8, // pop {r4, r5, r6, pc}
        0x01, 0x00, 0xA0, 0xE3, // Match: mov r0, #1
        0x70, 0x80, 0xBD, 0xE8, // pop {r4, r5, r6, pc}
        ..BitConverter.GetBytes(itemTableVA),
        ..BitConverter.GetBytes(count)
    ];

    // 2. ItemToOrder_Assm
    private static byte[] ItemToOrderRoutine(int itemTableVA, int count) => [
        0x70, 0x40, 0x2D, 0xE9, // push {r4, r5, r6, lr}
        0x30, 0x40, 0x9F, 0xE5, // ldr r4, [pc, #48] -> itemTableRAM
        0x00, 0x10, 0xA0, 0xE3, // mov r1, #0
        0x2C, 0x60, 0x9F, 0xE5, // ldr r6, [pc, #44] -> limit
        0x81, 0x20, 0x84, 0xE0, // Loop: add r2, r4, r1, lsl #1
        0xB0, 0x20, 0xD2, 0xE1, // ldrh r2, [r2]
        0x00, 0x00, 0x52, 0xE1, // cmp r2, r0
        0x04, 0x00, 0x00, 0x0A, // beq Match
        0x01, 0x10, 0x81, 0xE2, // add r1, r1, #1
        0x06, 0x00, 0x51, 0xE1, // cmp r1, r6
        0xF8, 0xFF, 0xFF, 0x3A, // bcc Loop
        0x00, 0x00, 0xA0, 0xE3, // mov r0, #0
        0x70, 0x80, 0xBD, 0xE8, // pop {r4, r5, r6, pc}
        0x01, 0x00, 0xA0, 0xE1, // Match: mov r0, r1
        0x70, 0x80, 0xBD, 0xE8, // pop {r4, r5, r6, pc}
        ..BitConverter.GetBytes(itemTableVA),
        ..BitConverter.GetBytes(count)
    ];

    // 3. OrderToMove_Assm (emitted right before OrderToItem: LocateCustom reads both tables from these literals)
    private static byte[] OrderToMoveRoutine(int moveTableVA, int count)
    {
        uint cmpLim = 0xE3500000 | (uint)count;
        return [
            0x10, 0x40, 0x2D, 0xE9,
            .. BitConverter.GetBytes(cmpLim),
            0x0C, 0x40, 0x9F, 0x35,
            0x00, 0x00, 0xA0, 0x23,
            0x80, 0x00, 0xA0, 0x31,
            0xB0, 0x00, 0x94, 0x31,
            0x10, 0x80, 0xBD, 0xE8,
            ..BitConverter.GetBytes(moveTableVA)
        ];
    }

    // 4. OrderToItem_Assm
    private static byte[] OrderToItemRoutine(int itemTableVA, int count)
    {
        uint cmpLim = 0xE3500000 | (uint)count;
        return [
            0x10, 0x40, 0x2D, 0xE9,
            .. BitConverter.GetBytes(cmpLim),
            0x0C, 0x40, 0x9F, 0x35,
            0x00, 0x00, 0xA0, 0x23,
            0x80, 0x00, 0xA0, 0x31,
            0xB0, 0x00, 0x94, 0x31,
            0x10, 0x80, 0xBD, 0xE8,
            ..BitConverter.GetBytes(itemTableVA)
        ];
    }

    // 5. ItemToMove_Assm
    // Fallback re-pushes r4 and resumes after "cmp r1,#100; bcc" (VA 0x375E30): the code there reloads r2/r12 from its own
    // literals and writes r1 before reading it, so only r0/lr are live and both are intact. Checked by disassembly, not yet booted.
    private static byte[] ItemToMoveRoutine(int itemTableVA, int moveTableVA, int count, int fallbackVA) => [
        0x70, 0x40, 0x2D, 0xE9, // push {r4, r5, r6, lr}
        0x40, 0x40, 0x9F, 0xE5, // ldr r4, [pc, #64] -> uItemRAM
        0x40, 0x50, 0x9F, 0xE5, // ldr r5, [pc, #64] -> uMoveRAM
        0x00, 0x10, 0xA0, 0xE3, // mov r1, #0
        0x3C, 0x60, 0x9F, 0xE5, // ldr r6, [pc, #60] -> uCount
        0x81, 0x20, 0x84, 0xE0, // Loop: add r2, r4, r1, lsl #1
        0xB0, 0x30, 0xD2, 0xE1, // ldrh r3, [r2]
        0x00, 0x00, 0x53, 0xE1, // cmp r3, r0
        0x06, 0x00, 0x00, 0x0A, // beq Match
        0x01, 0x10, 0x81, 0xE2, // add r1, r1, #1
        0x06, 0x00, 0x51, 0xE1, // cmp r1, r6
        0xF8, 0xFF, 0xFF, 0x3A, // bcc Loop
        // No match. Restore registers and fallback to Z-Crystal check!
        0x70, 0x40, 0xBD, 0xE8, // pop {r4, r5, r6, lr}
        0x04, 0x40, 0x2D, 0xE5, // push {r4} (Must execute the instruction we skipped!)
        0x04, 0xF0, 0x1F, 0xE5, // ldr pc, [pc, #-4]
        ..BitConverter.GetBytes(fallbackVA), // fallbackAddress
        0x81, 0x00, 0x85, 0xE0, // Match: add r0, r5, r1, lsl #1
        0xB0, 0x00, 0xD0, 0xE1, // ldrh r0, [r0]
        0x70, 0x80, 0xBD, 0xE8, // pop {r4, r5, r6, pc}
        ..BitConverter.GetBytes(itemTableVA),
        ..BitConverter.GetBytes(moveTableVA),
        ..BitConverter.GetBytes(count)
    ];
}
