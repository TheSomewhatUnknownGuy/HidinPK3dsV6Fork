using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

using pk3DS.Core;
using pk3DS.Core.Modding.Research;

namespace pk3DS.WinForms;

/// <summary>
/// Workstream 5 - edits a species/form's 3D model height (the one confirmed-real float in the
/// per-group "settings" file, the 9th/last file in each model group within `models`, a094 - see
/// <see cref="ModelSettingsFile"/> for how that offset was actually found, correcting an earlier,
/// unverified assumption about where it lived).
/// <para>
/// Uses the lazy GARC accessor (like <see cref="SpeciesInsertionWizard"/>) so opening this editor
/// only ever reads file[0] (the species/group table, a few KB) plus the one settings file for
/// whichever species/form is currently selected - never the whole multi-gigabyte `models` archive.
/// </para>
/// <para>
/// Known gap, confirmed live rather than assumed: Table 1's own per-species form count does NOT
/// cover Mega Evolutions - Venusaur's real record here says `count=1` despite Mega Venusaur having
/// its own real, separate in-game model, so this editor's form list (built directly from that same
/// count) has no way to reach Mega forms at all. Mega Evolution model routing goes through a
/// separate mechanism (`code_map.csv`'s own "MEGA FORMS" section, not yet mapped) - out of scope
/// for this pass; every base form and every real Table-1-tracked alt form (regional forms, costumes,
/// gender differences, ...) works correctly.
/// </para>
/// </summary>
public partial class ModelScaleEditor : Form
{
    private readonly GameConfig config;
    private readonly LazyGARCFile modelsGarc;
    private readonly byte[] file0;
    private readonly string[] speciesNames;
    private readonly int maxSpeciesId;
    private readonly HashSet<int> touchedFiles = [];
    private int currentSettingsFileIndex = -1;
    /// <summary>The raw stored height for whatever's currently selected, at the moment it was loaded - the NumericUpDown shows a PERCENTAGE relative to this, not the bare float, since a number like "0.9" means nothing to most people on its own; "100%" (unchanged) / "150%" (half again as big) does.</summary>
    private float baselineHeight = 1f;

    private sealed class Option
    {
        public int Value { get; set; }
        public string Display { get; set; } = "";
        public override string ToString() => Display;
    }

    public ModelScaleEditor()
    {
        InitializeComponent();

        config = Main.Config;
        if (config == null || config.Generation != 7)
        {
            WinFormsUtil.Error("No Gen 7 ROM loaded.", "Model scale editing is only supported for Gen 7 (Sun/Moon/Ultra Sun/Ultra Moon) right now.");
            Shown += (_, _) => Close();
            return;
        }

        modelsGarc = config.GetlzGARCData("models");
        if (modelsGarc == null)
        {
            WinFormsUtil.Error("Missing game data.", "This ROM doesn't have the 'models' (a094) archive.");
            Shown += (_, _) => Close();
            return;
        }
        file0 = modelsGarc[0];

        speciesNames = (string[])config.GetText(TextName.SpeciesNames).Clone();
        if (speciesNames.Length > 0) speciesNames[0] = "---";
        maxSpeciesId = config.MaxSpeciesID;

        PopulateSpeciesList();
        CB_Species.SelectedIndex = 0;
    }

    private void PopulateSpeciesList()
    {
        var opts = new List<Option>(maxSpeciesId);
        for (int i = 1; i <= maxSpeciesId; i++)
        {
            string name = i < speciesNames.Length && !string.IsNullOrWhiteSpace(speciesNames[i]) ? speciesNames[i] : $"Species {i}";
            opts.Add(new Option { Value = i, Display = $"{i:0000}: {name}" });
        }
        CB_Species.DisplayMember = nameof(Option.Display);
        CB_Species.ValueMember = nameof(Option.Value);
        CB_Species.DataSource = opts;
    }

    private List<Option> GetFormOptions(int speciesId)
    {
        int recordCount = ModelGroupInsertion.Table1RecordCount(file0, modelsGarc.FileCount);
        var opts = new List<Option>();
        if (speciesId >= 0 && speciesId < recordCount)
        {
            for (int form = 0; ; form++)
            {
                int? group = ModelGroupInsertion.GetGroupIndexForForm(file0, modelsGarc.FileCount, speciesId, form);
                if (group == null) break;
                opts.Add(new Option { Value = form, Display = form == 0 ? "(base)" : $"Form {form}" });
            }
        }
        if (opts.Count == 0) opts.Add(new Option { Value = -1, Display = "(no model data in this ROM)" });
        return opts;
    }

    private void CB_Species_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (CB_Species.SelectedItem is not Option opt) return;
        CB_Form.DisplayMember = nameof(Option.Display);
        CB_Form.ValueMember = nameof(Option.Value);
        CB_Form.DataSource = GetFormOptions(opt.Value);
    }

    private void CB_Form_SelectedIndexChanged(object sender, EventArgs e)
    {
        NUD_Height.Enabled = B_SaveOne.Enabled = false;
        currentSettingsFileIndex = -1;
        if (CB_Species.SelectedItem is not Option speciesOpt || CB_Form.SelectedItem is not Option formOpt || formOpt.Value < 0)
        {
            L_Status.Text = "No model data for this species/form in this ROM.";
            return;
        }

        int? group = ModelGroupInsertion.GetGroupIndexForForm(file0, modelsGarc.FileCount, speciesOpt.Value, formOpt.Value);
        if (group == null)
        {
            L_Status.Text = "No model data for this species/form in this ROM.";
            return;
        }

        currentSettingsFileIndex = ModelGroupInsertion.GetSettingsFileIndex(group.Value);
        try
        {
            var settings = ModelSettingsFile.Decode(modelsGarc[currentSettingsFileIndex]);
            if (!settings.HasModelHeight)
            {
                L_Status.Text = $"Model group {group.Value}'s settings file is too short to contain a height value.";
                return;
            }
            baselineHeight = settings.ModelHeight;
            NUD_Height.Value = 100m; // always starts at "100% = unchanged" for whatever's currently selected
            NUD_Height.Enabled = true;
            L_Status.Text = $"Model group {group.Value} - current raw scale value: {baselineHeight:0.0000} (100% below).";
        }
        catch (Exception ex)
        {
            L_Status.Text = $"Couldn't read this model's settings file: {ex.Message}";
        }
    }

    private void NUD_Height_ValueChanged(object sender, EventArgs e) => B_SaveOne.Enabled = currentSettingsFileIndex >= 0;

    private void B_SaveOne_Click(object sender, EventArgs e)
    {
        if (currentSettingsFileIndex < 0) return;
        float newHeight = baselineHeight * (float)(NUD_Height.Value / 100m);
        var settings = ModelSettingsFile.Decode(modelsGarc[currentSettingsFileIndex]);
        settings.ModelHeight = newHeight;
        modelsGarc[currentSettingsFileIndex] = settings.Encode();
        touchedFiles.Add(currentSettingsFileIndex);
        baselineHeight = newHeight; // the new value becomes the "100%" baseline for further tweaks to this same selection
        NUD_Height.Value = 100m;
        B_SaveOne.Enabled = false;
        B_SaveAll.Enabled = true;
        L_Status.Text = $"Model scale updated in memory (raw value now {newHeight:0.0000}) - click \"Save to ROM\" to write it.";
    }

    private void B_SaveAll_Click(object sender, EventArgs e)
    {
        if (touchedFiles.Count == 0)
        {
            WinFormsUtil.Alert("Nothing to save.", "No model height changes have been made yet.");
            return;
        }
        modelsGarc.Save();
        WinFormsUtil.Alert("Model heights saved!", $"{touchedFiles.Count} settings file(s) written back to the models archive.");
        touchedFiles.Clear();
        B_SaveAll.Enabled = false;
    }
}
