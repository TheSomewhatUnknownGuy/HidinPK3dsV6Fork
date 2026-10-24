using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

public sealed class RelocationReport
{
    public uint From { get; init; }
    public uint To { get; init; }
    public int Length { get; init; }
    public int ExternalBranchesFixed { get; set; }
    public int InternalBranchesKept { get; set; }
    public int SlotsRepointed { get; set; }
    public int TargetsRepointed { get; set; }
    public List<string> Warnings { get; } = [];

    public override string ToString() =>
        $"0x{From:X6} -> 0x{To:X6} ({Length}B): {ExternalBranchesFixed} branch(es) re-encoded, " +
        $"{InternalBranchesKept} kept, {SlotsRepointed} slot(s) + {TargetsRepointed} target(s) repointed" +
        (Warnings.Count == 0 ? "" : $", {Warnings.Count} warning(s)");
}

public static class CodeRelocator
{
    public static RelocationReport MoveCode(byte[] rom, uint from, int length, uint to)
    {
        ArgumentNullException.ThrowIfNull(rom);
        var report = new RelocationReport { From = from, To = to, Length = length };

        if (length <= 0) { report.Warnings.Add("nothing to move"); return report; }
        if (from + length > rom.Length || to + length > rom.Length)
        { report.Warnings.Add("move would run past the end of the binary"); return report; }

        Array.Copy(rom, (int)from, rom, (int)to, length);

        // Re-encode branches now that the copy sits at a different PC.
        for (int i = 0; i + 4 <= length; i += 4)
        {
            uint word = BitConverter.ToUInt32(rom, (int)to + i);
            if (!ARMCodec.IsBranch(word)) continue;

            uint oldPc = from + (uint)i;
            uint newPc = to + (uint)i;
            uint target = ARMCodec.DecodeBranchTarget(word, oldPc);

            // A branch that stays within the block keeps its relative distance, so the bytes we
            // just copied are already correct.
            if (target >= from && target < from + length) { report.InternalBranchesKept++; continue; }

            // Preserve condition and the link bit; only the 24-bit displacement changes.
            int delta = (int)(target - (newPc + 8)) >> 2;
            if (delta > 0x7FFFFF || delta < -0x800000)
            {
                report.Warnings.Add($"+0x{i:X}: target 0x{target:X6} is out of ARM branch range from 0x{newPc:X6}");
                continue;
            }
            uint rebuilt = (word & 0xFF000000) | ((uint)delta & 0x00FFFFFF);
            BitConverter.GetBytes(rebuilt).CopyTo(rom, (int)to + i);
            report.ExternalBranchesFixed++;
        }

        return report;
    }

    public static (byte[] Code, RelocationReport Report) RebaseCode(byte[] body, uint originalBase, uint newBase)
    {
        ArgumentNullException.ThrowIfNull(body);
        var report = new RelocationReport { From = originalBase, To = newBase, Length = body.Length };
        var copy = (byte[])body.Clone();
        if (originalBase == newBase) return (copy, report);

        for (int i = 0; i + 4 <= copy.Length; i += 4)
        {
            uint word = BitConverter.ToUInt32(copy, i);
            if (!ARMCodec.IsBranch(word)) continue;

            uint oldPc = originalBase + (uint)i;
            uint target = ARMCodec.DecodeBranchTarget(word, oldPc);

            if (target >= originalBase && target < originalBase + body.Length)
            { report.InternalBranchesKept++; continue; }

            int delta = (int)(target - (newBase + (uint)i + 8)) >> 2;
            if (delta > 0x7FFFFF || delta < -0x800000)
            {
                report.Warnings.Add($"+0x{i:X}: 0x{target:X6} is out of branch range from 0x{newBase + (uint)i:X6}");
                continue;
            }
            BitConverter.GetBytes((word & 0xFF000000) | ((uint)delta & 0x00FFFFFF)).CopyTo(copy, i);
            report.ExternalBranchesFixed++;
        }

        // PC-relative loads reaching outside the block would need their literal copied too. Flag
        // them rather than guess: a silently wrong literal is far worse than a refusal to install.
        for (int i = 0; i + 4 <= copy.Length; i += 4)
        {
            uint word = BitConverter.ToUInt32(copy, i);
            if ((word & 0x0F7F0000) != 0x051F0000) continue; // LDR Rd, [PC, #+/-imm12]
            int imm = (int)(word & 0xFFF);
            if ((word & 0x00800000) == 0) imm = -imm;
            long literal = originalBase + i + 8 + imm;
            if (literal < originalBase || literal >= originalBase + body.Length)
                report.Warnings.Add($"+0x{i:X}: loads a literal at 0x{literal:X6}, outside the block; verify it after rebasing");
        }

        return (copy, report);
    }

    public sealed class CodeBlock
    {
        public string Name { get; set; } = "";
        public uint OriginalBase { get; set; }
        public byte[] Body { get; set; } = [];
        public uint NewBase { get; set; }
        public bool IsData { get; set; }

        public byte[] Rebased { get; internal set; }
        public bool Contains(uint address) => address >= OriginalBase && address < OriginalBase + Body.Length;
    }

    public static RelocationReport RebaseGroup(IList<CodeBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var report = new RelocationReport();

        foreach (var block in blocks)
        {
            var copy = (byte[])block.Body.Clone();
            block.Rebased = copy;
            if (block.IsData || block.NewBase == block.OriginalBase) continue;

            for (int i = 0; i + 4 <= copy.Length; i += 4)
            {
                uint word = BitConverter.ToUInt32(copy, i);
                if (!ARMCodec.IsBranch(word)) continue;

                uint oldPc = block.OriginalBase + (uint)i;
                uint target = ARMCodec.DecodeBranchTarget(word, oldPc);

                // Where does this land once everything has moved?
                uint newTarget;
                var host = blocks.FirstOrDefault(b => b.Contains(target));
                if (host == null)
                {
                    newTarget = target;                        // out to the game: unchanged
                }
                else if (host == block)
                {
                    report.InternalBranchesKept++;             // within this block: still correct
                    continue;
                }
                else
                {
                    newTarget = host.NewBase + (target - host.OriginalBase);
                    report.Warnings.Add($"{block.Name}+0x{i:X}: cross-block call to {host.Name} " +
                                        $"re-aimed 0x{target:X6} -> 0x{newTarget:X6}");
                }

                int delta = (int)(newTarget - (block.NewBase + (uint)i + 8)) >> 2;
                if (delta > 0x7FFFFF || delta < -0x800000)
                {
                    report.Warnings.Add($"{block.Name}+0x{i:X}: 0x{newTarget:X6} out of branch range");
                    continue;
                }
                BitConverter.GetBytes((word & 0xFF000000) | ((uint)delta & 0x00FFFFFF)).CopyTo(copy, i);
                report.ExternalBranchesFixed++;
            }
        }

        return report;
    }

    public static RelocationReport MoveBlock(
        DecompiledCRO cro, uint from, int length, uint to, bool fixInstructionBranches = true)
    {
        ArgumentNullException.ThrowIfNull(cro);

        RelocationReport report;
        if (fixInstructionBranches)
        {
            report = MoveCode(cro.RawData, from, length, to);
        }
        else
        {
            report = new RelocationReport { From = from, To = to, Length = length };
            if (length <= 0 || from + length > cro.RawData.Length || to + length > cro.RawData.Length)
            { report.Warnings.Add("move would run past the end of the binary"); return report; }
            Array.Copy(cro.RawData, (int)from, cro.RawData, (int)to, length);
        }

        report.SlotsRepointed = RepointSlotsIn(cro, from, length, to, report);
        report.TargetsRepointed = RepointTargetsIn(cro, from, length, to, report);
        return report;
    }

    public static int RepointSlotsIn(DecompiledCRO cro, uint from, int length, uint to, RelocationReport report = null)
    {
        uint[] segs = cro.SegmentStarts;
        int moved = 0;

        foreach (var r in cro.Relocations)
        {
            uint slot = r.AbsoluteWriteTo(segs);
            if (slot < from || slot >= from + length) continue;

            uint newSlot = to + (slot - from);
            int seg = CROUtil.GetSegmentForAddress(newSlot, cro.RawData);
            if (seg < 0 || seg >= segs.Length)
            {
                report?.Warnings.Add($"slot 0x{newSlot:X6} is outside every segment; relocation {r.Index} left alone");
                continue;
            }
            r.RawWord0 = ((newSlot - segs[seg]) << 4) | (uint)(seg & 0xF);
            moved++;
        }
        return moved;
    }

    public static int RepointTargetsIn(DecompiledCRO cro, uint from, int length, uint to, RelocationReport report = null)
    {
        uint[] segs = cro.SegmentStarts;
        int moved = 0;

        foreach (var r in cro.Relocations)
        {
            uint target = r.AbsoluteTarget(segs);
            if (target < from || target >= from + length) continue;

            uint newTarget = to + (target - from);
            int seg = CROUtil.GetSegmentForAddress(newTarget, cro.RawData);
            if (seg < 0 || seg >= segs.Length)
            {
                report?.Warnings.Add($"target 0x{newTarget:X6} is outside every segment; relocation {r.Index} left alone");
                continue;
            }
            // Segment lives in byte 1 of word 1; the addend is relative to that segment's start.
            r.RawWord1 = (r.RawWord1 & ~0x0000FF00u) | ((uint)(seg & 0xFF) << 8);
            r.Addend = newTarget - segs[seg];
            moved++;
        }
        return moved;
    }

    public static bool AddPointer(DecompiledCRO cro, uint slot, uint target, Action<string> log = null)
    {
        uint[] segs = cro.SegmentStarts;
        int slotSeg = CROUtil.GetSegmentForAddress(slot, cro.RawData);
        int tgtSeg = CROUtil.GetSegmentForAddress(target, cro.RawData);

        if (slotSeg < 0 || tgtSeg < 0 || slotSeg >= segs.Length || tgtSeg >= segs.Length)
        {
            log?.Invoke($"cannot add pointer 0x{slot:X6} -> 0x{target:X6}: outside the segment map");
            return false;
        }

        var entry = CRORelocationEntry.Create(slotSeg, slot - segs[slotSeg], tgtSeg, target - segs[tgtSeg]);
        entry.Index = cro.Relocations.Count;
        cro.Relocations.Add(entry);
        cro.Header.PatchTableCount = (uint)cro.Relocations.Count;

        // The slot is blank on disk; the loader writes it. Zeroing makes that explicit and stops a
        // stale value from being mistaken for a resolved pointer when the file is re-read.
        BitConverter.GetBytes(0u).CopyTo(cro.RawData, (int)slot);
        return true;
    }

    public static bool SetOrAddPointer(DecompiledCRO cro, uint slot, uint target, Action<string> log = null)
    {
        uint[] segs = cro.SegmentStarts;
        int slotSeg = CROUtil.GetSegmentForAddress(slot, cro.RawData);
        int tgtSeg = CROUtil.GetSegmentForAddress(target, cro.RawData);

        if (slotSeg < 0 || tgtSeg < 0 || slotSeg >= segs.Length || tgtSeg >= segs.Length)
        {
            log?.Invoke($"cannot set pointer 0x{slot:X6} -> 0x{target:X6}: outside the segment map");
            return false;
        }

        var existing = cro.Relocations.FirstOrDefault(r => r.AbsoluteWriteTo(segs) == slot);
        if (existing != null)
        {
            existing.RawWord1 = (existing.RawWord1 & ~0xFF00u) | ((uint)(tgtSeg & 0xFF) << 8);
            existing.RawWord2 = target - segs[tgtSeg];
            BitConverter.GetBytes(0u).CopyTo(cro.RawData, (int)slot);
            return true;
        }
        return AddPointer(cro, slot, target, log);
    }

    public static bool IsRangeFree(DecompiledCRO cro, uint start, int length)
    {
        if (length <= 0) return true;
        if (start + length > cro.RawData.Length) return false;

        for (uint i = start; i < start + length; i++)
            if (cro.RawData[i] != 0x00 && cro.RawData[i] != 0xCC) return false;

        uint[] segs = cro.SegmentStarts;
        return !cro.Relocations.Any(r =>
        {
            uint slot = r.AbsoluteWriteTo(segs);
            uint tgt = r.AbsoluteTarget(segs);
            return (slot >= start && slot < start + length) || (tgt >= start && tgt < start + length);
        });
    }

    public static (uint Offset, int Length) FindReserve(DecompiledCRO cro, int minimum = 0x40)
    {
        byte[] rom = cro.RawData;
        uint[] segs = cro.SegmentStarts;

        // A relocation's write slot is BLANK ON DISK BY DESIGN - AddPointer explicitly zeroes it
        // ("the slot is blank on disk; the loader writes it"), and the loader fills it only once the
        // CRO is actually loaded. Requiring the byte to be non-blank before protecting it therefore
        // excluded exactly the addresses this set exists to protect: every legitimately-registered
        // but not-yet-resolved pointer slot in the whole file read as 0x00 and so was never added
        // here, leaving it free for a later FindReserve call to hand out and overwrite. That is
        // exactly what happened to Avoid Crash Missing Animation's install: its block landed on top
        // of an earlier recipe's still-live timing-table pointer slot, whose relocation entry then
        // had the loader overwrite the guard's first instruction with an unrelated resolved pointer
        // at CRO-load time - Citra's dynarmic then tried to execute that pointer value as an ARM
        // instruction and asserted. Every write slot and target a relocation names is claimed
        // regardless of its current byte value; only bytes with NO relocation touching them at all
        // are actually free.
        var referenced = new SortedSet<uint>();
        foreach (var r in cro.Relocations)
        {
            uint wTo = r.AbsoluteWriteTo(segs);
            uint tgt = r.AbsoluteTarget(segs);
            if (wTo < rom.Length) referenced.Add(wTo);
            if (tgt < rom.Length) referenced.Add(tgt);
        }

        (uint Offset, int Length) best = (0, 0);

        // Segment 0 (.text) and segment 1 (.rodata) are scanned as two SEPARATE windows, never as
        // one merged [CodeStart, CodeEnd) range. There is a real, structural gap between where
        // segment 0's declared bounds end and segment 1's declared bounds start (0x68C blank-but-
        // unowned bytes on stock USUM Battle.cro, alignment padding between .text and .rodata that
        // belongs to neither segment's declared size) - a scan that treats the whole span as one
        // contiguous "code segment" can return a run straddling that gap, and any address inside it
        // is legitimately outside every segment (GetSegmentForAddress correctly returns -1 there).
        // That produced exactly "could not register the stub's timing-table pointer" for a real
        // chunk of patch-packages: the reserve handed out was, in part, unowned padding no relocation
        // can legally target. Scanning each real segment on its own means a returned reserve is
        // always entirely inside one segment's declared bounds.
        ScanWindow(cro.Segments != null && cro.Segments.Length > 0 ? cro.Segments[0] : null);
        ScanWindow(cro.Segments != null && cro.Segments.Length > 1 ? cro.Segments[1] : null);

        return best.Length >= minimum ? best : (0, 0);

        void ScanWindow(CROSegmentEntry seg)
        {
            if (seg == null || seg.Start == 0 || seg.Size == 0) return;
            uint start = seg.Start;
            uint end = Math.Min(seg.Start + seg.Size, (uint)rom.Length);
            if (end <= start) return;

            uint runStart = start;
            bool inRun = false;
            for (uint p = start; p <= end; p++)
            {
                bool blank = p < end && (rom[p] == 0x00 || rom[p] == 0xCC);
                if (blank)
                {
                    if (!inRun) { runStart = p; inRun = true; }
                    continue;
                }
                if (!inRun) continue;

                Consider(runStart, p);
                inRun = false;
            }
        }

        void Consider(uint from, uint to)
        {
            uint cursor = from;
            foreach (uint hit in referenced.GetViewBetween(from, to == 0 ? 0 : to - 1))
            {
                Take(cursor, hit);
                cursor = hit + 4;   // skip the referenced word itself
            }
            Take(cursor, to);
        }

        void Take(uint from, uint to)
        {
            uint aligned = (from + 3u) & ~3u;
            if (to <= aligned) return;
            int length = (int)(to - aligned);
            if (length > best.Length) best = (aligned, length);
        }
    }

    public static bool EnsureRelocationHeadroom(DecompiledCRO cro, int extraRelocations, Action<string> log = null)
    {
        log ??= _ => { };
        byte[] rom = cro.RawData;
        if (rom == null || extraRelocations <= 0) return false;

        uint U(int o) => BitConverter.ToUInt32(rom, o);
        uint segTab = U(0xC8), segNum = U(0xCC);
        if (segNum < 3) { log("no data segment to move"); return false; }

        int seg2 = (int)segTab + 24;                 // segment 2 = .data
        uint dataStart = U(seg2), dataSize = U(seg2 + 4);
        uint hdrData = U(0xB8);
        uint ipt = U(0x128), num = U(0x12C);
        uint end = ipt + num * 12;

        if (dataStart != hdrData)
            log($"note: segment table says .data 0x{dataStart:X6}, header says 0x{hdrData:X6} - they disagree");

        uint need = ipt + (num + (uint)extraRelocations) * 12;
        if (need <= dataStart)
        {
            log($"relocation headroom: {(dataStart - end) / 12} spare entr(ies); no move needed");
            return false;
        }

        // Slide far enough, rounded up, resizing the binary if needed.
        uint shift = ((need - dataStart) + 0x1F) & ~0x1Fu;
        uint newStart = dataStart + shift;
        if (newStart + dataSize > rom.Length)
        {
            int newLen = (int)((newStart + dataSize + 0xFFFu) & ~0xFFFu);
            Array.Resize(ref cro.RawData, newLen);
            rom = cro.RawData;
            cro.Header.FileSize = (uint)newLen;
            log($"expanded CRO buffer to 0x{newLen:X6} to accommodate relocation table headroom");
        }

        var data = new byte[dataSize];
        Array.Copy(rom, (int)dataStart, data, 0, (int)dataSize);
        Array.Clear(rom, (int)dataStart, (int)Math.Min(dataSize + shift, (uint)rom.Length - dataStart));
        data.CopyTo(rom, (int)newStart);

        BitConverter.GetBytes(newStart).CopyTo(rom, seg2);   // segment table bytes
        BitConverter.GetBytes(newStart).CopyTo(rom, 0xB8);   // header bytes - both must agree
        cro.Header.DataStart = newStart;

        if (cro.Segments.Length > 2 && cro.Segments[2] != null)
            cro.Segments[2].Start = newStart;

        log($"moved .data 0x{dataStart:X6} -> 0x{newStart:X6} (+0x{shift:X}); " +
            $"room for {(newStart - end) / 12} more relocation(s)");
        return true;
    }

    public static (uint Offset, int Length) FindTailReserve(DecompiledCRO cro, int minimum = 0x40)
    {
        // Segment 1's own tail first, then segment 0's - never scanning across the real gap
        // between them (see FindReserve's comment: that gap is blank-but-unowned padding, not
        // part of either segment's declared size, and a run can legally include none of it).
        var fromSeg1 = TailOf(cro.Segments != null && cro.Segments.Length > 1 ? cro.Segments[1] : null);
        if (fromSeg1.Length >= minimum) return fromSeg1;

        var fromSeg0 = TailOf(cro.Segments != null && cro.Segments.Length > 0 ? cro.Segments[0] : null);
        return fromSeg0.Length >= minimum ? fromSeg0 : (0, 0);

        (uint Offset, int Length) TailOf(CROSegmentEntry seg)
        {
            if (seg == null || seg.Start == 0 || seg.Size == 0) return (0, 0);
            uint start = seg.Start;
            uint end = Math.Min(seg.Start + seg.Size, (uint)cro.RawData.Length);
            if (end <= start) return (0, 0);

            uint p = end;
            while (p > start && (cro.RawData[p - 1] == 0x00 || cro.RawData[p - 1] == 0xCC)) p--;

            uint reserve = (p + 3u) & ~3u;
            return (reserve, (int)(end - reserve));
        }
    }
}
