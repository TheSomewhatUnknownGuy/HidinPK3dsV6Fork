using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

public sealed class CodeBinAnchoredPatch
{
    public string Name { get; set; } = "";
    public string Symbol { get; set; } = "";
    public int Offset { get; set; }
    public string Original { get; set; } = "";
    public string Patched { get; set; }
    public string BranchTo { get; set; }
    public int BranchOffset { get; set; }
    public bool Link { get; set; }

    public List<CodeBinRetarget> Retargets { get; set; } = [];

    public byte[] OriginalBytes => Convert.FromHexString(Original);
    public byte[] PatchedBytes => string.IsNullOrEmpty(Patched) ? null : Convert.FromHexString(Patched);
}

public sealed class CodeBinRetarget
{
    public string At { get; set; } = "0x0";
    public string From { get; set; } = "0x0";

    public string Symbol { get; set; }
    public string Verify { get; set; }
    public int SearchWindow { get; set; } = 0x40;

    public string Addr { get; set; }
    public int Offset { get; set; }

    [JsonIgnore]
    public uint AddrValue => string.IsNullOrWhiteSpace(Addr) ? 0u
        : Convert.ToUInt32(Addr.Trim(), Addr.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
    public bool Link { get; set; } = true;

    [JsonIgnore]
    public uint AtValue => Convert.ToUInt32(At.Trim(), At.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
    [JsonIgnore]
    public uint FromValue => Convert.ToUInt32(From.Trim(), From.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
}

public sealed class CodeBinBlock
{
    public string Name { get; set; } = "";
    public List<CodeBinRetarget> Retargets { get; set; } = [];
    public string Code { get; set; } = "";
    public string SourceBase { get; set; } = "0x0";
    public List<string> CallsSymbols { get; set; } = [];

    public byte[] CodeBytes => Convert.FromHexString(Code);
    public uint SourceBaseValue => Convert.ToUInt32(SourceBase, 16);
}

public sealed class CodeBinPort
{
    public uint LoadBase { get; set; } = 0x00100000;
    public List<CodeBinBlock> Blocks { get; set; } = [];
    public List<CodeBinAnchoredPatch> Patches { get; set; } = [];
}

public static class PortCodeBinInstaller
{
    public static PortResult Apply(byte[] codeBin, CodeBinPort port, string romFsPath, Action<string> log = null,
                                   IEnumerable<(uint Start, int Size)> keepOut = null,
                                   int textEnd = ResearchEngine.CodeTextRegionEnd)
    {
        var result = new PortResult();
        log ??= _ => { };
        if (codeBin == null || port == null) { result.Fail("no code.bin or port supplied"); return result; }

        // --- symbols ---
        var needed = port.Patches.Select(p => p.Symbol)
            .Concat(port.Blocks.SelectMany(b => b.CallsSymbols))
            .Concat(port.Blocks.SelectMany(b => (b.Retargets ?? []).Select(r => r.Symbol)))
            .Concat(port.Patches.SelectMany(p => (p.Retargets ?? []).Select(r => r.Symbol)))
            .Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();

        var syms = PortDataInstaller.ResolveStaticSymbols(romFsPath, needed);
        result.Say($"symbols: {syms.Count}/{needed.Count} resolved from static.crs");
        foreach (string s in needed.Where(s => !syms.ContainsKey(s)))
            result.Fail($"symbol not exported by this build: {s}");
        if (!result.Success) return result;

        uint File2Load(uint file) => file + port.LoadBase;
        uint Load2File(uint load) => load - port.LoadBase;

        var work = (byte[])codeBin.Clone();

        // Ultra Sun and Ultra Moon ship the SAME static.crs, so a reported symbol address is only
        // reliable for Ultra Sun; in the 0x4A_____ region Ultra Moon sits 8 bytes later. Re-find the
        // real entry by its prologue word rather than trusting the table.
        uint ResolveDriftBuf(byte[] buf, uint reported, uint verify, int window, out int drift)
        {
            drift = 0;
            for (int d = 0; d <= window; d += 4)
                foreach (int sign in d == 0 ? new[] { 1 } : new[] { 1, -1 })
                {
                    long cand = reported + sign * d;
                    long f = cand - port.LoadBase;
                    if (f < 0 || f + 4 > buf.Length) continue;
                    if (BitConverter.ToUInt32(buf, (int)f) == verify) { drift = sign * d; return (uint)cand; }
                }
            return uint.MaxValue;
        }
        uint ResolveDrift(uint reported, uint verify, int window, out int drift) =>
            ResolveDriftBuf(work, reported, verify, window, out drift);

        // --- verify every anchor before touching anything ---
        var todo = new List<CodeBinAnchoredPatch>();
        var chainedTargets = new Dictionary<CodeBinAnchoredPatch, uint>();
        var patchLocations = new Dictionary<CodeBinAnchoredPatch, uint>();
        int literalPatches = port.Patches.Count(p => string.IsNullOrEmpty(p.BranchTo)), alreadyInstalled = 0;

        foreach (var p in port.Patches)
        {
            uint symAddr = !string.IsNullOrEmpty(p.Symbol) && syms.TryGetValue(p.Symbol, out uint sa) ? sa : 0;
            uint at = !string.IsNullOrEmpty(p.Symbol) ? Load2File(symAddr + (uint)p.Offset) : (uint)p.Offset;
            var want = p.OriginalBytes;
            if (at + want.Length > codeBin.Length) { result.Fail($"{p.Name}: past the end of code.bin"); continue; }

            var actual = new byte[want.Length];
            Array.Copy(codeBin, (int)at, actual, 0, want.Length);

            // Re-running a package: the site already holds this patch. Skip it rather than letting the
            // drift search below find the original word somewhere nearby and patch a second, wrong spot.
            if (string.IsNullOrEmpty(p.BranchTo) && !actual.SequenceEqual(want))
            {
                long installed = FindInstalled(codeBin, at, p, search: !string.IsNullOrEmpty(p.Symbol));
                if (installed >= 0)
                {
                    result.Say($"  {p.Name}: already installed at 0x{installed:X}, left as is");
                    alreadyInstalled++;
                    continue;
                }
            }

            if (!actual.SequenceEqual(want) && !string.IsNullOrEmpty(p.Symbol) && want.Length >= 4)
            {
                uint wantWord = BitConverter.ToUInt32(want, 0);
                uint found = ResolveDriftBuf(codeBin, symAddr + (uint)p.Offset, wantWord, 0x40, out int drift);
                if (found != uint.MaxValue && drift != 0)
                {
                    result.Say($"  {p.Name}: {p.Symbol} is really at 0x{found:X} ({drift:+#;-#} from static.crs)");
                    at = Load2File(found);
                    actual = new byte[want.Length];
                    Array.Copy(codeBin, (int)at, actual, 0, want.Length);
                }
            }

            if (actual.SequenceEqual(want))
            {
                patchLocations[p] = at;
                todo.Add(p);
                continue;
            }

            // Only an unconditional branch (cond=AL) can be a hook this same installer wrote for an
            // earlier package - EncodeBranch/EncodeBranchLink always emit cond=AL. Requiring that
            // here (not just "is some branch") keeps a coincidental, genuinely-unrelated CONDITIONAL
            // branch that happens to already sit at a wrong/unverified anchor from being mistaken for
            // a previous package's hook and chained onto - it now falls through to the ordinary
            // "build differs at this anchor" failure below instead, exactly the safe outcome an
            // unverified offset should produce.
            if (!string.IsNullOrEmpty(p.BranchTo) && want.Length == 4 &&
                ARMCodec.IsBranch(BitConverter.ToUInt32(actual, 0)) &&
                (BitConverter.ToUInt32(actual, 0) >> 28) == 0xE)
            {
                uint hookLoadAddr = File2Load(at);
                uint prevBranchTarget = ARMCodec.DecodeBranchTarget(BitConverter.ToUInt32(actual, 0), hookLoadAddr);
                result.Say($"  {p.Name}: {(string.IsNullOrEmpty(p.Symbol) ? "" : p.Symbol + "+")}0x{p.Offset:X} already hooked " +
                           $"(-> 0x{prevBranchTarget:X8}) - chaining hook into previous handler");
                chainedTargets[p] = prevBranchTarget;
                patchLocations[p] = at;
                todo.Add(p);
                continue;
            }

            result.Fail($"{p.Name}: at {(string.IsNullOrEmpty(p.Symbol) ? "" : p.Symbol + "+")}0x{p.Offset:X} the build holds {Convert.ToHexString(actual)}, " +
                        $"expected {Convert.ToHexString(want)}");
        }
        if (!result.Success) { result.Say("refusing to write: this build differs at an anchor"); return result; }
        if (alreadyInstalled > 0 && alreadyInstalled == literalPatches)
        {
            // Every literal patch is in place, so this package is installed: placing its blocks again
            // (and re-chaining its hooks onto themselves) would only duplicate it.
            result.Say("  code.bin: this package is already installed - nothing to do");
            result.Output = codeBin;
            return result;
        }
        if (alreadyInstalled > 0)
        {
            result.Fail($"code.bin: {alreadyInstalled} of {literalPatches} patch(es) are already installed and the rest are not; " +
                        "revert this package first, then install it again");
            return result;
        }
        if (port.Patches.Count > 0 && todo.Count == 0) { result.Say("  code.bin: nothing to do - every anchor is already patched"); result.Output = codeBin; return result; }
        if (port.Patches.Count == 0 && port.Blocks.Count == 0) { result.Say("  code.bin: nothing to do"); result.Output = codeBin; return result; }

        // --- free space for the blocks ---
        var allocator = CodeSpaceAllocator.ForCodeBin(work, keepOut, textEnd);
        int totalNeeded = port.Blocks.Sum(b => (b.CodeBytes.Length + 3) & ~3);
        var grant = allocator.Allocate(totalNeeded, port.Blocks.Count > 0 ? port.Blocks[0].Name : null);
        // No other free-space scan: it could not see the keep-out ranges and would hand back the bytes refused here.
        uint free = grant.Success ? grant.Offset : 0;
        if (free == 0 && port.Blocks.Count > 0)
        {
            var (largestAt, largest) = allocator.LargestFree();
            result.Fail($"code.bin has no executable room for {totalNeeded} (0x{totalNeeded:X}) bytes of code: the largest free run " +
                        $"inside .text is {largest} (0x{largest:X}) bytes at file offset 0x{largestAt:X}. Earlier builds put such blocks in .rodata/.data, which " +
                        "cannot execute (NoExecuteFault) and overwrote live constants; that path is closed. Uninstall another " +
                        "code.bin package or expand .text first.");
            return result;
        }
        uint bump = free;

        foreach (var b in port.Blocks)
        {
            byte[] body = b.CodeBytes;
            uint at = (bump + 3u) & ~3u;
            uint loadAt = File2Load(at);

            // Rebase, treating the source's own symbol calls as external so they re-aim correctly.
            var (rebased, rep) = CodeRelocator.RebaseCode(body, b.SourceBaseValue, loadAt);
            rebased.CopyTo(work, (int)at);
            bump = at + (uint)rebased.Length;
            result.Placed[b.Name] = loadAt;
            result.Say($"  block '{b.Name}' 0x{b.SourceBaseValue:X8} -> 0x{loadAt:X8} " +
                       $"({rep.ExternalBranchesFixed} external, {rep.InternalBranchesKept} internal)");
            foreach (var w in rep.Warnings) result.Say($"    ! {w}");
        }

        // --- retargets inside placed blocks ---
        foreach (var b in port.Blocks)
        {
            if ((b.Retargets?.Count ?? 0) == 0) continue;
            if (!result.Placed.TryGetValue(b.Name, out uint at)) continue;
            uint bFile = Load2File(at);
            foreach (var rt in b.Retargets)
            {
                uint dest;
                if (!string.IsNullOrEmpty(rt.Symbol) || !string.IsNullOrEmpty(rt.Addr))
                {
                    uint sa;
                    string what;
                    if (!string.IsNullOrEmpty(rt.Symbol))
                    {
                        if (!syms.TryGetValue(rt.Symbol, out sa))
                        { result.Fail($"{b.Name}: retarget symbol not exported: {rt.Symbol}"); continue; }
                        what = rt.Symbol;
                    }
                    else { sa = rt.AddrValue; what = $"0x{sa:X}"; }

                    uint verify = Convert.ToUInt32(rt.Verify, 16);
                    dest = ResolveDrift(sa, verify, rt.SearchWindow <= 0 ? 0x40 : rt.SearchWindow, out int drift);
                    if (dest == uint.MaxValue)
                    { result.Fail($"{b.Name}: {what} does not start with {rt.Verify} anywhere near 0x{sa:X}"); continue; }
                    if (drift != 0)
                        result.Say($"  {b.Name}: {what} is really at 0x{dest:X} ({drift:+#;-#} from the Ultra Sun address)");
                    dest = (uint)(dest + rt.Offset);
                }
                else
                {
                    uint sf = Load2File(rt.FromValue);
                    if (sf + 4 > work.Length) { result.Fail($"{b.Name}: retarget source past end"); continue; }
                    uint inst = BitConverter.ToUInt32(work, (int)sf);
                    if (!ARMCodec.IsBranch(inst)) { result.Fail($"{b.Name}: retarget source is not a branch"); continue; }
                    dest = ARMCodec.DecodeBranchTarget(inst, rt.FromValue);
                }
                uint site = at + rt.AtValue;
                var br = rt.Link ? ARMCodec.EncodeBranchLink(site, dest) : ARMCodec.EncodeBranch(site, dest);
                br.CopyTo(work, (int)(bFile + rt.AtValue));
                result.Say($"  {b.Name}: retarget +0x{rt.AtValue:X} -> {(rt.Link ? "BL" : "B")} 0x{dest:X8}");
            }
        }

        // --- patches ---
        foreach (var p in todo)
        {
            uint symAddr = !string.IsNullOrEmpty(p.Symbol) && syms.TryGetValue(p.Symbol, out uint sa) ? sa : 0;
            bool verified = patchLocations.TryGetValue(p, out uint pa);
            uint fileAt = verified ? pa :
                          (!string.IsNullOrEmpty(p.Symbol) ? Load2File(symAddr + (uint)p.Offset) : (uint)p.Offset);
            uint loadAt = File2Load(fileAt);

            // Same drift problem for the anchor itself: search for the expected Original nearby. Never
            // for a site already verified above: a chained hook's site holds the earlier package's
            // branch, and searching for the original word moved the hook onto a coincidental match.
            if (!verified && !string.IsNullOrEmpty(p.Symbol) && p.OriginalBytes.Length >= 4)
            {
                uint want = BitConverter.ToUInt32(p.OriginalBytes, 0);
                if (fileAt + 4 > work.Length || BitConverter.ToUInt32(work, (int)fileAt) != want)
                {
                    uint found = ResolveDrift(loadAt, want, 0x40, out int drift);
                    if (found != uint.MaxValue && drift != 0)
                    {
                        result.Say($"  {p.Name}: {p.Symbol} is really at 0x{found:X} ({drift:+#;-#} from static.crs)");
                        fileAt = Load2File(found); loadAt = found;
                    }
                }
            }

            if (!string.IsNullOrEmpty(p.BranchTo))
            {
                if (!result.Placed.TryGetValue(p.BranchTo, out uint blockAt))
                { result.Fail($"{p.Name}: block '{p.BranchTo}' was not placed"); continue; }
                uint dest = blockAt + (uint)p.BranchOffset;

                // If this site was previously hooked, re-aim fallback branch in the new block to the previous handler
                if (chainedTargets.TryGetValue(p, out uint prevHandler))
                {
                    uint blockFileAt = Load2File(blockAt);
                    var b = port.Blocks.FirstOrDefault(x => x.Name == p.BranchTo);
                    if (b != null)
                    {
                        for (int i = 0; i < b.CodeBytes.Length; i += 4)
                        {
                            uint inst = BitConverter.ToUInt32(work, (int)blockFileAt + i);
                            if (ARMCodec.IsBranch(inst))
                            {
                                uint branchTarget = ARMCodec.DecodeBranchTarget(inst, blockAt + (uint)i);
                                if (Math.Abs((long)branchTarget - (long)loadAt) <= 8)
                                {
                                    var chainedBr = ARMCodec.EncodeBranch(blockAt + (uint)i, prevHandler);
                                    chainedBr.CopyTo(work, (int)blockFileAt + i);
                                    result.Say($"  chained fallback in '{b.Name}'+0x{i:X} -> 0x{prevHandler:X8}");

                                    // NOP the re-PUSH (STMFD / PUSH) immediately before the fallback branch.
                                    // Each block's exit is: POP {own_regs} / PUSH {orig_regs} / B <original_body>.
                                    // When chaining, the next handler has its own prologue, so the re-PUSH is
                                    // orphaned and corrupts the stack. Replace it with MOV R0, R0 (NOP).
                                    if (i >= 4)
                                    {
                                        uint preceding = BitConverter.ToUInt32(work, (int)blockFileAt + i - 4);
                                        if ((preceding & 0xFFFF0000) == 0xE92D0000) // PUSH / STMFD SP!, {...}
                                        {
                                            uint nop = 0xE1A00000; // MOV R0, R0
                                            BitConverter.GetBytes(nop).CopyTo(work, (int)blockFileAt + i - 4);
                                            result.Say($"  NOP'd re-PUSH at '{b.Name}'+0x{i - 4:X} to prevent stack corruption");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                var br = p.Link ? ARMCodec.EncodeBranchLink(loadAt, dest) : ARMCodec.EncodeBranch(loadAt, dest);
                br.CopyTo(work, (int)fileAt);
                result.Say($"  {p.Name}: {p.Symbol}+0x{p.Offset:X} -> {(p.Link ? "BL" : "B")} 0x{dest:X8}");
            }
            else
            {
                var bytes = p.PatchedBytes;
                if (bytes == null) { result.Fail($"{p.Name}: neither Patched nor BranchTo"); continue; }

                // Resolve retargets BEFORE the write: a vanilla source site very often lies inside
                // the range this patch is about to overwrite, so reading it afterwards would decode
                // our own freshly-written bytes instead of the build's original call.
                var resolved = new List<(uint at, uint dest, bool link)>();
                bool bad = false;
                foreach (var rt in p.Retargets ?? [])
                {
                    // Symbol / Addr form: re-find the callee by its prologue, since a shared static.crs
                    // reports Ultra Sun addresses for both builds.
                    if (!string.IsNullOrEmpty(rt.Symbol) || !string.IsNullOrEmpty(rt.Addr))
                    {
                        uint rtBase; string what;
                        if (!string.IsNullOrEmpty(rt.Symbol))
                        {
                            if (!syms.TryGetValue(rt.Symbol, out rtBase))
                            { result.Fail($"{p.Name}: retarget symbol not exported: {rt.Symbol}"); bad = true; continue; }
                            what = rt.Symbol;
                        }
                        else { rtBase = rt.AddrValue; what = $"0x{rtBase:X}"; }
                        uint vfy = Convert.ToUInt32(rt.Verify, 16);
                        uint d2 = ResolveDrift(rtBase, vfy, rt.SearchWindow <= 0 ? 0x40 : rt.SearchWindow, out int dr);
                        if (d2 == uint.MaxValue)
                        { result.Fail($"{p.Name}: {what} does not start with {rt.Verify} near 0x{rtBase:X}"); bad = true; continue; }
                        if (dr != 0)
                            result.Say($"  {p.Name}: {what} is really at 0x{d2:X} ({dr:+#;-#} from the Ultra Sun address)");
                        resolved.Add((rt.AtValue, (uint)(d2 + rt.Offset), rt.Link));
                        continue;
                    }

                    uint srcFile = Load2File(rt.FromValue);
                    if (srcFile + 4 > work.Length)
                    { result.Fail($"{p.Name}: retarget source 0x{rt.FromValue:X} is past the end"); bad = true; continue; }
                    uint inst = BitConverter.ToUInt32(work, (int)srcFile);
                    if (!ARMCodec.IsBranch(inst))
                    { result.Fail($"{p.Name}: retarget source 0x{rt.FromValue:X} holds {inst:X8}, which is not a branch"); bad = true; continue; }
                    resolved.Add((rt.AtValue, ARMCodec.DecodeBranchTarget(inst, rt.FromValue), rt.Link));
                }
                if (bad) continue;

                bytes.CopyTo(work, (int)fileAt);
                foreach (var (at, dest, link) in resolved)
                {
                    uint site = loadAt + at;
                    var br = link ? ARMCodec.EncodeBranchLink(site, dest) : ARMCodec.EncodeBranch(site, dest);
                    br.CopyTo(work, (int)(fileAt + at));
                    result.Say($"  {p.Name}: retarget +0x{at:X} -> {(link ? "BL" : "B")} 0x{dest:X8} (from this build)");
                }
                result.Say($"  {p.Name}: {p.Symbol}+0x{p.Offset:X} <- {Convert.ToHexString(bytes)}");
            }
        }

        result.Output = work;
        return result;
    }

    public static PortResult Revert(byte[] codeBin, CodeBinPort port, string romFsPath, out int reverted)
    {
        var result = new PortResult();
        reverted = 0;
        var work = (byte[])codeBin.Clone();

        var needed = port.Patches.Select(p => p.Symbol).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        var syms = needed.Count > 0 ? PortDataInstaller.ResolveStaticSymbols(romFsPath, needed) : [];

        foreach (var p in port.Patches)
        {
            byte[] orig = p.OriginalBytes;
            if (orig.Length == 0) { result.Say($"  {p.Name}: no original bytes recorded, left alone"); continue; }

            long site;
            if (string.IsNullOrEmpty(p.Symbol))
            {
                site = p.Offset;
            }
            else if (!syms.TryGetValue(p.Symbol, out uint symAddr))
            {
                result.Fail($"{p.Name}: symbol {p.Symbol} not exported by this build, left alone");
                continue;
            }
            else
            {
                long reported = (long)symAddr + p.Offset - port.LoadBase;
                site = FindInstalled(work, reported, p, search: true);
                if (site < 0) site = reported;
            }

            if (site >= 0 && site + orig.Length <= work.Length && work.AsSpan((int)site, orig.Length).SequenceEqual(orig))
            { result.Say($"  {p.Name}: already original"); continue; }
            if (!HoldsInstalled(work, site, p))
            { result.Fail($"{p.Name}: 0x{site:X} does not hold this package's bytes, left alone"); continue; }

            Array.Copy(orig, 0, work, site, orig.Length);
            reverted++;
            result.Say($"  {p.Name}: restored 0x{site:X}");
        }

        result.Output = work;
        return result;
    }

    private static bool HoldsInstalled(byte[] buf, long at, CodeBinAnchoredPatch p)
    {
        int len = p.OriginalBytes.Length;
        if (len == 0 || at < 0 || at + len > buf.Length) return false;
        if (!string.IsNullOrEmpty(p.BranchTo))
        {
            uint w = BitConverter.ToUInt32(buf, (int)at);
            return ARMCodec.IsBranch(w) && ARMCodec.IsBranchLink(w) == p.Link;
        }
        var patched = p.PatchedBytes;
        if (patched == null || patched.Length != len) return false;
        var retargeted = (p.Retargets ?? []).Select(r => (int)r.AtValue).ToHashSet();
        for (int i = 0; i < len; i += 4)
        {
            int n = Math.Min(4, len - i);
            if (retargeted.Contains(i))
            {
                if (n < 4 || !ARMCodec.IsBranch(BitConverter.ToUInt32(buf, (int)at + i))) return false;
            }
            else if (!buf.AsSpan((int)at + i, n).SequenceEqual(patched.AsSpan(i, n))) return false;
        }
        return true;
    }

    private static long FindInstalled(byte[] buf, long reported, CodeBinAnchoredPatch p, bool search)
    {
        for (int d = 0; d <= (search ? 0x40 : 0); d += 4)
            foreach (int sign in d == 0 ? new[] { 1 } : new[] { 1, -1 })
                if (HoldsInstalled(buf, reported + sign * d, p)) return reported + sign * d;
        return -1;
    }
}
