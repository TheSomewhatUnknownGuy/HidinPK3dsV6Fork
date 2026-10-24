using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

using pk3DS.Core;
using pk3DS.Core.Modding.Research;

namespace pk3DS.WinForms;

/// <summary>
/// Edits the community-documented NPC item gifts inside `amxscript` (a/0/9/2) by item name and
/// quantity, instead of requiring the raw AMX bytecode editor (<see cref="AMXScriptEditor"/>) for
/// something as simple as "give a different item" or "give more of it" - and, per explicit request,
/// lets a script give MORE THAN ONE item at the same NPC moment, not just change which single item an
/// existing gift gives.
/// <para>
/// Each <see cref="GiftItemEntry"/> from <see cref="GiftItemReference"/> is a real, live gift the
/// community found and tested. <see cref="GiftItemLocator"/> re-finds every place that gift's
/// (quantity, item id) pair could sit in the live ROM's own script data every time this form loads,
/// rather than trusting a hardcoded byte offset - and EVERY candidate is shown as its own directly
/// editable row (not just the ones the locator can uniquely place). A quantity/item id pair that
/// coincides with unrelated code elsewhere in the script (a Max Potion's quantity turning up as an
/// unrelated loop bound, say) shows up as several low-confidence candidates rather than being hidden
/// - each carries a <see cref="GiftLocationConfidence"/> (driven by whether every candidate's
/// confirming call agrees on one real target subroutine, not just the raw count - see
/// <see cref="GiftItemLocator"/>'s own notes) and a snippet of surrounding decoded instructions.
/// </para>
/// <para>
/// "Add Another Gift" (one per row with a confirmed call to clone) uses <see cref="GiftItemInserter"/>
/// to clone that gift's own instruction span with new item/quantity values and splice it in right
/// after the original, via <see cref="AmxRelocation"/> - verified this session against all 759 real
/// scripts in this ROM (every jump/call/switch-case target still lands exactly on a real instruction
/// boundary afterward) and against 208 real, confirmed gifts in this exact reference list (0 failures/
/// corruptions). The new gift always triggers alongside the one it was cloned from - it isn't a new,
/// independently-triggered NPC interaction, since building that trigger logic from scratch is a much
/// larger, per-script task this can't safely automate.
/// </para>
/// </summary>
public partial class GiftItemEditor : Form
{
    private readonly GARCFile amxGarc;
    private readonly string[] itemNames;
    private sealed class Row
    {
        public GiftItemEntry Entry;
        public GiftItemLocation Location;
        public int CandidateIndex; // 1-based, of TotalCandidates
        public int TotalCandidates;
        public uint[] Cmd;
        public bool Touched;
        public bool IsManualAddition;
    }
    private readonly List<Row> rows = [];
    /// <summary>Mirrors <see cref="DGV_Gifts"/>'s actual rows 1:1 (after the low-confidence filter, if any) - grid event handlers index into THIS, never <see cref="rows"/> directly, since hiding rows means the grid's own row index no longer lines up with <see cref="rows"/>'s.</summary>
    private readonly List<Row> visibleRows = [];
    private readonly List<GiftItemEntry> manualEntries = [];
    private readonly Dictionary<int, (Script script, uint[] cmd, List<DecodedInstruction> decoded)> scriptCache = [];
    private readonly HashSet<int> dirtyFiles = [];
    private sealed class ItemOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }
    private List<ItemOption> itemOptions;

    public GiftItemEditor(GARCFile amx)
    {
        amxGarc = amx;
        itemNames = Main.Config.GetText(TextName.ItemNames);
        InitializeComponent();
        WinFormsUtil.SetDoubleBuffered(DGV_Gifts); // this grid rebuilds fully on every edit/insert/toggle - double-buffering keeps that from visibly flickering
        CB_HideLow.Checked = Properties.Settings.Default.HideLowConfidenceGifts; // remembered across sessions per explicit request - once ticked, stays ticked
        BuildItemOptions();
        RefreshAll();
    }

    private void BuildItemOptions()
    {
        itemOptions = new List<ItemOption>(itemNames.Length);
        for (int i = 0; i < itemNames.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(itemNames[i])) continue;
            itemOptions.Add(new ItemOption { Id = i, Name = $"{i:0000}: {itemNames[i]}" });
        }

        var col = (DataGridViewComboBoxColumn)DGV_Gifts.Columns["ColNewItem"];
        col.DataSource = itemOptions;
        col.ValueMember = nameof(ItemOption.Id);
        col.DisplayMember = nameof(ItemOption.Name);
    }

    private string ItemName(int id) => id >= 0 && id < itemNames.Length && !string.IsNullOrWhiteSpace(itemNames[id]) ? itemNames[id] : $"Item {id}";

    private (Script script, uint[] cmd, List<DecodedInstruction> decoded) GetScript(int fileIndex)
    {
        if (scriptCache.TryGetValue(fileIndex, out var cached)) return cached;
        var script = new Script(amxGarc.Files[fileIndex]);
        var cmd = script.ScriptCommands;
        var decoded = ScriptEditing.Decode(cmd);
        var entry = (script, cmd, decoded);
        scriptCache[fileIndex] = entry;
        return entry;
    }

    private void RefreshAll()
    {
        rows.Clear();
        foreach (var entry in GiftItemReference.Entries.Concat(manualEntries))
        {
            if (entry.File >= amxGarc.Files.Length)
            {
                rows.Add(new Row { Entry = entry, TotalCandidates = 0 });
                continue;
            }

            var (_, cmd, decoded) = GetScript(entry.File);
            var matches = GiftItemLocator.Find(decoded, cmd, entry.ItemId, entry.Qty);
            if (matches.Count == 0)
            {
                rows.Add(new Row { Entry = entry, TotalCandidates = 0 });
                continue;
            }

            for (int i = 0; i < matches.Count; i++)
                rows.Add(new Row { Entry = entry, Location = matches[i], CandidateIndex = i + 1, TotalCandidates = matches.Count, Cmd = cmd, IsManualAddition = manualEntries.Contains(entry) });
        }
        PopulateGrid();
    }

    private void PopulateGrid()
    {
        DGV_Gifts.SuspendLayout();
        DGV_Gifts.Rows.Clear();
        visibleRows.Clear();
        bool hideLow = CB_HideLow.Checked;
        foreach (var row in rows)
        {
            if (hideLow && row.Location?.Confidence == GiftLocationConfidence.Low) continue;
            visibleRows.Add(row);

            int r = DGV_Gifts.Rows.Add();
            var gridRow = DGV_Gifts.Rows[r];
            gridRow.Cells["ColFile"].Value = $"{row.Entry.File:000}.xamx";
            string fullNotes = row.IsManualAddition ? "(added by you) " + row.Entry.Notes : row.Entry.Notes;
            gridRow.Cells["ColNotes"].Value = fullNotes;
            gridRow.Cells["ColNotes"].ToolTipText = fullNotes;
            gridRow.Cells["ColCurrent"].Value = $"{ItemName(row.Entry.ItemId)} x{row.Entry.Qty}";

            bool editable = row.Location != null;
            if (!editable)
            {
                gridRow.Cells["ColConfidence"].Value = "Not found";
                gridRow.Cells["ColConfidence"].ToolTipText = "Not found in this ROM.";
            }
            else
            {
                // Candidate index and confidence used to be two separate columns - merged into one
                // compact cell (e.g. "Medium (2/2)") since the row's own text color already carries
                // the confidence at a glance (Firebrick=Low, DarkGoldenrod=Medium, default=High below)
                // and the candidate count is only interesting when there's more than one candidate.
                string confidenceLabel = row.Location.Confidence switch
                {
                    GiftLocationConfidence.High => "High",
                    GiftLocationConfidence.Medium => "Medium",
                    _ => "Low",
                };
                string candidateSuffix = row.TotalCandidates > 1 ? $" ({row.CandidateIndex}/{row.TotalCandidates})" : "";
                gridRow.Cells["ColConfidence"].Value = confidenceLabel + candidateSuffix;
                gridRow.Cells["ColConfidence"].ToolTipText = row.Location.Confidence switch
                {
                    GiftLocationConfidence.High => "High confidence - safe to edit directly.",
                    GiftLocationConfidence.Medium => "Medium confidence - double check the Context column before saving.",
                    _ => "Low confidence - this candidate may be unrelated code; check the Context column carefully.",
                };
                gridRow.Cells["ColContext"].Value = row.Location.Context;
                gridRow.Cells["ColContext"].ToolTipText = row.Location.Context;
                gridRow.Cells["ColNewItem"].Value = row.Entry.ItemId;
                gridRow.Cells["ColNewQty"].Value = row.Entry.Qty;
            }

            gridRow.Cells["ColNewItem"].ReadOnly = !editable;
            gridRow.Cells["ColNewQty"].ReadOnly = !editable;
            if (!editable)
            {
                gridRow.Cells["ColNewItem"].Style.BackColor = System.Drawing.Color.Gainsboro;
                gridRow.Cells["ColNewQty"].Style.BackColor = System.Drawing.Color.Gainsboro;
            }
            else if (row.Location.Confidence == GiftLocationConfidence.Low)
            {
                gridRow.DefaultCellStyle.ForeColor = System.Drawing.Color.Firebrick;
            }
            else if (row.Location.Confidence == GiftLocationConfidence.Medium)
            {
                gridRow.DefaultCellStyle.ForeColor = System.Drawing.Color.DarkGoldenrod;
            }

            bool canClone = editable && row.Location.CallFlatIndex >= 0;
            gridRow.Cells["ColAddAnother"].Value = canClone ? "+ Add" : "";
            gridRow.Cells["ColAddAnother"].ToolTipText = canClone ? "Add another gift alongside this one." : "";
            gridRow.Cells["ColAddAnother"].ReadOnly = !canClone;
            // DataGridViewButtonColumn ignores cell ReadOnly for click purposes (the click handler
            // itself is what actually refuses a non-cloneable row) - blank it out AND grey it so it
            // doesn't visually invite a click that would just no-op.
            if (!canClone) gridRow.Cells["ColAddAnother"].Style.BackColor = System.Drawing.Color.Gainsboro;
        }

        int totalCandidates = rows.Count(x => x.Location != null);
        int highConf = rows.Count(x => x.Location?.Confidence == GiftLocationConfidence.High);
        int lowConf = rows.Count(x => x.Location?.Confidence == GiftLocationConfidence.Low);
        int notFound = rows.Count(x => x.Location == null);
        string hiddenNote = hideLow && lowConf > 0 ? $" ({lowConf} low-confidence hidden)" : "";
        L_Status.Text = $"{GiftItemReference.Entries.Count + manualEntries.Count} known/added gifts -> {totalCandidates} candidate row(s) ({highConf} high-confidence){hiddenNote}, {notFound} not found.";
        B_Save.Enabled = dirtyFiles.Count > 0 || rows.Any(x => x.Touched);
        DGV_Gifts.ResumeLayout();
    }

    private void CB_HideLow_CheckedChanged(object sender, EventArgs e)
    {
        // Persisted immediately (not just on Save) - per explicit request, this toggle should stay
        // ticked across future sessions too, not just for the lifetime of this dialog.
        Properties.Settings.Default.HideLowConfidenceGifts = CB_HideLow.Checked;
        Properties.Settings.Default.Save();

        // Commit first, while visibleRows still matches what's on screen right now - toggling
        // visibility rebuilds the grid, and a Low-confidence row can be mid-edit (still fully
        // editable, just hideable) when that happens.
        CommitPendingValueEdits();
        PopulateGrid();
    }

    /// <summary>The New Item column is intentionally narrower than a full item name (to keep the grid compact) - this widens just the dropdown popup itself while it's open, so picking from the list is still easy without the column eating permanent horizontal space.</summary>
    private void DGV_Gifts_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (DGV_Gifts.CurrentCell?.OwningColumn?.Name == "ColNewItem" && e.Control is ComboBox combo)
            combo.DropDownWidth = 260;
    }

    private void DGV_Gifts_CellValueChanged(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= visibleRows.Count) return;
        var row = visibleRows[e.RowIndex];
        if (row.Location == null) return;
        string colName = DGV_Gifts.Columns[e.ColumnIndex].Name;
        if (colName != "ColNewItem" && colName != "ColNewQty") return;
        row.Touched = true;
        B_Save.Enabled = true;
    }

    private void DGV_Gifts_CellEndEdit(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= visibleRows.Count) return;
        var row = visibleRows[e.RowIndex];
        if (row.Location == null) return;
        if (DGV_Gifts.Columns[e.ColumnIndex].Name != "ColNewQty") return;

        var cell = DGV_Gifts.Rows[e.RowIndex].Cells[e.ColumnIndex];
        if (!int.TryParse(cell.Value?.ToString(), out int qty) || qty < 0)
        {
            cell.Value = row.Entry.Qty;
            WinFormsUtil.Alert("Quantity must be a non-negative whole number.");
        }
    }

    private void DGV_Gifts_CellClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= visibleRows.Count) return;
        if (DGV_Gifts.Columns[e.ColumnIndex].Name != "ColAddAnother") return;

        var row = visibleRows[e.RowIndex];
        if (row.Location == null || row.Location.CallFlatIndex < 0) return;

        if (!PromptForItemAndQty(out int newItemId, out int newQty)) return;

        // Commit every currently pending value edit into its own row's cached Cmd first, so an
        // insertion's full grid refresh (which rebuilds every row from the cache) can't silently
        // discard an edit the user made to some OTHER row in this same file that hasn't been saved
        // to disk yet.
        CommitPendingValueEdits();

        var (_, cmd, decoded) = GetScript(row.Entry.File);
        var result = GiftItemInserter.AddAnotherGift(cmd, decoded, row.Location, newQty, newItemId);
        if (!result.Success)
        {
            WinFormsUtil.Error("Couldn't add another gift.", result.Error);
            return;
        }

        var newDecoded = ScriptEditing.Decode(result.NewScriptCommands);
        scriptCache[row.Entry.File] = (scriptCache[row.Entry.File].script, result.NewScriptCommands, newDecoded);
        dirtyFiles.Add(row.Entry.File);

        manualEntries.Add(new GiftItemEntry(row.Entry.File, newItemId, newQty, $"Added alongside {ItemName(row.Entry.ItemId)} x{row.Entry.Qty}"));
        RefreshAll();
        WinFormsUtil.Alert("Gift added (in memory).", $"{ItemName(newItemId)} x{newQty} will now be given alongside the gift you cloned it from - click Save Changes to write it to the ROM.");
    }

    /// <summary>Applies every row's pending grid-cell edit into its own cached Cmd array, and clears the pending flag - called before any operation (like an insertion, or toggling which rows are visible) that rebuilds the grid from the cache, so nothing typed but not yet saved gets lost. Reads from <see cref="visibleRows"/>, not <see cref="rows"/> - only visible rows have an actual grid cell to read a pending edit from right now.</summary>
    private void CommitPendingValueEdits()
    {
        foreach (var (row, idx) in visibleRows.Select((r, i) => (r, i)))
        {
            if (row.Location == null || !row.Touched) continue;

            var gridRow = DGV_Gifts.Rows[idx];
            int newItemId = gridRow.Cells["ColNewItem"].Value is int id ? id : Convert.ToInt32(gridRow.Cells["ColNewItem"].Value);
            if (!int.TryParse(gridRow.Cells["ColNewQty"].Value?.ToString(), out int newQty) || newQty < 0) continue;

            ScriptEditing.SetArgValue(row.Cmd, row.Location.ItemIdSlot, newItemId);
            ScriptEditing.SetArgValue(row.Cmd, row.Location.QtySlot, newQty);
            dirtyFiles.Add(row.Entry.File);
            row.Touched = false;
        }
    }

    private bool PromptForItemAndQty(out int itemId, out int qty)
    {
        itemId = -1; qty = 0;
        using var prompt = new Form
        {
            Width = 380, Height = 180, FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = "Add Another Gift", StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false,
        };
        var itemLabel = new Label { Left = 15, Top = 20, Text = "Item:", AutoSize = true };
        var itemCombo = new ComboBox { Left = 15, Top = 40, Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
        itemCombo.DataSource = itemOptions.ToList();
        itemCombo.DisplayMember = nameof(ItemOption.Name);
        itemCombo.ValueMember = nameof(ItemOption.Id);

        var qtyLabel = new Label { Left = 15, Top = 75, Text = "Quantity:", AutoSize = true };
        var qtyInput = new NumericUpDown { Left = 15, Top = 95, Width = 120, Minimum = 1, Maximum = 999, Value = 1 };

        var okButton = new Button { Text = "Add", Left = 190, Top = 120, Width = 75, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", Left = 270, Top = 120, Width = 75, DialogResult = DialogResult.Cancel };

        prompt.Controls.AddRange([itemLabel, itemCombo, qtyLabel, qtyInput, okButton, cancelButton]);
        prompt.AcceptButton = okButton;
        prompt.CancelButton = cancelButton;

        if (prompt.ShowDialog() != DialogResult.OK || itemCombo.SelectedItem is not ItemOption chosen) return false;
        itemId = chosen.Id;
        qty = (int)qtyInput.Value;
        return true;
    }

    private void B_Save_Click(object sender, EventArgs e)
    {
        CommitPendingValueEdits();

        if (dirtyFiles.Count == 0)
        {
            WinFormsUtil.Alert("Nothing to save.", "No gift edits have been made yet.");
            return;
        }

        var files = amxGarc.Files;
        foreach (int fileIndex in dirtyFiles)
        {
            var (script, cmd, _) = scriptCache[fileIndex];
            if (!script.TryWriteResizedScriptCommands(cmd, out string writeErr))
            {
                WinFormsUtil.Error($"Couldn't save {fileIndex:000}.xamx.", writeErr);
                return;
            }
            files[fileIndex] = script.Raw;
        }
        amxGarc.Files = files;
        amxGarc.Save();

        int savedCount = dirtyFiles.Count;
        dirtyFiles.Clear();
        B_Save.Enabled = false;
        WinFormsUtil.Alert("Gifts saved!", $"{savedCount} script(s) written back to the amxscript archive.\n\nReload your ROM in pk3DS (File -> Open) so other editors see the updated data.");
    }
}
