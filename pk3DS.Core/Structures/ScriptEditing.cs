using System;
using System.Collections.Generic;

namespace pk3DS.Core;

public sealed class ScriptArgSlot
{
    public int FlatIndex;

    public bool Packed;

    public string Label = "";
}

public sealed class DecodedInstruction
{
    public int FlatIndex;

    public int WordCount;

    public string Display = "";

    public List<ScriptArgSlot> Args { get; } = [];
}

public static class ScriptEditing
{
    public static List<DecodedInstruction> Decode(uint[] cmd, uint[] nativeHashes = null)
    {
        string[] lines = Scripts.ParseScript(cmd, nativeHashes: nativeHashes);
        var flatIndices = new int[lines.Length];
        for (int k = 0; k < lines.Length; k++)
            flatIndices[k] = ParseLineByteOffset(lines[k]) / 4;

        var result = new List<DecodedInstruction>(lines.Length);
        for (int k = 0; k < lines.Length; k++)
        {
            int flatIndex = flatIndices[k];
            int nextFlatIndex = k + 1 < lines.Length ? flatIndices[k + 1] : cmd.Length;
            int wordCount = Math.Max(1, nextFlatIndex - flatIndex);
            var instr = new DecodedInstruction { FlatIndex = flatIndex, WordCount = wordCount, Display = lines[k] };

            if (wordCount == 1)
            {
                if (HasDisplayedArgument(lines[k]))
                    instr.Args.Add(new ScriptArgSlot { FlatIndex = flatIndex, Packed = true, Label = "Arg (packed)" });
            }
            else
            {
                for (int w = 1; w < wordCount; w++)
                    instr.Args.Add(new ScriptArgSlot { FlatIndex = flatIndex + w, Packed = false, Label = $"Word {w}" });
            }
            result.Add(instr);
        }
        return result;
    }

    private static int ParseLineByteOffset(string line)
    {
        int colon = line.IndexOf(':');
        return Convert.ToInt32(line[2..colon], 16);
    }

    private static bool HasDisplayedArgument(string line)
    {
        int open = line.IndexOf('(');
        if (open < 0) return false;
        int close = line.IndexOf(')', open);
        return close > open + 1;
    }

    public static int GetArgValue(uint[] cmd, ScriptArgSlot slot) =>
        slot.Packed ? (short)(cmd[slot.FlatIndex] >> 16) : unchecked((int)cmd[slot.FlatIndex]);

    public static void SetArgValue(uint[] cmd, ScriptArgSlot slot, int value)
    {
        cmd[slot.FlatIndex] = slot.Packed
            ? (cmd[slot.FlatIndex] & 0xFFFF) | ((uint)(ushort)value << 16)
            : unchecked((uint)value);
    }

    private static readonly HashSet<uint> BranchOpcodes =
    [
        0x31, 0x33, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x81,
    ];

    private const uint SwitchOpcode = 0x82;

    public static uint[] RemoveWords(uint[] cmd, int startFlatIndex, int wordCount)
    {
        if (wordCount <= 0) return (uint[])cmd.Clone();
        int removeEnd = startFlatIndex + wordCount;
        if (startFlatIndex < 0 || removeEnd > cmd.Length)
            throw new ArgumentOutOfRangeException(nameof(startFlatIndex), "Removal range falls outside the instruction stream.");

        var work = (uint[])cmd.Clone();
        var decoded = Decode(work);

        int Shifted(int flatIndex) => flatIndex >= removeEnd ? flatIndex - wordCount : flatIndex;

        void AdjustDelta(int instrFlatIndex, int deltaWordIndex)
        {
            int delta = (int)work[deltaWordIndex];
            int targetFlatIndex = (instrFlatIndex * 4 + delta) / 4;

            if (targetFlatIndex >= startFlatIndex && targetFlatIndex < removeEnd)
                throw new InvalidOperationException(
                    $"A branch at word {instrFlatIndex} targets word {targetFlatIndex}, inside the " +
                    $"span being removed [{startFlatIndex}, {removeEnd}) - not safe to remove.");

            int newDelta = (Shifted(targetFlatIndex) - Shifted(instrFlatIndex)) * 4;
            if (newDelta != delta) work[deltaWordIndex] = unchecked((uint)newDelta);
        }

        foreach (var instr in decoded)
        {
            uint opcode = work[instr.FlatIndex] & 0x7FFF;
            if (BranchOpcodes.Contains(opcode) && instr.WordCount >= 2)
            {
                AdjustDelta(instr.FlatIndex, instr.FlatIndex + 1);
            }
            else if (opcode == SwitchOpcode && instr.WordCount >= 2)
            {
                // CASETBL's own per-case target isn't opcode-relative the way every other branch
                // here is - confirmed directly from Scripts.cs's own decoder: "toOffset = ((i - 2) *
                // 4) + jmp" where i has already moved past the jmp word being read, so (i-2) is the
                // FLAT INDEX OF THE WORD IMMEDIATELY BEFORE THAT JMP WORD - the previous case's
                // ifValue, or "count" itself for the very first case - not this instruction's own
                // opcode position. Using instr.FlatIndex here (an earlier version of this method did)
                // silently mis-adjusts every CASETBL delta that crosses a removed/inserted span,
                // which looks like nothing is wrong (the script still decodes and round-trips fine)
                // right up until the game actually takes that jump.
                int end = instr.FlatIndex + instr.WordCount;
                uint count = work[instr.FlatIndex + 1];
                int j = instr.FlatIndex + 2;
                for (int c = 0; c < count && j + 1 < end; c++, j += 2)
                    AdjustDelta(j - 1, j);
                if (j < end)
                    AdjustDelta(j - 1, j); // the trailing default case
            }
        }

        var result = new uint[work.Length - wordCount];
        Array.Copy(work, 0, result, 0, startFlatIndex);
        Array.Copy(work, removeEnd, result, startFlatIndex, work.Length - removeEnd);
        return result;
    }

    public static uint[] InsertWords(uint[] cmd, int startFlatIndex, uint[] words)
    {
        if (words.Length == 0) return (uint[])cmd.Clone();
        if (startFlatIndex < 0 || startFlatIndex > cmd.Length)
            throw new ArgumentOutOfRangeException(nameof(startFlatIndex), "Insertion point falls outside the instruction stream.");

        var withGap = new uint[cmd.Length + words.Length];
        Array.Copy(cmd, 0, withGap, 0, startFlatIndex);
        Array.Copy(words, 0, withGap, startFlatIndex, words.Length);
        Array.Copy(cmd, startFlatIndex, withGap, startFlatIndex + words.Length, cmd.Length - startFlatIndex);

        var decoded = Decode(withGap);
        int insertEnd = startFlatIndex + words.Length;

        // A word position in the ORIGINAL stream maps to (position + words.Length) once it is at or
        // after the insertion point, and is unchanged before it - the mirror image of RemoveWords's
        // own Shifted().
        int Shifted(int originalFlatIndex) => originalFlatIndex >= startFlatIndex ? originalFlatIndex + words.Length : originalFlatIndex;

        void AdjustDelta(int instrFlatIndexNew, int deltaWordIndexNew)
        {
            // Skip anything inside the freshly-inserted span itself - it has no "original" position.
            if (instrFlatIndexNew >= startFlatIndex && instrFlatIndexNew < insertEnd) return;

            int delta = (int)withGap[deltaWordIndexNew];
            int oldInstrFlatIndex = instrFlatIndexNew >= insertEnd ? instrFlatIndexNew - words.Length : instrFlatIndexNew;
            int oldTargetFlatIndex = (oldInstrFlatIndex * 4 + delta) / 4;
            int newDelta = (Shifted(oldTargetFlatIndex) - instrFlatIndexNew) * 4;
            if (newDelta != delta) withGap[deltaWordIndexNew] = unchecked((uint)newDelta);
        }

        foreach (var instr in decoded)
        {
            uint opcode = withGap[instr.FlatIndex] & 0x7FFF;
            if (BranchOpcodes.Contains(opcode) && instr.WordCount >= 2)
            {
                AdjustDelta(instr.FlatIndex, instr.FlatIndex + 1);
            }
            else if (opcode == SwitchOpcode && instr.WordCount >= 2)
            {
                // Same correction as RemoveWords: CASETBL's per-case target is relative to the word
                // immediately before each jmp value (the previous case's ifValue, or "count" for the
                // first case), confirmed from Scripts.cs's own decoder - not this instruction's own
                // opcode position.
                int end = instr.FlatIndex + instr.WordCount;
                uint count = withGap[instr.FlatIndex + 1];
                int j = instr.FlatIndex + 2;
                for (int c = 0; c < count && j + 1 < end; c++, j += 2)
                    AdjustDelta(j - 1, j);
                if (j < end)
                    AdjustDelta(j - 1, j);
            }
        }

        return withGap;
    }
}
