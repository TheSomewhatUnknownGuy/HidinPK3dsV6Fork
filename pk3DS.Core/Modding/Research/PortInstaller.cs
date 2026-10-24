using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

/// <summary>Outcome of applying a <see cref="PortManifest"/> to a ROM.</summary>
public sealed class PortResult
{
    public bool Success { get; set; } = true;
    public List<string> Log { get; } = [];
    public List<string> Errors { get; } = [];
    public byte[] Output { get; set; }

    /// <summary>A master table, .data or a segment moved: anything holding a literal address into it would break.</summary>
    public bool TableRelocated { get; set; }

    /// <summary>Ported routine name -> where it ended up, so hooks can be aimed at it.</summary>
    public Dictionary<string, uint> Placed { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal void Say(string s) => Log.Add(s);
    internal void Fail(string s) { Errors.Add(s); Success = false; }
    public string Describe() => string.Join(Environment.NewLine, Log.Concat(Errors.Select(e => "ERROR: " + e)));
}

/// <summary>
/// Reproduces a set of edits on a different build of the same game.
/// <para>
/// Nothing is copied by address. Master tables are located by id fingerprint, borrowed routines are
/// resolved through the stock mechanic that owns them, new routines are rebased to wherever they
/// land, and hooks are re-encoded against the target's own reserve. The only thing taken literally
/// is a site patch's <em>original</em> bytes, which are checked before anything is written — a build
/// that has shifted its code fails the check instead of being silently corrupted.
/// </para>
/// </summary>
public static class PortInstaller
{
    /// <summary>Applies <paramref name="manifest"/> to <paramref name="targetRom"/>.</summary>
    /// <param name="nameTables">
    /// Optional id -> name lists per kind, so every id can be confirmed to name what the manifest
    /// claims before an effect is attached to it.
    /// </param>
    public static PortResult Apply(
        byte[] targetRom,
        PortManifest manifest,
        ResearchDatabase db,
        Dictionary<CustomMechanicKind, string[]> nameTables = null)
    {
        var result = new PortResult();
        if (targetRom == null || manifest == null) { result.Fail("no ROM or manifest supplied"); return result; }

        var map = BattleMechanicMap.Build(targetRom, db);
        if (map.Tables.Count == 0) { result.Fail("no master tables located in the target ROM"); return result; }
        var cro = map.Cro;

        var reserve = CodeRelocator.FindReserve(cro);
        if (reserve.Offset == 0) { result.Fail("target ROM has no code reserve; expand it first"); return result; }
        uint bump = reserve.Offset;

        uint lowestExisting = uint.MaxValue;
        foreach (var pm in manifest.Mechanics)
        {
            if (Enum.TryParse<CustomMechanicKind>(pm.Kind, true, out var kind))
            {
                var ex = map.Find(kind, (uint)pm.Id);
                if (ex != null && ex.HandlerOffset != 0)
                {
                    if (ex.HandlerOffset < lowestExisting) lowestExisting = ex.HandlerOffset;
                }
            }
        }
        if (lowestExisting != uint.MaxValue && lowestExisting >= reserve.Offset && lowestExisting < reserve.Offset + reserve.Length)
        {
            bump = lowestExisting;
        }

        result.Say($"target: {map.Tables.Count} tables located, reserve 0x{reserve.Offset:X6} +{reserve.Length} (bump starting @ 0x{bump:X6})");

        int extra = manifest.Mechanics.Sum(m => m.Slots.Count + 2) + 8;
        if (!GameConfig.AllowGrowth && RelocationHeadroomNeedsBufferGrowth(cro, extra))
        {
            result.Say("  ! growth is off (Options: Allow code.bin and CRO growth)");
            result.Fail("reserve exhausted: Battle.cro needs additional relocation-table space. Expand the code segment and retry.");
            return result;
        }
        result.TableRelocated |= CodeRelocator.EnsureRelocationHeadroom(cro, extra, s => result.Say("  " + s));

        // ---- 1. Verify every site patch against the bytes the manifest says should be there ----
        var sites = manifest.SitePatches.Where(p => !PortManifest.StructuralOffsets.Contains(p.OffsetValue)).ToList();
        var applicable = new List<PortedSitePatch>();
        int alreadyDone = 0;
        foreach (var p in sites)
        {
            if (p.OffsetValue + p.OriginalBytes.Length > targetRom.Length)
            { result.Fail($"0x{p.OffsetValue:X6}: past the end of the target"); continue; }

            var actual = new byte[p.OriginalBytes.Length];
            Array.Copy(cro.RawData, (int)p.OffsetValue, actual, 0, actual.Length);
            if (actual.SequenceEqual(p.OriginalBytes)) { applicable.Add(p); continue; }

            bool done = actual.SequenceEqual(p.PatchedBytes) ||
                        (p.IsHook && actual.Length == 4 && ARMCodec.IsBranch(BitConverter.ToUInt32(actual, 0)));
            if (done)
            {
                alreadyDone++;
                continue;
            }

            result.Fail($"0x{p.OffsetValue:X6}: target holds {Convert.ToHexString(actual)}, " +
                        $"manifest expected {Convert.ToHexString(p.OriginalBytes)} — this build differs here");
        }
        result.Say($"site patches: {applicable.Count}/{sites.Count} to apply" + (alreadyDone > 0 ? $", {alreadyDone} already applied" : ""));
        if (!result.Success) { result.Say("refusing to write: the target does not match the manifest"); return result; }

        // ---- 2. Mechanics ----
        foreach (var pm in manifest.Mechanics)
        {
            var kind = pm.KindValue;
            var effects = new List<MechanicEffect>();
            bool bad = false;

            foreach (var slot in pm.Slots)
            {
                if (slot.Reuse != null)
                {
                    byte wantTiming = string.IsNullOrEmpty(slot.Reuse.Timing) ? (byte)0 : (byte)Convert.ToInt32(slot.Reuse.Timing, 16);
                    var earlier = effects.FirstOrDefault(e => wantTiming == 0 || e.Timing == wantTiming);
                    if (earlier != null)
                    {
                        effects.Add(new MechanicEffect
                        {
                            Timing = slot.TimingByte,
                            Code = earlier.Code,
                            SourceBase = earlier.SourceBase,
                            ExistingFunction = earlier.ExistingFunction,
                            Name = $"{pm.Name} (reuses 0x{earlier.Timing:X2})",
                        });
                        continue;
                    }

                    string donorName = slot.Reuse.Name ?? "";
                    uint placedFn = 0;
                    foreach (var kv in result.Placed)
                    {
                        if (kv.Key.EndsWith($":0x{wantTiming:X2}") && (string.IsNullOrEmpty(donorName) || kv.Key.StartsWith(donorName)))
                        {
                            placedFn = kv.Value;
                            break;
                        }
                    }
                    if (placedFn != 0)
                    {
                        effects.Add(new MechanicEffect
                        {
                            Timing = slot.TimingByte,
                            ExistingFunction = placedFn,
                            Name = $"{pm.Name} (reuses {donorName})",
                        });
                        continue;
                    }

                    // Resolve the borrowed routine through its owner on THIS build.
                    var owner = map.Find(Enum.Parse<CustomMechanicKind>(slot.Reuse.Kind, true), slot.Reuse.Id);
                    if (owner == null)
                    { result.Fail($"{pm.Name}: donor {slot.Reuse.Kind} {slot.Reuse.Id} not found in the target"); bad = true; break; }

                    TimingSlot donorSlot = wantTiming == 0
                        ? owner.Slots.FirstOrDefault()
                        : owner.Slots.FirstOrDefault(s => s.Timing == wantTiming);
                    if (donorSlot.FunctionOffset == 0)
                    { result.Fail($"{pm.Name}: donor {slot.Reuse.Kind} {slot.Reuse.Id} has no usable routine"); bad = true; break; }

                    effects.Add(new MechanicEffect
                    {
                        Timing = slot.TimingByte,
                        ExistingFunction = donorSlot.FunctionOffset,
                        Name = $"{pm.Name} (reuses {slot.Reuse.Name ?? owner.Name})",
                    });
                }
                else
                {
                    byte[] code = slot.CodeBytes;
                    if (code is not { Length: > 0 })
                    { result.Fail($"{pm.Name}: slot 0x{slot.TimingByte:X2} has neither code nor a donor"); bad = true; break; }

                    effects.Add(new MechanicEffect
                    {
                        Timing = slot.TimingByte,
                        Code = code,
                        SourceBase = slot.SourceBaseValue,   // installer rebases to the real placement
                        Name = pm.Name,
                    });
                }
            }
            if (bad) continue;

            var req = new NewMechanicRequest
            {
                Kind = kind,
                Id = pm.Id,
                Name = pm.Name,
                Effects = effects,
                NameTable = nameTables != null && nameTables.TryGetValue(kind, out var t) ? t : null,
            };

            var res = MechanicInstaller.AddMechanic(cro, map, req, reserve, ref bump);
            for (int growTries = 0; !res.Success && growTries < 4 && ReserveExhausted(res.Errors); growTries++)
            {
                if (!TryGrowReserve(ref cro, ref map, ref reserve, ref bump, db, result)) break;
                res = MechanicInstaller.AddMechanic(cro, map, req, reserve, ref bump);
            }
            foreach (var l in res.Log) result.Say($"  {pm.Name}: {l}");
            result.TableRelocated |= res.TableRelocated;
            if (!res.Success) { foreach (var e in res.Errors) result.Fail($"{pm.Name}: {e}"); continue; }

            foreach (var s in res.Slots) result.Placed[$"{pm.Name}:0x{s.Timing:X2}"] = s.Function;
            result.Placed[pm.Name] = res.HandlerOffset;

            map = BattleMechanicMap.Build(CROCompiler.Compile(cro), db);
            cro = map.Cro;
            if (!GameConfig.AllowGrowth && RelocationHeadroomNeedsBufferGrowth(cro, extra))
            {
                result.Say("  ! growth is off (Options: Allow code.bin and CRO growth)");
                result.Fail("reserve exhausted: Battle.cro needs additional relocation-table space. Expand the code segment and retry.");
                return result;
            }
            result.TableRelocated |= CodeRelocator.EnsureRelocationHeadroom(cro, extra, _ => { });
        }

        // ---- 2b. Hook bodies into the reserve, rebased to where they land ----
        foreach (var block in manifest.Blocks)
        {
            byte[] body = block.CodeBytes;
            if (body.Length == 0) { result.Fail($"block '{block.Name}' carries no code"); continue; }

            uint at = (bump + 3u) & ~3u;
            for (int growTries = 0; at + body.Length > reserve.Offset + reserve.Length && growTries < 4; growTries++)
            {
                if (!TryGrowReserve(ref cro, ref map, ref reserve, ref bump, db, result)) break;
                at = (bump + 3u) & ~3u;
            }
            if (at + body.Length > reserve.Offset + reserve.Length)
            { result.Fail($"block '{block.Name}' does not fit in the reserve"); continue; }

            var (rebased, rep) = CodeRelocator.RebaseCode(body, block.SourceBaseValue, at);
            rebased.CopyTo(cro.RawData, (int)at);
            bump = at + (uint)rebased.Length;
            result.Placed[block.Name] = at;
            result.Say($"  block '{block.Name}' 0x{block.SourceBaseValue:X6} -> 0x{at:X6} " +
                       $"({rep.ExternalBranchesFixed} external, {rep.InternalBranchesKept} internal)");
            foreach (var w in rep.Warnings) result.Say($"    ! {w}");
        }

        // ---- 3. Site patches: literal ones copied, hooks re-encoded ----
        foreach (var p in applicable)
        {
            if (!p.IsHook)
            {
                p.PatchedBytes.CopyTo(cro.RawData, (int)p.OffsetValue);
                result.Say($"  patched 0x{p.OffsetValue:X6} ({p.PatchedBytes.Length} bytes)");
                continue;
            }

            if (!result.Placed.TryGetValue(p.HookTarget ?? "", out uint dest))
            { result.Fail($"0x{p.OffsetValue:X6}: hook target '{p.HookTarget}' was not placed"); continue; }

            // Keep the authored condition and link bit (a BL hook returns into its caller).
            uint authored = p.PatchedBytes.Length == 4 ? BitConverter.ToUInt32(p.PatchedBytes, 0) : 0xEA000000u;
            ARMCodec.RetargetBranch(authored, p.OffsetValue, dest).CopyTo(cro.RawData, (int)p.OffsetValue);
            result.Say($"  hook 0x{p.OffsetValue:X6} -> 0x{dest:X6} ({p.HookTarget}" +
                       $"{(ARMCodec.IsBranchLink(authored) ? ", BL" : "")}), re-encoded for this build");
        }

        // ---- 4. Verify ----
        byte[] output = CROCompiler.Compile(cro);
        var final = BattleMechanicMap.Build(output, db);
        var report = CroVerifier.Verify(final.Cro);
        result.Say($"verify: {report.RelocationsChecked} relocations, ok={report.Ok}, broken chains={report.ChainsBroken}");
        if (!report.Ok) { result.Fail("structural verification failed"); return result; }

        foreach (var pm in manifest.Mechanics)
        {
            var m = final.Find(pm.KindValue, pm.Id);
            result.Say(m == null
                ? $"  MISSING {pm.Name}"
                : $"  {pm.Name,-16} {pm.KindValue} {pm.Id} -> 0x{m.HandlerOffset:X6} [{string.Join(", ", m.Slots.Select(s => $"0x{s.Timing:X2}"))}]");
            if (m == null) result.Fail($"{pm.Name} did not survive the write");
        }

        result.Output = output;
        return result;
    }

    /// <summary>True for the specific "ran out of reserve space" failure, not any other kind.</summary>
    private static bool ReserveExhausted(IEnumerable<string> errors) =>
        errors.Any(e => e.Contains("reserve exhausted", StringComparison.OrdinalIgnoreCase));

    private static bool RelocationHeadroomNeedsBufferGrowth(DecompiledCRO cro, int extraRelocations)
    {
        if (cro == null || cro.RawData == null || extraRelocations <= 0 || cro.RawData.LongLength < 0x130) return false;
        byte[] rom = cro.RawData;

        long segmentTable = BitConverter.ToUInt32(rom, 0xC8);
        long segmentCount = BitConverter.ToUInt32(rom, 0xCC);
        if (segmentCount < 3 || segmentTable + 32 > rom.LongLength) return false;

        long dataSegment = segmentTable + 24;
        long dataStart = BitConverter.ToUInt32(rom, checked((int)dataSegment));
        long dataSize = BitConverter.ToUInt32(rom, checked((int)dataSegment + 4));
        long relocationStart = BitConverter.ToUInt32(rom, 0x128);
        long relocationCount = BitConverter.ToUInt32(rom, 0x12C);
        long needed = relocationStart + (relocationCount + extraRelocations) * 12;
        if (needed <= dataStart) return false;

        long shift = ((needed - dataStart) + 0x1F) & ~0x1FL;
        return dataStart + shift + dataSize > rom.LongLength;
    }

    /// <summary>
    /// Grows the code reserve on demand, right when a specific install needs more of it, instead of
    /// guessing a size upfront for everyone to share.
    /// <para>
    /// A blanket pre-expansion was tried and measured to make things slightly WORSE (one fewer
    /// package installed in a controlled before/after test across all 77 real patch-packages):
    /// whichever packages happen to run first in the install order consume the shared new space a
    /// little each, so by the time a package that genuinely needs a large chunk in one go comes
    /// up, the room has already been nibbled away by everyone before it - regardless of how much
    /// was added. Growing exactly when and where a specific install falls short gives that
    /// install its own dedicated room instead of a shared pool others can crowd out.
    /// </para>
    /// <para>
    /// Always expands at the real end of segment 1 (rodata) - <c>cro.CodeEnd</c>, not the reserve's
    /// own end and never a blind mid-segment point. That boundary is guaranteed to have no real ARM
    /// code sitting past it (by definition of what "the segment's declared end" means), so there is
    /// no risk of splitting a stock, untracked raw branch the way inserting behind an
    /// arbitrarily-chosen reserve boundary could - CROUtil.ExpandSegment only re-encodes the CRO's
    /// own relocation table, pointer tables and header fields, not literal ARM branch instructions
    /// baked into already-placed code, so anywhere content could legitimately still follow after the
    /// insertion point is exactly the risk this specific point avoids.
    /// </para>
    /// </summary>
    private static bool TryGrowReserve(
        ref DecompiledCRO cro, ref BattleMechanicMap map, ref (uint Offset, int Length) reserve,
        ref uint bump, ResearchDatabase db, PortResult result)
    {
        if (!GameConfig.AllowGrowth)
        {
            result.Say("  ! growth is off (Options: Allow code.bin and CRO growth)");
            return false;
        }

        const int GrowBy = 0x1000; // comfortably covers every shortfall seen in real testing (under 0x400)

        byte[] current = CROCompiler.Compile(cro);
        uint insertAt = cro.CodeEnd;
        byte[] grown = CROUtil.ExpandSegment(current, 'c', GrowBy, (int)insertAt);
        if (grown.Length == current.Length)
        {
            result.Say($"  ! could not grow the code reserve (ExpandSegment declined at 0x{insertAt:X6})");
            return false;
        }

        // Compare against what's actually LEFT in the current reserve (reserve.Length is its
        // original total size; bump has already eaten into it), not the original total - a call
        // partway through a multi-effect package (several master-table entries already placed by
        // earlier effects in the same manifest) can have consumed most of a 6000+ byte reserve down
        // to a few hundred free bytes, and a fresh ~4KB reserve found elsewhere is a big improvement
        // even though 4KB looks "smaller" than the stale 6000+ figure. Comparing against the total
        // instead of the remainder rejected exactly this case and left the retry loop stuck.
        uint reserveEndPos = reserve.Offset + (uint)reserve.Length;
        uint remaining = bump < reserveEndPos ? reserveEndPos - bump : 0;
        var newMap = BattleMechanicMap.Build(grown, db);
        var newReserve = CodeRelocator.FindReserve(newMap.Cro);
        if (newReserve.Offset == 0 || newReserve.Length <= remaining)
        {
            result.Say("  ! grew the CRO but found no more usable room than what was already left");
            return false;
        }

        map = newMap;
        cro = map.Cro;
        reserve = newReserve;
        bump = reserve.Offset;
        result.TableRelocated = true; // the segments after segment 1 shifted
        result.Say($"  grew the code reserve by 0x{GrowBy:X} bytes at segment 1's end (0x{insertAt:X6}); " +
                   $"new reserve 0x{reserve.Offset:X6} +{reserve.Length}");
        return true;
    }

    /// <summary>
    /// Confirms a written file really contains what was intended, by re-reading it.
    /// <para>
    /// Worth doing every time: compiling from a decompiled image and editing a caller-owned copy of
    /// the bytes are easy to confuse, and when they diverge the in-memory report looks perfect while
    /// the file on disk is missing the code entirely.
    /// </para>
    /// </summary>
    public static bool VerifyOnDisk(string path, PortManifest manifest, ResearchDatabase db, Action<string> log = null)
    {
        log ??= _ => { };
        byte[] rom = System.IO.File.ReadAllBytes(path);
        var map = BattleMechanicMap.Build(rom, db, path);
        bool ok = true;

        foreach (var pm in manifest.Mechanics)
        {
            var m = map.Find(pm.KindValue, pm.Id);
            if (m == null) { log($"  MISSING on disk: {pm.Name}"); ok = false; continue; }

            foreach (var s in m.Slots)
            {
                uint w = BitConverter.ToUInt32(rom, (int)s.FunctionOffset);
                if (w != 0) continue;
                log($"  {pm.Name}: slot 0x{s.Timing:X2} points at 0x{s.FunctionOffset:X6}, which is blank");
                ok = false;
            }
            log($"  {pm.Name,-16} -> 0x{m.HandlerOffset:X6} [{string.Join(", ", m.Slots.Select(x => $"0x{x.Timing:X2}"))}]");
        }

        var report = CroVerifier.Verify(map.Cro);
        log($"  verify: {report.RelocationsChecked} relocations, ok={report.Ok}");
        return ok && report.Ok;
    }
}
