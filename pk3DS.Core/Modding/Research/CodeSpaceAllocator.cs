using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>Result of a space request.</summary>
public readonly record struct SpaceGrant(uint Offset, int Size, string Source)
{
    public bool Success => Offset != 0 && Size > 0;
    public static SpaceGrant Fail(string why) => new(0, 0, why);
}

/// <summary>
/// Hands out regions of a binary for new code or data.
/// <para>
/// Two sources are consulted, in order. First the researcher-maintained free-space registry
/// ("Table of my custom functions"), which records the offset, length and remaining room of every
/// region already carved out — this is authoritative, because it accounts for space that is
/// reserved-but-not-yet-filled and therefore looks free to a byte scan. Only if that yields
/// nothing does it fall back to scanning the file for runs of padding.
/// </para>
/// <para>
/// Grants are tracked for the lifetime of the allocator so two effects installed in one session
/// can never be handed the same bytes — the previous code path (a bare <c>FindFreeCodeSpace</c>
/// scan per call) would happily return the same offset twice.
/// </para>
/// </summary>
public sealed class CodeSpaceAllocator
{
    private readonly byte[] _binary;
    private readonly List<ResearchFreeSpace> _registry;
    private readonly List<(uint Start, uint End)> _granted = [];

    /// <summary>Bytes treated as padding when scanning: 0xCC traps and plain zero fill.</summary>
    private static readonly byte[] PaddingBytes = [0xCC, 0x00];
    private const int MinimumPaddingRun = 0x40;

    public ResearchTarget Target { get; }
    public IReadOnlyList<(uint Start, uint End)> Granted => _granted;

    private readonly int _textEnd;

    /// <param name="keepOut">Ranges never to hand out (Uhuru's own code, earlier installs).</param>
    /// <param name="textEnd">Where code.bin's executable .text ends (file offset); CRO targets scan the whole file. After text growth, pass <see cref="CodeGrowth.TextEndFor"/> using the grown exheader; the default remains the stock text boundary.</param>
    public CodeSpaceAllocator(byte[] binary, ResearchDatabase db, ResearchTarget target,
                              IEnumerable<(uint Start, int Size)> keepOut = null,
                              int textEnd = ResearchEngine.CodeTextRegionEnd)
    {
        _binary = binary ?? throw new ArgumentNullException(nameof(binary));
        Target = target;
        _textEnd = textEnd;
        _registry = db?.FreeSpaceFor(target) ?? [];
        foreach (var (start, size) in keepOut ?? [])
            Exclude(start, size);
    }

    /// <summary>
    /// The one allocator for code.bin blocks. Keep-out = <paramref name="placedByInstalls"/> plus, on Uhuru,
    /// its changed regions and caves. First-fit by ascending address, never at or past <paramref name="textEnd"/>.
    /// The default limit is the stock boundary and deliberately does not expose pages added by <see cref="CodeGrowth"/>.
    /// </summary>
    public static CodeSpaceAllocator ForCodeBin(byte[] code, IEnumerable<(uint Start, int Size)> placedByInstalls = null,
                                                int textEnd = ResearchEngine.CodeTextRegionEnd)
    {
        var keep = (placedByInstalls ?? []).ToList();
        if (UhuruGuard.Active) keep.AddRange(UhuruGuard.KeepOut("code.bin").Select(k => (k.Item1, k.Item2)));
        return new CodeSpaceAllocator(code, null, ResearchTarget.CodeBin, keep, textEnd);
    }

    /// <summary>Largest remaining 4-aligned pad run honoring keep-out and grants (for refusal messages).</summary>
    public (int Offset, int Length) LargestFree()
    {
        int limit = ScanLimit();
        int best = 0, bestAt = 0, runStart = -1;
        for (int i = 0; i <= limit; i++)
        {
            bool pad = i < limit && IsAvailablePadding(i);
            if (pad) { if (runStart < 0) runStart = i; continue; }
            if (runStart >= 0)
            {
                int runLength = i - runStart;
                int s = (runStart + 3) & ~3, len = i - s;
                if (runLength >= MinimumPaddingRun && len > best) { best = len; bestAt = s; }
                runStart = -1;
            }
        }
        return (bestAt, best);
    }

    private int ScanLimit() => Target == ResearchTarget.CodeBin ? Math.Min(_binary.Length, _textEnd) : _binary.Length;

    private bool IsAvailablePadding(int offset) => PaddingBytes.Contains(_binary[offset]) && !Overlaps((uint)offset, 1);

    /// <summary>
    /// Reserves <paramref name="size"/> bytes, 4-byte aligned. Returns a failed grant (rather than
    /// throwing or silently returning 0) so callers can surface a precise reason.
    /// </summary>
    public SpaceGrant Allocate(int size, string purpose = null)
    {
        if (size <= 0) return SpaceGrant.Fail("requested size must be positive");
        int need = (size + 3) & ~3;

        foreach (var region in _registry)
        {
            // Room is measured from the END of whatever already occupies the region; the bytes lost
            // to aligning the start count against it, or a grant could run up to 3 bytes past.
            uint end = region.Offset + (uint)Math.Max(0, region.Length);
            uint start = (end + 3u) & ~3u;
            if (region.Room - (int)(start - end) < need) continue;
            if (start + need > _binary.Length) continue;
            if (Overlaps(start, (uint)need)) continue;

            _granted.Add((start, start + (uint)need));
            return new SpaceGrant(start, need, $"registry:{region.Function ?? region.Origin.ToString()}");
        }

        var scanned = ScanForPadding(need);
        if (scanned.Success) return scanned;

        return SpaceGrant.Fail(
            $"no free region of {need} bytes in {Target} " +
            $"(registry had {_registry.Count} candidate(s), largest room {(_registry.Count > 0 ? _registry.Max(r => r.Room) : 0)})");
    }

    /// <summary>Fallback: find a run of padding long enough, avoiding anything already granted.</summary>
    private SpaceGrant ScanForPadding(int need)
    {
        int runStart = -1;
        // code.bin: only .text is executable; a hit in .rodata/.data faults on the first branch in.
        int limit = ScanLimit();
        for (int i = 0; i <= limit; i++)
        {
            bool pad = i < limit && IsAvailablePadding(i);
            if (pad) { if (runStart < 0) runStart = i; continue; }
            if (runStart < 0) continue;

            int runLength = i - runStart;
            int start = (runStart + 3) & ~3;
            if (runLength >= MinimumPaddingRun && i - start >= need)
            {
                uint at = (uint)start;
                _granted.Add((at, at + (uint)need));
                return new SpaceGrant(at, need, "scan:padding");
            }
            runStart = -1;
        }
        return SpaceGrant.Fail("no padding run found");
    }

    /// <summary>
    /// Marks a range as taken without granting it: blocks earlier installs placed (see
    /// <see cref="InstallRecord"/>). Their zero-filled tables otherwise look like free padding.
    /// </summary>
    public void Exclude(uint start, int size)
    {
        if (size > 0) _granted.Add((start, start + (uint)size));
    }

    private bool Overlaps(uint start, uint size)
    {
        uint end = start + size;
        return _granted.Any(g => start < g.End && end > g.Start);
    }

    /// <summary>Total bytes the registry believes are available, for reporting.</summary>
    public int RegistryRoom => _registry.Sum(r => r.Room);
    public int RegistryRegions => _registry.Count;
}
