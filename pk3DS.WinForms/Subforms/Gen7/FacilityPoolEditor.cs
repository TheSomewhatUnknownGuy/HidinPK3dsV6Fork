using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

using pk3DS.Core;
using pk3DS.Core.Modding;

namespace pk3DS.WinForms;

/// <summary>
/// Editor for Uhuru's trainer pools (Boss Rush, Labyrinth, PWT) inside `amxscript` (a/0/9/2), scripts 26 and 217.
/// Built on <see cref="FacilityPools"/>, so every edit goes through its guards: trainer ids 1..652, flags 1..4095,
/// fixed pool sizes, and copies (mirrors) that refuse writes. A refused edit shows the exception message and changes nothing.
/// <para>
/// Nothing is written until Save, and Save writes only the scripts whose bytes actually differ, so opening and
/// closing the form (or editing a cell back to its old value) leaves the archive untouched. Saving goes through the
/// same GARC path as <see cref="AMXScriptEditor"/>. a/0/9/2 is not code.bin or a CRO, so BinaryWriteGuard and
/// UhuruGuard.RefusalFor do not apply; the Uhuru check is in Main (Config.Info.TrainerBaseCount).
/// </para>
/// </summary>
public sealed class FacilityPoolEditor : Form
{
    private readonly GARCFile amxGarc;
    private readonly Dictionary<int, FacilityPools> pools = [];
    private readonly ListBox LB_Pools;
    private readonly DataGridView DGV_Cells;
    private readonly DataGridViewTextBoxColumn ColIndex, ColFlag;
    private readonly DataGridViewComboBoxColumn ColTrainer;
    private readonly RadioButton RB_RewriteFlag, RB_KeepFlag;
    private readonly Label L_Info;
    private bool loading;

    private sealed class TrainerOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }

    public FacilityPoolEditor(GARCFile amx)
    {
        amxGarc = amx;
        foreach (int script in FacilityPools.Defs.Select(d => d.Script).Distinct())
            pools[script] = new FacilityPools(amx.Files[script], script); // throws for a script that is not Uhuru's

        // Uhuru's name table has one name per trainer id (the difficulty blocks share it), so the id is the line.
        string[] names = Main.Config.GetText(TextName.TrainerNames);
        var options = new List<TrainerOption>();
        for (int id = 1; id <= FacilityPools.MaxTrainer; id++)
            options.Add(new TrainerOption { Id = id, Name = id < names.Length && !string.IsNullOrWhiteSpace(names[id]) ? $"{id:000}: {names[id]}" : $"{id:000}" });

        Text = "Facility Pools (Uhuru)";
        ClientSize = new Size(800, 520);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 420);

        var lblPools = new Label { Location = new Point(14, 12), AutoSize = true, Text = "Pool" };
        LB_Pools = new ListBox { Location = new Point(14, 32), Size = new Size(250, 330), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
        foreach (var d in FacilityPools.Defs) LB_Pools.Items.Add($"{d.Name} - script {d.Script}, {d.Count} {(d.Kind == PoolKind.TrainerFlagPairs ? "pairs" : "cells")}");

        L_Info = new Label { Location = new Point(14, 368), Size = new Size(250, 100), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };

        ColIndex = new DataGridViewTextBoxColumn { Name = "ColIndex", HeaderText = "#", ReadOnly = true, Width = 45, SortMode = DataGridViewColumnSortMode.NotSortable };
        ColTrainer = new DataGridViewComboBoxColumn
        {
            Name = "ColTrainer", HeaderText = "Trainer", Width = 300, SortMode = DataGridViewColumnSortMode.NotSortable,
            DataSource = options, ValueMember = nameof(TrainerOption.Id), DisplayMember = nameof(TrainerOption.Name), ValueType = typeof(int),
        };
        ColFlag = new DataGridViewTextBoxColumn { Name = "ColFlag", HeaderText = "Flag", Width = 80, SortMode = DataGridViewColumnSortMode.NotSortable };
        // Rows stay in pool order (row = pair or cell), so the grid must not be sortable.
        DGV_Cells = new DataGridView
        {
            Location = new Point(276, 32), Size = new Size(510, 330), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
            AutoGenerateColumns = false,
        };
        DGV_Cells.Columns.AddRange(ColIndex, ColTrainer, ColFlag);
        WinFormsUtil.SetDoubleBuffered(DGV_Cells);

        var grpFlag = new GroupBox { Text = "When a trainer is swapped", Location = new Point(276, 372), Size = new Size(510, 66), Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        RB_RewriteFlag = new RadioButton { Text = "Rewrite the flag to 3036 + new trainer id", Location = new Point(12, 18), AutoSize = true, Checked = true };
        RB_KeepFlag = new RadioButton { Text = "Keep the old flag", Location = new Point(12, 40), AutoSize = true };
        grpFlag.Controls.AddRange([RB_RewriteFlag, RB_KeepFlag]);
        new ToolTip().SetToolTip(grpFlag, "Only pairs whose flag was 3036 + the old trainer id are rewritten; other flags (3904, 4060) are always kept. PWT ids have no flag of their own.");

        var bSave = new Button { Text = "Save", Location = new Point(576, 478), Size = new Size(100, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        var bClose = new Button { Text = "Close", Location = new Point(686, 478), Size = new Size(100, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        bSave.Click += B_Save_Click;
        bClose.Click += (_, _) => Close();

        Controls.AddRange([lblPools, LB_Pools, L_Info, DGV_Cells, grpFlag, bSave, bClose]);

        LB_Pools.SelectedIndexChanged += (_, _) => LoadPool();
        DGV_Cells.CellValueChanged += DGV_Cells_CellValueChanged;
        DGV_Cells.DataError += (_, e) => e.ThrowException = false;
        // A combo box commits on leaving the cell by default; commit on selection so the edit applies at once.
        DGV_Cells.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (DGV_Cells.IsCurrentCellDirty && DGV_Cells.CurrentCell is DataGridViewComboBoxCell)
                DGV_Cells.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        FormClosing += (_, e) =>
        {
            DGV_Cells.EndEdit();
            if (HasChanges() && WinFormsUtil.Prompt(MessageBoxButtons.YesNo, "Discard unsaved pool edits?") != DialogResult.Yes)
                e.Cancel = true;
        };

        LB_Pools.SelectedIndex = 0;
    }

    private PoolDef Current => FacilityPools.Defs[LB_Pools.SelectedIndex];
    private FacilityPools Pools => pools[Current.Script];

    // Grid row -> pool cell. Pair pools have two cells per row (trainer, flag); the id and flag pools have one.
    private static int TrainerCell(PoolDef d, int row) => d.Kind == PoolKind.TrainerFlagPairs ? row * 2 : row;
    private static int FlagCell(PoolDef d, int row) => d.Kind == PoolKind.TrainerFlagPairs ? row * 2 + 1 : row;

    private void LoadPool()
    {
        var d = Current;
        loading = true;
        ColTrainer.Visible = d.Kind != PoolKind.FlagIds;
        ColFlag.Visible = d.Kind != PoolKind.TrainerIds;
        DGV_Cells.Rows.Clear();
        DGV_Cells.Rows.Add(d.Count);
        DGV_Cells.ReadOnly = d.Mirror != null; // a copy refuses writes; edit its source
        loading = false;
        RefreshValues();
        L_Info.Text = Describe(d);
    }

    /// <summary>Writes the pool's current values into the existing rows. Needed after an edit because aliases and copies change with their source.</summary>
    private void RefreshValues()
    {
        var d = Current;
        var fp = Pools;
        loading = true;
        for (int r = 0; r < DGV_Cells.Rows.Count; r++)
        {
            var row = DGV_Cells.Rows[r];
            row.Cells[ColIndex.Index].Value = r.ToString();
            if (d.Kind != PoolKind.FlagIds) row.Cells[ColTrainer.Index].Value = fp.Get(d, TrainerCell(d, r));
            if (d.Kind != PoolKind.TrainerIds) row.Cells[ColFlag.Index].Value = fp.Get(d, FlagCell(d, r)).ToString();
        }
        loading = false;
    }

    private static string Describe(PoolDef d)
    {
        if (d.Mirror != null)
            return $"Copy of the first {d.Count} pairs of {d.Mirror}. Read-only: edit {d.Mirror} and this follows.";
        if (d.Alias != null)
        {
            var src = FacilityPools.Defs.First(x => x.Name == d.Alias);
            return $"The same words as {d.Alias} pairs {(d.Address - src.Address) / 8}..{(d.Address - src.Address) / 8 + d.Count - 1}. An edit here is an edit there.";
        }
        var copies = FacilityPools.Defs.Where(c => c.Mirror == d.Name).Select(c => c.Name).ToList();
        if (copies.Count > 0)
            return $"{string.Join(", ", copies)} copies this pool's first {FacilityPools.Defs.First(c => c.Mirror == d.Name).Count} pairs and follows edits to them.";
        return d.Kind switch
        {
            PoolKind.TrainerIds => "One trainer id per slot. The pool size is fixed.",
            PoolKind.FlagIds => "Story flags that gate PWT rows. The pool size is fixed.",
            _ => "Trainer id and flag per entry. The pool size is fixed.",
        };
    }

    private void DGV_Cells_CellValueChanged(object sender, DataGridViewCellEventArgs e)
    {
        if (loading || e.RowIndex < 0) return;
        bool isTrainer = e.ColumnIndex == ColTrainer.Index;
        if (!isTrainer && e.ColumnIndex != ColFlag.Index) return;

        var d = Current;
        var fp = Pools;
        string error = null;
        object raw = DGV_Cells.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
        if (!int.TryParse(Convert.ToString(raw)?.Trim(), out int value))
            error = "Enter a whole number.";
        else
        {
            try
            {
                if (isTrainer)
                {
                    int tc = TrainerCell(d, e.RowIndex), old = fp.Get(d, tc);
                    int fc = d.Kind == PoolKind.TrainerFlagPairs ? tc + 1 : -1;
                    bool rewrite = RB_RewriteFlag.Checked && fc >= 0 && fp.Get(d, fc) == 3036 + old;
                    fp.Set(d, tc, value);
                    if (rewrite) fp.Set(d, fc, 3036 + value);
                }
                else
                    fp.Set(d, FlagCell(d, e.RowIndex), value);
            }
            catch (ArgumentException ex) { error = ex.Message; }
        }

        RefreshValues(); // shows the accepted value, or puts the old one back after a refusal
        if (error != null) BeginInvoke(() => WinFormsUtil.Error("Edit refused.", error));
    }

    /// <summary>True when any script's bytes differ from the archive. Editing a cell back to its old value is not a change.</summary>
    private bool HasChanges() => pools.Any(kv => !kv.Value.ToBytes().AsSpan().SequenceEqual(amxGarc.Files[kv.Key]));

    private void B_Save_Click(object sender, EventArgs e)
    {
        DGV_Cells.EndEdit();
        var files = amxGarc.Files;
        var saved = new List<int>();
        foreach (var (script, fp) in pools)
        {
            byte[] bytes = fp.ToBytes();
            if (bytes.AsSpan().SequenceEqual(files[script])) continue;
            files[script] = bytes;
            saved.Add(script);
        }
        if (saved.Count == 0)
        {
            WinFormsUtil.Alert("Nothing to save.", "No pool was changed.");
            return;
        }

        amxGarc.Files = files;
        amxGarc.Save();
        foreach (int script in saved) pools[script] = new FacilityPools(files[script], script); // the saved bytes are the new baseline
        LoadPool();
        WinFormsUtil.Alert("Pools saved.", $"Script(s) {string.Join(", ", saved)} written back to the amxscript archive.\n\nReload your ROM in pk3DS (File -> Open) so other editors see the updated data.");
    }
}
