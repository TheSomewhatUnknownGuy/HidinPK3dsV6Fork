using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Inserts new words into a decoded AMX instruction stream and fixes up every jump/call/switch
/// delta so it still points at the same logical destination afterward - the mechanical part of
/// "add a new instruction sequence to an existing script" that has nothing to do with gifts
/// specifically, kept separate so any future AMX-editing feature that needs to insert code (not
/// just edit an existing argument in place, which <see cref="ScriptEditing.SetArgValue"/> already
/// covers) can reuse it.
/// <para>
/// Every jump/call-shaped instruction in this dialect stores its target as a byte delta relative to
/// one specific word's own position - confirmed directly from <see cref="Scripts.ParseScript"/>'s
/// source for both shapes that exist: a plain single-target instruction (CallFunc, Jump, BEQ, BNE,
/// JumpEQ/NEQ/SGeq/SGrtr/SLeq/SLess, and the single-target `Switch`/0x81) stores its delta relative
/// to its OWN opcode word (`newOfs = (line*4) + delta` in <see cref="Scripts"/>, where `line` is the
/// opcode word's index); the multi-case `switch`/0x82 stores each case's delta relative to that
/// CASE'S OWN jmp word instead (`toOffset = ((i-2)*4) + jmp`, confirmed from the same source) - a
/// real, meaningful difference this class accounts for explicitly rather than assuming one
/// convention everywhere.
/// </para>
/// <para>
/// Inserting N words at some position shifts everything at or after that position by N words - a
/// delta only needs adjusting when its own word and its target land on DIFFERENT sides of the
/// insertion point (both shifting equally leaves their difference, the delta, unchanged); this is
/// computed directly from each word's absolute position, not inferred from "before/after" as a
/// separate case, so it's correct regardless of direction.
/// </para>
/// </summary>
public static class AmxRelocation
{
    /// <summary>One delta-bearing word: its own word index (what the delta is relative to) and the word index actually holding the raw delta value (the same word for every shape in this dialect, but kept distinct in case that ever changes).</summary>
    public readonly record struct DeltaRef(int SelfWordIndex, int DeltaWordIndex);

    /// <summary>Every delta-bearing word found in <paramref name="decoded"/>, covering both the simple single-target shapes and the multi-case `switch`.</summary>
    public static List<DeltaRef> FindDeltaRefs(List<DecodedInstruction> decoded)
    {
        var refs = new List<DeltaRef>();
        foreach (var instr in decoded)
        {
            string disp = instr.Display;
            if (!disp.Contains("=>", StringComparison.Ordinal)) continue;

            // The multi-case `switch` (0x82) is the only shape whose first line has no "=>" of its
            // own - just the bare opcode name, e.g. "[82] switch" - with each case's own "n => target
            // (delta)" on its own indented line after. Every single-target shape (CallFunc, Jump,
            // BEQ, the single-target "Switch"/0x81, ...) puts its own "=>" directly on that first line.
            string firstLine = disp.Split(Environment.NewLine)[0];
            bool isMultiCaseSwitch = !firstLine.Contains("=>", StringComparison.Ordinal);
            if (isMultiCaseSwitch)
            {
                // Words: [0]=opcode (not in Args), [1]=count, then (jmp,value) pairs, then a final
                // default jmp with no paired value - Args mirrors this exactly (Args[0]=count,
                // Args[1..]=the pairs/default) since ScriptEditing.Decode adds one slot per word.
                //
                // Confirmed directly from Scripts.cs's own 0x82 case: each jmp's target is computed
                // as `((i-2)*4) + jmp` where `i` is the read cursor's position immediately AFTER
                // reading that jmp word (one past its own position) - so `(i-2)` lands on the word
                // BEFORE the jmp word itself, not the jmp word's own position. Verified against real
                // decoded output (011.xamx's switch at flat 26: the first case's jmp word sits at
                // flat 28, and only `self = 28-1 = 27` reproduces the displayed target 0x0054 - using
                // the jmp word's own position (28) instead gives 0x0058, which isn't even a real
                // instruction boundary in that script).
                if (instr.Args.Count < 3) continue; // count + at least a default jmp
                int pairWords = instr.Args.Count - 2; // minus count, minus the trailing default jmp
                int pairs = pairWords / 2;
                for (int k = 0; k < pairs; k++)
                {
                    var jmpSlot = instr.Args[1 + 2 * k];
                    refs.Add(new DeltaRef(jmpSlot.FlatIndex - 1, jmpSlot.FlatIndex));
                }
                var defaultSlot = instr.Args[^1];
                refs.Add(new DeltaRef(defaultSlot.FlatIndex - 1, defaultSlot.FlatIndex));
            }
            else
            {
                // Single-target shape: the one arg word holds the delta, relative to the opcode word.
                if (instr.Args.Count == 0) continue;
                refs.Add(new DeltaRef(instr.FlatIndex, instr.Args[0].FlatIndex));
            }
        }
        return refs;
    }

    /// <summary>
    /// Returns a new instruction array with <paramref name="wordsToInsert"/> spliced in at
    /// <paramref name="insertionWordIndex"/>, with every delta found by <see cref="FindDeltaRefs"/>
    /// against the ORIGINAL <paramref name="cmd"/>/<paramref name="decoded"/> adjusted to still point
    /// at the same logical word it did before. <paramref name="extraDeltaRefs"/> covers delta words
    /// that live INSIDE the newly inserted block itself (e.g. a cloned CallFunc's own delta, which
    /// needs recomputing for its new position even though it didn't exist in the original array to
    /// be found by <see cref="FindDeltaRefs"/>) - each extra ref's <c>DeltaWordIndex</c>/`SelfWordIndex`
    /// are given as ALREADY-POST-INSERTION word indices, and its ORIGINAL (pre-insertion) target word
    /// index is supplied via <paramref name="extraOriginalTargets"/> (same order), so the same
    /// shifting rule can be applied to it too.
    /// </summary>
    public static uint[] InsertWords(
        uint[] cmd,
        List<DecodedInstruction> decoded,
        int insertionWordIndex,
        uint[] wordsToInsert,
        IReadOnlyList<DeltaRef> extraDeltaRefs = null,
        IReadOnlyList<int> extraOriginalTargets = null)
    {
        if (insertionWordIndex < 0 || insertionWordIndex > cmd.Length)
            throw new ArgumentOutOfRangeException(nameof(insertionWordIndex));

        var deltaRefs = FindDeltaRefs(decoded);
        int n = wordsToInsert.Length;

        uint[] result = new uint[cmd.Length + n];
        Array.Copy(cmd, 0, result, 0, insertionWordIndex);
        Array.Copy(wordsToInsert, 0, result, insertionWordIndex, n);
        Array.Copy(cmd, insertionWordIndex, result, insertionWordIndex + n, cmd.Length - insertionWordIndex);

        int Shift(int wordIndex) => wordIndex >= insertionWordIndex ? wordIndex + n : wordIndex;

        foreach (var r in deltaRefs)
        {
            int oldDelta = unchecked((int)cmd[r.DeltaWordIndex]);
            int oldTargetWordIndex = r.SelfWordIndex + oldDelta / 4;

            // `SelfWordIndex` is never an independently-shiftable word in its own right - it's always
            // exactly `DeltaWordIndex` (packed single-word instructions, where the arg lives in the
            // opcode's own word) or `DeltaWordIndex - 1` (every unpacked 2-word single-target shape,
            // and every switch-case jmp, whose "self" is defined as one word before the jmp itself).
            // Shifting the two independently is wrong whenever the insertion point lands exactly AT
            // DeltaWordIndex: DeltaWordIndex itself is `>= insertionWordIndex` (shifts), while
            // `DeltaWordIndex - 1` is NOT (stays put) - silently splitting a pair that must always
            // move together, since they don't correspond to two independent stored words, only one
            // (the delta word) truly exists as data; "self" is simply a fixed offset from it. Found
            // via a real, live test (a switch's own default case, inserting immediately before it,
            // drifted to the wrong target only because of this). Deriving self from delta's own
            // shifted position - preserving the original fixed offset - fixes this for every shape
            // uniformly rather than special-casing switch vs. single-target.
            int selfToDeltaOffset = r.DeltaWordIndex - r.SelfWordIndex; // always 0 or 1 in this dialect
            int newDeltaWordIndex = Shift(r.DeltaWordIndex);
            int newSelfWordIndex = newDeltaWordIndex - selfToDeltaOffset;
            int newTargetWordIndex = Shift(oldTargetWordIndex);

            int newDelta = (newTargetWordIndex - newSelfWordIndex) * 4;
            result[newDeltaWordIndex] = unchecked((uint)newDelta);
        }

        if (extraDeltaRefs != null)
        {
            for (int i = 0; i < extraDeltaRefs.Count; i++)
            {
                var r = extraDeltaRefs[i];
                int originalTargetWordIndex = extraOriginalTargets[i];
                int newTargetWordIndex = Shift(originalTargetWordIndex);
                int newDelta = (newTargetWordIndex - r.SelfWordIndex) * 4;
                result[r.DeltaWordIndex] = unchecked((uint)newDelta);
            }
        }

        return result;
    }
}
