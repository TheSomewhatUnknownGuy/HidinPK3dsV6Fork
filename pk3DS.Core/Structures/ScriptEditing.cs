using System;
using System.Collections.Generic;

namespace pk3DS.Core;

/// <summary>One numeric argument slot belonging to a decoded AMX instruction, editable in place.</summary>
public sealed class ScriptArgSlot
{
    /// <summary>Index into the flat instruction word array this argument lives in.</summary>
    public int FlatIndex;

    /// <summary>True if this argument is packed into bits 16-31 of the opcode word itself (a
    /// single-word instruction), rather than being its own separate following word.</summary>
    public bool Packed;

    public string Label = "";
}

/// <summary>One decoded AMX instruction, with its argument slots ready to read or edit.</summary>
public sealed class DecodedInstruction
{
    /// <summary>Index of this instruction's opcode word in the flat instruction array.</summary>
    public int FlatIndex;

    /// <summary>Total words this instruction occupies (1 for packed/argless, 2+ otherwise).</summary>
    public int WordCount;

    /// <summary>The original human-readable line from <see cref="Scripts.ParseScript"/> for this instruction.</summary>
    public string Display = "";

    public List<ScriptArgSlot> Args { get; } = [];
}

/// <summary>
/// Turns a decompressed AMX instruction stream into a list of editable instructions, and back.
/// <para>
/// Deliberately does NOT reimplement <see cref="Scripts.ParseScript"/>'s large per-opcode switch -
/// with 256 distinct opcodes seen across this ROM's real scripts (179 of them still unnamed
/// placeholders), hand-transcribing that switch's word-consumption rules a second time would risk
/// a silent, hard-to-notice mismatch that corrupts editing for whichever opcode got it wrong.
/// Instead, this calls the already-verified <c>ParseScript</c> once and derives each instruction's
/// word span from the hex offset prefix every line already carries (<c>"0x0004: [2E] ..."</c>) -
/// the span between consecutive instructions' offsets, divided by 4, is that instruction's real
/// word count, whatever shape its own opcode case happens to have (including the one genuinely
/// variable-length opcode, the 0x82 switch/jump table - no special-casing needed, since its
/// consumed word count falls straight out of the same offset math as everything else).
/// </para>
/// <para>
/// A one-word instruction's argument (if the displayed text shows one) is packed into that same
/// word's upper 16 bits (bits 16-31, a signed 16-bit value) - matching the switch's own
/// <c>(short)(c &gt;&gt; 16)</c> pattern; anything spanning more than one word has its remaining
/// words each treated as an independent, whole-word argument. This can't know each argument's
/// individual semantic meaning (item id vs quantity vs a jump delta, say) without the opcode names
/// this ROM is still missing - editing is generic by design, and jump-shaped instructions (their
/// argument is a byte offset relative to the instruction's own position) are left editable but
/// clearly displayed as such, same as everything else, so the caller/UI can warn appropriately.
/// </para>
/// </summary>
public static class ScriptEditing
{
    /// <param name="nativeHashes">
    /// Forwarded to <see cref="Scripts.ParseScript"/> - see <see cref="Script.NativeFunctionHashes"/>.
    /// Only affects the human-readable <see cref="DecodedInstruction.Display"/> text; word boundaries
    /// and argument slots are unaffected either way.
    /// </param>
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

    /// <summary>Parses the leading <c>"0xNNNN:"</c> byte offset every <see cref="Scripts.ParseScript"/> line starts with.</summary>
    private static int ParseLineByteOffset(string line)
    {
        int colon = line.IndexOf(':');
        return Convert.ToInt32(line[2..colon], 16);
    }

    /// <summary>True if the line's own parenthesised argument list isn't empty (i.e. not literally "()").</summary>
    private static bool HasDisplayedArgument(string line)
    {
        int open = line.IndexOf('(');
        if (open < 0) return false;
        int close = line.IndexOf(')', open);
        return close > open + 1;
    }

    public static int GetArgValue(uint[] cmd, ScriptArgSlot slot) =>
        slot.Packed ? (short)(cmd[slot.FlatIndex] >> 16) : unchecked((int)cmd[slot.FlatIndex]);

    /// <summary>Writes a new value into an argument slot, in place, without touching any other word.</summary>
    public static void SetArgValue(uint[] cmd, ScriptArgSlot slot, int value)
    {
        cmd[slot.FlatIndex] = slot.Packed
            ? (cmd[slot.FlatIndex] & 0xFFFF) | ((uint)(ushort)value << 16)
            : unchecked((uint)value);
    }

    /// <summary>
    /// Opcodes whose word right after the opcode is a self-relative branch/call delta - target =
    /// (this instruction's own flat word index * 4) + delta - mirrored exactly from
    /// <see cref="Scripts.ParseScript"/>'s own case list (CallFunc, the unconditional jumps, and
    /// every conditional branch). <see cref="SwitchOpcode"/> (0x82) is handled separately: it packs
    /// several such deltas, one per case plus a default, at positions that depend on its own case
    /// count rather than a fixed offset.
    /// </summary>
    private static readonly HashSet<uint> BranchOpcodes =
    [
        0x31, 0x33, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x81,
    ];

    private const uint SwitchOpcode = 0x82;

    /// <summary>
    /// Removes <paramref name="wordCount"/> words starting at <paramref name="startFlatIndex"/> from
    /// a decompressed instruction stream, and corrects every branch/call/switch-case delta ELSEWHERE
    /// in the same stream whose source and target end up on opposite sides of the removed span.
    /// <para>
    /// A delta entirely before the removed span, or entirely after it, needs no change: both its
    /// source and target shift by the same amount when later instructions move down to fill the
    /// gap, so their difference - the delta itself - is unaffected. Only a delta that CROSSES the
    /// removed span would otherwise end up pointing at the wrong place once its neighbours move;
    /// this recomputes exactly those, and only those.
    /// </para>
    /// <para>
    /// Throws if some other instruction's branch target lands INSIDE the span being removed - that
    /// would mean removing code something else still jumps into, which is never safe to silently
    /// patch around. Reuses <see cref="Decode"/> for instruction boundaries rather than re-deriving
    /// word counts here, for the same reason <see cref="Decode"/> itself avoids re-deriving them
    /// from the opcode table: a second, hand-written copy of those rules is exactly how a rename or
    /// a new opcode quietly goes stale in one copy and not the other.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// The exact inverse of <see cref="RemoveWords"/>: inserts <paramref name="words"/> starting at
    /// <paramref name="startFlatIndex"/>, correcting every branch/call/switch-case delta elsewhere
    /// whose source and target end up on opposite sides of the new span - the same crossing-delta
    /// adjustment <see cref="RemoveWords"/> makes, just shifting outward instead of closing a gap.
    /// Exists to undo a removal cleanly (repair a bad edit) rather than requiring the original,
    /// un-edited stream to still be available.
    /// </summary>
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
