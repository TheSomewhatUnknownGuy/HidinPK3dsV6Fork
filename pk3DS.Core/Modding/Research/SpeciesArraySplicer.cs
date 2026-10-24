using System;
using System.Collections.Generic;

namespace pk3DS.Core.Modding.Research;

public sealed class SpliceResult
{
    public byte[][] Files { get; set; } = [];
    public int InsertedAt { get; init; }
    public int InsertedCount { get; init; }
    public int CrossReferencesShifted { get; set; }
    public List<string> Notes { get; } = [];
    public string Describe() =>
        $"inserted {InsertedCount} entr{(InsertedCount == 1 ? "y" : "ies")} at index {InsertedAt}; " +
        $"{CrossReferencesShifted} cross-reference(s) shifted to match" +
        (Notes.Count == 0 ? "" : "\n" + string.Join("\n", Notes));
}

public static class SpeciesArraySplicer
{
    public static SpliceResult Insert(byte[][] files, int insertAt, int count, byte[] template = null,
        int altFormBlockStart = -1, int? crossRefByteOffset = null, int crossRefSize = 4)
    {
        if (files == null) throw new ArgumentNullException(nameof(files));
        if (insertAt < 0 || insertAt > files.Length) throw new ArgumentOutOfRangeException(nameof(insertAt));
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "must insert at least one entry");
        if (altFormBlockStart < 0) altFormBlockStart = insertAt;

        var result = new SpliceResult { InsertedAt = insertAt, InsertedCount = count };

        // Every surviving entry is cloned, never just reference-copied - this must never mutate
        // the caller's original array (a real bug caught this session: aliasing here meant the
        // caller's "before" data silently became the "after" data the moment Insert ran).
        var newFiles = new byte[files.Length + count][];
        for (int i = 0; i < insertAt; i++)
            newFiles[i] = files[i] != null ? (byte[])files[i].Clone() : null;
        for (int i = 0; i < count; i++)
            newFiles[insertAt + i] = template != null ? (byte[])template.Clone() : new byte[template?.Length ?? (files.Length > 0 ? files[0].Length : 0)];
        for (int i = insertAt; i < files.Length; i++)
            newFiles[i + count] = files[i] != null ? (byte[])files[i].Clone() : null;

        if (crossRefByteOffset.HasValue)
        {
            int shifted = 0;
            // Scan the WHOLE result, not just the untouched prefix before insertAt: a cross-
            // reference field can live inside the shifted block too (confirmed this session - a
            // self-referencing entry sitting right after the last untouched entry needs its own
            // pointer updated to match its new position, exactly like any entry before it that
            // pointed at the same now-moved block). The brand-new inserted entries are covered by
            // this loop too, but harmlessly - they're blank/all-zero, so their read value is 0 and
            // never qualifies as "past altFormBlockStart" unless a non-empty template says otherwise.
            for (int i = 0; i < newFiles.Length; i++)
            {
                var entry = newFiles[i];
                if (entry == null || entry.Length < crossRefByteOffset.Value + crossRefSize) continue;

                long value = ReadInt(entry, crossRefByteOffset.Value, crossRefSize);
                if (value < altFormBlockStart) continue; // doesn't point into the shifted block

                WriteInt(entry, crossRefByteOffset.Value, crossRefSize, value + count);
                shifted++;
            }
            result.CrossReferencesShifted = shifted;
        }
        else
        {
            result.Notes.Add("no cross-reference offset given - only the array itself was spliced; " +
                             "if this GARC's records reference the alt-form block by index, fix those up separately");
        }

        result.Files = newFiles;
        return result;
    }

    private static long ReadInt(byte[] data, int offset, int size) => size switch
    {
        1 => data[offset],
        2 => BitConverter.ToUInt16(data, offset),
        4 => BitConverter.ToUInt32(data, offset),
        _ => throw new ArgumentException("crossRefSize must be 1, 2, or 4"),
    };

    private static void WriteInt(byte[] data, int offset, int size, long value)
    {
        switch (size)
        {
            case 1: data[offset] = (byte)value; break;
            case 2: BitConverter.GetBytes((ushort)value).CopyTo(data, offset); break;
            case 4: BitConverter.GetBytes((uint)value).CopyTo(data, offset); break;
            default: throw new ArgumentException("crossRefSize must be 1, 2, or 4");
        }
    }
}
