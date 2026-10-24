#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace pk3DS.Core.CTR;

public sealed class GarcLayout
{
    public long DataOffset;
    public long Total;
    public long[] Starts = Array.Empty<long>();

    internal int[] Lengths = Array.Empty<int>();
    internal long[] Paddings = Array.Empty<long>();
}

public static class GarcRepack
{
    public static GarcLayout Plan(IGARCFileProvider p, int version, int pad)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (version != GARC.VER_4 && version != GARC.VER_6)
            throw new FormatException("Invalid GARC Version: 0x" + version.ToString("X4"));
        if (pad <= 0)
            throw new ArgumentOutOfRangeException(nameof(pad), "GARC padding must be positive.");

        int count = p.FileCount;
        if (count < 0 || count > ushort.MaxValue)
            throw new InvalidDataException($"GARC member count {count} is outside the supported range 0..{ushort.MaxValue}.");

        long fatoHeaderSize = 0xC + ((long)count * 4);
        long fatbHeaderSize = 0xC + ((long)count * 16);
        long headerLength = (version == GARC.VER_6 ? 0x24L : 0x1CL) + fatoHeaderSize + fatbHeaderSize + 0xCL;
        var starts = new long[count];
        var lengths = new int[count];
        var paddings = new long[count];
        long dataLength = 0;

        for (int i = 0; i < count; i++)
        {
            int length = p.GetFileLength(i);
            if (length < 0)
                throw new InvalidDataException($"GARC member {i} has a negative length ({length}).");

            long remainder = length % (long)pad;
            long padding = remainder == 0 ? 0 : pad - remainder;
            starts[i] = dataLength;
            lengths[i] = length;
            paddings[i] = padding;
            dataLength = checked(dataLength + length + padding);
        }

        long total = checked(headerLength + dataLength);
        if (total > uint.MaxValue)
            throw new InvalidDataException($"Planned GARC size {total} bytes exceeds the 4294967295 byte limit.");

        return new GarcLayout
        {
            DataOffset = headerLength,
            Total = total,
            Starts = starts,
            Lengths = lengths,
            Paddings = paddings,
        };
    }

    public static void WriteAtomic(IGARCFileProvider p, string dest, int version, int pad)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentException.ThrowIfNullOrEmpty(dest);
        string part = dest + ".part";
        try
        {
            WriteVerifiedPart(p, part, version, pad);
            File.Move(part, dest, overwrite: true);
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    internal static void WriteVerifiedPart(IGARCFileProvider p, string part, int version, int pad)
    {
        // Deleting first removes a stale file or a symlink itself, so the write below can never follow a link onto another file.
        TryDelete(part);
        try
        {
            GARC.PackGARC(p, part, version, pad);
            Verify(p, part);
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    private static void Verify(IGARCFileProvider p, string path)
    {
        GARC.GARCFile garc = GARC.UnpackGARC(path);
        long actualLength = new FileInfo(path).Length;
        if (garc.fatb.FileCount != p.FileCount || garc.fato.EntryCount != p.FileCount)
            throw new InvalidDataException($"Packed GARC has {garc.fatb.FileCount} members; provider has {p.FileCount}.");
        if (garc.FileSize != actualLength)
            throw new InvalidDataException($"Packed GARC header length {garc.FileSize} does not match file length {actualLength}.");

        for (int i = 0; i < p.FileCount; i++)
        {
            int expectedLength = p.GetFileLength(i);
            var member = garc.fatb.Entries[i].SubEntries[0];
            if (!member.Exists || member.Length != expectedLength)
                throw new InvalidDataException($"Packed GARC member {i} has length {member.Length}; provider has {expectedLength}.");
        }
    }

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }
}

public readonly record struct AppendedMember(int FromBase, byte[]? Bytes);

public sealed class AppendGarcProvider : IGARCFileProvider, IDisposable
{
    private readonly FileStream _source;
    private readonly BaseMember[] _baseMembers;
    private readonly Dictionary<int, byte[]> _replace;
    private readonly AppendedMember[] _append;
    private readonly object _sourceLock = new();
    private readonly byte[] _copyBuffer = new byte[64 * 1024];
    private bool _disposed;

    public AppendGarcProvider(string baseGarcPath, IReadOnlyDictionary<int, byte[]> replace, IReadOnlyList<AppendedMember> append)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseGarcPath);
        ArgumentNullException.ThrowIfNull(replace);
        ArgumentNullException.ThrowIfNull(append);

        GARC.GARCFile garc = GARC.UnpackGARC(baseGarcPath);
        var members = new List<BaseMember>(Math.Max(0, garc.fatb.FileCount));
        long baseLength = new FileInfo(baseGarcPath).Length;
        foreach (GARC.FATB_Entry entry in garc.fatb.Entries)
        {
            foreach (GARC.FATB_SubEntry subEntry in entry.SubEntries)
            {
                if (!subEntry.Exists) continue;
                long offset = (long)subEntry.Start + garc.DataOffset;
                long length = subEntry.Length;
                if (offset < 0 || offset > long.MaxValue - length)
                    throw new InvalidDataException("Base GARC member offset is outside the supported range.");
                if (offset + length > baseLength)
                    throw new InvalidDataException("Base GARC member extends past the end of the file.");
                if (length > int.MaxValue)
                    throw new InvalidDataException($"Base GARC member length {length} exceeds the provider limit {int.MaxValue}.");
                members.Add(new BaseMember(offset, (int)length));
            }
        }

        if (members.Count != garc.fatb.FileCount)
            throw new InvalidDataException($"Base GARC declares {garc.fatb.FileCount} members but contains {members.Count}.");

        _baseMembers = members.ToArray();
        _replace = new Dictionary<int, byte[]>(replace);
        _append = new AppendedMember[append.Count];
        for (int i = 0; i < append.Count; i++)
        {
            AppendedMember member = append[i];
            if (member.FromBase < -1 || member.FromBase >= _baseMembers.Length)
                throw new ArgumentOutOfRangeException(nameof(append), $"Appended member {i} refers to invalid base member {member.FromBase}.");
            _append[i] = member;
        }
        foreach (int index in _replace.Keys)
        {
            if ((uint)index >= (uint)_baseMembers.Length)
                throw new ArgumentOutOfRangeException(nameof(replace), $"Replacement refers to invalid base member {index}.");
        }

        _source = new FileStream(baseGarcPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
    }

    public int FileCount => _baseMembers.Length + _append.Length;

    public int GetFileLength(int index)
    {
        ValidateIndex(index);
        if (index < _baseMembers.Length)
            return _replace.TryGetValue(index, out byte[]? replacement) ? replacement.Length : _baseMembers[index].Length;

        AppendedMember appended = _append[index - _baseMembers.Length];
        return appended.FromBase >= 0
            ? _baseMembers[appended.FromBase].Length
            : appended.Bytes?.Length ?? 0;
    }

    public void WriteFile(int index, BinaryWriter gw)
    {
        ArgumentNullException.ThrowIfNull(gw);
        ValidateIndex(index);
        if (index < _baseMembers.Length)
        {
            if (_replace.TryGetValue(index, out byte[]? replacement))
            {
                gw.Write(replacement);
                return;
            }
            WriteBase(index, gw);
            return;
        }

        AppendedMember appended = _append[index - _baseMembers.Length];
        if (appended.FromBase >= 0)
        {
            WriteBase(appended.FromBase, gw);
            return;
        }

        if (appended.Bytes is { Length: > 0 } bytes) gw.Write(bytes);
    }

    private void WriteBase(int index, BinaryWriter gw)
    {
        BaseMember member = _baseMembers[index];
        long remaining = member.Length;
        lock (_sourceLock)
        {
            _source.Position = member.Offset;
            while (remaining > 0)
            {
                int count = (int)Math.Min(_copyBuffer.Length, remaining);
                _source.ReadExactly(_copyBuffer, 0, count);
                gw.Write(_copyBuffer, 0, count);
                remaining -= count;
            }
        }
    }

    private void ValidateIndex(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)index >= (uint)FileCount)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.Dispose();
    }

    private readonly record struct BaseMember(long Offset, int Length);
}

public sealed class GarcTransaction : IDisposable
{
    private readonly List<StagedFile> _staged = new();
    private bool _disposed;

    public void Stage(string dest, IGARCFileProvider p, int version, int pad)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(dest);
        ArgumentNullException.ThrowIfNull(p);
        foreach (StagedFile staged in _staged)
        {
            if (string.Equals(Path.GetFullPath(staged.Destination), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Destination is already staged: {dest}");
        }
        string part = dest + ".part";
        GarcRepack.WriteVerifiedPart(p, part, version, pad);
        _staged.Add(new StagedFile(dest, part));
    }

    public IReadOnlyList<string> Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string[] destinations = new string[_staged.Count];
        // Every replaced file is copied aside first; if any move fails, the ones already replaced are put back.
        var backups = new List<(string Destination, string? Backup)>();
        try
        {
            for (int i = 0; i < _staged.Count; i++)
            {
                StagedFile staged = _staged[i];
                string? backup = null;
                if (File.Exists(staged.Destination))
                {
                    backup = staged.Destination + ".commit-bak";
                    File.Copy(staged.Destination, backup, overwrite: true);
                }
                backups.Add((staged.Destination, backup));
                File.Move(staged.Part, staged.Destination, overwrite: true);
                destinations[i] = staged.Destination;
            }
        }
        catch
        {
            foreach ((string destination, string? backup) in backups)
            {
                try
                {
                    if (backup != null) File.Move(backup, destination, overwrite: true);
                    else if (File.Exists(destination)) File.Delete(destination);
                }
                catch { }
            }
            throw;
        }
        foreach ((_, string? backup) in backups)
            if (backup != null) GarcRepack.TryDelete(backup);
        _staged.Clear();
        return destinations;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (StagedFile staged in _staged)
            GarcRepack.TryDelete(staged.Part);
        _staged.Clear();
    }

    private readonly record struct StagedFile(string Destination, string Part);
}
