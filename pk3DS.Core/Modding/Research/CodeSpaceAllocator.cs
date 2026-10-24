using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public readonly record struct SpaceGrant(uint Offset, int Size, string Source)
{
    public bool Success => Offset != 0 && Size > 0;
    public static SpaceGrant Fail(string why) => new(0, 0, why);
}

public sealed class CodeSpaceAllocator
{
    private readonly byte[] _binary;
    private readonly List<ResearchFreeSpace> _registry;
    private readonly List<(uint Start, uint End)> _granted = [];

    private static readonly byte[] PaddingBytes = [0xCC, 0x00];
    private const int MinimumPaddingRun = 0x40;

    public ResearchTarget Target { get; }
    public IReadOnlyList<(uint Start, uint End)> Granted => _granted;

    private readonly int _textEnd;

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

    public static CodeSpaceAllocator ForCodeBin(byte[] code, IEnumerable<(uint Start, int Size)> placedByInstalls = null,
                                                int textEnd = ResearchEngine.CodeTextRegionEnd)
    {
        var keep = (placedByInstalls ?? []).ToList();
        if (UhuruGuard.Active) keep.AddRange(UhuruGuard.KeepOut("code.bin").Select(k => (k.Item1, k.Item2)));
        return new CodeSpaceAllocator(code, null, ResearchTarget.CodeBin, keep, textEnd);
    }

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

    public void Exclude(uint start, int size)
    {
        if (size > 0) _granted.Add((start, start + (uint)size));
    }

    private bool Overlaps(uint start, uint size)
    {
        uint end = start + size;
        return _granted.Any(g => start < g.End && end > g.Start);
    }

    public int RegistryRoom => _registry.Sum(r => r.Room);
    public int RegistryRegions => _registry.Count;
}
