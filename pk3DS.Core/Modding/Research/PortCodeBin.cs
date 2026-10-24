using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// A code.bin edit anchored to an exported symbol rather than an address.
/// <para>
/// code.bin has no relocation table to walk and no id fingerprints to match, so nothing about it
/// can be re-derived from its own contents. What it does have is <c>static.crs</c>, which maps
/// every exported symbol to its address in that build. Anchoring to a symbol plus an offset is
/// therefore the only form of code.bin edit that survives a rebuild.
/// </para>
/// </summary>
public sealed class CodeBinAnchoredPatch
{
    public string Name { get; set; } = "";
    /// <summary>Mangled export the address is measured from.</summary>
    public string Symbol { get; set; } = "";
    /// <summary>Byte offset from the symbol's address.</summary>
    public int Offset { get; set; }
    /// <summary>Bytes expected before patching; the run aborts if they differ.</summary>
    public string Original { get; set; } = "";
    /// <summary>Literal replacement, hex. Ignored when <see cref="BranchTo"/> is set.</summary>
    public string Patched { get; set; }
    /// <summary>Name of a <see cref="CodeBinBlock"/> to branch to instead of writing literal bytes.</summary>
    public string BranchTo { get; set; }
    /// <summary>
    /// Byte offset into <see cref="BranchTo"/>'s block. A hook usually enters partway in — the mint
    /// dispatcher sits 0xB4 bytes inside the region that also holds the routine it calls, and the
    /// two have to move together to keep their relative branches intact.
    /// </summary>
    public int BranchOffset { get; set; }
    /// <summary>Use BL rather than B.</summary>
    public bool Link { get; set; }

    /// <summary>
    /// Branches inside <see cref="Patched"/> whose destination must be taken from the build being
    /// patched rather than baked into the hex.
    /// <para>
    /// This exists because the interesting callees are often NOT exported in static.crs, so the
    /// symbol path cannot resolve them, and their addresses differ between Ultra Sun and Ultra Moon
    /// (the ability-slot helpers differ by exactly 8). Hardcoding either build's address silently
    /// corrupts the other. Instead each entry names a vanilla site that already calls the wanted
    /// function in whichever build is loaded: the installer decodes that site's branch target BEFORE
    /// writing anything, then re-encodes an equivalent branch at the given offset inside the patch.
    /// The result is version-correct with no per-version table and no runtime check.
    /// </para>
    /// </summary>
    public List<CodeBinRetarget> Retargets { get; set; } = [];

    public byte[] OriginalBytes => Convert.FromHexString(Original);
    public byte[] PatchedBytes => string.IsNullOrEmpty(Patched) ? null : Convert.FromHexString(Patched);
}

/// <summary>One branch inside a patch whose target is copied from the live build.</summary>
public sealed class CodeBinRetarget
{
    /// <summary>Byte offset of the branch within the patch's own bytes.</summary>
    public string At { get; set; } = "0x0";
    /// <summary>Load address of a vanilla instruction that already branches where we want to go.</summary>
    public string From { get; set; } = "0x0";

    /// <summary>
    /// Alternative to <see cref="From"/>: resolve an exported symbol, then CONFIRM it by checking that
    /// <see cref="Verify"/> is the word actually sitting there, searching a small window if it is not.
    /// <para>
    /// This is necessary because Ultra Sun and Ultra Moon ship a BYTE-IDENTICAL static.crs (same MD5),
    /// so the symbol table is really only correct for Ultra Sun. Functions above roughly 0x4A0000 sit
    /// 8 bytes later on Ultra Moon while the table still reports the Ultra Sun address, and trusting it
    /// lands mid-instruction. The prologue word is a reliable signature to re-find the real entry.
    /// </para>
    /// </summary>
    public string Symbol { get; set; }
    /// <summary>Expected first word at the symbol, hex, e.g. "E92D4070".</summary>
    public string Verify { get; set; }
    /// <summary>How far either side of the reported address to look. Default 0x40.</summary>
    public int SearchWindow { get; set; } = 0x40;

    /// <summary>
    /// Like <see cref="Symbol"/> but for a callee that static.crs does not export at all: the address it
    /// occupies on Ultra Sun, re-found on this build by the same <see cref="Verify"/> prologue signature.
    /// </summary>
    public string Addr { get; set; }
    /// <summary>Added to the resolved address, e.g. 4 to branch past a prologue a trampoline replays.</summary>
    public int Offset { get; set; }

    [JsonIgnore]
    public uint AddrValue => string.IsNullOrWhiteSpace(Addr) ? 0u
        : Convert.ToUInt32(Addr.Trim(), Addr.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
    /// <summary>Emit BL rather than B.</summary>
    public bool Link { get; set; } = true;

    [JsonIgnore]
    public uint AtValue => Convert.ToUInt32(At.Trim(), At.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
    [JsonIgnore]
    public uint FromValue => Convert.ToUInt32(From.Trim(), From.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
}

/// <summary>A routine placed in code.bin's free space, rebased on arrival.</summary>
public sealed class CodeBinBlock
{
    public string Name { get; set; } = "";
    /// <summary>Branches inside this block whose destination must be resolved against the live build.</summary>
    public List<CodeBinRetarget> Retargets { get; set; } = [];
    public string Code { get; set; } = "";
    /// <summary>Address the bytes were assembled for, as "0x005B9AAC".</summary>
    public string SourceBase { get; set; } = "0x0";
    /// <summary>
    /// Symbols this block calls. Each is resolved on the target and the matching call re-encoded,
    /// so a block that calls FlipTokuseiIndex keeps working when that routine moves.
    /// </summary>
    public List<string> CallsSymbols { get; set; } = [];

    public byte[] CodeBytes => Convert.FromHexString(Code);
    public uint SourceBaseValue => Convert.ToUInt32(SourceBase, 16);
}

/// <summary>The code.bin half of a port.</summary>
public sealed class CodeBinPort
{
    /// <summary>Where code.bin is mapped at run time. 0x00100000 for USUM.</summary>
    public uint LoadBase { get; set; } = 0x00100000;
    public List<CodeBinBlock> Blocks { get; set; } = [];
    public List<CodeBinAnchoredPatch> Patches { get; set; } = [];
}

/// <summary>Applies a <see cref="CodeBinPort"/> to a target build's code.bin.</summary>
public static class PortCodeBinInstaller
{
    /// <summary>
    /// Resolves symbols, places blocks in free space, then writes the anchored patches.
    /// Nothing is written unless every patch's recorded original still matches.
    /// </summary>
    /// <param name="keepOut">File ranges earlier installs placed, which must not be handed out again.</param>
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

    /// <summary>
    /// Puts back the <see cref="CodeBinAnchoredPatch.Original"/> bytes of every anchored patch, finding
    /// each site the way <see cref="Apply"/> did (symbol + offset, converted to a file offset, with the
    /// Ultra Moon drift window). A site is only restored when it still holds what the install wrote:
    /// the literal patch (retargeted words just have to be branches) or, for a hook, a B/BL.
    /// </summary>
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

    /// <summary>
    /// Whether <paramref name="at"/> holds what installing <paramref name="p"/> wrote: its literal
    /// bytes (words a Retarget rewrites only have to be branches) or, for a hook, a B/BL of its kind.
    /// </summary>
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

    /// <summary>
    /// Where <paramref name="p"/> is installed: <paramref name="reported"/> itself or, when
    /// <paramref name="search"/>, the nearest site within the 0x40 Ultra Moon drift window. -1 if none.
    /// </summary>
    private static long FindInstalled(byte[] buf, long reported, CodeBinAnchoredPatch p, bool search)
    {
        for (int d = 0; d <= (search ? 0x40 : 0); d += 4)
            foreach (int sign in d == 0 ? new[] { 1 } : new[] { 1, -1 })
                if (HoldsInstalled(buf, reported + sign * d, p)) return reported + sign * d;
        return -1;
    }
}
