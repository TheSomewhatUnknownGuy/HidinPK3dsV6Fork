using System;
using System.Collections.Generic;

namespace pk3DS.Core.Modding.Research;

public sealed class GiftItemInsertResult
{
    public bool Success;
    public string Error = "";
    public uint[] NewScriptCommands = [];
    public int WordsAdded;
}

public static class GiftItemInserter
{
    public static GiftItemInsertResult AddAnotherGift(uint[] cmd, List<DecodedInstruction> decoded, GiftItemLocation template, int newQty, int newItemId)
    {
        var r = new GiftItemInsertResult();
        if (template.CallFlatIndex < 0)
        {
            r.Error = "This gift has no confirmed call to clone - pick a candidate whose context actually shows a CallFunc, or add the new instructions manually via the AMX Scripts editor.";
            return r;
        }

        int spanStart = template.QtySlot.FlatIndex;
        int spanEndExclusive = template.CallFlatIndex + template.CallWordCount;
        int spanLength = spanEndExclusive - spanStart;
        if (spanLength <= 0 || spanStart < 0 || spanEndExclusive > cmd.Length)
        {
            r.Error = "This gift's instruction span looks invalid - refusing to clone it.";
            return r;
        }

        uint[] clone = new uint[spanLength];
        Array.Copy(cmd, spanStart, clone, 0, spanLength);

        // A qty/item-id slot can be PACKED - its value living in the upper 16 bits of a single word
        // that ALSO holds the opcode in its lower bits (ScriptEditing.Decode's "wordCount == 1" case)
        // - so this has to go through SetArgValue like any other edit, not a raw word overwrite: doing
        // that once corrupted the clone's own opcode byte, which cascaded into every instruction after
        // it decoding as garbage (confirmed live - caught before this ever reached real ROM data).
        int qtyRelative = template.QtySlot.FlatIndex - spanStart;
        int itemIdRelative = template.ItemIdSlot.FlatIndex - spanStart;
        var qtyCloneSlot = new ScriptArgSlot { FlatIndex = qtyRelative, Packed = template.QtySlot.Packed };
        var itemIdCloneSlot = new ScriptArgSlot { FlatIndex = itemIdRelative, Packed = template.ItemIdSlot.Packed };
        ScriptEditing.SetArgValue(clone, qtyCloneSlot, newQty);
        ScriptEditing.SetArgValue(clone, itemIdCloneSlot, newItemId);

        int insertionWordIndex = spanEndExclusive;

        // The clone's own CallFunc delta word needs recomputing for its new position - its target
        // subroutine (template.CallTargetWordIndex) hasn't moved from where it was in the ORIGINAL
        // array, but may itself shift if it sits at or after the insertion point, exactly like every
        // other pre-existing delta AmxRelocation.InsertWords already fixes up.
        int callDeltaRelative = (template.CallFlatIndex + 1) - spanStart; // CallFunc is 2 words: opcode, then its one delta arg
        int cloneCallSelfWordIndex = insertionWordIndex + (template.CallFlatIndex - spanStart);
        int cloneCallDeltaWordIndex = insertionWordIndex + callDeltaRelative;

        var extraRefs = new List<AmxRelocation.DeltaRef> { new(cloneCallSelfWordIndex, cloneCallDeltaWordIndex) };
        var extraTargets = new List<int> { template.CallTargetWordIndex };

        r.NewScriptCommands = AmxRelocation.InsertWords(cmd, decoded, insertionWordIndex, clone, extraRefs, extraTargets);
        r.WordsAdded = clone.Length;
        r.Success = true;
        return r;
    }
}
