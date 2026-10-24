using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;

namespace pk3DS.WinForms;

/// <summary>
/// Lets any species' box/party-list icon (`partyicon`, a257) and summary-screen icon (`menuicon`,
/// a/0/6/2) be viewed, replaced with another existing species/form's icon already in the ROM, or
/// replaced with a brand-new image imported from disk.
/// <para>
/// Member resolution is driven by <see cref="SpeciesIconTable"/> - code.bin's OWN icon-lookup ARM
/// routine's real data tables, disassembled and verified this session - not a filename/ordinal guess.
/// That correction came from two real, live bugs the user caught: "Mewtwo is actually Marowak" (a
/// naive "member == species id" assumption, wrong for every stock species past the first Mega
/// Evolution) and, after switching to a filename-derived catalog as a first fix, "Gen 6/7 forms
/// across the board are messed up" and "Gen 8/9 alternative forms do not show" - the catalog's
/// best-effort ordinal form-matching turned out wrong for ~12% of stock species (Pikachu's 8 costume
/// forms, for one, are stored in a completely different physical order than their real form number),
/// and it had no way at all to find Gen 8/9 alt-form icons (Dudunsparce's second segment, Zacian's
/// Crowned form, ...), which turn out to live in a separate "compact added form icon" table the game
/// itself falls back to. <see cref="SpeciesIconTable"/> reads both of code.bin's real tables directly
/// instead, verified against real ROM data (every one of 1330 real personal-table species/form
/// combinations in this ROM resolves to a real icon member through it, including previously
/// "invisible" cases like Chespin/Greninja and Dudunsparce's second form).
/// </para>
/// <para>
/// If no ExeFS is loaded (or this pk3DS build's documented code.bin routine doesn't match what's
/// actually in the ROM), falls back to <see cref="PartyIconCatalog"/>'s filename-derived best-effort
/// matching instead of refusing to work - clearly flagged as degraded, not silently assumed correct.
/// </para>
/// </summary>
public partial class IconEditor : Form
{
    private byte[][] curPartyIconFiles, curMenuIconFiles;
    private SpeciesIconTable iconTable;
    private bool useAuthoritative;
    private Dictionary<int, List<PartyIconEntry>> bySpecies;
    private Dictionary<int, string> tagByMember;
    private bool formulaRemapped;
    private string[] speciesNames;
    private int maxSpeciesId;
    private int[] baseForms = [], formVal = [];
    private Dictionary<int, List<int>> altFormsBySpecies = [];
    private bool dirty;

    private sealed class Option
    {
        public int Value { get; set; }
        public string Display { get; set; } = "";
        public override string ToString() => Display;
    }

    public IconEditor()
    {
        InitializeComponent();

        var config = Main.Config;
        if (config == null || config.Generation != 7)
        {
            WinFormsUtil.Error("No Gen 7 ROM loaded.", "Icon editing is only supported for Gen 7 (Sun/Moon/Ultra Sun/Ultra Moon) right now.");
            Shown += (_, _) => Close();
            return;
        }

        var partyGarc = config.GetGARCData("partyicon");
        var menuGarc = config.GetGARCData("menuicon");
        if (partyGarc?.Files == null || menuGarc?.Files == null)
        {
            WinFormsUtil.Error("Missing game data.", "This ROM doesn't have the 'partyicon' (a257) / 'menuicon' (a/0/6/2) archives.");
            Shown += (_, _) => Close();
            return;
        }

        curPartyIconFiles = partyGarc.Files;
        curMenuIconFiles = menuGarc.Files;

        speciesNames = (string[])config.GetText(TextName.SpeciesNames).Clone();
        if (speciesNames.Length > 0) speciesNames[0] = "---";
        maxSpeciesId = config.MaxSpeciesID;

        var catalog = PartyIconCatalog.Build(curPartyIconFiles);
        bySpecies = catalog.GroupBySpecies();
        tagByMember = catalog.Where(e => !string.IsNullOrEmpty(e.FormTag)).ToDictionary(e => e.Member, e => e.FormTag);

        string codeBinLoadError = TryLoadCodeBin(config, out byte[] codeBin);
        if (codeBin != null)
        {
            iconTable = SpeciesIconTable.Analyze(codeBin, config.Version);
            formulaRemapped = IconEggSentinelPatcher.IsRemapped(codeBin) ?? false;
            useAuthoritative = iconTable.Success;
            if (!useAuthoritative)
                L_Status.Text = $"⚠ FALLBACK MODE: best-effort icon matching, NOT code.bin's own authoritative table " +
                                 $"(didn't match what this was verified against: {iconTable.Error}). " +
                                 "This mode is wrong for roughly 1 in 8 species/forms - the sprite you pick may not be the one that actually ends up on the Pokemon. " +
                                 "Every copy is still logged in the status line so a wrong one can be spotted and redone.";
        }
        else
        {
            L_Status.Text = $"⚠ FALLBACK MODE: best-effort icon matching, NOT code.bin's own authoritative table ({codeBinLoadError}). " +
                             "This mode is wrong for roughly 1 in 8 species/forms - load a full ROM (File -> Open, with ExeFS) for guaranteed-correct results.";
        }

        // The fallback path is the exact failure mode "the icon editor just displaces sprites onto
        // the wrong Pokemon" describes (~12% of species/forms, verified against real ROM data - see
        // the class doc comment above). A plain black status label reads identically to the normal
        // instructional text, so a user in fallback mode has no visual cue their edits aren't
        // guaranteed correct. Make the two states impossible to confuse at a glance.
        if (!useAuthoritative)
        {
            L_Status.ForeColor = Color.FromArgb(210, 40, 40);
            L_Status.Font = new Font(L_Status.Font, FontStyle.Bold);
        }
        else
        {
            L_Status.ForeColor = Color.FromArgb(30, 140, 60);
            L_Status.Text = "✓ Using code.bin's own authoritative icon table - verified against every species/form in this ROM. " + L_Status.Text;
        }

        // Same real personal-table form data every other per-species editor in this codebase already
        // reads (PersonalEditor7, MegaEvoEditor7, EvolutionEditor7...) - gives a real alt-form list
        // per species (baseForms[k]/formVal[k] for personal-table index k) instead of guessing.
        if (config.Personal != null)
        {
            var altForms = config.Personal.GetFormList(speciesNames, maxSpeciesId);
            config.Personal.GetPersonalEntryList(altForms, speciesNames, maxSpeciesId, out baseForms, out formVal);
            for (int k = 0; k < formVal.Length; k++)
            {
                if (formVal[k] <= 0) continue;
                int b = baseForms[k];
                if (!altFormsBySpecies.TryGetValue(b, out var list))
                    altFormsBySpecies[b] = list = [];
                list.Add(k);
            }
        }

        PopulateSpeciesList(CB_Species);
        PopulateSpeciesList(CB_SourceSpecies);
        CB_Species.SelectedIndex = 0;
        CB_SourceSpecies.SelectedIndex = 0;

        PB_Party.AllowDrop = true;
        PB_Party.DragEnter += Image_DragEnter;
        PB_Party.DragDrop += Image_DragDrop;
        PB_Menu.AllowDrop = true;
        PB_Menu.DragEnter += Image_DragEnter;
        PB_Menu.DragDrop += Image_DragDrop;
    }

    private static string TryLoadCodeBin(GameConfig config, out byte[] codeBin)
    {
        codeBin = null;
        if (string.IsNullOrEmpty(Main.ExeFSPath) || !Directory.Exists(Main.ExeFSPath))
            return "no ExeFS is loaded";
        try
        {
            string path = ExeFS.ResolveCodeBin(Main.ExeFSPath);
            if (!File.Exists(path)) return "code.bin was not found in the loaded ExeFS";
            codeBin = File.ReadAllBytes(path);
            return "";
        }
        catch (Exception ex) { return ex.Message; }
    }

    private void PopulateSpeciesList(ComboBox cb)
    {
        var opts = new List<Option>(maxSpeciesId);
        for (int i = 1; i <= maxSpeciesId; i++)
        {
            string name = i < speciesNames.Length && !string.IsNullOrWhiteSpace(speciesNames[i]) ? speciesNames[i] : $"Species {i}";
            opts.Add(new Option { Value = i, Display = $"{i:0000}: {name}" });
        }
        cb.DisplayMember = nameof(Option.Display);
        cb.ValueMember = nameof(Option.Value);
        cb.DataSource = opts;
    }

    /// <summary>Resolves a species' Nth form (0 = base) to an icon member - code.bin's own authoritative table when available, otherwise a best-effort fallback.</summary>
    private int ResolveMember(int speciesId, int form)
    {
        if (useAuthoritative)
        {
            int? m = iconTable.ResolveMember(speciesId, form);
            if (m.HasValue) return m.Value;
            return -1;
        }

        if (form == 0)
            return speciesId >= SpeciesIconLayout.StockSpeciesCount
                ? SpeciesIconLayout.ExpansionRegionMember(speciesId, formulaRemapped)
                : bySpecies.GetDefaultMember(speciesId) ?? -1;

        if (speciesId >= SpeciesIconLayout.StockSpeciesCount) return -1;
        if (!bySpecies.TryGetValue(speciesId, out var variants)) return -1;
        int? defaultMember = bySpecies.GetDefaultMember(speciesId);
        var alts = variants.Where(v => v.Member != defaultMember).OrderBy(v => v.Member).ToList();
        int idx = form - 1;
        return idx >= 0 && idx < alts.Count ? alts[idx].Member : -1;
    }

    /// <summary>Every selectable form for a species: "(default)" plus every real alt form the personal table records, each resolved to an icon member.</summary>
    private List<Option> GetFormOptions(int speciesId)
    {
        var opts = new List<Option> { new() { Value = ResolveMember(speciesId, 0), Display = "(default)" } };

        if (altFormsBySpecies.TryGetValue(speciesId, out var altEntries))
        {
            foreach (int personalIndex in altEntries)
            {
                int form = formVal[personalIndex];
                int member = ResolveMember(speciesId, form);
                string tag = member >= 0 && tagByMember.TryGetValue(member, out var t) ? t : null;
                string label = member < 0
                    ? $"Form {form} (no icon in this ROM)"
                    : string.IsNullOrEmpty(tag) ? $"Form {form}" : $"Form {form} ({tag})";
                opts.Add(new Option { Value = member, Display = label });
            }
        }

        if (opts[0].Value < 0 && opts.Count == 1)
            opts[0].Display = "(no icon found in this ROM)";
        return opts;
    }

    private void CB_Species_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (CB_Species.SelectedItem is not Option opt) return;
        CB_Form.DisplayMember = nameof(Option.Display);
        CB_Form.ValueMember = nameof(Option.Value);
        CB_Form.DataSource = GetFormOptions(opt.Value);
    }

    private void CB_Form_SelectedIndexChanged(object sender, EventArgs e) => RefreshPreview(PB_Party, PB_Menu, CurrentTargetMember());

    private void CB_SourceSpecies_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (CB_SourceSpecies.SelectedItem is not Option opt) return;
        CB_SourceForm.DisplayMember = nameof(Option.Display);
        CB_SourceForm.ValueMember = nameof(Option.Value);
        CB_SourceForm.DataSource = GetFormOptions(opt.Value);
    }

    private void CB_SourceForm_SelectedIndexChanged(object sender, EventArgs e) => RefreshPreview(PB_SourceParty, PB_SourceMenu, CurrentSourceMember());

    private int CurrentTargetMember() => CB_Form.SelectedItem is Option o ? o.Value : -1;
    private int CurrentSourceMember() => CB_SourceForm.SelectedItem is Option o ? o.Value : -1;

    // Species inserted past 1025 resolve (1154+(id-808)) onto a member the compact alt-form table
    // already owns, so writing it silently repaints that other Pokemon's icon too.
    private bool ConfirmSharedMember(int member)
    {
        if (!useAuthoritative || CB_Species.SelectedItem is not Option sp) return true;
        var owner = iconTable.CompactOwnerOf(member);
        if (owner == null || owner.Value.Species == sp.Value) return true;
        string name = owner.Value.Species < speciesNames.Length ? speciesNames[owner.Value.Species] : $"Species {owner.Value.Species}";
        return WinFormsUtil.Prompt(MessageBoxButtons.YesNo,
            $"Icon member {member} is shared with {name} (form {owner.Value.Form}).",
            "Writing it changes BOTH Pokemon's icon in game. Continue?") == DialogResult.Yes;
    }

    private void RefreshPreview(PictureBox partyBox, PictureBox menuBox, int member)
    {
        partyBox.Image?.Dispose();
        menuBox.Image?.Dispose();
        partyBox.Image = null;
        menuBox.Image = null;
        if (member < 0 || member >= curPartyIconFiles.Length || member >= curMenuIconFiles.Length)
        {
            if (partyBox == PB_Party)
                SetStatus("This species/form has no icon data in this ROM - nothing to preview. Import Image can still give it one.");
            return;
        }

        try { partyBox.Image = Scale(PartyIconImage.Decode(curPartyIconFiles[member]), 6); }
        catch (Exception ex) { SetStatus($"Party icon decode failed: {ex.Message}"); }

        try
        {
            byte[] raw = curMenuIconFiles[member];
            byte[] data = raw.Length > 0 && raw[0] == 0x11 ? LZSS.Decompress(raw) : raw;
            menuBox.Image = Scale(new BFLIM(data).GetBitmap(crop: false), 6);
        }
        catch (Exception ex) { SetStatus($"Menu icon decode failed: {ex.Message}"); }
    }

    private const string DimensionHint = "Expected size: 32x32 (one pose, auto-centered in the 64-wide canvas) or 64x32 (already a full sheet). " +
                                          "Any other size is automatically resized/compressed to fit - a wide (~2:1) image is treated as an already-full " +
                                          "sheet, anything else as a single pose to center.";

    private void SetStatus(string message) => L_Status.Text = message + "\r\n" + DimensionHint;

    private static Bitmap Scale(Bitmap src, int factor)
    {
        var big = new Bitmap(src.Width * factor, src.Height * factor);
        using (var g = Graphics.FromImage(big))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.DrawImage(src, 0, 0, big.Width, big.Height);
        }
        src.Dispose();
        return big;
    }

    private void B_CopyFrom_Click(object sender, EventArgs e)
    {
        int from = CurrentSourceMember();
        int to = CurrentTargetMember();
        if (from < 0 || to < 0)
        { WinFormsUtil.Error("Pick a valid source and target species/form first - one of them has no icon in this ROM."); return; }
        if (from == to)
        { WinFormsUtil.Alert("Source and target are already the same icon - nothing to do."); return; }
        if (!ConfirmSharedMember(to)) return;

        var result = SpeciesIconLayout.CopyIconByMember(curPartyIconFiles, curMenuIconFiles, from, to);
        if (!result.Success)
        { WinFormsUtil.Error("Couldn't copy icon.", result.Error); return; }

        curPartyIconFiles = result.PartyIconFiles;
        curMenuIconFiles = result.MenuIconFiles;
        dirty = true;
        RefreshPreview(PB_Party, PB_Menu, to);
        SetStatus($"Copied icon member {from} onto member {to} (in memory - click Save to ROM to write it).");
    }

    private void B_ImportImage_Click(object sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "Image Files|*.png;*.bmp;*.jpg;*.jpeg;*.gif" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        ImportImage(ofd.FileName);
    }

    private void Image_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
    }

    private void Image_DragDrop(object sender, DragEventArgs e)
    {
        string path = ((string[])e.Data.GetData(DataFormats.FileDrop))[0];
        ImportImage(path);
    }

    private void ImportImage(string path)
    {
        int to = CurrentTargetMember();
        if (to < 0)
        {
            WinFormsUtil.Error("Can't import here.", "This species/form has no icon slot in this ROM to replace.");
            return;
        }
        if (!ConfirmSharedMember(to)) return;

        try
        {
            using var loaded = Image.FromFile(path);
            using var bmp = new Bitmap(loaded.Width, loaded.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
            }

            curPartyIconFiles = (byte[][])curPartyIconFiles.Clone();
            curMenuIconFiles = (byte[][])curMenuIconFiles.Clone();
            curPartyIconFiles[to] = PartyIconImage.Encode(bmp, $"poke_icon_custom_{to}");
            curMenuIconFiles[to] = MenuIconImage.Encode(bmp);
            dirty = true;
            RefreshPreview(PB_Party, PB_Menu, to);
            SetStatus($"Imported \"{Path.GetFileName(path)}\" ({bmp.Width}x{bmp.Height}) onto icon member {to} (in memory - click Save to ROM to write it).");
        }
        catch (Exception ex)
        {
            WinFormsUtil.Error("Import failed.", ex.Message);
        }
    }

    private void B_Save_Click(object sender, EventArgs e)
    {
        if (!dirty)
        { WinFormsUtil.Alert("Nothing to save.", "No icon changes have been made yet."); return; }

        var config = Main.Config;
        var partyGarc = config.GetGARCData("partyicon");
        partyGarc.Files = curPartyIconFiles;
        partyGarc.Save();

        var menuGarc = config.GetGARCData("menuicon");
        menuGarc.Files = curMenuIconFiles;
        menuGarc.Save();

        dirty = false;
        WinFormsUtil.Alert("Icons saved!", "Wrote partyicon and menuicon back to the ROM.");
    }

    private void IconEditor_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (!dirty) return;
        if (WinFormsUtil.Prompt(MessageBoxButtons.YesNo, "Discard unsaved icon changes?") != DialogResult.Yes)
            e.Cancel = true;
    }
}
