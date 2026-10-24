using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public enum HandlerShape
{
    Unknown = 0,
    CallSetup,
    DirectRoutine,
}

public readonly record struct TimingSlot(int Index, byte Timing, uint EntryOffset, uint FunctionOffset);

public sealed class ResolvedChain
{
    public uint HandlerOffset { get; init; }
    public HandlerShape Shape { get; init; }
    public int Count { get; init; }
    public uint TimingTableOffset { get; init; }
    public List<TimingSlot> Slots { get; } = [];
    public string Note { get; init; }

    public bool HasTimingTable => Shape == HandlerShape.CallSetup && TimingTableOffset != 0;
}

public static class MechanicChainResolver
{
    // ARM encodings used to recognise the stub prologue.
    private const uint MaskCond = 0x0FFFFFFF;
    private const uint StrR1AtR0 = 0x05801000;   // STR R1, [R0]
    private const uint BxLr = 0x012FFF1E;        // BX LR
    private const uint MovR1ImmMask = 0x0FFFF000;
    private const uint MovR1Imm = 0x03A01000;    // MOV R1, #imm

    private const int StubWindowWords = 8;

    public static ResolvedChain Resolve(byte[] rom, uint handler, Func<uint, uint> resolveSlot)
    {
        if (rom == null || resolveSlot == null || handler == 0 || handler + 8 > rom.Length)
            return new ResolvedChain { HandlerOffset = handler, Shape = HandlerShape.Unknown, Note = "out of range" };

        // A stub is short and returns almost immediately. Find BX LR within the window; anything
        // that doesn't return that fast is a routine being called directly.
        int bxAt = -1;
        for (int w = 0; w < StubWindowWords && handler + (w + 1) * 4 <= rom.Length; w++)
        {
            uint word = BitConverter.ToUInt32(rom, (int)handler + w * 4);
            if ((word & MaskCond) == BxLr) { bxAt = w; break; }
        }

        if (bxAt < 0)
            return new ResolvedChain { HandlerOffset = handler, Shape = HandlerShape.DirectRoutine, Note = "no early return; treated as a leaf routine" };

        // Stubs publish the count by storing into [R0].
        bool storesCount = false;
        for (int w = 0; w < bxAt; w++)
        {
            uint word = BitConverter.ToUInt32(rom, (int)handler + w * 4);
            if ((word & MaskCond) == StrR1AtR0) { storesCount = true; break; }
        }
        if (!storesCount)
            return new ResolvedChain { HandlerOffset = handler, Shape = HandlerShape.DirectRoutine, Note = "returns early but publishes no count" };

        // The literal pool sits immediately after the return. Exactly one of those words is
        // relocated - that is the timing table pointer. Any other is the count.
        uint tableOffset = 0, countLiteral = 0;
        bool haveCountLiteral = false;
        for (int w = bxAt + 1; w < bxAt + 4 && handler + (w + 1) * 4 <= rom.Length; w++)
        {
            uint at = handler + (uint)(w * 4);
            uint target = resolveSlot(at);
            if (target != 0 && tableOffset == 0) { tableOffset = target; continue; }
            if (target == 0 && !haveCountLiteral)
            {
                countLiteral = BitConverter.ToUInt32(rom, (int)at);
                haveCountLiteral = true;
            }
        }

        // Count comes from a MOV immediate when present, otherwise from the literal.
        int count = 0;
        for (int w = 0; w < bxAt; w++)
        {
            uint word = BitConverter.ToUInt32(rom, (int)handler + w * 4);
            if ((word & MovR1ImmMask) != MovR1Imm) continue;
            count = (int)pk3DS.Core.CTR.ARMCodec.DecodeImm8r4((word >> 8) & 0xF, word & 0xFF);
            break;
        }
        if (count == 0 && haveCountLiteral && countLiteral > 0)
        {
            if (countLiteral <= 64) count = (int)countLiteral;
            else
            {
                uint low = countLiteral & 0xFFFF;
                if (low > 0 && low <= 64) count = (int)low;
            }
        }

        if (tableOffset == 0)
            return new ResolvedChain { HandlerOffset = handler, Shape = HandlerShape.DirectRoutine, Note = "stub shape but no relocated table pointer" };

        var chain = new ResolvedChain
        {
            HandlerOffset = handler,
            Shape = HandlerShape.CallSetup,
            Count = count,
            TimingTableOffset = tableOffset,
        };

        // Read the timing table. Trust the published count, but stop early if a slot isn't
        // relocated - that means the table ended sooner than the count claimed.
        for (int k = 0; k < count; k++)
        {
            uint at = tableOffset + (uint)(k * 8);
            if (at + 8 > rom.Length) break;
            uint fn = resolveSlot(at + 4);
            if (fn == 0) break;
            chain.Slots.Add(new TimingSlot(k, rom[at], at, fn));
        }

        return chain;
    }

    public static Dictionary<uint, ResolvedChain> ResolveTable(
        byte[] rom, LocatedMechanicTable table, Func<uint, uint> resolveSlot)
    {
        var map = new Dictionary<uint, ResolvedChain>();
        if (rom == null || table == null) return map;

        foreach (var (_, _, _, handler) in MechanicTableLocator.ReadEntries(rom, table, resolveSlot))
        {
            if (handler == 0 || map.ContainsKey(handler)) continue;
            map[handler] = Resolve(rom, handler, resolveSlot);
        }
        return map;
    }
}
