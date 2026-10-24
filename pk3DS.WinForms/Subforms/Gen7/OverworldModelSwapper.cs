using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

using pk3DS.Core;
using pk3DS.Core.Modding.Research;

namespace pk3DS.WinForms;

public partial class OverworldModelSwapper : Form
{
    private readonly LazyGARCFile owGarc;
    private readonly Dictionary<int, NestedModelContainer> areaCache = [];
    private readonly HashSet<int> dirtyAreas = [];
    private List<(string Name, int Slot)> targetCharacterEntries = [];
    private List<(string Name, int Slot)> sourceCharacterEntries = [];

    public OverworldModelSwapper()
    {
        InitializeComponent();

        var config = Main.Config;
        if (config == null || config.Generation != 7)
        {
            WinFormsUtil.Error("No Gen 7 ROM loaded.", "Overworld model swapping is only supported for Gen 7 (Sun/Moon/Ultra Sun/Ultra Moon) right now.");
            Shown += (_, _) => Close();
            return;
        }

        owGarc = config.GetlzGARCData("owmodels");
        if (owGarc == null)
        {
            WinFormsUtil.Error("Missing game data.", "This ROM doesn't have the overworld model archive (a/0/8/2 or a/0/8/3) registered.");
            Shown += (_, _) => Close();
            return;
        }

        PopulateZoneCombo(CB_TargetZone);
        PopulateZoneCombo(CB_SourceZone);

        L_Status.Text = $"Archive loaded ({owGarc.FileCount} members). Pick a Known zone (auto-fills the area file index) or use Custom for anything else from your own zone reference sheet, then Load.";
    }

    private static void PopulateZoneCombo(ComboBox cb)
    {
        cb.Items.Clear();
        foreach (var (zoneBase, name) in OverworldZoneReference.KnownZones)
            cb.Items.Add($"{name} (area file {OverworldZoneReference.AcFileIndex(zoneBase)})");
        cb.Items.Add("(Custom - enter area file index manually)");
        cb.SelectedIndex = cb.Items.Count - 1;
    }

    private static bool TryGetSelectedZoneBase(ComboBox cb, out int zoneBaseIndex)
    {
        int idx = cb.SelectedIndex;
        if (idx >= 0 && idx < OverworldZoneReference.KnownZones.Count)
        {
            zoneBaseIndex = OverworldZoneReference.KnownZones[idx].ZoneBaseIndex;
            return true;
        }
        zoneBaseIndex = -1;
        return false;
    }

    private void CB_TargetZone_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (TryGetSelectedZoneBase(CB_TargetZone, out int zoneBase))
        {
            NUD_TargetArea.Value = OverworldZoneReference.AcFileIndex(zoneBase);
            NUD_TargetArea.Enabled = false;
        }
        else
        {
            NUD_TargetArea.Enabled = true;
        }
    }

    private void CB_SourceZone_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (TryGetSelectedZoneBase(CB_SourceZone, out int zoneBase))
        {
            NUD_SourceArea.Value = OverworldZoneReference.AcFileIndex(zoneBase);
            NUD_SourceArea.Enabled = false;
        }
        else
        {
            NUD_SourceArea.Enabled = true;
        }
    }

    private static List<(string Name, int Slot)> PopulateCharacterCombo(ComboBox cb, int areaIdx, int cpCount)
    {
        cb.Items.Clear();
        var entries = OverworldCharacterReference.SlotsByAcIndex.TryGetValue(areaIdx, out var list)
            ? list.Where(e => e.Slot >= 0 && e.Slot < cpCount).OrderBy(e => e.Slot).ToList()
            : [];

        if (entries.Count == 0)
        {
            cb.Items.Add("(no character data cataloged for this area file)");
            cb.Enabled = false;
        }
        else
        {
            foreach (var (name, slot) in entries)
                cb.Items.Add($"{name} (slot {slot})");
            cb.Enabled = true;
        }
        cb.SelectedIndex = 0;
        return entries;
    }

    private static void UpdateResolvedCharacterName(int areaIdx, int slot, TextBox noteBox)
    {
        string resolved = OverworldCharacterReference.TryResolveSlotName(areaIdx, slot);
        if (resolved != null) noteBox.Text = resolved;
    }

    private void CB_TargetCharacter_SelectedIndexChanged(object sender, EventArgs e)
    {
        int idx = CB_TargetCharacter.SelectedIndex;
        if (idx < 0 || idx >= targetCharacterEntries.Count) return;
        var (name, slot) = targetCharacterEntries[idx];
        if (slot <= NUD_TargetSlot.Maximum) NUD_TargetSlot.Value = slot;
        TB_TargetSlotNote.Text = name;
    }

    private void CB_SourceCharacter_SelectedIndexChanged(object sender, EventArgs e)
    {
        int idx = CB_SourceCharacter.SelectedIndex;
        if (idx < 0 || idx >= sourceCharacterEntries.Count) return;
        var (name, slot) = sourceCharacterEntries[idx];
        if (slot <= NUD_SourceSlot.Maximum) NUD_SourceSlot.Value = slot;
        TB_SourceSlotNote.Text = name;
    }

    private void NUD_TargetSlot_ValueChanged(object sender, EventArgs e)
        => UpdateResolvedCharacterName((int)NUD_TargetArea.Value, (int)NUD_TargetSlot.Value, TB_TargetSlotNote);

    private void NUD_SourceSlot_ValueChanged(object sender, EventArgs e)
        => UpdateResolvedCharacterName((int)NUD_SourceArea.Value, (int)NUD_SourceSlot.Value, TB_SourceSlotNote);

    private NestedModelContainer LoadArea(int index, out string error)
    {
        error = "";
        if (areaCache.TryGetValue(index, out var cached)) return cached;
        if (index < 0 || index >= owGarc.FileCount) { error = $"Index {index} is out of range (0-{owGarc.FileCount - 1})."; return null; }

        try
        {
            var area = NestedModelContainer.DecodeAreaFile(owGarc[index]);
            if (area.Magic != "AC")
            { error = $"Member {index} is a \"{area.Magic}\" file, not an \"AC\" area/model file - pick a different index."; return null; }
            areaCache[index] = area;
            return area;
        }
        catch (Exception ex)
        {
            error = $"Couldn't decode member {index}: {ex.Message}";
            return null;
        }
    }

    private static int? FindCmChildIndex(NestedModelContainer cp)
    {
        for (int i = 0; i < cp.Children.Count; i++)
            if (cp.DecodedChild(i)?.Magic == "CM") return i;
        return null;
    }

    private void B_LoadTarget_Click(object sender, EventArgs e)
    {
        int index = (int)NUD_TargetArea.Value;
        var area = LoadArea(index, out string error);
        if (area == null) { WinFormsUtil.Error("Couldn't load this area file.", error); return; }

        int cpCount = area.Children.Count;
        NUD_TargetSlot.Maximum = Math.Max(0, cpCount - 1);

        targetCharacterEntries = PopulateCharacterCombo(CB_TargetCharacter, index, cpCount);
        NUD_TargetSlot.Value = targetCharacterEntries.Count > 0 ? targetCharacterEntries[0].Slot : 0;
        UpdateResolvedCharacterName(index, (int)NUD_TargetSlot.Value, TB_TargetSlotNote);

        string zoneName = OverworldZoneReference.TryResolveZoneName(index);
        string zoneSuffix = zoneName != null ? $" ({zoneName})" : "";
        string charInfo = targetCharacterEntries.Count > 0
            ? $"{targetCharacterEntries.Count} known character(s) from your sheet - pick one below, or set the slot number directly."
            : "No character data cataloged for this area file in your sheet - set the slot number directly and type a name if you know it.";
        L_Status.Text = $"Target area {index}{zoneSuffix} loaded - {cpCount} placement slot(s) (0-{cpCount - 1}). {charInfo}";
    }

    private void B_LoadSource_Click(object sender, EventArgs e)
    {
        int index = (int)NUD_SourceArea.Value;
        var area = LoadArea(index, out string error);
        if (area == null) { WinFormsUtil.Error("Couldn't load this area file.", error); return; }

        int cpCount = area.Children.Count;
        NUD_SourceSlot.Maximum = Math.Max(0, cpCount - 1);

        sourceCharacterEntries = PopulateCharacterCombo(CB_SourceCharacter, index, cpCount);
        NUD_SourceSlot.Value = sourceCharacterEntries.Count > 0 ? sourceCharacterEntries[0].Slot : 0;
        UpdateResolvedCharacterName(index, (int)NUD_SourceSlot.Value, TB_SourceSlotNote);

        string zoneName = OverworldZoneReference.TryResolveZoneName(index);
        string zoneSuffix = zoneName != null ? $" ({zoneName})" : "";
        string charInfo = sourceCharacterEntries.Count > 0
            ? $"{sourceCharacterEntries.Count} known character(s) from your sheet - pick one below, or set the slot number directly."
            : "No character data cataloged for this area file in your sheet - set the slot number directly and type a name if you know it.";
        L_Status.Text = $"Source area {index}{zoneSuffix} loaded - {cpCount} placement slot(s) (0-{cpCount - 1}). {charInfo}";
    }

    private void B_Swap_Click(object sender, EventArgs e)
    {
        int targetAreaIdx = (int)NUD_TargetArea.Value;
        int sourceAreaIdx = (int)NUD_SourceArea.Value;
        int targetSlot = (int)NUD_TargetSlot.Value;
        int sourceSlot = (int)NUD_SourceSlot.Value;

        if (!areaCache.TryGetValue(targetAreaIdx, out var targetArea))
        { WinFormsUtil.Error("Load the target area first."); return; }
        if (!areaCache.TryGetValue(sourceAreaIdx, out var sourceArea))
        { WinFormsUtil.Error("Load the source area first."); return; }

        if (targetSlot < 0 || targetSlot >= targetArea.Children.Count) { WinFormsUtil.Error("Target slot is out of range for the target area."); return; }
        if (sourceSlot < 0 || sourceSlot >= sourceArea.Children.Count) { WinFormsUtil.Error("Source slot is out of range for the source area."); return; }

        var targetCp = targetArea.DecodedChild(targetSlot);
        var sourceCp = sourceArea.DecodedChild(sourceSlot);
        if (targetCp == null || targetCp.Magic != "CP") { WinFormsUtil.Error($"Target slot {targetSlot} isn't a \"CP\" placement entry."); return; }
        if (sourceCp == null || sourceCp.Magic != "CP") { WinFormsUtil.Error($"Source slot {sourceSlot} isn't a \"CP\" placement entry."); return; }

        int? targetCmIdx = FindCmChildIndex(targetCp);
        int? sourceCmIdx = FindCmChildIndex(sourceCp);
        if (targetCmIdx == null) { WinFormsUtil.Error($"Target slot {targetSlot} has no \"CM\" model data to replace."); return; }
        if (sourceCmIdx == null) { WinFormsUtil.Error($"Source slot {sourceSlot} has no \"CM\" model data to copy."); return; }

        // Replace the WHOLE CM sub-container wholesale (not individual leaf files inside it) - the
        // community guide's own "replace all the .bins in CM" is exactly this, just done as one
        // in-memory swap instead of a file-by-file copy.
        var sourceCm = sourceCp.DecodedChild(sourceCmIdx.Value);
        targetCp.Children[targetCmIdx.Value] = sourceCm.Encode();

        // Propagate the change back up: CP's own bytes changed, so the target area's own child entry
        // for this slot needs re-encoding too.
        targetArea.Children[targetSlot] = targetCp.Encode();
        dirtyAreas.Add(targetAreaIdx);

        string targetDesc = DescribeAreaSlot(targetAreaIdx, targetSlot, TB_TargetSlotNote.Text);
        string sourceDesc = DescribeAreaSlot(sourceAreaIdx, sourceSlot, TB_SourceSlotNote.Text);
        L_Status.Text = $"Swapped: {targetDesc}'s model now matches {sourceDesc}'s model (in memory) - click \"Save to ROM\" to write it.";
        B_SaveAll.Enabled = true;
    }

    private static string DescribeAreaSlot(int areaIdx, int slot, string slotNote)
    {
        string zoneName = OverworldZoneReference.TryResolveZoneName(areaIdx);
        string areaPart = zoneName != null ? $"area {areaIdx} ({zoneName})" : $"area {areaIdx}";
        string resolvedChar = OverworldCharacterReference.TryResolveSlotName(areaIdx, slot);
        string label = resolvedChar ?? (string.IsNullOrWhiteSpace(slotNote) ? null : slotNote.Trim());
        string slotPart = label != null ? $"slot {slot} ({label})" : $"slot {slot}";
        return $"{areaPart} {slotPart}";
    }

    private void B_SaveAll_Click(object sender, EventArgs e)
    {
        if (dirtyAreas.Count == 0)
        { WinFormsUtil.Alert("Nothing to save.", "No model swaps have been made yet."); return; }

        foreach (int areaIdx in dirtyAreas)
        {
            var area = areaCache[areaIdx];
            owGarc[areaIdx] = area.EncodeAreaFile();
        }
        owGarc.Save();

        WinFormsUtil.Alert("Model swap saved!", $"{dirtyAreas.Count} area file(s) written back to the overworld model archive.");
        dirtyAreas.Clear();
        B_SaveAll.Enabled = false;
    }
}
