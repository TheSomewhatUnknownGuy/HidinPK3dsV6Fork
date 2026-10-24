using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

/// <summary>Address-anchored edits to a CRO that has no master tables of its own.</summary>
public sealed class CroSitePort
{
    /// <summary>Routines placed in the CRO's own free space; hooks branch into these.</summary>
    public List<PortedBlock> Blocks { get; set; } = [];

    /// <summary>Fixed-address edits, each carrying the bytes that were there before.</summary>
    public List<PortedSitePatch> SitePatches { get; set; } = [];
}

/// <summary>
/// Applies a <see cref="CroSitePort"/> to a CRO such as Bag.cro.
/// <para>
/// Simpler than the Battle.cro path because there is nothing to re-derive: no tables to locate, no
/// routines to resolve through a donor. That makes the recorded originals the whole safety net, so
/// a build that differs at any anchor is refused outright rather than half-patched.
/// </para>
/// </summary>
public static class PortCroSiteInstaller
{
    public static PortResult Apply(byte[] croBytes, CroSitePort port, string label = "CRO", Action<string> log = null)
    {
        var result = new PortResult();
        log ??= _ => { };
        if (croBytes == null || port == null) { result.Fail($"{label}: no bytes or port supplied"); return result; }

        DecompiledCRO cro;
        try { cro = CRODecompiler.DecompileStructure((byte[])croBytes.Clone()); }
        catch (Exception ex) { result.Fail($"{label}: cannot read the CRO ({ex.Message})"); return result; }

        // ---- verify every anchor before writing a byte ----
        var todo = new List<PortedSitePatch>();
        var chainedTargets = new Dictionary<PortedSitePatch, uint>();
        var alreadyPlaced = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        int already = 0;
        foreach (var p in port.SitePatches)
        {
            if (p.OffsetValue + p.OriginalBytes.Length > cro.RawData.Length)
            { result.Fail($"{label}: 0x{p.OffsetValue:X6} is past the end"); continue; }

            var actual = new byte[p.OriginalBytes.Length];
            Array.Copy(cro.RawData, (int)p.OffsetValue, actual, 0, actual.Length);

            if (actual.SequenceEqual(p.OriginalBytes)) { todo.Add(p); continue; }

            // This exact package's own result already sitting there - a genuine re-run, nothing to do.
            if (actual.SequenceEqual(p.PatchedBytes)) { already++; continue; }

            // A hook site already pointing at a byte-identical copy of THIS package's own block is a
            // shared region several packages deliberately have in common - Bag.cro's item-dispatch
            // region is the motivating case: every item package ships the exact same block and only
            // contributes its own ids to that block's id list. Chaining is wrong for that shape (its
            // "not one of mine" exits branch to the game's own continuation addresses, nowhere near
            // the hook site, so a chained fallback can never be re-aimed onto them) and placing a
            // second copy is worse than useless: only the most recently installed copy stays
            // reachable and every earlier package's ids become dead code. Treat it as already
            // applied instead, so no duplicate block is placed and the existing region is reused -
            // the caller's data-table pass then merges this package's ids into the one shared list.
            // Only unchained hooks (fallbackBranchOffset < 0) are candidates for shared deduplication;
            // chained hooks (fallbackBranchOffset >= 0) must chain in front of existing handlers.
            if (p.IsHook && p.FallbackBranchOffset < 0 && actual.Length == 4 && ARMCodec.IsBranch(BitConverter.ToUInt32(actual, 0)) &&
                (BitConverter.ToUInt32(actual, 0) >> 28) == 0xE && p.PatchedBytes?.Length == 4)
            {
                uint liveTarget = ARMCodec.DecodeBranchTarget(BitConverter.ToUInt32(actual, 0), p.OffsetValue);
                uint authoredTarget = ARMCodec.DecodeBranchTarget(BitConverter.ToUInt32(p.PatchedBytes, 0), p.OffsetValue);
                var ownerBlock = port.Blocks.FirstOrDefault(b => authoredTarget >= b.SourceBaseValue &&
                                                                authoredTarget < b.SourceBaseValue + b.CodeBytes.Length);
                if (ownerBlock != null)
                {
                    long liveBase = (long)liveTarget - (authoredTarget - ownerBlock.SourceBaseValue);
                    // Compare only the block's leading code, not its whole body: the trailing data
                    // (the id list) legitimately differs once earlier packages have contributed ids.
                    int compare = Math.Min(ownerBlock.CodeBytes.Length, 0x38);
                    bool sameBlock = liveBase >= 0 && liveBase + compare <= cro.RawData.Length;
                    // Compare by DECODED branch target rather than raw bytes: relocation rewrites the
                    // offset field of every branch that leaves the block, so an already-placed copy is
                    // never byte-identical to the authored bytes even though it is the same routine.
                    for (int i = 0; sameBlock && i + 4 <= compare; i += 4)
                    {
                        uint live = BitConverter.ToUInt32(cro.RawData, (int)liveBase + i);
                        uint auth = BitConverter.ToUInt32(ownerBlock.CodeBytes, i);
                        if (ARMCodec.IsBranch(live) && ARMCodec.IsBranch(auth))
                        {
                            uint liveT = ARMCodec.DecodeBranchTarget(live, (uint)liveBase + (uint)i);
                            uint authT = ARMCodec.DecodeBranchTarget(auth, ownerBlock.SourceBaseValue + (uint)i);
                            bool authInternal = authT >= ownerBlock.SourceBaseValue &&
                                                authT < ownerBlock.SourceBaseValue + ownerBlock.CodeBytes.Length;
                            // A branch that stays inside the block keeps its offset, so compare it
                            // relative to each copy's own base; one that leaves keeps its absolute target.
                            bool targetsMatch = authInternal
                                ? liveT - (uint)liveBase == authT - ownerBlock.SourceBaseValue
                                : liveT == authT;
                            if ((live >> 24) != (auth >> 24) || !targetsMatch) sameBlock = false;
                        }
                        else if (live != auth) sameBlock = false;
                    }
                    if (sameBlock)
                    {
                        result.Say($"  {label}: 0x{p.OffsetValue:X6} already points at this same block at " +
                                   $"0x{liveBase:X6} - reusing that shared region rather than placing a duplicate");
                        alreadyPlaced[ownerBlock.Name] = (uint)liveBase;
                        already++;
                        continue;
                    }
                }
            }

            // A hook site already branching somewhere else was installed by an EARLIER, DIFFERENT
            // package, not a mismatched build. The previous behaviour treated this as "already done"
            // and silently skipped writing this package's own hook at all - meaning only the first
            // package ever installed at a shared site actually ran, and every later one silently
            // no-op'd while still reporting success. Chain instead: record the existing branch's
            // target and still queue this site so THIS package's own hook gets written too, pointing
            // at its own newly-placed block; that block's own fallback branch is re-aimed below to
            // the previous handler instead of the original site, so both packages' checks run in
            // sequence rather than one going unreachable.
            // Only an unconditional branch (cond=AL) can be a hook this same installer wrote for an
            // earlier package - a coincidental, genuinely-unrelated CONDITIONAL branch sitting at a
            // wrong/unverified anchor must fall through to the plain mismatch failure below instead
            // of being mistaken for a previous package's hook and chained onto.
            if (p.IsHook && actual.Length == 4 && ARMCodec.IsBranch(BitConverter.ToUInt32(actual, 0)) &&
                (BitConverter.ToUInt32(actual, 0) >> 28) == 0xE)
            {
                uint prevTarget = ARMCodec.DecodeBranchTarget(BitConverter.ToUInt32(actual, 0), p.OffsetValue);
                result.Say($"  {label}: 0x{p.OffsetValue:X6} already hooked (-> 0x{prevTarget:X6}) - chaining in front of it");
                chainedTargets[p] = prevTarget;
                todo.Add(p);
                continue;
            }

            result.Fail($"{label}: 0x{p.OffsetValue:X6} holds {Convert.ToHexString(actual)}, " +
                        $"expected {Convert.ToHexString(p.OriginalBytes)}");
        }
        result.Say($"{label}: {todo.Count} site(s) to apply" + (already > 0 ? $", {already} already applied" : ""));
        if (!result.Success) { result.Say($"{label}: refusing to write — the build differs at an anchor"); return result; }
        if (todo.Count == 0)
        {
            foreach (var b in port.Blocks)
            {
                if (alreadyPlaced.TryGetValue(b.Name, out uint existingAt))
                {
                    result.Placed[b.Name] = existingAt;
                    continue;
                }
                uint at = b.SourceBaseValue;
                var matchingHook = port.SitePatches.FirstOrDefault(sp => sp.IsHook && sp.PatchedBytes?.Length == 4 &&
                    (sp.HookTarget == b.Name || (sp.HookTarget != null && sp.HookTarget.StartsWith(b.Name))));
                if (matchingHook != null && matchingHook.OffsetValue + 4 <= cro.RawData.Length)
                {
                    uint hookInst = BitConverter.ToUInt32(cro.RawData, (int)matchingHook.OffsetValue);
                    if (ARMCodec.IsBranch(hookInst))
                    {
                        uint target = ARMCodec.DecodeBranchTarget(hookInst, matchingHook.OffsetValue);
                        uint authored = ARMCodec.DecodeBranchTarget(BitConverter.ToUInt32(matchingHook.PatchedBytes, 0), matchingHook.OffsetValue);
                        if (authored >= b.SourceBaseValue && authored < b.SourceBaseValue + b.CodeBytes.Length)
                        {
                            at = target - (authored - b.SourceBaseValue);
                        }
                    }
                }
                result.Placed[b.Name] = at;
            }
            result.Output = cro.RawData;
            return result;
        }

        // ---- place the blocks in this CRO's own reserve ----
        var reserve = CodeRelocator.FindReserve(cro);
        if (port.Blocks.Count > 0 && reserve.Offset == 0)
        { result.Fail($"{label}: no free space for {port.Blocks.Count} block(s)"); return result; }
        uint bump = reserve.Offset;
        if (port.Blocks.Count > 0)
            result.Say($"{label}: reserve 0x{reserve.Offset:X6} +{reserve.Length}");

        foreach (var b in port.Blocks)
        {
            if (alreadyPlaced.TryGetValue(b.Name, out uint existingAt))
            {
                result.Placed[b.Name] = existingAt;
                result.Say($"  block '{b.Name}' already in place at 0x{existingAt:X6} - reusing");
                continue;
            }

            byte[] body = b.CodeBytes;
            uint at = (bump + 3u) & ~3u;
            if (at + body.Length > reserve.Offset + reserve.Length)
            { result.Fail($"{label}: block '{b.Name}' does not fit"); return result; }

            var (rebased, rep) = CodeRelocator.RebaseCode(body, b.SourceBaseValue, at);
            rebased.CopyTo(cro.RawData, (int)at);
            bump = at + (uint)rebased.Length;
            result.Placed[b.Name] = at;
            result.Say($"  block '{b.Name}' 0x{b.SourceBaseValue:X6} -> 0x{at:X6} " +
                       $"({rep.ExternalBranchesFixed} external, {rep.InternalBranchesKept} internal)");
            foreach (var w in rep.Warnings) result.Say($"    ! {w}");
        }

        // ---- write the sites, re-encoding hooks against where the blocks actually landed ----
        foreach (var p in todo)
        {
            if (p.IsHook && !string.IsNullOrEmpty(p.HookTarget))
            {
                if (!result.Placed.TryGetValue(p.HookTarget, out uint dest))
                {
                    uint authored = p.PatchedBytes.Length == 4
                        ? ARMCodec.DecodeBranchTarget(BitConverter.ToUInt32(p.PatchedBytes, 0), p.OffsetValue)
                        : 0;
                    var owner = port.Blocks.FirstOrDefault(b => authored >= b.SourceBaseValue &&
                                                                authored < b.SourceBaseValue + b.CodeBytes.Length);
                    if (owner == null || !result.Placed.TryGetValue(owner.Name, out uint placedAt))
                    {
                        result.Fail($"{label}: hook at 0x{p.OffsetValue:X6} targets '{p.HookTarget}', which is " +
                                    "neither a block in this package nor an address inside one");
                        continue;
                    }
                    dest = placedAt + (authored - owner.SourceBaseValue);
                }

                // If this exact site was previously hooked by another package (detected above), find
                // the fallback branch inside the block THIS package just placed - the one meant to
                // jump back to the original hook site once this package decides the item isn't one of
                // its own - and re-aim it to the previous handler instead, so the two packages' checks
                // chain rather than the earlier one becoming unreachable dead code.
                if (chainedTargets.TryGetValue(p, out uint prevHandler) &&
                    result.Placed.TryGetValue(p.HookTarget, out uint blockAt))
                {
                    var chainBlock = port.Blocks.FirstOrDefault(x => x.Name == p.HookTarget);
                    if (chainBlock != null)
                    {
                        if (p.FallbackBranchOffset >= 0 && p.FallbackBranchOffset + 4 <= chainBlock.CodeBytes.Length)
                        {
                            uint at = blockAt + (uint)p.FallbackBranchOffset;
                            var chainedBr = ARMCodec.EncodeBranch(at, prevHandler);
                            chainedBr.CopyTo(cro.RawData, (int)at);
                            result.Say($"  chained fallback in '{chainBlock.Name}'+0x{p.FallbackBranchOffset:X} -> 0x{prevHandler:X6}");
                        }
                        else
                        {
                            for (int i = 0; i + 4 <= chainBlock.CodeBytes.Length; i += 4)
                            {
                                uint inst = BitConverter.ToUInt32(cro.RawData, (int)blockAt + i);
                                if (!ARMCodec.IsBranch(inst)) continue;
                                uint branchTarget = ARMCodec.DecodeBranchTarget(inst, blockAt + (uint)i);
                                if (Math.Abs((long)branchTarget - (long)p.OffsetValue) > 8) continue;

                                var chainedBr = ARMCodec.EncodeBranch(blockAt + (uint)i, prevHandler);
                                chainedBr.CopyTo(cro.RawData, (int)blockAt + i);
                                result.Say($"  chained fallback in '{chainBlock.Name}'+0x{i:X} -> 0x{prevHandler:X6}");

                                // NOP the re-PUSH preceding the fallback branch to prevent stack corruption.
                                if (i >= 4)
                                {
                                    uint preceding = BitConverter.ToUInt32(cro.RawData, (int)blockAt + i - 4);
                                    if ((preceding & 0xFFFF0000) == 0xE92D0000) // PUSH / STMFD SP!, {...}
                                    {
                                        uint nop = 0xE1A00000; // MOV R0, R0
                                        BitConverter.GetBytes(nop).CopyTo(cro.RawData, (int)blockAt + i - 4);
                                        result.Say($"  NOP'd re-PUSH at '{chainBlock.Name}'+0x{i - 4:X} to prevent stack corruption");
                                    }
                                }
                                break;
                            }
                        }
                    }
                }

                uint word = p.PatchedBytes.Length == 4 ? BitConverter.ToUInt32(p.PatchedBytes, 0) : 0xEA000000u;
                ARMCodec.RetargetBranch(word, p.OffsetValue, dest).CopyTo(cro.RawData, (int)p.OffsetValue);
                result.Say($"  hook 0x{p.OffsetValue:X6} -> 0x{dest:X6} ({p.HookTarget}" +
                           $"{(ARMCodec.IsBranchLink(word) ? ", BL" : "")})");
            }
            else
            {
                p.PatchedBytes.CopyTo(cro.RawData, (int)p.OffsetValue);
                result.Say($"  patched 0x{p.OffsetValue:X6} ({p.PatchedBytes.Length} bytes)");
            }
        }

        byte[] output;
        try { output = CROCompiler.Compile(cro); }
        catch (Exception ex) { result.Fail($"{label}: could not rebuild ({ex.Message})"); return result; }

        var verify = CroVerifier.Verify(CRODecompiler.DecompileStructure((byte[])output.Clone()));
        result.Say($"{label}: verify {verify.RelocationsChecked} relocations, ok={verify.Ok}");
        if (!verify.Ok)
        {
            foreach (var f in verify.Findings.Where(f => f.Severity == PlanSeverity.Error).Take(5))
                result.Fail($"{label}: {f}");
            return result;
        }

        result.Output = output;
        return result;
    }
}
