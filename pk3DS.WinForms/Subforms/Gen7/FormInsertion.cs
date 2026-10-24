using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Insertion;

namespace pk3DS.WinForms;

public partial class FormInsertion : Form
{
    private readonly byte[][] personalFiles;
    private readonly byte[][] evolutionFiles;
    private readonly byte[][] levelupFiles;
    private readonly byte[][] eggmoveFiles;
    private readonly string[] speciesNames;
    private readonly string[] entryNames;

    public FormInsertion(byte[][] personal, byte[][] evolution, byte[][] levelup, byte[][] eggmoves, string[] species, string[] entries, int[] bases, int[] forms)
    {
        if (personal.Length > 0 && personal.Last().Length != personal[0].Length)
            personal = personal.Take(personal.Length - 1).ToArray();

        personalFiles = personal;
        evolutionFiles = evolution;
        levelupFiles = levelup;
        eggmoveFiles = eggmoves;
        speciesNames = species;
        entryNames = entries;

        InitializeComponent();

        CB_TargetSpecies.Items.AddRange(speciesNames.Take(Math.Min(speciesNames.Length, Main.Config.MaxSpeciesID + 1)).ToArray());
        CB_TargetSpeciesEnd.Items.AddRange(speciesNames.Take(Math.Min(speciesNames.Length, Main.Config.MaxSpeciesID + 1)).ToArray());
        CB_CopyFrom.Items.AddRange(entryNames);

        CB_TargetSpecies.SelectedIndex = 1;
        CB_TargetSpeciesEnd.SelectedIndex = 1;
        CB_CopyFrom.SelectedIndex = 1;

        RTB_BatchList.Text = "Bulbasaur\nIvysaur\nVenusaur";
    }

    private void B_Insert_Click(object sender, EventArgs e)
    {
        try
        {
            var kind = RomLayout.DetectFromRomFs(Main.RomFSPath, out _, out _);
            if (pk3DS.Core.Modding.UhuruGuard.Active) kind = RomKind.Uhuru;
            string layoutRefusal = RomLayout.Refusal(kind, species: false);
            if (layoutRefusal != null)
            {
                WinFormsUtil.Error(layoutRefusal);
                return;
            }

            List<int> speciesToInsert = [];
            if (CHK_BatchList.Checked)
            {
                foreach (string name in RTB_BatchList.Lines)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    int idx = Array.FindIndex(speciesNames, s => s.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (idx > 0) speciesToInsert.Add(idx);
                }
            }
            else if (CHK_Batch.Checked)
            {
                int start = CB_TargetSpecies.SelectedIndex;
                int end = CB_TargetSpeciesEnd.SelectedIndex;
                if (start > 0 && end >= start)
                {
                    for (int i = start; i <= end; i++) speciesToInsert.Add(i);
                }
            }
            else
            {
                int idx = CB_TargetSpecies.SelectedIndex;
                if (idx > 0) speciesToInsert.Add(idx);
            }

            if (speciesToInsert.Count == 0)
            {
                WinFormsUtil.Error("No valid species selected for insertion.");
                return;
            }

            int count = (int)NUD_FormCount.Value;
            bool isBatch = CHK_BatchList.Checked || CHK_Batch.Checked;
            int copyIndex = isBatch ? -1 : CB_CopyFrom.SelectedIndex;
            if (!isBatch && copyIndex < 0)
            {
                WinFormsUtil.Error("Please select a template species to copy from.");
                return;
            }

            if (!isBatch && copyIndex >= personalFiles.Length)
            {
                WinFormsUtil.Error($"Template index {copyIndex} exceeds personal data bounds ({personalFiles.Length}).");
                return;
            }

            string speciesSummary = speciesToInsert.Count > 1
                ? $"{speciesToInsert.Count} species"
                : (speciesToInsert[0] < speciesNames.Length ? speciesNames[speciesToInsert[0]] : $"Entry #{speciesToInsert[0]}");
            string templateName = isBatch ? "Base Form of each Species" : (copyIndex < entryNames.Length ? entryNames[copyIndex] : $"Entry #{copyIndex}");

            if (WinFormsUtil.Prompt(MessageBoxButtons.YesNo, $"Insert {count} forms for {speciesSummary}?", "Template: " + templateName) != DialogResult.Yes)
                return;

            foreach (int species in speciesToInsert)
            {
                if (species < personalFiles.Length && personalFiles[species] != null && personalFiles[species].Length > 0x20)
                {
                    int currentForms = personalFiles[species][0x20];
                    if (currentForms + count > 255)
                    {
                        WinFormsUtil.Error($"Form count overflow for species #{species}. Maximum forms per species is 255 (current: {currentForms}, adding: {count}).");
                        return;
                    }
                }
            }

            SpeciesTables edited = SpeciesTables.Load(Main.RomFSPath, out string loadError);
            if (edited == null)
            {
                WinFormsUtil.Error("Insertion failed.", loadError);
                return;
            }

            // Keep current editor changes for these tables; megaevo and evolutionbase come from disk.
            edited.Personal = personalFiles;
            edited.Evolution = evolutionFiles;
            edited.Levelup = levelupFiles;
            edited.EggMove = eggmoveFiles;

            byte[] codeBin = null;
            if (!string.IsNullOrEmpty(Main.ExeFSPath) && Directory.Exists(Main.ExeFSPath))
            {
                string codePath = ExeFS.ResolveCodeBin(Main.ExeFSPath);
                if (File.Exists(codePath)) codeBin = File.ReadAllBytes(codePath);
            }

            var forms = new List<(int Species, int Count)>(speciesToInsert.Count);
            foreach (int species in speciesToInsert) forms.Add((species, count));
            var options = new InsertionOptions { CodeBin = codeBin };
            InsertionPlan plan = InsertionPipeline.PlanForms(Main.RomFSPath, edited, forms,
                isBatch ? -1 : copyIndex, options, out string refusal);
            if (plan == null)
            {
                WinFormsUtil.Error("Insertion refused.", refusal ?? "The insertion plan was refused.");
                return;
            }
            if (plan.Violations.Count > 0)
            {
                WinFormsUtil.Error("Insertion validation failed.", string.Join(Environment.NewLine, plan.Violations));
                return;
            }

            using (var tx = new GarcTransaction())
            {
                plan.Stage(tx, Main.RomFSPath, Main.RomFSPath);
                List<string> outputProblems = InsertionPipeline.VerifyOutput(Main.RomFSPath, Main.RomFSPath, plan);
                if (outputProblems.Count > 0)
                {
                    WinFormsUtil.Error("Insertion output check failed; nothing was written.", string.Join(Environment.NewLine, outputProblems));
                    return;
                }
                tx.Commit();
            }

            ResultPersonal = plan.After.Personal;
            ResultEvolution = plan.After.Evolution;
            ResultLevelUp = plan.After.Levelup;
            ResultEggMoves = plan.After.EggMove;

            WinFormsUtil.Alert("Insertion complete!", $"Added forms for {speciesToInsert.Count} species.");
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            WinFormsUtil.Error("Insertion failed.", ex.Message + "\n" + ex.StackTrace);
        }
    }

    public byte[][] ResultPersonal;
    public byte[][] ResultEvolution;
    public byte[][] ResultLevelUp;
    public byte[][] ResultEggMoves;
}
