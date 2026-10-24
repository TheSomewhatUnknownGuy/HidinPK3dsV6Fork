using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public static class AmxRelocation
{
    public readonly record struct DeltaRef(int SelfWordIndex, int DeltaWordIndex);

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
