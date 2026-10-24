using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public enum GiftLocationConfidence
{
    High,
    Medium,
    Low,
}

public sealed class GiftItemLocation
{
    public ScriptArgSlot QtySlot;
    public ScriptArgSlot ItemIdSlot;
    public int InstructionIndex;
    public GiftLocationConfidence Confidence;
    public string Context = "";
    public int CallFlatIndex = -1;
    public int CallWordCount;
    public int CallTargetWordIndex = -1;
}

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

    private static bool IsPlainPushConst(string display) =>
        display.Contains("PushConst(", StringComparison.Ordinal) && !display.Contains("=>", StringComparison.Ordinal);
}
