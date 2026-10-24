using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Adds a new case to an existing multi-case `switch` (AMX opcode 0x82/CASETBL) - the mechanism a
/// per-zone "ZS" script uses to dispatch on <see cref="pk3DS.Core.Structures.FieldNpcEntry.ScriptId"/>
/// (confirmed live this session: Iki Town's real ZS[0] script contains exactly this shape, a switch
/// whose case values are 1-26, matching every non-zero `ScriptId` seen in that zone's real NPC
/// placement data).
/// <para>
/// <see cref="AddAliasCase"/> is the safe, immediately-useful v1 capability: it adds a brand-new case
/// value that dispatches to the SAME target an already-existing case (or the switch's own default)
/// already uses - no new code is written or cloned, so there's nothing new to get wrong about
/// unfamiliar case-block boundaries. This gives a new NPC a real, distinct `ScriptId` that reuses a
/// known-working behavior, the same "reuse a real example" philosophy this session has used
/// throughout (cloning CM model data, trainer collision/path offsets, etc.) - just applied to a
/// dispatch table instead of a data blob. Wiring a BRAND NEW behavior (e.g. a custom item gift) still
/// needs new code spliced in and pointed at from a new case - not built by this class, which only
/// ever aliases to something that already exists and already works.
/// </para>
/// <para>
/// Reuses <see cref="AmxRelocation.InsertWords"/> for the actual splice and delta relocation (both
/// for every OTHER delta in the script that shifts because of this insertion, and for the new case's
/// own jmp via <c>extraDeltaRefs</c>/<c>extraOriginalTargets</c>) rather than re-deriving relocation
/// logic - the CASETBL's own word layout (<c>[opcode][count][jmp0][value0][jmp1][value1]...
/// [default_jmp]</c>, each case's jmp delta relative to ONE WORD BEFORE its own position) is exactly
/// what <see cref="AmxRelocation.FindDeltaRefs"/> already models for this opcode.
/// </para>
/// </summary>
public static class SwitchCaseInserter
{
    public sealed class AddCaseResult
    {
        public bool Success;
        public string Error = "";
        public uint[] NewScriptCommands = [];
        public int WordsAdded;
    }

    /// <summary>Every multi-case `switch` (0x82) instruction's own flat word index in a decoded script - candidates to extend.</summary>
    public static List<int> FindSwitchCaseTables(List<DecodedInstruction> decoded)
    {
        var result = new List<int>();
        foreach (var instr in decoded)
        {
            string firstLine = instr.Display.Split(Environment.NewLine)[0];
            if (!firstLine.Contains("=>", StringComparison.Ordinal) && firstLine.Contains("switch", StringComparison.OrdinalIgnoreCase))
                result.Add(instr.FlatIndex);
        }
        return result;
    }

    /// <summary>One decoded case entry: the value it matches (null for the trailing default) and the real, already-resolved absolute word index its jmp points at.</summary>
    public readonly record struct CaseInfo(int? Value, int TargetWordIndex, int JmpSlotFlatIndex);

    /// <summary>Decodes every case (and the default) of one CASETBL instruction into real target word indices - for the caller to pick which existing target to alias a new case to.</summary>
    public static List<CaseInfo> DecodeCases(uint[] cmd, List<DecodedInstruction> decoded, int caseTblFlatIndex)
    {
        var instr = decoded.First(d => d.FlatIndex == caseTblFlatIndex);
        var cases = new List<CaseInfo>();
        int pairWords = instr.Args.Count - 2; // minus count, minus the trailing default jmp
        int pairs = pairWords / 2;
        for (int k = 0; k < pairs; k++)
        {
            var jmpSlot = instr.Args[1 + 2 * k];
            var valueSlot = instr.Args[2 + 2 * k];
            int jmpDelta = ScriptEditing.GetArgValue(cmd, jmpSlot);
            int value = ScriptEditing.GetArgValue(cmd, valueSlot);
            int selfWordIndex = jmpSlot.FlatIndex - 1;
            int targetWordIndex = selfWordIndex + jmpDelta / 4;
            cases.Add(new CaseInfo(value, targetWordIndex, jmpSlot.FlatIndex));
        }

        var defaultSlot = instr.Args[^1];
        int defaultDelta = ScriptEditing.GetArgValue(cmd, defaultSlot);
        int defaultSelf = defaultSlot.FlatIndex - 1;
        int defaultTarget = defaultSelf + defaultDelta / 4;
        cases.Add(new CaseInfo(null, defaultTarget, defaultSlot.FlatIndex));

        return cases;
    }

    /// <summary>
    /// Adds a new case (<paramref name="newCaseValue"/>) to the CASETBL at
    /// <paramref name="caseTblFlatIndex"/>, aliasing it to the same real target
    /// <paramref name="aliasTargetWordIndex"/> an existing case or the default already uses.
    /// </summary>
    public static AddCaseResult AddAliasCase(uint[] cmd, List<DecodedInstruction> decoded, int caseTblFlatIndex, int newCaseValue, int aliasTargetWordIndex)
    {
        var instr = decoded.FirstOrDefault(d => d.FlatIndex == caseTblFlatIndex);
        if (instr == null)
            return new AddCaseResult { Success = false, Error = $"No instruction found at flat index {caseTblFlatIndex}." };

        string firstLine = instr.Display.Split(Environment.NewLine)[0];
        if (firstLine.Contains("=>", StringComparison.Ordinal) || !firstLine.Contains("switch", StringComparison.OrdinalIgnoreCase))
            return new AddCaseResult { Success = false, Error = $"Instruction at flat index {caseTblFlatIndex} isn't a multi-case switch (CASETBL)." };

        if (instr.Args.Count < 2)
            return new AddCaseResult { Success = false, Error = "Malformed CASETBL - fewer than 2 arg words (count + a default jmp)." };

        // Insert the new (jmp, value) pair right before the default jmp - i.e. after every existing
        // explicit case's own complete (jmp, value) pair. This is the only structurally-safe
        // insertion point for this densely-packed table (anywhere else would land INSIDE some
        // existing case's own pair, splitting its jmp from its value and corrupting how the whole
        // table re-parses) - it necessarily means the default's own jmp shifts, which used to
        // silently break (see AmxRelocation.InsertWords's own fix, applied to address exactly this)
        // because "self" for a switch-case jmp is always exactly `jmpFlatIndex - 1`, a fixed offset
        // from the jmp word rather than an independently-movable word of its own.
        var defaultSlot = instr.Args[^1];
        int insertionWordIndex = defaultSlot.FlatIndex;

        uint[] wordsToInsert = [0, unchecked((uint)newCaseValue)]; // placeholder jmp, real value

        // The new jmp word lands at `insertionWordIndex` in the POST-insertion array (that's exactly
        // where AmxRelocation.InsertWords splices new words in). Its "self" reference (one word
        // before its own position, matching every other case in this same CASETBL) is therefore
        // `insertionWordIndex - 1` - already in post-insertion word-index space since nothing before
        // the insertion point moves.
        var extraDeltaRefs = new List<AmxRelocation.DeltaRef> { new(insertionWordIndex - 1, insertionWordIndex) };
        var extraOriginalTargets = new List<int> { aliasTargetWordIndex };

        uint[] newCmd = AmxRelocation.InsertWords(cmd, decoded, insertionWordIndex, wordsToInsert, extraDeltaRefs, extraOriginalTargets);

        // Bump the CASETBL's own case count (word right after its opcode word) - this word's own
        // position is always BEFORE the insertion point (it's part of the CASETBL's fixed header, and
        // insertion only ever happens after all existing cases), so it never shifts.
        int countWordIndex = caseTblFlatIndex + 1;
        newCmd[countWordIndex] = newCmd[countWordIndex] + 1;

        return new AddCaseResult { Success = true, NewScriptCommands = newCmd, WordsAdded = wordsToInsert.Length };
    }

    public sealed class AddClonedCaseResult
    {
        public bool Success;
        public string Error = "";
        public uint[] NewScriptCommands = [];
        /// <summary>Where the cloned code landed, in the RETURNED array's own word-index space - informational, and useful if the caller wants to inspect/further edit the clone afterward.</summary>
        public int ClonedBodyFlatIndex;
    }

    /// <summary>
    /// Clones an existing case's own self-contained code span (<paramref name="templateBodyStart"/>
    /// to <paramref name="templateBodyEnd"/>, exclusive - e.g. a `Begin(0)...Return(0)` subroutine
    /// body a simple case dispatches to) as brand-new code appended at the very end of the script,
    /// optionally overwriting some of the clone's own argument values first (<paramref name="argOverrides"/>,
    /// keyed by that argument's 0-based position among every arg slot the clone's OWN instructions
    /// expose, in order - matching how <see cref="GiftItemInserter.AddAnotherGift"/> already rewrites
    /// a cloned gift's item id/quantity via <see cref="ScriptEditing.SetArgValue"/>, just generalized
    /// to any argument position rather than assuming a fixed (qty, itemId) shape), then adds a new
    /// case to the CASETBL at <paramref name="caseTblFlatIndex"/> pointing directly at the clone.
    /// <para>
    /// Appending at the very end of the whole script is deliberately the simplest possible insertion
    /// point - nothing in the script lives after it, so this insertion alone can never split any
    /// existing self/delta pair the way inserting into the CASETBL's own case list can (see
    /// <see cref="AddAliasCase"/>'s remarks and <see cref="AmxRelocation.InsertWords"/>'s own fix for
    /// that specific, different edge case) - the CASETBL extension this method ALSO performs (via
    /// <see cref="AddAliasCase"/>, once the clone's final position is known) is exactly as safe as
    /// any other alias, since by then the clone is just another already-existing target.
    /// </para>
    /// <para>
    /// Any delta-bearing instruction WITHIN the cloned span (e.g. a `CallFunc` to a shared helper
    /// subroutine elsewhere in the script, confirmed live to be how real "give a reward" cases like
    /// Iki Town's case 6/7/8 call their own shared `0x1768` helper) is re-targeted correctly regardless
    /// of whether its real target sits inside the clone or elsewhere in the original script - computed
    /// from the template's own ORIGINAL absolute position, then run through the same
    /// <see cref="AmxRelocation.InsertWords"/> shift logic as everything else, so a cloned CallFunc
    /// keeps calling the SAME real shared helper the template did. A nested multi-case `switch`
    /// WITHIN the cloned span is not supported (real case bodies this session has found are simple,
    /// linear code - Begin/PushConst/CallFunc/ClearAll/Return - never another switch of their own).
    /// </para>
    /// </summary>
    public static AddClonedCaseResult AddClonedCase(
        uint[] cmd, List<DecodedInstruction> decoded, int caseTblFlatIndex, int newCaseValue,
        int templateBodyStart, int templateBodyEnd,
        IReadOnlyDictionary<int, int> argOverrides = null)
    {
        int len = templateBodyEnd - templateBodyStart;
        if (len <= 0)
            return new AddClonedCaseResult { Success = false, Error = "Invalid template body span (end must be after start)." };

        uint[] clonedWords = new uint[len];
        Array.Copy(cmd, templateBodyStart, clonedWords, 0, len);

        // Decode the clone IN ISOLATION (as its own tiny self-contained script) both to find any
        // delta-bearing instructions inside it, and to enumerate every argument slot in order for
        // argOverrides to index into.
        var clonedDecoded = ScriptEditing.Decode(clonedWords);

        if (argOverrides != null)
        {
            var allArgSlots = clonedDecoded.SelectMany(d => d.Args).ToList();
            foreach (var (argIndex, newValue) in argOverrides)
            {
                if (argIndex < 0 || argIndex >= allArgSlots.Count)
                    return new AddClonedCaseResult { Success = false, Error = $"argOverrides key {argIndex} is out of range (this template has {allArgSlots.Count} argument slots)." };
                ScriptEditing.SetArgValue(clonedWords, allArgSlots[argIndex], newValue);
            }
            // Re-decode after edits - SetArgValue never changes word COUNT, only values, so this is
            // just to make sure any later delta-scan below sees the final, overridden bytes (matters
            // only if an override ever targeted a delta-bearing word itself, which callers shouldn't
            // do, but re-decoding keeps this method correct even then rather than assuming).
            clonedDecoded = ScriptEditing.Decode(clonedWords);
        }

        int insertPos = cmd.Length; // append at the very end - see class remarks for why this is safe

        var extraDeltaRefs = new List<AmxRelocation.DeltaRef>();
        var extraOriginalTargets = new List<int>();
        foreach (var instr in clonedDecoded)
        {
            string disp = instr.Display;
            if (!disp.Contains("=>", StringComparison.Ordinal)) continue;
            string firstLine = disp.Split(Environment.NewLine)[0];
            if (!firstLine.Contains("=>", StringComparison.Ordinal))
                return new AddClonedCaseResult { Success = false, Error = "Template body contains a nested multi-case switch - not supported by this method." };
            if (instr.Args.Count == 0) continue;

            var argSlot = instr.Args[0];
            int delta = ScriptEditing.GetArgValue(clonedWords, argSlot);

            // Real position of this instruction/arg BEFORE cloning (in the ORIGINAL script) - the
            // delta is relative to the opcode word's own position for every single-target shape here
            // (CallFunc, Jump, etc. - a multi-case switch inside the clone is already rejected above).
            int originalSelf = templateBodyStart + instr.FlatIndex;
            int originalTarget = originalSelf + delta / 4;

            int newSelf = insertPos + instr.FlatIndex;
            int newDeltaWordIndex = insertPos + argSlot.FlatIndex;
            extraDeltaRefs.Add(new AmxRelocation.DeltaRef(newSelf, newDeltaWordIndex));
            extraOriginalTargets.Add(originalTarget);
        }

        uint[] afterClone = AmxRelocation.InsertWords(cmd, decoded, insertPos, clonedWords, extraDeltaRefs, extraOriginalTargets);

        var decodedAfterClone = ScriptEditing.Decode(afterClone);
        var addResult = AddAliasCase(afterClone, decodedAfterClone, caseTblFlatIndex, newCaseValue, insertPos);
        if (!addResult.Success)
            return new AddClonedCaseResult { Success = false, Error = addResult.Error };

        return new AddClonedCaseResult { Success = true, NewScriptCommands = addResult.NewScriptCommands, ClonedBodyFlatIndex = insertPos };
    }

    /// <summary>
    /// One numbered case whose own dispatched body looks like a "give a reward" pattern: a short,
    /// self-contained `Begin...ClearAll/Return` block with 2+ `PushConst` arguments feeding exactly
    /// one `CallFunc` to some shared helper subroutine - confirmed live this session for Iki Town's
    /// real case 6/7/8 (each an 11-word block differing in exactly one pushed value, all three
    /// calling the SAME shared helper). <see cref="SharedHelperTarget"/> being used by 2+ different
    /// numbered cases is the actual signal this is a reusable "reward" mechanism rather than one-off
    /// zone-specific code - a single isolated match isn't enough to tell the two apart, so
    /// <see cref="FindRewardLikeCases"/> only returns cases that clear that bar.
    /// <para>
    /// Read plainly: this is a structural pattern match (several near-identical cases differing in
    /// exactly one argument, converging on one shared helper), not a confirmed understanding of what
    /// that helper does at the ARM level - it's presented to the user as "reward-shaped", not
    /// asserted to definitely be a Bag item id.
    /// </para>
    /// <para>
    /// <see cref="VaryingArgIndex"/> and <see cref="AllArgValues"/> are indexed the SAME way
    /// <see cref="AddClonedCase"/>'s own <c>argOverrides</c> parameter expects - every argument slot
    /// in body order (including `Begin`/`ClearAll`/`Return`'s own packed zero args, not just the
    /// `PushConst`s) - so a caller can pass <see cref="VaryingArgIndex"/> straight through as an
    /// `argOverrides` key without any re-indexing.
    /// </para>
    /// </summary>
    public sealed record RewardLikeCase(int CaseValue, int BodyStart, int BodyEnd, int SharedHelperTarget, List<int> AllArgValues, int VaryingArgIndex);

    /// <summary>Finds every numbered case in the CASETBL at <paramref name="caseTblFlatIndex"/> that looks reward-shaped, per <see cref="RewardLikeCase"/>'s own remarks.</summary>
    public static List<RewardLikeCase> FindRewardLikeCases(uint[] cmd, List<DecodedInstruction> decoded, int caseTblFlatIndex)
    {
        var cases = DecodeCases(cmd, decoded, caseTblFlatIndex);
        var raw = new List<(int Value, int Start, int End, int HelperTarget, List<int> AllArgs)>();

        foreach (var c in cases.Where(c => c.Value.HasValue))
        {
            // Every real case checked this session dispatches through a tiny, uniform outer stub
            // first (`PushConst(0), CallFunc(subroutine), Jump(cleanup)`, confirmed live for Iki
            // Town's cases 1/2/6/7/8 alike) - the actual per-NPC content (including any reward
            // pattern) lives in the SUBROUTINE that stub calls, not in the switch's own recorded
            // target directly. Follow that one CallFunc first.
            var stubWindow = decoded.Where(d => d.FlatIndex >= c.TargetWordIndex && d.FlatIndex < c.TargetWordIndex + 6)
                .OrderBy(d => d.FlatIndex).ToList();
            var stubCall = stubWindow.FirstOrDefault(d => d.Display.Contains("CallFunc", StringComparison.Ordinal));
            if (stubCall == null || stubCall.Args.Count == 0) continue;
            int stubDelta = ScriptEditing.GetArgValue(cmd, stubCall.Args[0]);
            int subroutineStart = stubCall.FlatIndex + stubDelta / 4;

            var body = decoded.Where(d => d.FlatIndex >= subroutineStart && d.FlatIndex < subroutineStart + 30)
                .OrderBy(d => d.FlatIndex).ToList();
            int returnIdx = body.FindIndex(d => d.Display.Contains("Return", StringComparison.Ordinal));
            if (returnIdx < 0) continue;
            var relevant = body.Take(returnIdx + 1).ToList();

            var pushConstCount = relevant.Count(d => d.Display.Contains("PushConst", StringComparison.Ordinal));
            var callFuncs = relevant.Where(d => d.Display.Contains("CallFunc", StringComparison.Ordinal)).ToList();
            if (pushConstCount < 2 || callFuncs.Count != 1) continue;

            var call = callFuncs[0];
            if (call.Args.Count == 0) continue;
            int delta = ScriptEditing.GetArgValue(cmd, call.Args[0]);
            int helperTarget = call.FlatIndex + delta / 4;
            int bodyEnd = relevant[^1].FlatIndex + relevant[^1].WordCount;
            // ALL args in body order (matching AddClonedCase's own indexing exactly), not just the
            // PushConst subset - so VaryingArgIndex can be handed straight to argOverrides.
            var allArgValues = relevant.SelectMany(d => d.Args).Select(a => ScriptEditing.GetArgValue(cmd, a)).ToList();

            raw.Add((c.Value!.Value, subroutineStart, bodyEnd, helperTarget, allArgValues));
        }

        // Only keep helper targets shared by 2+ different cases - the convergence signal that this
        // is a reusable mechanism, not one-off code that merely happens to match the shape once.
        var sharedTargets = raw.GroupBy(r => r.HelperTarget).Where(g => g.Count() >= 2).Select(g => g.Key).ToHashSet();
        var kept = raw.Where(r => sharedTargets.Contains(r.HelperTarget)).ToList();

        // Within each shared-helper group, find which argument POSITION actually varies across the
        // group's members (the customizable "reward value" slot) - the one whose value differs from
        // at least one other member sharing the same helper and the same argument count.
        var result = new List<RewardLikeCase>();
        foreach (var group in kept.GroupBy(r => r.HelperTarget))
        {
            var members = group.ToList();
            int argCount = members[0].AllArgs.Count;
            if (members.Any(m => m.AllArgs.Count != argCount)) continue; // shape must match exactly to compare positionally

            int varyingIndex = -1;
            for (int i = 0; i < argCount; i++)
            {
                var distinctValuesAtI = members.Select(m => m.AllArgs[i]).Distinct().Count();
                if (distinctValuesAtI > 1) { varyingIndex = i; break; }
            }
            if (varyingIndex < 0) continue; // every arg identical across the group - nothing to customize

            foreach (var m in members)
                result.Add(new RewardLikeCase(m.Value, m.Start, m.End, m.HelperTarget, m.AllArgs, varyingIndex));
        }

        return result;
    }
}
