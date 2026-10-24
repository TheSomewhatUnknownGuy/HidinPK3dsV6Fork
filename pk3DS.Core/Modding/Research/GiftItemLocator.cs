using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>How much to trust that a located (qty, item id) pair is a real "give item" call rather than a coincidental value match elsewhere in the script.</summary>
public enum GiftLocationConfidence
{
    /// <summary>The only place in the whole script this exact (qty, item id) pair occurs - almost certainly real.</summary>
    High,
    /// <summary>2 or more occurrences, but every one of them calls the SAME subroutine afterward - very often a genuine Ultra Sun/Ultra Moon duplicate of the same real gift (or a real, deliberately repeated one), not noise.</summary>
    Medium,
    /// <summary>Multiple occurrences that call DIFFERENT subroutines afterward, or none at all - the strongest sign this (qty, item id) pair is just a coincidental value match in unrelated code, not a real gift.</summary>
    Low,
}

/// <summary>One place in a decoded xamx script where a gift's quantity/item-id pair might live.</summary>
public sealed class GiftItemLocation
{
    public ScriptArgSlot QtySlot;
    public ScriptArgSlot ItemIdSlot;
    public int InstructionIndex;
    public GiftLocationConfidence Confidence;
    /// <summary>A short window of the surrounding decoded instructions, for a human to eyeball whether this candidate really looks like a gift call (a CallFunc a few lines later) or just an unrelated coincidence.</summary>
    public string Context = "";
    /// <summary>Flat word index of the CallFunc instruction confirmed to follow this pair (the actual "give item" call), or -1 if none was found nearby. The whole span from <see cref="QtySlot"/> through this call, inclusive, is what a real gift's own argument-passing sequence looks like - the unit <see cref="GiftItemInserter"/> clones to add another gift alongside this one.</summary>
    public int CallFlatIndex = -1;
    /// <summary>Word count of the confirmed CallFunc instruction (2 for a plain call) - 0 if <see cref="CallFlatIndex"/> is -1.</summary>
    public int CallWordCount;
    /// <summary>The CallFunc's own absolute target word index (its subroutine's entry point) - shared by every real duplicate of the same gift, and what makes a genuine repeat distinguishable from unrelated coincidental matches.</summary>
    public int CallTargetWordIndex = -1;
}

/// <summary>
/// Finds every place a <see cref="GiftItemEntry"/>'s (item id, quantity) pair could sit in a
/// script's live, decoded instruction stream - never trusts a hardcoded byte offset, since the exact
/// "give item" call shape varies from script to script (each xamx file is its own separately
/// compiled unit; a stricter shape - requiring a specific wrapper of `GetArg`/`PushConst` before the
/// call - was tried and only matched 5 of 255 known real entries, confirming the wrapper genuinely
/// differs per file and can't be used as a filter). The one part of the shape that IS consistent:
/// quantity immediately followed by item id, as two adjacent single-word `PushConst` instructions.
/// <para>
/// Every match is returned, not just a unique one - a common quantity+item combination (a Max
/// Potion's item id turning up as an unrelated loop bound elsewhere, for instance) can coincide with
/// completely unrelated code many times over in a single script, and there's no way to tell those
/// apart from inside this function alone. Real, deliberate example found this session: 075.xamx's
/// "qty=2, item=24" pair recurs ~37 times, each with an incrementing THIRD value pushed just before
/// it and the confirming call alternating between two different targets (0x0460/0x07CC) - the
/// signature of a loop calling some unrelated function with 2 and 24 as fixed, coincidental
/// parameters, not 37 separate Max Potion gifts. A real gift's own confirming call, even when it
/// legitimately repeats (a genuine Ultra Sun/Ultra Moon duplicate), always targets the SAME
/// subroutine every time - so <see cref="GiftLocationConfidence"/> is driven by that (do every
/// candidate's confirming calls agree on one target?), not just the raw match count, and each
/// candidate carries a <see cref="GiftItemLocation.Context"/> snippet so a human can make the final
/// call regardless.
/// </para>
/// </summary>
public static class GiftItemLocator
{
    public static List<GiftItemLocation> Find(List<DecodedInstruction> decoded, uint[] cmd, int itemId, int qty)
    {
        var raw = new List<GiftItemLocation>();
        for (int k = 0; k + 1 < decoded.Count; k++)
        {
            var a = decoded[k];
            var b = decoded[k + 1];
            if (a.Args.Count != 1 || b.Args.Count != 1) continue;
            if (!IsPlainPushConst(a.Display) || !IsPlainPushConst(b.Display)) continue;

            int aVal = ScriptEditing.GetArgValue(cmd, a.Args[0]);
            int bVal = ScriptEditing.GetArgValue(cmd, b.Args[0]);
            if (aVal != qty || bVal != itemId) continue;

            var loc = new GiftItemLocation
            {
                QtySlot = a.Args[0],
                ItemIdSlot = b.Args[0],
                InstructionIndex = k,
                Context = BuildContext(decoded, k),
            };
            FindConfirmingCall(decoded, cmd, k, loc);
            raw.Add(loc);
        }

        // Raw match count is still the primary signal - a small count (2-3) is very often a
        // genuine Ultra Sun/Ultra Moon version-branch duplicate (confirmed live: 103.xamx's real
        // Normalium Z duplicate sits right after a BEQ version check, and its two branches' "give
        // item" calls land at two DIFFERENT targets - so requiring target agreement for a SMALL
        // count would wrongly punish exactly this real, legitimate case). For a LARGE count,
        // though, real gifts don't cluster like that - the confirming call target is the
        // tie-breaker: 075.xamx's real, confirmed noise case (~37 matches for "qty=2, item=24")
        // only agrees on a target 1-in-3 times, the signature of a loop calling different unrelated
        // functions with the same two coincidental constants each time, not 37 separate gifts.
        var realTargetGroups = raw.Where(r => r.CallTargetWordIndex >= 0).GroupBy(r => r.CallTargetWordIndex).ToList();
        int largestRealGroup = realTargetGroups.Count == 0 ? 0 : realTargetGroups.Max(g => g.Count());
        var confidence = raw.Count switch
        {
            <= 1 => GiftLocationConfidence.High,
            <= 3 => GiftLocationConfidence.Medium,
            _ => largestRealGroup == raw.Count ? GiftLocationConfidence.Medium : GiftLocationConfidence.Low,
        };
        foreach (var loc in raw) loc.Confidence = confidence;

        return raw;
    }

    /// <summary>Looks for a CallFunc within a few instructions after the item-id push - the actual "give item" call this (qty, item id) pair is really an argument to, if any.</summary>
    private static void FindConfirmingCall(List<DecodedInstruction> decoded, uint[] cmd, int itemIdInstrIndex, GiftItemLocation loc)
    {
        for (int j = itemIdInstrIndex + 2; j < Math.Min(decoded.Count, itemIdInstrIndex + 2 + 6); j++)
        {
            string disp = decoded[j].Display;
            if (!disp.StartsWith("0x", StringComparison.Ordinal) || !disp.Contains("CallFunc", StringComparison.Ordinal)) continue;

            loc.CallFlatIndex = decoded[j].FlatIndex;
            loc.CallWordCount = decoded[j].WordCount;
            // CallFunc's delta is its own single (unpacked) arg word, self-relative to its own opcode word.
            int delta = ScriptEditing.GetArgValue(cmd, decoded[j].Args[0]);
            loc.CallTargetWordIndex = decoded[j].FlatIndex + delta / 4;
            return;
        }
    }

    private static string BuildContext(List<DecodedInstruction> decoded, int k)
    {
        int lo = Math.Max(0, k - 1);
        int hi = Math.Min(decoded.Count - 1, k + 4);
        var parts = new List<string>();
        for (int j = lo; j <= hi; j++)
            parts.Add(decoded[j].Display.Split('\n')[0].Trim());
        return string.Join("  |  ", parts);
    }

    /// <summary>True for a plain, single-argument `PushConst(N)` line - excludes jump/call-shaped instructions whose displayed number is a target address, not a real value.</summary>
    private static bool IsPlainPushConst(string display) =>
        display.Contains("PushConst(", StringComparison.Ordinal) && !display.Contains("=>", StringComparison.Ordinal);
}
