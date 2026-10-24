using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public static class SwitchCaseInserter
{
    public sealed class AddCaseResult
    {
        public bool Success;
        public string Error = "";
        public uint[] NewScriptCommands = [];
        public int WordsAdded;
    }

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

    public readonly record struct CaseInfo(int? Value, int TargetWordIndex, int JmpSlotFlatIndex);

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
        public int ClonedBodyFlatIndex;
    }

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

    public sealed record RewardLikeCase(int CaseValue, int BodyStart, int BodyEnd, int SharedHelperTarget, List<int> AllArgValues, int VaryingArgIndex);

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
