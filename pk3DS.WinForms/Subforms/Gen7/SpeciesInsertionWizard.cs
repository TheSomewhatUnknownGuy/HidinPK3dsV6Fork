using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Insertion;
using pk3DS.Core.Modding.Research;

namespace pk3DS.WinForms;

/// <summary>
/// UI for planning one or more species with InsertionPipeline, then applying each row's code.bin,
/// icon, and cry changes before the user reviews and saves the result.
/// </summary>
public partial class SpeciesInsertionWizard : Form
{
    private byte[][] personalReal;

    // The models archive is planned and streamed by InsertionPipeline. Status needs only its small
    // metadata member and count; cry remains loaded at build time for the optional cry edit.
    private byte[] modelsFile0;
    private int modelsFileCount;
    private int cryFileCount;
    private byte[][] cryFiles;

    // partyicon/menuicon (the box/party-list and summary-screen icons - a completely separate GARC
    // pair from "models", the 3D battle model) are small enough (a few thousand ~4KB members) that
    // there's no real cost concern loading them fully - unlike "models"/"cry" there's no lazy path
    // needed here, just a straight GetGARCData at build time.
    private byte[][] partyIconFiles, menuIconFiles;
    private bool hasPartyIcon, hasMenuIcon;

    private byte[] codeBin, exHeader, ncch0;
    private string codeBinPath, exHeaderPath, ncch0Path;
    private bool hasCry;

    private sealed class BuildOutcome
    {
        public InsertionPlan Plan;
        public byte[][] CryFiles = [];
        public byte[][] PartyIconFiles = [], MenuIconFiles = [];
        public byte[] CodeBin, ExHeader, NcchHeader0;
        public bool TouchedCodeBin;
        public int SpeciesAdded;

        /// <summary>Grown copy of TextName.SpeciesNames with every new species' typed-in name set -
        /// SpeciesInsertionOrchestrator itself never touches text data, so this is built and saved
        /// here in the wizard instead, the same way SMTE/MaisonEditor7 grow their own name lists.</summary>
        public string[] SpeciesNames = [];
    }

    private BuildOutcome lastOutcome;

    private sealed class DropdownOption
    {
        // WinForms data-binding (DataGridViewComboBoxColumn's ValueMember/DisplayMember) resolves
        // these by name through PropertyDescriptor reflection, which only enumerates PROPERTIES,
        // not fields - plain fields here threw "Field called Display does not exist" the moment
        // the grid tried to bind, confirmed the first time this form was actually opened.
        public int Value { get; set; }
        public string Display { get; set; } = "";
    }

    private static readonly DropdownOption NoneOption = new() { Value = -1, Display = "(none)" };
    private List<DropdownOption> modelOptions = [NoneOption];
    private List<DropdownOption> cryOptions = [NoneOption];

    public SpeciesInsertionWizard()
    {
        InitializeComponent();
        LoadGarcs(out _); // best-effort - populates dropdown data before the first row is created
        BuildDropdownOptionLists(); // compute the real option lists BEFORE any grid row exists,
                                     // so SetupGrid's columns start bound to real data instead of
                                     // the single-entry placeholder - avoids ever having to swap a
                                     // combo column's DataSource out from under an existing row.
        SetupGrid();
        RefreshStatus();
    }

    private void SetupGrid()
    {
        DGV_Species.AutoGenerateColumns = false;
        // Editable, not just a "New #N" placeholder label - this is the name actually written into
        // TextName.SpeciesNames for this species once Save to ROM runs, so every other editor's
        // species dropdown shows something the user chose rather than a generic placeholder.
        DGV_Species.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColLabel", HeaderText = "Species Name", Width = 110 });
        DGV_Species.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColExtraForms", HeaderText = "Extra Forms", Width = 75 });
        DGV_Species.Columns.Add(new DataGridViewCheckBoxColumn { Name = "ColGenderDiff", HeaderText = "Gender Diff", Width = 75 });

        var modelCol = new DataGridViewComboBoxColumn
        {
            Name = "ColCloneModel", HeaderText = "Clone Model", Width = 160,
            DataSource = modelOptions, ValueMember = "Value", DisplayMember = "Display",
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
        };
        DGV_Species.Columns.Add(modelCol);

        DGV_Species.Columns.Add(new DataGridViewCheckBoxColumn { Name = "ColDistinctCry", HeaderText = "Distinct Form Cry", Width = 105 });

        // Blank (the default) = this species doesn't evolve from anything and is its own lowest
        // evolution, matching AppendEvolutionBase's own self-referencing default. Set it to make
        // this row a middle/final stage of an existing family - or of an EARLIER row in this same
        // batch (enter the base species first, then its evolutions in later rows).
        DGV_Species.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColLowestEvo", HeaderText = "Lowest Evo (blank=itself)", Width = 130 });

        var cryCol = new DataGridViewComboBoxColumn
        {
            Name = "ColCloneCry", HeaderText = "Clone Cry Member", Width = 160,
            DataSource = cryOptions, ValueMember = "Value", DisplayMember = "Display",
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
        };
        DGV_Species.Columns.Add(cryCol);

        SyncGridRowCount();
    }

    /// <summary>Rebuilds the two dropdown columns' backing option lists from whatever GARC data is currently loaded.</summary>
    private void BuildDropdownOptionLists()
    {
        modelOptions = [NoneOption];
        if (personalReal != null)
        {
            string[] speciesNames = Main.Config?.GetText(TextName.SpeciesNames) ?? [];
            for (int i = 0; i < personalReal.Length; i++)
            {
                string name = i < speciesNames.Length && !string.IsNullOrWhiteSpace(speciesNames[i]) ? speciesNames[i] : "---";
                modelOptions.Add(new DropdownOption { Value = i, Display = $"{i:0000}: {name}" });
            }
        }

        cryOptions = [NoneOption];
        if (hasCry)
        {
            // Members 0-5332 are unrelated shared audio data and 5333 is the shared CSEQ (not a
            // per-form cry) - restrict the list to the meaningful base/form CWAR range this
            // session mapped, from CryBaseRegionStart through whatever the archive currently ends
            // at (so a ROM that already had species added in an earlier session still offers its
            // real, current cry members, not just the original 5334-5617). Only the member COUNT
            // is needed for this - never the audio bytes themselves.
            for (int i = CryTablePatcher.CryBaseRegionStart; i < cryFileCount; i++)
                cryOptions.Add(new DropdownOption { Value = i, Display = $"{i}" });
        }
    }

    /// <summary>
    /// Rebuilds the option lists and, if the grid's combo columns already exist, re-points them at
    /// the fresh lists. Never assigns `DataSource = null` as an intermediate step - doing that while
    /// existing rows still hold a value briefly leaves the column with zero valid items and throws
    /// "DataGridViewComboBoxCell value is not valid" the moment the grid revalidates, confirmed the
    /// first time this form was actually used. A direct reassignment to a new list object is always
    /// picked up without that transient gap, and the `(none)`/-1 sentinel (always the first entry)
    /// keeps any already-selected "(none)" cells valid across the swap either way.
    /// </summary>
    private void PopulateDropdownSources()
    {
        BuildDropdownOptionLists();
        if (DGV_Species.Columns["ColCloneModel"] is DataGridViewComboBoxColumn modelCol)
            modelCol.DataSource = modelOptions;
        if (DGV_Species.Columns["ColCloneCry"] is DataGridViewComboBoxColumn cryCol)
            cryCol.DataSource = cryOptions;
    }

    private void SyncGridRowCount()
    {
        int wanted = (int)NUD_SpeciesCount.Value;
        while (DGV_Species.Rows.Count < wanted)
        {
            int idx = DGV_Species.Rows.Add();
            var row = DGV_Species.Rows[idx];
            row.Cells["ColLabel"].Value = $"New Species {idx + 1}";
            row.Cells["ColExtraForms"].Value = "0";
            row.Cells["ColGenderDiff"].Value = false;
            row.Cells["ColCloneModel"].Value = -1;
            row.Cells["ColDistinctCry"].Value = false;
            row.Cells["ColCloneCry"].Value = -1;
            row.Cells["ColLowestEvo"].Value = "";
        }
        while (DGV_Species.Rows.Count > wanted)
            DGV_Species.Rows.RemoveAt(DGV_Species.Rows.Count - 1);
    }

    private void NUD_SpeciesCount_ValueChanged(object sender, EventArgs e) => SyncGridRowCount();

    /// <summary>
    /// Loads everything needed to show status and configure the grid - the small per-species GARCs
    /// eagerly, but "models"/"cry" only far enough to get file 0 and a member count. Safe to call
    /// as often as needed (constructor, every RefreshStatus) since it never touches the bulk of
    /// either large archive.
    /// </summary>
    private bool LoadGarcs(out string error)
    {
        error = "";
        var config = Main.Config;
        if (config == null || config.Generation != 7)
        { error = "Species insertion is only supported for Gen 7 (Sun/Moon/Ultra Sun/Ultra Moon) ROMs."; return false; }

        var personal = config.GetGARCData("personal");
        var evolution = config.GetGARCData("evolution");
        var levelup = config.GetGARCData("levelup");
        var megaevo = config.GetGARCData("megaevo");
        var evolutionBase = config.GetGARCData("evolutionbase");

        if (personal?.Files == null || evolution?.Files == null || levelup?.Files == null || megaevo?.Files == null || evolutionBase?.Files == null)
        { error = "Core per-species game data (personal/evolution/levelup/megaevo/evolutionbase) could not be read from this ROM."; return false; }

        int personalEntryLen = personal.Files[0].Length;
        personalReal = personal.Files.Where(f => f != null && f.Length == personalEntryLen).ToArray();

        try
        {
            var modelsLazy = config.GetlzGARCData("models");
            if (modelsLazy == null || modelsLazy.FileCount == 0)
            { error = "This ROM doesn't have the expanded 'models' (a094) archive this feature needs."; return false; }
            modelsFile0 = modelsLazy[0];
            modelsFileCount = modelsLazy.FileCount;
        }
        catch (Exception ex)
        {
            error = $"Couldn't read the 'models' (a094) archive: {ex.Message}";
            return false;
        }

        try
        {
            var cryLazy = config.GetlzGARCData("cry");
            hasCry = cryLazy != null;
            cryFileCount = cryLazy?.FileCount ?? 0;
        }
        catch
        {
            hasCry = false;
            cryFileCount = 0;
        }

        return true;
    }

    /// <summary>
    /// Loads the optional cry and icon arrays for code-side edits. Model members are streamed by
    /// the Core insertion plan and are never loaded into this form.
    /// </summary>
    private bool LoadFullModelAndCryData(out string error)
    {
        error = "";
        var config = Main.Config;
        cryFiles = hasCry ? config.GetGARCData("cry")?.Files : null;
        hasCry = cryFiles != null;

        // "Clone Model" previously only ever cloned the 3D battle model - the box/party-list and
        // summary-screen icons (partyicon/a257, menuicon/a/0/6/2) are separate GARCs entirely, so a
        // cloned species kept showing the ROM's pre-filled placeholder icon instead of the
        // template's. Loaded here (not lazily) since both are small (~4KB/member, a few thousand
        // members) compared to "models"/"cry".
        partyIconFiles = config.GetGARCData("partyicon")?.Files;
        hasPartyIcon = partyIconFiles != null;
        menuIconFiles = config.GetGARCData("menuicon")?.Files;
        hasMenuIcon = menuIconFiles != null;

        return true;
    }

    private bool LoadCodeBin(out string error)
    {
        error = "";
        if (string.IsNullOrEmpty(Main.ExeFSPath) || !Directory.Exists(Main.ExeFSPath))
        { error = "No ExeFS is loaded - open a full ROM (not just RomFS). Every species insertion needs " +
                  "code.bin to raise its species-count ceiling, or the new species displays as an Egg in-game."; return false; }

        codeBinPath = ExeFS.ResolveCodeBin(Main.ExeFSPath);
        if (!File.Exists(codeBinPath))
        { error = "code.bin was not found in the loaded ExeFS."; return false; }

        string romRoot = Path.GetDirectoryName(Main.RomFSPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        exHeaderPath = Path.Combine(romRoot ?? "", "ExHeader.bin");
        ncch0Path = Path.Combine(romRoot ?? "", "HeaderNCCH0.bin");
        if (!File.Exists(exHeaderPath))
        { error = $"ExHeader.bin was not found next to RomFS/ExeFS at:\n{exHeaderPath}\n\nNeeded in case a distinct form cry (or anything else) has to grow code.bin when there's no room left."; return false; }

        codeBin = File.ReadAllBytes(codeBinPath);
        exHeader = File.ReadAllBytes(exHeaderPath);
        ncch0 = File.Exists(ncch0Path) ? File.ReadAllBytes(ncch0Path) : null;
        return true;
    }

    private void RefreshStatus()
    {
        RomKind kind = RomLayout.DetectFromRomFs(Main.RomFSPath, out _, out _);
        if (pk3DS.Core.Modding.UhuruGuard.Active) kind = RomKind.Uhuru;
        string layoutRefusal = RomLayout.Refusal(kind, species: true);
        if (layoutRefusal != null)
        {
            L_Status.Text = "Not available:\n" + layoutRefusal;
            B_Build.Enabled = false;
            return;
        }

        if (!LoadGarcs(out string error))
        {
            L_Status.Text = "Not available:\n" + error;
            B_Build.Enabled = false;
            return;
        }

        PopulateDropdownSources();

        // Models' table 1 only ever holds one record per real BASE species (0..MaxSpeciesID) - a
        // Mega Evolution/regional form never gets its own table-1 slot, it just adds extra model
        // GROUPS under its base species' own record. So table 1's free-slot search legitimately
        // lands on the same boundary personal's own alt-form block starts at (not personal's full
        // length, which also counts every alt-form entry) - confirmed by a full, byte-level dry run
        // against real data: inserting there correctly shifts the alt-form block out by one and
        // fixes up all 207 real species' FormStatsIndex cross-references, with zero unexpected
        // byte changes anywhere else.
        int nextSlot = ModelGroupInsertion.FindNextFreeSpeciesSlot(modelsFile0, modelsFileCount);
        int nextGroup = ModelGroupInsertion.ComputeNextFreeGroupIndex(modelsFile0, modelsFileCount);

        L_Status.Text =
            $"Personal-table entries: {personalReal.Length}\n" +
            $"Next free species slot: {(nextSlot >= 0 ? nextSlot.ToString() : "none left - grow the models table first")}\n" +
            $"Next free model group: {(nextGroup >= 0 ? nextGroup.ToString() : "?")}\n" +
            $"cry GARC members: {(hasCry ? cryFileCount.ToString() : "not present on this ROM")}\n";

        B_Build.Enabled = nextSlot >= 0;
    }

    private static int ParseIntOrDefault(object cellValue, int defaultValue)
    {
        string s = cellValue?.ToString()?.Trim();
        return !string.IsNullOrEmpty(s) && int.TryParse(s, out int v) ? v : defaultValue;
    }

    private void B_Build_Click(object sender, EventArgs e)
    {
        int speciesCount = (int)NUD_SpeciesCount.Value;
        var rows = new List<SpeciesInsertionRequest>();
        var planRows = new List<SpeciesRow>();
        var names = new List<string>();
        for (int i = 0; i < speciesCount && i < DGV_Species.Rows.Count; i++)
        {
            var gridRow = DGV_Species.Rows[i];
            int extraForms = Math.Max(0, ParseIntOrDefault(gridRow.Cells["ColExtraForms"].Value, 0));
            bool genderDiff = gridRow.Cells["ColGenderDiff"].Value is true;
            int cloneModel = gridRow.Cells["ColCloneModel"].Value is int cm ? cm : -1;
            bool distinctCry = gridRow.Cells["ColDistinctCry"].Value is true && extraForms > 0;
            int cloneCry = gridRow.Cells["ColCloneCry"].Value is int cc ? cc : -1;
            string name = gridRow.Cells["ColLabel"].Value?.ToString()?.Trim();
            int lowestEvo = ParseIntOrDefault(gridRow.Cells["ColLowestEvo"].Value, -1);

            var request = new SpeciesInsertionRequest
            {
                ExtraFormGroups = extraForms,
                HasGenderDifference = genderDiff,
                CloneModelFromSlot = cloneModel,
                AddDistinctFormCry = distinctCry,
                CloneCryFromMember = cloneCry,
                LowestEvolutionSpeciesId = lowestEvo,
            };
            rows.Add(request);
            planRows.Add(new SpeciesRow
            {
                ExtraFormGroups = extraForms,
                GenderDiff = genderDiff,
                CloneModelFromSlot = cloneModel,
                LowestEvolution = lowestEvo,
            });
            names.Add(string.IsNullOrWhiteSpace(name) ? $"New Species {i + 1}" : name);
        }

        InsertionPlan plan = InsertionPipeline.PlanSpecies(Main.RomFSPath, planRows,
            new InsertionOptions(), out string refusal);
        RomKind kind = RomLayout.DetectFromRomFs(Main.RomFSPath, out _, out _);
        if (pk3DS.Core.Modding.UhuruGuard.Active) kind = RomKind.Uhuru;
        string layoutRefusal = RomLayout.Refusal(kind, species: true);
        if (plan == null || layoutRefusal != null)
        {
            WinFormsUtil.Error("Can't build.", layoutRefusal ?? refusal ?? "The insertion plan was refused.");
            B_Save.Enabled = false;
            lastOutcome = null;
            return;
        }

        var log = new StringBuilder();
        log.AppendLine("--- Core insertion plan ---");
        foreach (string line in plan.Log) log.AppendLine(line);
        foreach (string violation in plan.Violations) log.AppendLine("VIOLATION: " + violation);
        if (plan.Violations.Count > 0)
        {
            TB_Log.Text = log.ToString();
            WinFormsUtil.Error("Insertion validation failed.", string.Join(Environment.NewLine, plan.Violations));
            B_Save.Enabled = false;
            lastOutcome = null;
            return;
        }

        if (!LoadCodeBin(out string codeError))
        {
            WinFormsUtil.Error("Can't insert a species.", codeError);
            return;
        }

        // Load only the optional cry and icon arrays; model members are streamed by the plan.
        if (!LoadFullModelAndCryData(out string loadError))
        {
            WinFormsUtil.Error("Can't build.", loadError);
            return;
        }

        byte[][] curCry = cryFiles;
        byte[][] curPartyIcon = partyIconFiles, curMenuIcon = menuIconFiles;
        byte[] curCodeBin = codeBin, curExHeader = exHeader, curNcch0 = ncch0;
        bool touchedCodeBin = false;
        string[] curSpeciesNames = Main.Config.GetText(TextName.SpeciesNames);

        for (int i = 0; i < rows.Count; i++)
        {
            var result = SpeciesInsertionOrchestrator.ApplyCodeSide(curCodeBin, curExHeader, curNcch0,
                plan.NewSpeciesIds[i], rows[i], curPartyIcon, curMenuIcon, curCry);
            log.AppendLine($"--- Species #{i + 1} ---");
            log.AppendLine(result.Describe());

            if (!result.Success)
            {
                log.AppendLine();
                log.AppendLine($"Build FAILED at species #{i + 1}: {result.Error}");
                log.AppendLine("Nothing will be saved - fix this row (or an earlier one) and rebuild.");
                TB_Log.Text = log.ToString();
                B_Save.Enabled = false;
                lastOutcome = null;
                return;
            }

            curCodeBin = result.CodeBin;
            curExHeader = result.ExHeader;
            curNcch0 = result.NcchHeader0;
            curCry = hasCry ? result.CryFiles : null;
            curPartyIcon = hasPartyIcon ? result.PartyIconFiles : null;
            curMenuIcon = hasMenuIcon ? result.MenuIconFiles : null;

            int speciesId = plan.NewSpeciesIds[i];
            if (speciesId >= curSpeciesNames.Length) Array.Resize(ref curSpeciesNames, speciesId + 1);
            curSpeciesNames[speciesId] = names[i];
            touchedCodeBin |= result.TouchedCodeBin;
        }

        lastOutcome = new BuildOutcome
        {
            Plan = plan,
            CryFiles = curCry ?? [],
            PartyIconFiles = curPartyIcon ?? [],
            MenuIconFiles = curMenuIcon ?? [],
            CodeBin = curCodeBin,
            ExHeader = curExHeader,
            NcchHeader0 = curNcch0,
            TouchedCodeBin = touchedCodeBin,
            SpeciesAdded = rows.Count,
            SpeciesNames = curSpeciesNames,
        };

        log.AppendLine();
        log.AppendLine($"Build succeeded for all {rows.Count} new species - review the log above, then click \"Save to ROM\" to write it.");
        TB_Log.Text = log.ToString();
        B_Save.Enabled = true;
    }

    private void B_Save_Click(object sender, EventArgs e)
    {
        if (lastOutcome == null) return;
        var config = Main.Config;
        if (config == null) return;
        if (lastOutcome.TouchedCodeBin && pk3DS.Core.Modding.UhuruGuard.RefusalFor(codeBinPath) is { } uhuru)
        { WinFormsUtil.Alert(uhuru); return; }

        try
        {
            using var tx = new GarcTransaction();
            lastOutcome.Plan.Stage(tx, Main.RomFSPath, Main.RomFSPath);
            var outputProblems = pk3DS.Core.Modding.Insertion.InsertionPipeline.VerifyOutput(Main.RomFSPath, Main.RomFSPath, lastOutcome.Plan);
            if (outputProblems.Count > 0)
                throw new InvalidDataException("Insertion output check failed: " + string.Join("; ", outputProblems));
            if (lastOutcome.PartyIconFiles.Length > 0)
                StageArrayArchive(tx, config.GetGARCPath("partyicon"), lastOutcome.PartyIconFiles);
            if (lastOutcome.MenuIconFiles.Length > 0)
                StageArrayArchive(tx, config.GetGARCPath("menuicon"), lastOutcome.MenuIconFiles);
            if (lastOutcome.CryFiles.Length > 0)
                StageArrayArchive(tx, config.GetGARCPath("cry"), lastOutcome.CryFiles);
            tx.Commit();
        }
        catch (Exception ex)
        {
            WinFormsUtil.Error("Saving insertion GARCs failed.", $"{ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (lastOutcome.TouchedCodeBin)
        {
            BackupThenWrite(codeBinPath, lastOutcome.CodeBin);
            BackupThenWrite(exHeaderPath, lastOutcome.ExHeader);
            if (lastOutcome.NcchHeader0 is { Length: > 0 } && File.Exists(ncch0Path))
                BackupThenWrite(ncch0Path, lastOutcome.NcchHeader0);
        }

        if (lastOutcome.SpeciesNames.Length > 0)
        {
            // SetText alone only updates the in-memory GameTextStrings cache - SaveText actually
            // writes the gametext GARC back to disk (see SMTE's Add Trainer(s) for why both matter).
            Main.Config.SetText(TextName.SpeciesNames, lastOutcome.SpeciesNames);
            Main.Config.SaveText(TextName.SpeciesNames);
        }

        config.InitializePersonal();
        config.Info?.RecalculateLimits(config);
        config.InitializeEvos();

        string savedList = "models, Photo Club" + (lastOutcome.CryFiles.Length > 0 ? ", cry" : "") +
                            (lastOutcome.PartyIconFiles.Length > 0 ? ", partyicon" : "") +
                            (lastOutcome.MenuIconFiles.Length > 0 ? ", menuicon" : "") +
                            ", personal, evolution, levelup, megaevo, evolutionbase, species names";
        if (lastOutcome.TouchedCodeBin) savedList += ", code.bin, ExHeader.bin";

        WinFormsUtil.Alert($"{lastOutcome.SpeciesAdded} new species inserted and saved!",
            $"Wrote: {savedList}\n\n" +
            (lastOutcome.TouchedCodeBin
                ? "code.bin was resized - remember to also copy ExHeader.bin into the mods folder's " +
                  "ROOT (next to exefs/romfs, not inside exefs) as \"exheader.bin\" when deploying.\n\n"
                : "") +
            "Please reload your ROM in pk3DS (File -> Open) so every editor picks up the new data.");

        B_Save.Enabled = false;
        lastOutcome = null;
        RefreshStatus();
    }

    private static void StageArrayArchive(GarcTransaction tx, string path, byte[][] files)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            throw new FileNotFoundException("optional insertion GARC is missing", path);
        GARC.GARCFile header = GARC.UnpackGARC(path);
        long padding = header.ContentPadToNearest;
        if (padding <= 0 || padding > int.MaxValue)
            throw new InvalidDataException($"{path} has invalid GARC padding {padding}");
        tx.Stage(path, new GARC.ArrayGARCFileProvider(files), header.Version, checked((int)padding));
    }

    private static void BackupThenWrite(string path, byte[] data)
    {
        string bakPath = path + ".bak";
        if (!File.Exists(bakPath)) File.Copy(path, bakPath);
        File.WriteAllBytes(path, data);
    }
}
