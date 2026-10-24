#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public static class RecipeConflicts
{
    private sealed record Conflict(string[] GroupA, string[] GroupB, string Reason);

    private static readonly Conflict[] Known =
    [
        new(
            GroupA: ["PCAnyWhere(overwrite BattleVideo)", "PCAnyWhere(overwrite OPTION)", "PCAnyWhere(overwrite QR)"],
            GroupB: ["Move Relearner Replaces Cafe"],
            Reason: "PC Anywhere's injected trampoline (code.bin ~0x4B9EE0) calls the shared utility " +
                    "function at 0x0029A044 - confirmed to have 15+ independent call sites elsewhere " +
                    "in code.bin, not just the Cafe menu - expecting it untouched. Move Relearner " +
                    "Replaces Cafe overwrites that function's own second instruction (its stack-frame " +
                    "prologue, originally 'SUB R13,R13,#0xC') with a branch into its own logic, so " +
                    "EVERY caller of that shared function, PC Anywhere's trampoline included, gets " +
                    "diverted through Move Relearner's code instead and never gets its expected stack " +
                    "setup - corrupting register/stack state for whatever menu that caller belongs to " +
                    "(observed: the Bag) and crashing the next time that menu is opened."),
    ];

    private sealed record Unsafe(string[] Names, string Reason);

    private static readonly Unsafe[] AlwaysUnsafe =
    [
        new(
            Names: ["PCAnyWhere(overwrite BattleVideo)", "PCAnyWhere(overwrite OPTION)", "PCAnyWhere(overwrite QR)"],
            Reason: "PC Anywhere's own trampoline (code.bin 0x4B9EE0-0x4B9F68) is not written into " +
                    "free space - it overwrites a real pristine function (a 7-entry table-search " +
                    "routine at 0x4B9F1C-0x4B9F84, plus its lookup table at 0x4B9EE0-0x4B9F18) that a " +
                    "separate, unrelated function tail-branches into as its own cold path (a plain 'B' " +
                    "from code.bin 0x22360C, not a call). Installing PC Anywhere overwrites that cold " +
                    "path with its own code, so whenever the unrelated caller reaches that branch it " +
                    "runs PC Anywhere's logic with the wrong register/stack state instead - this " +
                    "happens regardless of any other recipe being installed."),
        new(
            Names: ["Move Relearner Replaces Cafe"],
            Reason: "Move Relearner Replaces Cafe's second trampoline (code.bin 0x4B9F8C-0x4BA000) is " +
                    "also not written into free space - it overwrites two real pristine cold-path " +
                    "function fragments: 0x4B9FB4-0x4B9FE0 (reached by a tail branch from 0x2259A4) " +
                    "and 0x4B9FE8-0x4B9FFC (reached from 0x347C7C). The second fragment's containing " +
                    "function (0x347B84-0x347DA8) is called 13 times from a tight code cluster at " +
                    "0x3478BC-0x347A5C, directly adjacent to code.bin's confirmed evolution-forme " +
                    "handling code ('Mega Evolution keeps its forme' hooks 0x344C7F in the same " +
                    "region) - consistent with this being the evolution sequence's shared message/" +
                    "window-draw routine. This corruption happens with Move Relearner Replaces Cafe " +
                    "installed alone, regardless of any other recipe."),
    ];

    public static bool TryFindUnsafe(string candidateName, out string reason)
    {
        foreach (var u in AlwaysUnsafe)
        {
            if (u.Names.Any(n => string.Equals(n, candidateName, StringComparison.OrdinalIgnoreCase)))
            {
                reason = u.Reason;
                return true;
            }
        }
        reason = "";
        return false;
    }

    public static bool TryFindConflict(string candidateName, IEnumerable<string> installedNames, out string reason)
    {
        foreach (var c in Known)
        {
            bool candidateInA = c.GroupA.Any(n => string.Equals(n, candidateName, StringComparison.OrdinalIgnoreCase));
            bool candidateInB = c.GroupB.Any(n => string.Equals(n, candidateName, StringComparison.OrdinalIgnoreCase));
            if (!candidateInA && !candidateInB) continue;

            string[] otherGroup = candidateInA ? c.GroupB : c.GroupA;
            foreach (string installed in installedNames)
            {
                if (otherGroup.Any(n => string.Equals(n, installed, StringComparison.OrdinalIgnoreCase)))
                {
                    reason = $"conflicts with already-installed '{installed}' - {c.Reason}";
                    return true;
                }
            }
        }
        reason = "";
        return false;
    }

    public static bool TryFindConflict(string candidateName, IEnumerable<string> installedNames, byte[]? codeBin, out string reason)
    {
        if (TryFindConflict(candidateName, installedNames, out reason)) return true;
        if (codeBin == null) return false;

        bool candidateIsPcAnywhere = candidateName.StartsWith("PCAnyWhere(", StringComparison.OrdinalIgnoreCase);
        bool candidateIsMoveRelearner = string.Equals(candidateName, "Move Relearner Replaces Cafe", StringComparison.OrdinalIgnoreCase);
        if (!candidateIsPcAnywhere && !candidateIsMoveRelearner) return false;

        var pair = Known[0]; // the only entry with a byte signature today

        // PC Anywhere's hook site 0x27CB10: pristine word is 0xEB0006CB (BL 0x0027E644).
        bool pcAnywherePresent = codeBin.Length >= 0x27CB14 &&
            BitConverter.ToUInt32(codeBin, 0x27CB10) != 0xEB0006CB;
        // Move Relearner Replaces Cafe's hook into the shared function's prologue at 0x29A048:
        // pristine word is 0xE24DD00C (SUB R13,R13,#0xC).
        bool moveRelearnerPresent = codeBin.Length >= 0x29A050 &&
            BitConverter.ToUInt32(codeBin, 0x29A048) != 0xE24DD00C;

        if (candidateIsPcAnywhere && moveRelearnerPresent)
        {
            reason = $"conflicts with already-installed 'Move Relearner Replaces Cafe' - {pair.Reason}";
            return true;
        }
        if (candidateIsMoveRelearner && pcAnywherePresent)
        {
            reason = "conflicts with an already-installed PC Anywhere variant - " + pair.Reason;
            return true;
        }
        return false;
    }
}
