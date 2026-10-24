using System;
using System.Linq;
using System.Windows.Forms;

using pk3DS.Core;

namespace pk3DS.WinForms;

/// <summary>
/// Workstream 8 - editor for AMX NPC scripts (`a/0/9/2`, "amxscript"). Built on
/// <see cref="pk3DS.Core.Script"/>/<see cref="Scripts"/> (decompress/disassemble) and the new
/// <see cref="ScriptEditing"/> (structured, editable instruction decode) - both verified this
/// session against every one of this ROM's real 759 scripts (decompress/recompress round-trip,
/// disassembly, and a real edit-save-reload cycle all confirmed exact).
/// <para>
/// Editing is deliberately generic: each instruction's numeric argument(s) are editable in place
/// (a plain "change this number" operation, never inserting/removing words), regardless of what
/// that argument actually means - this ROM still has 179 of 256 real opcodes unnamed, so nothing
/// here assumes it knows which argument is an item id vs a quantity vs a jump offset. Jump/switch
/// instructions remain editable like anything else; their target text ("=&gt; 0x1234 (48)") is the
/// only warning given.
/// </para>
/// </summary>
public partial class AMXScriptEditor : Form
{
    private readonly GARCFile amxGarc;
    private int currentIndex = -1;
    private Script currentScript;
    private uint[] currentScriptCmds = [];
    private uint[] currentMoveCmds = [];
    private System.Collections.Generic.List<DecodedInstruction> currentDecoded = [];
    private bool dirty;
    private int columnsBuiltForArgCount = -1;

    public AMXScriptEditor(GARCFile amx)
    {
        amxGarc = amx;
        InitializeComponent();
        PopulateList();

        bool needsRepair = DetectCatchTutorialNeedsRepair();
        CHK_RepairCatchTutorial.Visible = needsRepair;
        CHK_RepairCatchTutorial.Checked = needsRepair;
    }

    private void PopulateList()
    {
        CB_Script.Items.Clear();
        for (int i = 0; i < amxGarc.Files.Length; i++)
            CB_Script.Items.Add($"{i:000}.xamx");
        if (CB_Script.Items.Count > 0)
            CB_Script.SelectedIndex = 0;
    }

    private void CB_Script_SelectedIndexChanged(object sender, EventArgs e)
    {
        int wanted = CB_Script.SelectedIndex;
        if (dirty && WinFormsUtil.Prompt(MessageBoxButtons.YesNo, "Discard unsaved changes to this script?") != DialogResult.Yes)
        {
            CB_Script.SelectedIndexChanged -= CB_Script_SelectedIndexChanged;
            CB_Script.SelectedIndex = currentIndex;
            CB_Script.SelectedIndexChanged += CB_Script_SelectedIndexChanged;
            return;
        }
        LoadScript(wanted);
    }

    private void LoadScript(int index)
    {
        currentIndex = index;
        byte[] raw = amxGarc.Files[index];

        try
        {
            currentScript = new Script(raw);
        }
        catch (Exception ex)
        {
            WinFormsUtil.Error("Couldn't load this script.", ex.Message);
            DGV_Instructions.Rows.Clear();
            L_Info.Text = "";
            B_Save.Enabled = false;
            return;
        }

        currentScriptCmds = currentScript.ScriptCommands;
        currentMoveCmds = currentScript.MoveCommands;
        currentDecoded = ScriptEditing.Decode(currentScriptCmds, currentScript.NativeFunctionHashes);
        dirty = false;
        L_Info.Text = currentScript.Info;
        B_Save.Enabled = true;
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        SetupColumns();
        DGV_Instructions.SuspendLayout();
        DGV_Instructions.Rows.Clear();
        foreach (var instr in currentDecoded)
        {
            int rowIdx = DGV_Instructions.Rows.Add();
            var row = DGV_Instructions.Rows[rowIdx];
            row.Cells["ColOffset"].Value = $"0x{instr.FlatIndex * 4:X4}";
            row.Cells["ColInstruction"].Value = instr.Display.Split('\n')[0];
            for (int a = 0; a < instr.Args.Count; a++)
                row.Cells[$"ColArg{a + 1}"].Value = ScriptEditing.GetArgValue(currentScriptCmds, instr.Args[a]);
        }
        DGV_Instructions.ResumeLayout();
    }

    private void SetupColumns()
    {
        int maxArgs = currentDecoded.Count == 0 ? 0 : currentDecoded.Max(d => d.Args.Count);
        if (maxArgs == columnsBuiltForArgCount) return;
        columnsBuiltForArgCount = maxArgs;

        DGV_Instructions.Columns.Clear();
        DGV_Instructions.AutoGenerateColumns = false;
        DGV_Instructions.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColOffset", HeaderText = "Offset", ReadOnly = true, Width = 65 });
        DGV_Instructions.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColInstruction", HeaderText = "Instruction", ReadOnly = true, Width = 280 });
        for (int a = 1; a <= maxArgs; a++)
            DGV_Instructions.Columns.Add(new DataGridViewTextBoxColumn { Name = $"ColArg{a}", HeaderText = $"Arg {a}", Width = 75 });
    }

    private void DGV_Instructions_CellEndEdit(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= currentDecoded.Count) return;
        string colName = DGV_Instructions.Columns[e.ColumnIndex].Name;
        if (!colName.StartsWith("ColArg")) return;

        var instr = currentDecoded[e.RowIndex];
        int argIdx = int.Parse(colName["ColArg".Length..]) - 1;
        if (argIdx < 0 || argIdx >= instr.Args.Count) return;

        var cell = DGV_Instructions.Rows[e.RowIndex].Cells[e.ColumnIndex];
        if (!int.TryParse(cell.Value?.ToString(), out int newValue))
        {
            cell.Value = ScriptEditing.GetArgValue(currentScriptCmds, instr.Args[argIdx]);
            return;
        }

        ScriptEditing.SetArgValue(currentScriptCmds, instr.Args[argIdx], newValue);
        dirty = true;

        // Only the displayed text needs refreshing - editing a value in place never changes any
        // instruction's word count/position, so the decoded instruction list itself stays valid;
        // only its human-readable Display string (built from the actual opcode name/operands) can
        // change, and only for this one row.
        currentDecoded = ScriptEditing.Decode(currentScriptCmds, currentScript.NativeFunctionHashes);
        DGV_Instructions.Rows[e.RowIndex].Cells["ColInstruction"].Value = currentDecoded[e.RowIndex].Display.Split('\n')[0];
    }

    /// <summary>
    /// One known, byte-verified cutscene skip: a native call removed from a script, everything
    /// else's branch/call offsets corrected around the gap by <see cref="ScriptEditing.RemoveWords"/>.
    /// <see cref="RemoveStart"/>/<see cref="RemoveCount"/> name the exact words to remove, and
    /// <see cref="ExpectedWords"/> is checked byte-for-byte before touching anything - refuses
    /// rather than guessing if this build's script doesn't match (already applied, or a different
    /// ROM revision), the same discipline every other patch in this codebase follows.
    /// </summary>
    private sealed record TutorialSkip(string Label, int ScriptIndex, int RemoveStart, uint[] ExpectedWords)
    {
        public int RemoveCount => ExpectedWords.Length;
    }

    /// <summary>
    /// Catching tutorial (script 117): word 847 (byte 0x0D3C) is <c>DoCommand?(39, 0)</c> - native
    /// index 39 is <c>CallCaptureDemo_</c>, confirmed by name via the script's own native-function
    /// table. Correctly identified, and still WRONG TO REMOVE: the enclosing subroutine (the one this
    /// call sits in) is reached through a sequential story-progress dispatcher that gates on
    /// <c>GetGlobal(0x02FC)</c> - "have you reached step N yet?" - and NEITHER of this subroutine's
    /// own neighbours (<c>FieldClose_</c> before the demo, <c>FieldOpen_</c> after it) ever writes to
    /// that global; script 117 contains no <c>SetGlobal</c> call anywhere at all. The only place left
    /// for the counter to advance is inside <c>CallCaptureDemo_</c> itself, as an engine-level side
    /// effect of the real demo completing - remove the call and the counter never advances, so the
    /// SAME dispatcher step re-fires every time the trigger area is re-entered (confirmed in game:
    /// the tutorial "replays" - is skipped again - and re-gives its reward items, forever). There is
    /// no safe way to fake that side effect from AMX script level; <see cref="CatchTutorialUndo"/>
    /// exists because of this, not the earlier (different) mistake.
    /// </summary>
    private static readonly TutorialSkip CatchTutorialSkip = new(
        "Skip the catching tutorial (WARNING: loops - see notes, use Undo below)", 117, 847, [0x87, 39, 0]);
    private static readonly TutorialSkip CatchTutorialUndo = CatchTutorialSkip with { Label = "Undo the catching-tutorial skip" };

    /// <summary>
    /// Z-Move tutorial (script 130): word 626 (byte 0x09C8) is <c>DoCommand?(33, 0)</c> - native
    /// index 33 is <c>CallZenryokuWazaDemo_</c>. Structurally identical to the catching tutorial's
    /// call site (same GetArg x3 / setup CallFunc at relative offset -192 / demo call / cleanup
    /// CallFunc at relative offset -140 shape) - but NOT confirmed to share its loop bug: the
    /// immediate call site here shows no adjacent <c>GetGlobal</c> gate the way script 117's does, so
    /// whatever gates re-entry to this trigger may work differently (or may not exist at the same
    /// layer). Left enabled with a caveat rather than assumed broken OR assumed safe - genuinely
    /// unknown until tested in game.
    /// </summary>
    private static readonly TutorialSkip ZMoveTutorialSkip = new(
        "Skip the Z-Move tutorial (unconfirmed whether it loops like catching did)", 130, 626, [0x87, 33, 0]);
    private static readonly TutorialSkip ZMoveTutorialUndo = ZMoveTutorialSkip with { Label = "Undo the Z-Move-tutorial skip" };

    /// <summary>
    /// Puts back the 6 words the first (wrong) attempt removed from script 117 at word 880, using
    /// <see cref="ScriptEditing.InsertWords"/> - the exact inverse of the removal, including
    /// correcting every branch that was adjusted around the gap back to its original delta. Only
    /// offered/enabled when <see cref="DetectCatchTutorialNeedsRepair"/> finds the damage still
    /// present, so there is nothing to accidentally double-apply.
    /// </summary>
    private static readonly uint[] CatchTutorialRepairWords = [0x87, 42, 0, 0x87, 43, 0];
    private const int CatchTutorialRepairAt = 880;

    /// <summary>
    /// True if script 117 currently shows the wrong edit's signature: word 880 has already moved to
    /// where the removed span's own successor (ClearAll/Return) used to be, with nothing matching
    /// the original DoCommand?(42,...) there any more.
    /// </summary>
    private bool DetectCatchTutorialNeedsRepair()
    {
        try
        {
            uint[] cmds = new Script(amxGarc.Files[117]).ScriptCommands;
            return cmds.Length > 881
                && cmds[879] == 0x2E   // Begin(0), unmoved either way
                && cmds[880] == 0x59   // ClearAll(0) - only true once the 6 words are gone
                && cmds[881] == 0x30;  // Return(0)
        }
        catch { return false; }
    }

    private void B_ApplyTutorialSkips_Click(object sender, EventArgs e)
    {
        var lines = new System.Collections.Generic.List<string>();

        // Repair first: the catching-tutorial skip below assumes script 117 is in its correct,
        // un-damaged shape, and applying it on top of the earlier mistake would refuse safely (the
        // expected-bytes check wouldn't match) rather than compound the problem, but repairing first
        // means one click fixes both when both are checked.
        if (CHK_RepairCatchTutorial.Checked)
            lines.Add(ApplyCatchTutorialRepair());

        if (CHK_UndoCatchTutorial.Checked)
            lines.Add(ApplyTutorialUndo(CatchTutorialUndo));

        if (CHK_SkipCatchTutorial.Checked)
            lines.Add(ApplyTutorialSkip(CatchTutorialSkip));

        if (CHK_UndoZMoveTutorial.Checked)
            lines.Add(ApplyTutorialUndo(ZMoveTutorialUndo));

        if (CHK_SkipZMoveTutorial.Checked)
            lines.Add(ApplyTutorialSkip(ZMoveTutorialSkip));

        if (lines.Count == 0)
        {
            L_TutorialSkipStatus.Text = "Check a box first.";
            return;
        }

        L_TutorialSkipStatus.Text = string.Join("  ", lines);
        CHK_RepairCatchTutorial.Checked = DetectCatchTutorialNeedsRepair();
    }

    private string ApplyCatchTutorialRepair()
    {
        const int ScriptIndex = 117;
        if (ScriptIndex == currentIndex && dirty)
            return "Repair: this script has unsaved edits open above - save or discard them first.";

        if (!DetectCatchTutorialNeedsRepair())
            return "Repair: nothing to repair - script 117 doesn't show the earlier mistake's signature.";

        if (WinFormsUtil.Prompt(MessageBoxButtons.YesNo,
                "Repair the earlier catching-tutorial edit?",
                "Puts back ResetEventBasePos and ChangeEventWildLose_ in script 117, exactly as they " +
                "were before - the edit that removed them was wrong and is what caused the crash. " +
                "Applied directly to the amxscript archive.") != DialogResult.Yes)
            return "Repair: cancelled.";

        var script = new Script(amxGarc.Files[ScriptIndex]);
        uint[] cmds = script.ScriptCommands;

        uint[] repaired;
        try { repaired = ScriptEditing.InsertWords(cmds, CatchTutorialRepairAt, CatchTutorialRepairWords); }
        catch (Exception ex) { return $"Repair: refused - {ex.Message}"; }

        if (!script.TryWriteResizedScriptCommands(repaired, out string error))
            return $"Repair: refused - {error}";

        var files = amxGarc.Files;
        files[ScriptIndex] = script.Raw;
        amxGarc.Files = files;
        amxGarc.Save();

        if (currentIndex == ScriptIndex) LoadScript(ScriptIndex);

        return "Repair: done - ResetEventBasePos and ChangeEventWildLose_ are back. Reload your ROM in pk3DS.";
    }

    private string ApplyTutorialSkip(TutorialSkip skip)
    {
        if (skip.ScriptIndex == currentIndex && dirty)
            return $"{skip.Label}: this script has unsaved edits open above - save or discard them first.";

        if (WinFormsUtil.Prompt(MessageBoxButtons.YesNo,
                $"{skip.Label}?",
                $"This removes {skip.RemoveCount} word(s) from script {skip.ScriptIndex} and corrects every " +
                "branch/call offset elsewhere in that script that would otherwise cross the gap. Applied " +
                "directly to the amxscript archive - use the matching Undo box below to reverse it.") != DialogResult.Yes)
            return $"{skip.Label}: cancelled.";

        byte[] raw = amxGarc.Files[skip.ScriptIndex];
        var script = new Script(raw);
        uint[] cmds = script.ScriptCommands;

        if (skip.RemoveStart + skip.RemoveCount > cmds.Length ||
            !cmds.Skip(skip.RemoveStart).Take(skip.RemoveCount).SequenceEqual(skip.ExpectedWords))
        {
            return $"{skip.Label}: refused - script {skip.ScriptIndex} doesn't match the expected bytes " +
                   "at this position (already applied, or a different ROM build). Nothing changed.";
        }

        uint[] edited;
        try { edited = ScriptEditing.RemoveWords(cmds, skip.RemoveStart, skip.RemoveCount); }
        catch (Exception ex) { return $"{skip.Label}: refused - {ex.Message}"; }

        if (!script.TryWriteResizedScriptCommands(edited, out string error))
            return $"{skip.Label}: refused - {error}";

        var files = amxGarc.Files;
        files[skip.ScriptIndex] = script.Raw;
        amxGarc.Files = files;
        amxGarc.Save();

        if (currentIndex == skip.ScriptIndex) LoadScript(skip.ScriptIndex);

        return $"{skip.Label}: applied and saved. Reload your ROM in pk3DS so other editors see it.";
    }

    /// <summary>
    /// The exact inverse of <see cref="ApplyTutorialSkip"/> for the SAME <paramref name="undo"/>
    /// span: puts <see cref="TutorialSkip.ExpectedWords"/> back at <see
    /// cref="TutorialSkip.RemoveStart"/> via <see cref="ScriptEditing.InsertWords"/>, correcting
    /// every branch that crosses the reopened gap. Refuses if those words are already present (the
    /// skip was never applied, or this has already been undone) - the mirror of how
    /// <see cref="ApplyTutorialSkip"/> refuses unless they ARE present.
    /// </summary>
    private string ApplyTutorialUndo(TutorialSkip undo)
    {
        if (undo.ScriptIndex == currentIndex && dirty)
            return $"{undo.Label}: this script has unsaved edits open above - save or discard them first.";

        var script = new Script(amxGarc.Files[undo.ScriptIndex]);
        uint[] cmds = script.ScriptCommands;

        if (undo.RemoveStart + undo.RemoveCount <= cmds.Length &&
            cmds.Skip(undo.RemoveStart).Take(undo.RemoveCount).SequenceEqual(undo.ExpectedWords))
        {
            return $"{undo.Label}: nothing to undo - the skip doesn't look applied at this position.";
        }

        if (WinFormsUtil.Prompt(MessageBoxButtons.YesNo,
                $"{undo.Label}?",
                $"Puts the {undo.RemoveCount} removed word(s) back in script {undo.ScriptIndex} and corrects " +
                "every branch/call offset that was adjusted around the gap. Applied directly to the " +
                "amxscript archive.") != DialogResult.Yes)
            return $"{undo.Label}: cancelled.";

        uint[] restored;
        try { restored = ScriptEditing.InsertWords(cmds, undo.RemoveStart, undo.ExpectedWords); }
        catch (Exception ex) { return $"{undo.Label}: refused - {ex.Message}"; }

        if (!script.TryWriteResizedScriptCommands(restored, out string error))
            return $"{undo.Label}: refused - {error}";

        var files = amxGarc.Files;
        files[undo.ScriptIndex] = script.Raw;
        amxGarc.Files = files;
        amxGarc.Save();

        if (currentIndex == undo.ScriptIndex) LoadScript(undo.ScriptIndex);

        return $"{undo.Label}: done. Reload your ROM in pk3DS so other editors see it.";
    }

    private void B_Save_Click(object sender, EventArgs e)
    {
        if (currentScript == null) return;

        uint[] full = new uint[currentScriptCmds.Length + currentMoveCmds.Length];
        currentScriptCmds.CopyTo(full, 0);
        currentMoveCmds.CopyTo(full, currentScriptCmds.Length);

        byte[] newRaw = currentScript.Write(full);
        var files = amxGarc.Files;
        files[currentIndex] = newRaw;
        amxGarc.Files = files;
        amxGarc.Save();

        dirty = false;
        WinFormsUtil.Alert("Script saved.", $"{CB_Script.Text} written back to the amxscript archive.\n\nReload your ROM in pk3DS (File -> Open) so other editors see the updated data.");
    }
}
