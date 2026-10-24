using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Linq;
using pk3DS.Core;
using pk3DS.Core.Modding;
using pk3DS.Core.Modding.Research;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures.PersonalInfo;

namespace pk3DS.WinForms;

public partial class TMEditor7 : Form
{
    private readonly string codebin;
    private byte[] data;                     // code.bin as this form last read or wrote it
    private readonly TMTable table;          // null = read-only, reason in L_Status
    private readonly ushort[] openMoves;     // moves as opened, for the compatibility remap
    private readonly string[] movelist = Main.Config.GetText(TextName.MoveNames);
    private bool dirty;

    public TMEditor7()
    {
        InitializeComponent();
        codebin = Main.ExeFSPath == null ? null : ExeFS.ResolveCodeBin(Main.ExeFSPath);
        if (codebin == null || !File.Exists(codebin)) { WinFormsUtil.Alert("No .code.bin detected."); Close(); return; }
        data = File.ReadAllBytes(codebin);
        if (data.Length % 0x200 != 0) { WinFormsUtil.Alert(".code.bin not decompressed. Aborting."); Close(); return; }
        movelist[0] = "";
        table = TMTable.Locate(data, out string err);
        L_Status.Text = table == null ? "TM table not recognised: " + err + " (read-only)" : $"{table.Layout}, {table.Count} TMs";
        openMoves = table?.ReadMoves(data) ?? [];
        SetupDGV();
        GetList();
        B_Save.Enabled = B_RTM.Enabled = B_ImportTxt.Enabled = table != null;
        B_Expand.Enabled = table?.Layout == TMTable.Kind.Stock;
        dgvTM.CellValueChanged += (_, _) => dirty = true;
    }

    /// <summary>code.bin as it is on disk now, if its TM table is still where this form opened it; otherwise null, with the error shown.</summary>
    private byte[] FreshCodeBin()
    {
        // The form is non-modal, so a randomizer run or an install may have rewritten code.bin since it opened.
        byte[] fresh = File.ReadAllBytes(codebin);
        var now = TMTable.Locate(fresh, out _);
        if (now != null && now.MoveOffset == table.MoveOffset && now.ItemOffset == table.ItemOffset && now.Count == table.Count && fresh.Length == data.Length)
            return fresh;
        WinFormsUtil.Error("Nothing was written.", "code.bin changed after this editor opened, so its TM table moved or changed size. Close and reopen the TM editor, then redo the edit.");
        return null;
    }

    private bool Save()
    {
        dgvTM.EndEdit(); // a combo edit commits (and fires CellValueChanged) only when the cell is left
        if (table == null || !dirty) return true;
        var names = dgvTM.Rows.Cast<DataGridViewRow>().Select(r => r.Cells[2].Value?.ToString()).ToArray();
        ushort[] moves = names.Select(n => (ushort)Math.Max(0, Array.IndexOf(movelist, n))).ToArray();
        // Changed and changed back: nothing to write, so no approval prompt.
        if (moves.SequenceEqual(openMoves)) { dirty = false; return true; }
        // A name that is not in movelist would save as move 0 and wipe that TM. Refuse rather than write it.
        for (int i = 0; i < moves.Length; i++)
            if (moves[i] == 0 && openMoves[i] != 0 && Array.IndexOf(movelist, names[i]) < 0)
            { WinFormsUtil.Error("TM save refused; nothing was written.", $"TM{i + 1:00} is \"{names[i]}\", which is not a move name."); return false; }
        if (TMCompat.PassiveRefusal(openMoves, moves) is { } passiveWhy) { WinFormsUtil.Error("TM save refused; nothing was written.", passiveWhy); return false; } // Remap runs after code.bin is written, so decide now
        byte[] fresh = FreshCodeBin();
        if (fresh == null) return false;
        if (!table.ReadMoves(fresh).SequenceEqual(openMoves))
        { WinFormsUtil.Error("TM save refused; nothing was written.", "The TM moves in code.bin changed after this editor opened. Close and reopen the TM editor, then redo the edit."); return false; }
        ushort[] items = table.ReadItems(fresh);
        table.Write(fresh, moves, items);
        if (!BinaryWriteGuard.TryWrite(codebin, fresh, "Save TM moves", $"{moves.Where((m, i) => m != openMoves[i]).Count()} TM slot(s) changed", uhuruDiffChecked: true))
        { WinFormsUtil.Alert("TM save was refused or declined; nothing was written."); return false; }
        data = fresh;

        // Descriptions: only for slots whose move changed.
        var flavor = Main.Config.GetText(TextName.ItemFlavor);
        var moveFlavor = Main.Config.GetText(TextName.MoveFlavor);
        bool textChanged = false;
        for (int i = 0; i < moves.Length; i++)
            if (moves[i] != openMoves[i] && items[i] < flavor.Length && moves[i] < moveFlavor.Length)
            { flavor[items[i]] = moveFlavor[moves[i]]; textChanged = true; }
        if (textChanged) { Main.Config.SetText(TextName.ItemFlavor, flavor); Main.Config.SaveText(TextName.ItemFlavor); }

        // Compatibility follows moved moves.
        var from = TMCompat.FromSlots(openMoves, moves);
        var files = Main.Config.GARCPersonal.Files;
        int changed = TMCompat.Remap(files, from);
        if (changed > 0)
        {
            Main.Config.GARCPersonal.Files = files;
            Main.Config.GARCPersonal.Save();
            // Same refresh PersonalEditor7 does (PersonalEditor7.cs:555), so other editors and the randomizer see the new bits this session.
            Main.Config.Personal.Table = files.Where(f => f.Length == files[0].Length).Select(f => PersonalTable.GetInfo(f, Main.Config.Version)).ToArray();
        }

        int movedSlots = from.Where((f, i) => f >= 0 && f != i).Count(), newSlots = from.Count(f => f < 0);
        WinFormsUtil.Alert("TMs saved.", $"{movedSlots} slot(s) took their move's old learnability; {newSlots} slot(s) with a new move kept the slot's learnability; {changed} Pokémon entries updated.");
        moves.CopyTo(openMoves, 0);
        dirty = false;
        return true;
    }

    private void B_Save_Click(object sender, EventArgs e) => Save();

    private void Form_Closing(object sender, FormClosingEventArgs e)
    {
        dgvTM.EndEdit();
        if (!dirty) return;
        var r = WinFormsUtil.Prompt(MessageBoxButtons.YesNoCancel, "Save TM changes?");
        if (r == DialogResult.Cancel || (r == DialogResult.Yes && !Save())) e.Cancel = true;
    }

    private void B_Expand_Click(object sender, EventArgs e)
    {
        if (table == null || table.Layout != TMTable.Kind.Stock) return;
        dgvTM.EndEdit(); // a pending combo edit sets dirty only when committed, so commit it before the check
        if (dirty && !Save()) return;
        int n;
        using (var f = new Form { Text = "Expand TMs", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new System.Drawing.Size(240, 70), MaximizeBox = false, MinimizeBox = false })
        {
            var nud = new NumericUpDown { Minimum = table.Count + 1, Maximum = TMExpansion.EffectiveMax, Value = TMExpansion.EffectiveMax, Location = new System.Drawing.Point(12, 12), Width = 80 };
            var ok = new Button { Text = "Expand", DialogResult = DialogResult.OK, Location = new System.Drawing.Point(100, 10) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new System.Drawing.Point(100, 38) };
            f.Controls.AddRange([nud, ok, cancel]);
            f.AcceptButton = ok; f.CancelButton = cancel;
            if (f.ShowDialog(this) != DialogResult.OK) return;
            n = (int)nud.Value;
        }
        ushort[] newMoves = Enumerable.Repeat((ushort)1, n - table.Count).ToArray(); // Pound; edit them after expanding
        byte[] fresh = FreshCodeBin();
        if (fresh == null) return;
        var grown = ExpandedTMItems.ExpandCode(Main.Config, fresh, n, newMoves,
            out ushort[] ids, out string err, out GrowResult growth);
        if (grown == null) { WinFormsUtil.Error("TM expansion refused; nothing was written.", err); return; }
        if (growth != null)
        {
            // code.bin and the exheader must land together: refuse before writing anything if either file cannot be written.
            string? root = Directory.GetParent(Path.GetFullPath(Main.Config.ExeFS))?.FullName;
            foreach (string name in new[] { "ExHeader.bin", "HeaderNCCH0.bin" })
            {
                string path = root == null ? "" : Path.Combine(root, name);
                if (!File.Exists(path)) continue;
                try
                {
                    using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    // The .orig backup is made now, so nothing can fail on it after code.bin is written.
                    if (!File.Exists(path + ".orig")) File.Copy(path, path + ".orig", false);
                }
                catch (Exception ex) { WinFormsUtil.Error($"{name} cannot be written, so code.bin was not grown; nothing was written.", ex.Message); return; }
            }
        }
        if (!BinaryWriteGuard.TryWrite(codebin, grown, $"Expand TMs to {n}", $"New TM items {ids.First()}-{ids.Last()}")) return;
        // code.bin now holds the expanded table. Whatever happens next, this form must not save its old buffer over it.
        if (growth != null)
        {
            try
            {
                string romRoot = Directory.GetParent(Path.GetFullPath(Main.Config.ExeFS))?.FullName
                    ?? throw new DirectoryNotFoundException("The ROM root next to ExeFS was not found");
                string exHeaderPath = Path.Combine(romRoot, "ExHeader.bin");
                byte[] oldExHeader = File.ReadAllBytes(exHeaderPath);
                long insertionOffset = CodeGrowth.TextEndFor(oldExHeader);
                long addedBytes = growth.Pages * 0x1000L;
                if (insertionOffset < 0 || insertionOffset > uint.MaxValue || addedBytes <= 0 || addedBytes > uint.MaxValue)
                    throw new InvalidDataException("The code.bin growth journal offsets are outside the supported range");

                CodeBinExpansionJournal.RecordExpansion(Main.Config.ExeFS, CodeBinExpander.Segment.Text,
                    (uint)insertionOffset, (uint)addedBytes);
                WriteWithOrigBackup(exHeaderPath, growth.ExHeader);

                string ncch0Path = Path.Combine(romRoot, "HeaderNCCH0.bin");
                if (File.Exists(ncch0Path) && growth.Ncch0.Length > 0)
                    WriteWithOrigBackup(ncch0Path, growth.Ncch0);
            }
            catch (Exception ex)
            {
                WinFormsUtil.Error("code.bin was expanded, but its ExHeader.bin or HeaderNCCH0.bin could not be updated.", ex.Message);
                dirty = false;
                Close();
                return;
            }
        }

        try
        {
            ExpandedTMItems.WriteItemData(Main.Config, ids, newMoves, table.Count);
            string detail = "Reopen the TM editor to edit the new slots.";
            if (growth != null)
                detail += "\n\nIMPORTANT deployment step: the mod folder also needs the new ExHeader.bin. " +
                    "Copy it into the mods folder ROOT (next to the exefs/romfs folders, not inside exefs) as " +
                    "\"exheader.bin\" so Citra or Luma3DS reads the new code size. Skipping this can hang at " +
                    "\"Now Loading...\" instead of booting. Please reload your ROM in pk3DS (File -> Open) " +
                    "so all editors update to the new size.";
            WinFormsUtil.Alert($"Expanded to {n} TMs.", detail);
        }
        catch (Exception ex)
        {
            WinFormsUtil.Error("code.bin was expanded, but the TM names and descriptions were not written.", ex.Message);
        }
        finally
        {
            dirty = false;
            Close();
        }
    }

    private static void WriteWithOrigBackup(string path, byte[] bytes)
    {
        string backup = path + ".orig";
        if (!File.Exists(backup)) File.Copy(path, backup, false);
        File.WriteAllBytes(path, bytes);
    }

    private void SetupDGV()
    {
        dgvTM.Columns.Clear();
        var dgvIndex = new DataGridViewTextBoxColumn();
        {
            dgvIndex.HeaderText = "Index";
            dgvIndex.DisplayIndex = 0;
            dgvIndex.Width = 45;
            dgvIndex.ReadOnly = true;
            dgvIndex.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvIndex.SortMode = DataGridViewColumnSortMode.NotSortable;
        }
        var dgvItem = new DataGridViewTextBoxColumn();
        {
            dgvItem.HeaderText = "Item ID";
            dgvItem.DisplayIndex = 1;
            dgvItem.Width = 55;
            dgvItem.ReadOnly = true; // item ids come from the table; only the move is edited
            dgvItem.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvItem.SortMode = DataGridViewColumnSortMode.NotSortable;
        }
        var dgvMove = new DataGridViewComboBoxColumn();
        {
            dgvMove.HeaderText = "Move";
            dgvMove.DisplayIndex = 2;
            dgvMove.Items.AddRange(movelist);

            dgvMove.Width = 133;
            dgvMove.FlatStyle = FlatStyle.Flat;
            dgvMove.SortMode = DataGridViewColumnSortMode.NotSortable;
        }
        dgvTM.Columns.Add(dgvIndex);
        dgvTM.Columns.Add(dgvItem);
        dgvTM.Columns.Add(dgvMove);
    }

    private void GetList()
    {
        dgvTM.Rows.Clear();
        if (table == null) return;

        ushort[] items = table.ReadItems(data);
        for (int i = 0; i < table.Count; i++)
        {
            int moveId = openMoves[i] < movelist.Length ? openMoves[i] : 0;
            dgvTM.Rows.Add((i + 1).ToString(), items[i].ToString(), movelist[moveId]);
        }
    }

    private void B_RandomTM_Click(object sender, EventArgs e)
    {
        if (WinFormsUtil.Prompt(MessageBoxButtons.YesNo, "Randomize TMs?", "Move compatibility will be the same as the base TMs.") != DialogResult.Yes) return;

        int[] randomMoves = Enumerable.Range(1, movelist.Length - 1).Where(i => Legal.IsUsableMove(i, movelist)).ToArray();
        Util.Shuffle(randomMoves);

        int[] banned = [.. Legal.Z_Moves, .. new[] { 165, 464, 621 }];
        int ctr = 0;

        for (int i = 0; i < dgvTM.Rows.Count; i++)
        {
            int val = Array.IndexOf(movelist, dgvTM.Rows[i].Cells[2].Value);
            if (banned.Contains(val)) continue;
            while (ctr < randomMoves.Length && banned.Contains(randomMoves[ctr])) ctr++;
            if (ctr >= randomMoves.Length)
            {
                Util.Shuffle(randomMoves);
                ctr = 0;
                while (ctr < randomMoves.Length && banned.Contains(randomMoves[ctr])) ctr++;
            }

            dgvTM.Rows[i].Cells[2].Value = movelist[randomMoves[ctr++]];
            dirty = true;
        }
        WinFormsUtil.Alert("Randomized!");
    }

    public static ushort[] GetTMHMList()
    {
        string path = Main.ExeFSPath == null ? null : ExeFS.ResolveCodeBin(Main.ExeFSPath);
        if (path == null || !File.Exists(path)) return [];
        byte[] code = File.ReadAllBytes(path);
        return code.Length % 0x200 == 0 ? TMTable.Locate(code, out _)?.ReadMoves(code) ?? [] : [];
    }

    private void B_ExportTxt_Click(object sender, EventArgs e)
    {
        var sfd = new SaveFileDialog { FileName = "TMs.txt", Filter = "Text File|*.txt" };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        var lines = new List<string>();
        for (int i = 0; i < dgvTM.Rows.Count; i++)
        {
            string moveName = dgvTM.Rows[i].Cells[2].Value?.ToString() ?? "";
            lines.Add($"TM{i + 1:00}: {moveName}");
        }
        File.WriteAllLines(sfd.FileName, lines);
        WinFormsUtil.Alert("TM data exported!");
    }

    private void B_ImportTxt_Click(object sender, EventArgs e)
    {
        var ofd = new OpenFileDialog { Filter = "Text File|*.txt" };
        if (ofd.ShowDialog() != DialogResult.OK) return;

        string[] lines = File.ReadAllLines(ofd.FileName);
        int updated = 0;
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;

            // Parse lines like "TM01: Work Up" or "TM01: 526"
            int colonIdx = line.IndexOf(':');
            if (colonIdx < 0) continue;

            string tmPart = line.Substring(0, colonIdx).Trim();
            string movePart = line.Substring(colonIdx + 1).Trim();

            // Extract TM number
            if (!tmPart.StartsWith("TM", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(tmPart.Substring(2), out int tmNum)) continue;
            int rowIdx = tmNum - 1;
            if (rowIdx < 0 || rowIdx >= dgvTM.Rows.Count) continue;

            // Try to match move by name first, then by index
            int moveIdx = Array.IndexOf(movelist, movePart);
            if (moveIdx < 0 && int.TryParse(movePart, out int moveId) && moveId >= 0 && moveId < movelist.Length)
                moveIdx = moveId;
            if (moveIdx < 0) continue;

            dgvTM.Rows[rowIdx].Cells[2].Value = movelist[moveIdx];
            dirty = true;
            updated++;
        }
        WinFormsUtil.Alert($"Imported {updated} TM entries!");
    }
}
