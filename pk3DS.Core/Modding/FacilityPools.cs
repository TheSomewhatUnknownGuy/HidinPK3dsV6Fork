#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.Modding;

/// <summary>How a pool's cells are laid out.</summary>
public enum PoolKind
{
    /// <summary>(trainer id, flag) pairs, two cells per entry.</summary>
    TrainerFlagPairs,
    /// <summary>One trainer id per cell.</summary>
    TrainerIds,
    /// <summary>One flag id per cell.</summary>
    FlagIds,
}

/// <summary>
/// One fixed-size pool in Uhuru's facility scripts. <see cref="Address"/> is the data-segment byte offset (AMX dat = 0).
/// <see cref="Count"/> is pairs for <see cref="PoolKind.TrainerFlagPairs"/>, cells otherwise.
/// <see cref="Alias"/> names a pool whose words this one shares (a window onto it, no copy).
/// <see cref="Mirror"/> names the pool this one copies (its first <see cref="Count"/> pairs); see <see cref="FacilityPools.Set"/>.
/// </summary>
public sealed record PoolDef(string Name, int Script, int Address, int Count, PoolKind Kind, string? Alias = null, string? Mirror = null);

/// <summary>
/// Reads and edits Uhuru's trainer pools in AMX scripts 26 and 217 without recompressing them.
/// <para>
/// Only cell values change: pool sizes and addresses are code operands. Every untouched word keeps its
/// original varint bytes and an edited word gets its own re-encoded varint, so the script can grow or shrink
/// by a byte. Only the offset-0 Length field follows. There is no add, remove or reorder API.
/// </para>
/// <para>
/// Alias: <c>0x160C</c> is <c>0x14EC</c> pairs 36..123, the same words, so either view sees a write.
/// Mirror: <c>0x2F60</c> and <c>0x2FC8</c> are copies of the first 13 pairs of <c>0x2D10</c> and <c>0x2DE0</c>.
/// A write to a copy is refused and names its source. A write to a source also writes the copy's matching cell,
/// so the two never diverge. Chosen over linked-write-both-ways because the source is the one place to edit.
/// </para>
/// </summary>
public sealed class FacilityPools
{
    public const int MaxTrainer = 652;
    public const int MaxFlag = 4095; // ponytail: conservative; largest flag seen is 4060, the engine limit is not measured

    public static readonly PoolDef[] Defs =
    [
        new("0x121C", 26, 0x121C, 90, PoolKind.TrainerFlagPairs),
        new("0x14EC", 26, 0x14EC, 124, PoolKind.TrainerFlagPairs),
        new("0x160C", 26, 0x160C, 88, PoolKind.TrainerFlagPairs, Alias: "0x14EC"),
        new("0x2CC8", 26, 0x2CC8, 5, PoolKind.TrainerFlagPairs),
        new("0x2CF0", 26, 0x2CF0, 4, PoolKind.TrainerFlagPairs),
        new("0x2D10", 26, 0x2D10, 26, PoolKind.TrainerFlagPairs),
        new("0x2DE0", 26, 0x2DE0, 48, PoolKind.TrainerFlagPairs),
        new("0x2F60", 26, 0x2F60, 13, PoolKind.TrainerFlagPairs, Mirror: "0x2D10"),
        new("0x2FC8", 26, 0x2FC8, 13, PoolKind.TrainerFlagPairs, Mirror: "0x2DE0"),
        new("PWT ids", 217, 0x1988, 146, PoolKind.TrainerIds),
        new("PWT gate flags", 217, 0x1BD0, 19, PoolKind.FlagIds),
    ];

    private readonly byte[] _raw;
    private readonly int _cod, _dataWord;
    private readonly uint[] _words;
    private readonly int[] _start;                    // _start[i] = file offset of word i's varint; _start[^1] = raw.Length
    private readonly Dictionary<int, uint> _edits = new();

    public int ScriptIndex { get; }

    public FacilityPools(byte[] raw, int scriptIndex)
    {
        var s = new Script(raw);
        // Uhuru only. The data segment size (hea - dat) is fixed per script (spike C, section 1), so vanilla and
        // Expansion scripts fail this check and are refused rather than edited at Uhuru's addresses.
        int expect = scriptIndex switch
        {
            26 => 0x3404,
            217 => 0x22AC,
            _ => throw new ArgumentOutOfRangeException(nameof(scriptIndex), "only Uhuru scripts 26 and 217 have pools"),
        };
        if (s.FinalOffset - s.ScriptMovementStart != expect)
            throw new InvalidDataException($"script {scriptIndex} is not Uhuru's (data segment 0x{s.FinalOffset - s.ScriptMovementStart:X}, expected 0x{expect:X})");

        ScriptIndex = scriptIndex;
        _raw = raw;
        _cod = s.ScriptInstructionStart;
        _dataWord = (s.ScriptMovementStart - _cod) / 4;
        _words = s.DecompressedInstructions;
        var starts = new List<int> { _cod };
        for (int p = _cod; p < raw.Length; p++)
            if ((raw[p] & 0x80) == 0) starts.Add(p + 1);   // a varint ends at the byte with bit 7 clear
        if (starts.Count - 1 != _words.Length || starts[^1] != raw.Length)
            throw new InvalidDataException("script stream does not end at the file end");
        _start = [.. starts];
    }

    private int Cells(PoolDef d) => d.Kind == PoolKind.TrainerFlagPairs ? d.Count * 2 : d.Count;

    private int Word(PoolDef d, int cell)
    {
        if (d.Script != ScriptIndex) throw new ArgumentException($"{d.Name} lives in script {d.Script}, not {ScriptIndex}", nameof(d));
        if ((uint)cell >= (uint)Cells(d)) throw new ArgumentOutOfRangeException(nameof(cell), $"{d.Name} has {Cells(d)} cells");
        return _dataWord + d.Address / 4 + cell;
    }

    /// <summary>True for cells that hold a trainer id. Even cells of a pair pool are trainers, odd cells are flags.</summary>
    public static bool IsTrainerCell(PoolDef d, int cell) =>
        d.Kind == PoolKind.TrainerIds || (d.Kind == PoolKind.TrainerFlagPairs && cell % 2 == 0);

    public int Get(PoolDef d, int cell)
    {
        int w = Word(d, cell);
        return (int)(_edits.TryGetValue(w, out uint v) ? v : _words[w]);
    }

    /// <summary>Every cell in order. Pairs flatten to trainer, flag, trainer, flag...</summary>
    public int[] Read(PoolDef d) => Enumerable.Range(0, Cells(d)).Select(c => Get(d, c)).ToArray();

    /// <summary>
    /// Sets one cell. Trainer cells take 1..652 and flag cells 1..4095. A copy refuses writes (see the class
    /// comment); a source also writes the same cell of its copy.
    /// </summary>
    public void Set(PoolDef d, int cell, int value)
    {
        int w = Word(d, cell);
        if (d.Mirror != null) throw new ArgumentException($"{d.Name} is a copy of {d.Mirror}; edit {d.Mirror}", nameof(d));
        bool trainer = IsTrainerCell(d, cell);
        int max = trainer ? MaxTrainer : MaxFlag;
        if (value < 1 || value > max) throw new ArgumentOutOfRangeException(nameof(value), $"{(trainer ? "trainer" : "flag")} id must be 1..{max}");
        _edits[w] = (uint)value;
        foreach (var copy in Defs.Where(c => c.Mirror == d.Name && cell < Cells(c)))
            _edits[Word(copy, cell)] = (uint)value;
    }

    /// <summary>The script with the edits spliced in. With no edits this is an identical copy.</summary>
    public byte[] ToBytes()
    {
        if (_edits.Count == 0) return (byte[])_raw.Clone();
        using var ms = new MemoryStream(_raw.Length + 8);
        ms.Write(_raw, 0, _cod);
        for (int i = 0; i < _words.Length; i++)
        {
            if (_edits.TryGetValue(i, out uint v) && v != _words[i]) ms.Write(Scripts.CompressValue((int)v));
            else ms.Write(_raw, _start[i], _start[i + 1] - _start[i]);
        }
        byte[] result = ms.ToArray();
        BitConverter.GetBytes(result.Length).CopyTo(result, 0);   // the file Length field
        return result;
    }

    /// <summary>File byte range of one cell's varint. Tests use it to predict the size change.</summary>
    internal (int Start, int End) VarintSpan(PoolDef d, int cell)
    {
        int w = Word(d, cell);
        return (_start[w], _start[w + 1]);
    }
}
