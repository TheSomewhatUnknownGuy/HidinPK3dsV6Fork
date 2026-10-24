#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>Outcome of planning or applying a recipe.</summary>
public sealed class RecipeResult
{
    public List<string> Steps { get; } = [];
    public List<string> Errors { get; } = [];
    /// <summary>Things the user must know that did not stop the operation (e.g. a part left as is).</summary>
    public List<string> Warnings { get; } = [];
    public bool Ok => Errors.Count == 0;

    internal void Say(string s) => Steps.Add(s);
    internal void Fail(string s) => Errors.Add(s);
    internal void Warn(string s) => Warnings.Add(s);

    public string Describe() =>
        string.Join(Environment.NewLine,
            Steps.Select(s => "  " + s)
                .Concat(Warnings.Select(w => "  WARNING: " + w))
                .Concat(Errors.Select(e => "  ERROR: " + e)));
}

/// <summary>
/// Applies a <see cref="Recipe"/>: its text first, then its effect.
/// </summary>
public static class RecipeInstaller
{
    /// <summary>Checks a recipe against a ROM without writing anything.</summary>
    /// <param name="values">
    /// Package parameter values by name. A package can claim more than one id — the mints package
    /// claims twenty-two — and supplying only the first left the rest on their authored defaults,
    /// which collide on most ROMs. Null keeps the old behaviour of binding just the first id.
    /// </param>
    public static RecipeResult Plan(Recipe recipe, GameConfig config, string? battleCroPath = null,
                                    ResearchDatabase? db = null, BattleMechanicMap? map = null,
                                    IDictionary<string, string>? values = null)
        => Run(recipe, config, battleCroPath, db, map, values, commit: false);

    /// <summary>Applies a recipe. Only call after a clean <see cref="Plan"/>.</summary>
    public static RecipeResult Apply(Recipe recipe, GameConfig config, string? battleCroPath = null,
                                     ResearchDatabase? db = null, BattleMechanicMap? map = null,
                                     IDictionary<string, string>? values = null)
        => Run(recipe, config, battleCroPath, db, map, values, commit: true);

    /// <summary>
    /// Writes a recipe's names and descriptions into every language the ROM ships.
    /// </summary>
    private static int WriteTextAllLanguages(GameConfig config, Recipe recipe,
                                             TextName nameTable, TextName? flavourTable,
                                             TextName? pluralTable, TextName? pluralAltTable,
                                             RecipeResult r)
    {
        int written = 0;
        var missed = new List<string>();

        int maxId = 0;
        foreach (var e in recipe.Entries)
            if (e.Id > maxId) maxId = e.Id;

        int originalLanguage = config.Language;
        try
        {
            for (int lang = 0; lang <= 9; lang++)
            {
                try
                {
                    config.Language = lang;
                    config.InitializeGameText();

                    var n = config.GetText(nameTable);
                    if (n == null || n.Length == 0) continue;
                    var f = flavourTable.HasValue ? config.GetText(flavourTable.Value) : null;
                    var p = pluralTable.HasValue ? config.GetText(pluralTable.Value) : null;
                    var pa = pluralAltTable.HasValue ? config.GetText(pluralAltTable.Value) : null;

                    if (maxId >= n.Length)
                    {
                        var grown = new string[maxId + 1];
                        Array.Copy(n, grown, n.Length);
                        for (int i = n.Length; i < grown.Length; i++) grown[i] = "???";
                        n = grown;
                    }
                    if (f != null && f.Length > 0 && maxId >= f.Length)
                    {
                        var grown = new string[maxId + 1];
                        Array.Copy(f, grown, f.Length);
                        for (int i = f.Length; i < grown.Length; i++) grown[i] = "";
                        f = grown;
                    }
                    if (p != null && p.Length > 0 && maxId >= p.Length)
                    {
                        var grown = new string[maxId + 1];
                        Array.Copy(p, grown, p.Length);
                        for (int i = p.Length; i < grown.Length; i++) grown[i] = "???";
                        p = grown;
                    }
                    if (pa != null && pa.Length > 0 && maxId >= pa.Length)
                    {
                        var grown = new string[maxId + 1];
                        Array.Copy(pa, grown, pa.Length);
                        for (int i = pa.Length; i < grown.Length; i++) grown[i] = "???";
                        pa = grown;
                    }

                    bool touchedName = false, touchedFlavour = false, touchedPlural = false, touchedPluralAlt = false;
                    foreach (var e in recipe.Entries)
                    {
                        if (e.Id >= 0 && e.Id < n.Length && !string.IsNullOrEmpty(e.Name))
                        { n[e.Id] = e.Name; touchedName = true; }

                        if (f != null && e.Id >= 0 && e.Id < f.Length && !string.IsNullOrEmpty(e.Description))
                        { f[e.Id] = ItemTextUtil.FormatDescription(e.Description); touchedFlavour = true; }

                        string plural = !string.IsNullOrWhiteSpace(e.Plural) ? e.Plural : ItemTextUtil.Pluralize(e.Name);
                        string pluralAlt = !string.IsNullOrWhiteSpace(e.PluralAlt) ? e.PluralAlt : ItemTextUtil.PluralAlt(e.Name);

                        if (p != null && e.Id >= 0 && e.Id < p.Length && !string.IsNullOrEmpty(plural))
                        { p[e.Id] = plural; touchedPlural = true; }

                        if (pa != null && e.Id >= 0 && e.Id < pa.Length && !string.IsNullOrEmpty(pluralAlt))
                        { pa[e.Id] = pluralAlt; touchedPluralAlt = true; }
                    }

                    if (touchedName)
                    {
                        config.SetText(nameTable, n);
                        config.SaveText(nameTable);
                    }
                    if (touchedFlavour && flavourTable.HasValue && f != null)
                    {
                        config.SetText(flavourTable.Value, f);
                        config.SaveText(flavourTable.Value);
                    }
                    if (touchedPlural && pluralTable.HasValue && p != null)
                    {
                        config.SetText(pluralTable.Value, p);
                        config.SaveText(pluralTable.Value);
                    }
                    if (touchedPluralAlt && pluralAltTable.HasValue && pa != null)
                    {
                        config.SetText(pluralAltTable.Value, pa);
                        config.SaveText(pluralAltTable.Value);
                    }
                    if (touchedName || touchedFlavour || touchedPlural || touchedPluralAlt) written++;
                }
                catch (Exception ex) { missed.Add($"{lang} ({ex.GetType().Name})"); }
            }
        }
        finally
        {
            try
            {
                config.Language = originalLanguage;
                config.InitializeGameText();
            }
            catch { }
        }

        if (missed.Count > 0 && written == 0)
            r.Say($"  text could not be written to any language: {string.Join(", ", missed)}");
        else if (missed.Count > 0)
            r.Say($"  language(s) skipped: {string.Join(", ", missed)}");

        return written;
    }

    /// <summary>
    /// Whether a version-stamped recipe matches the ROM actually loaded.
    /// </summary>
    private static bool VersionMatches(Recipe recipe, GameConfig config, out string wanted, out string loaded)
    {
        wanted = (recipe.ForVersion ?? "").Trim();
        loaded = ResearchVersion.Resolve(config).Trim();
        if (wanted.Length == 0 || loaded.Length == 0) return true;
        return string.Equals(wanted, loaded, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>RomFS path of the binary a recipe patches.</summary>
    private static string? ResolveTargetFile(ResearchTarget target, GameConfig config)
    {
        string? romfs = config?.RomFS;
        if (string.IsNullOrWhiteSpace(romfs)) return null;
        string name = target switch
        {
            ResearchTarget.BagCro => "Bag.cro",
            ResearchTarget.ShopCro => "Shop.cro",
            ResearchTarget.BoxCro => "Box.cro",
            ResearchTarget.StatusCro => "Status.cro",
            ResearchTarget.EvolutionCro => "Evolution.cro",
            ResearchTarget.FieldRoCro => "FieldRo.cro",
            _ => "Battle.cro",
        };
        return Path.Combine(romfs, name);
    }

    /// <summary>
    /// On Uhuru, why this recipe may not run, or null. Only patch packages (whose anchors check the original
    /// bytes), IPS patches (their records are checked in Run) and data-only recipes are allowed; the other kinds write unchecked.
    /// </summary>
    private static string? UhuruRecipeRefusal(Recipe recipe)
    {
        if (recipe.EffectKind == RecipeEffectKind.DataOnly) return null;
        if (recipe.EffectKind == RecipeEffectKind.IpsPatch) return null; // checked against the diff tables where the records are known
        if (recipe.EffectKind != RecipeEffectKind.Package)
            return UhuruGuard.RefusalFor("Battle.cro");

        string stem = Path.GetFileNameWithoutExtension(recipe.PackagePath ?? "");
        string name = (recipe.Package?.Name ?? "").Replace(" ", "");
        return UhuruGuard.PackageRefusal(stem) ?? UhuruGuard.PackageRefusal(name);
    }

    private static RecipeResult Run(Recipe recipe, GameConfig config, string? battleCroPath,
                                    ResearchDatabase? db, BattleMechanicMap? map,
                                    IDictionary<string, string>? values, bool commit)
    {
        var r = new RecipeResult();
        if (recipe == null) { r.Fail("no recipe supplied"); return r; }
        if (config == null) { r.Fail("no ROM loaded"); return r; }

        r.Say($"{recipe.Name} - {recipe.SlotCount} id(s), effect: {recipe.EffectKind}");

        var ids = recipe.Entries.Select(e => e.Id).ToList();
        bool codeOnly = recipe.Entries.Count == 0;

        bool packageDriven = recipe.EffectKind == RecipeEffectKind.Package;

        if (!codeOnly && !packageDriven)
        {
            // Say what is actually needed. "Every slot needs an id" gave no clue that a 21-mint
            // recipe wants twenty-one CONSECUTIVE free ids, which is the part that usually fails.
            if (ids.Any(i => i < 0))
            {
                r.Fail(recipe.Entries.Count == 1
                    ? "this needs a free id before it can be installed"
                    : $"this needs {recipe.Entries.Count} consecutive free ids before it can be installed; " +
                      "enter the first one");
                return r;
            }

            int maxId = recipe.Kind == CustomMechanicKind.Item ? config.Info.MaxItemID : config.MaxSpeciesID;
            foreach (int id in ids.Where(i => i > maxId && maxId > 0))
                r.Fail($"id {id} is above this ROM's ceiling of {maxId}");

            if (ids.Distinct().Count() != ids.Count) r.Fail("the same id was assigned to more than one slot");

            // Consecutive ids matter for any block the game walks by offset - the mints above all.
            if (recipe.SlotCount > 1)
            {
                bool run = ids.Zip(ids.Skip(1), (a, b) => b == a + 1).All(x => x);
                if (!run) r.Fail("this recipe needs a consecutive block of ids; the ones given have gaps");
                else r.Say($"ids {ids[0]}-{ids[^1]} are consecutive");
            }
        }

        if (!r.Ok) return r;

        if (UhuruGuard.Active)
        {
            string? blocked = UhuruRecipeRefusal(recipe);
            if (blocked != null) { r.Fail(blocked); return r; }
        }

        // --- text ---
        string[]? names = null, flavour = null, plurals = null, pluralAlts = null;
        if (recipe.Kind == CustomMechanicKind.Item)
        {
            names = config.GetText(TextName.ItemNames);
            flavour = config.GetText(TextName.ItemFlavor);
            plurals = config.GetText(TextName.ItemNamesPlural);
            pluralAlts = config.GetText(TextName.ItemNamesPluralAlt);
        }
        else if (recipe.Kind == CustomMechanicKind.Ability)
        {
            // Abilities have a name table but no separate flavour table in these games, so the
            // description a recipe carries is documentation rather than something the game shows.
            names = config.GetText(TextName.AbilityNames);
            flavour = null;
        }
        else
        {
            names = config.GetText(TextName.MoveNames);
            flavour = config.GetText(TextName.MoveFlavor);
        }

        if (names == null) { r.Fail("this ROM's name table could not be read"); return r; }

        bool textOwnedByEffect = recipe.EffectKind == RecipeEffectKind.Package;

        foreach (var e in recipe.Entries)
        {
            if (textOwnedByEffect) break;
            if (e.Id >= names.Length)
            {
                var grown = new string[e.Id + 1];
                Array.Copy(names, grown, names.Length);
                for (int i = names.Length; i < grown.Length; i++) grown[i] = "???";
                names = grown;
            }

            string was = names[e.Id] ?? "";
            r.Say($"  id {e.Id}: \"{was}\" -> \"{e.Name}\"");
            if (commit) names[e.Id] = e.Name;

            if (flavour != null && flavour.Length > 0)
            {
                if (e.Id >= flavour.Length)
                {
                    var grown = new string[e.Id + 1];
                    Array.Copy(flavour, grown, flavour.Length);
                    for (int i = flavour.Length; i < grown.Length; i++) grown[i] = "";
                    flavour = grown;
                }
                if (commit && !string.IsNullOrEmpty(e.Description))
                    flavour[e.Id] = ItemTextUtil.FormatDescription(e.Description);
            }

            if (plurals != null && plurals.Length > 0)
            {
                if (e.Id >= plurals.Length)
                {
                    var grown = new string[e.Id + 1];
                    Array.Copy(plurals, grown, plurals.Length);
                    for (int i = plurals.Length; i < grown.Length; i++) grown[i] = "???";
                    plurals = grown;
                }
                string plural = !string.IsNullOrWhiteSpace(e.Plural) ? e.Plural : ItemTextUtil.Pluralize(e.Name);
                if (commit) plurals[e.Id] = plural;
            }

            if (pluralAlts != null && pluralAlts.Length > 0)
            {
                if (e.Id >= pluralAlts.Length)
                {
                    var grown = new string[e.Id + 1];
                    Array.Copy(pluralAlts, grown, pluralAlts.Length);
                    for (int i = pluralAlts.Length; i < grown.Length; i++) grown[i] = "???";
                    pluralAlts = grown;
                }
                string plural = !string.IsNullOrWhiteSpace(e.Plural) ? e.Plural : ItemTextUtil.Pluralize(e.Name);
                string pluralAlt = !string.IsNullOrWhiteSpace(e.PluralAlt) ? e.PluralAlt : ItemTextUtil.PluralAlt(e.Name);
                if (commit) pluralAlts[e.Id] = pluralAlt;
            }
        }
        if (!r.Ok) return r;

        // Preconditions first: names used to be saved to every language before this check, so a
        // recipe refused as "built for a different binary" still left named, effect-less items behind.
        foreach (var a in recipe.Anchors)
        {
            string? apath = a.Target == ResearchTarget.CodeBin
                ? pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS)
                : ResolveTargetFile(a.Target, config);

            if (apath == null || !File.Exists(apath))
            {
                r.Fail($"{Recipe.FileNameOf(a.Target)} is needed to check this recipe suits your build, but was not found");
                continue;
            }

            byte[] abin;
            try { abin = File.ReadAllBytes(apath); }
            catch (Exception ex) { r.Fail($"{Recipe.FileNameOf(a.Target)} could not be read ({ex.GetType().Name})"); continue; }

            if (a.Present(abin)) { r.Say($"precondition met: {a.Describes}"); continue; }

            string found = a.Offset + a.Bytes.Length <= abin.Length
                ? Convert.ToHexString(abin, (int)a.Offset, a.Bytes.Length)
                : "past the end";

            r.Fail($"{a.Remedy ?? "this feature is built for a different binary and would corrupt this one"} " +
                   $"[{Recipe.FileNameOf(a.Target)} 0x{a.Offset:X6}: expected {Convert.ToHexString(a.Bytes)} " +
                   $"for {a.Describes}, found {found}]");
        }
        if (!r.Ok) return r;

        if (commit && !textOwnedByEffect)
        {
            var nameTable = recipe.Kind switch
            {
                CustomMechanicKind.Item => TextName.ItemNames,
                CustomMechanicKind.Ability => TextName.AbilityNames,
                _ => TextName.MoveNames,
            };
            TextName? flavourTable = recipe.Kind switch
            {
                CustomMechanicKind.Item => TextName.ItemFlavor,
                CustomMechanicKind.Ability => null,   // no flavour table for abilities in these games
                _ => TextName.MoveFlavor,
            };
            TextName? pluralTable = recipe.Kind == CustomMechanicKind.Item ? TextName.ItemNamesPlural : null;
            TextName? pluralAltTable = recipe.Kind == CustomMechanicKind.Item ? TextName.ItemNamesPluralAlt : null;

            int langs = WriteTextAllLanguages(config, recipe, nameTable, flavourTable, pluralTable, pluralAltTable, r);
            r.Say(langs > 1
                ? $"text written and saved to {langs} language(s)"
                : "text written and saved");
        }

        // --- effect ---
        switch (recipe.EffectKind)
        {
            case RecipeEffectKind.DataOnly:
                r.Say("no code patch: this recipe is data and text only");
                break;

            case RecipeEffectKind.LevelCap:
            {
                var table = LevelCapSettings.Table;
                var problems = table.Validate();
                foreach (string p in problems) r.Say("  note: " + p);

                string battlePath = ResolveTargetFile(ResearchTarget.BattleCro, config) ?? "";
                if (!File.Exists(battlePath)) { r.Fail("Battle.cro was not found - open the RomFS in pk3DS first"); break; }

                // code.bin is optional: without it the Rare Candy path stays uncapped, which is a
                // smaller hole than refusing to install the cap at all. Say so rather than hide it.
                string codePath = pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS);
                bool haveCode = File.Exists(codePath);

                byte[] battle = File.ReadAllBytes(battlePath);
                byte[]? code = haveCode ? File.ReadAllBytes(codePath) : null;

                var sites = LevelCapPatch.Install(battle, code, table);
                foreach (var s in sites) r.Say("  " + s);
                if (!haveCode)
                    r.Say("  code.bin was not available, so Rare Candy is not capped (open the ExeFS to include it)");

                if (sites.Count == 0 || !sites.Any(s => s.Applied))
                { r.Fail("nothing could be hooked; the cap was not installed"); break; }

                if (!commit)
                {
                    r.Say($"  would write {LevelCapPatch.BuildBlock(table).Length} bytes plus the hook(s)");
                    break;
                }

                // SaveCro, not WriteAllBytes: the cap routine and its hook change Battle.cro, and
                // a CRO written without refreshing its hashes is rejected wherever it is used.
                pk3DS.Core.CTR.CROUtil.SaveCro(battlePath, battle);
                if (code != null && sites.Any(s => s.Binary == "code.bin" && s.Applied))
                    File.WriteAllBytes(codePath, code);
                r.Say($"level cap installed: {table.Entries.Count} step(s)");
                break;
            }

            case RecipeEffectKind.ByteEdit:
            {
                if (recipe.ByteEdits.Count == 0) { r.Fail("this recipe records no byte writes"); break; }

                if (!VersionMatches(recipe, config, out string vWanted, out string vLoaded))
                { r.Fail($"this edit is for {vWanted}; the loaded ROM is {vLoaded}"); break; }

                // Honour the recipe's target. This assumed code.bin unconditionally, so a byte edit
                // aimed at a CRO wrote into the executable at the CRO's offset instead.
                bool intoCode = recipe.Target is ResearchTarget.CodeBin or ResearchTarget.Unknown;
                string codeFile;
                if (intoCode)
                {
                    codeFile = pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS);
                    if (!File.Exists(codeFile)) { r.Fail("ExeFS/.code.bin was not found - open the ExeFS in pk3DS first"); break; }
                }
                else
                {
                    codeFile = ResolveTargetFile(recipe.Target, config) ?? "";
                    if (!File.Exists(codeFile))
                    { r.Fail($"{Recipe.FileNameOf(recipe.Target)} was not found - open the RomFS in pk3DS first"); break; }
                }

                var groups = recipe.ByteEdits
                    .GroupBy(e => e.Target ?? recipe.Target)
                    .ToList();

                var buffers = new Dictionary<ResearchTarget, (string Path, byte[] Data)>();
                foreach (var g in groups)
                {
                    string p = g.Key == ResearchTarget.CodeBin
                        ? pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS)
                        : ResolveTargetFile(g.Key, config) ?? "";

                    if (!File.Exists(p))
                    { r.Fail($"{Recipe.FileNameOf(g.Key)} was not found - open the ROM in pk3DS first"); continue; }
                    buffers[g.Key] = (p, File.ReadAllBytes(p));
                }
                if (!r.Ok) break;

                int toWrite = 0, done = 0;
                foreach (var g in groups)
                {
                    var (_, data) = buffers[g.Key];
                    string fname = Recipe.FileNameOf(g.Key);
                    foreach (var e in g)
                    {
                        if (e.Offset >= data.Length)
                        { r.Fail($"{fname} 0x{e.Offset:X6} is past the end of the file"); continue; }

                        byte have = data[e.Offset];
                        if (have == e.To) { done++; continue; }
                        if (have != e.From)
                        {
                            r.Fail($"{fname} 0x{e.Offset:X6} holds 0x{have:X2}, expected 0x{e.From:X2} - " +
                                   "this build differs from the one the edit was written for");
                            continue;
                        }
                        toWrite++;
                    }
                }
                if (!r.Ok) break;

                foreach (var g in groups)
                {
                    if (g.Key == ResearchTarget.CodeBin) continue;   // not a CRO; no segment header
                    var writes = g.GroupBy(e => e.Offset / 4)
                                  .OrderBy(w => w.Key)
                                  .Select(w => (Offset: w.Key * 4,
                                                Bytes: w.OrderBy(e => e.Offset).Select(e => e.To).ToArray()))
                                  .ToList();
                    foreach (var f in PatchSanity.Check(buffers[g.Key].Data, writes, Recipe.FileNameOf(g.Key)))
                    {
                        if (f.Fatal) r.Fail(f.Message);
                        else r.Say("  warning: " + f.Message);
                    }
                }
                if (!r.Ok) break;

                foreach (var g in groups)
                    r.Say($"  {Recipe.FileNameOf(g.Key)}: {g.Count()} byte(s) recorded");

                if (!commit) { r.Say($"  would write {toWrite} byte(s); {done} already in place"); break; }

                foreach (var g in groups)
                {
                    var (path, data) = buffers[g.Key];
                    foreach (var e in g.Where(e => e.Offset < data.Length && data[e.Offset] == e.From))
                        data[e.Offset] = e.To;

                    // SaveCro refreshes a CRO's hashes before writing; a plain file passes straight
                    // through, so this is correct for code.bin as well.
                    pk3DS.Core.CTR.CROUtil.SaveCro(path, data);
                    r.Say($"  written to {Recipe.FileNameOf(g.Key)}");
                }
                r.Say($"{toWrite} byte(s) written across {groups.Count} file(s); {done} already in place");
                break;
            }

            case RecipeEffectKind.IpsPatch:
            {
                if (string.IsNullOrWhiteSpace(recipe.IpsPath) || !File.Exists(recipe.IpsPath))
                { r.Fail("the .ips file for this patch was not found"); break; }

                // Refuse outright rather than write a patch built for the other binary.
                if (!VersionMatches(recipe, config, out string ipsWanted, out string ipsLoaded))
                {
                    r.Fail($"this patch is built for {ipsWanted} and the loaded ROM is {ipsLoaded}; " +
                           "the two use different offsets and it will not be applied");
                    break;
                }

                string codePath = pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS);
                if (!File.Exists(codePath))
                { r.Fail("ExeFS/.code.bin was not found - open the ExeFS in pk3DS first"); break; }

                List<IpsRecord> records;
                try { records = IpsPatch.Read(File.ReadAllBytes(recipe.IpsPath)); }
                catch (Exception ex) { r.Fail($"this .ips could not be read: {ex.Message}"); break; }

                byte[] code = File.ReadAllBytes(codePath);
                r.Say($"{Path.GetFileName(recipe.IpsPath)}: {IpsPatch.Describe(records)}");

                // Translate record offsets through CodeBinExpansionJournal in case code.bin segments expanded
                records = CodeBinExpansionJournal.TranslateIpsRecords(records, codePath);

                var past = records.Where(x => x.End > code.Length).ToList();
                foreach (var x in past)
                    r.Fail($"writes 0x{x.Offset:X6}..0x{x.End:X6}, past the end of a " +
                           $"{code.Length:N0}-byte code.bin - this patch's offsets don't line up with the current code.bin layout");
                if (past.Count > 0) break;

                if (UhuruGuard.Active && UhuruGuard.IpsRefusal(records.Select(x => (x.Offset, x.Bytes.Length))) is { } ipsWhy)
                { r.Fail("Refused on Uhuru: " + ipsWhy); break; }

                int already = records.Count(x => Enumerable.Range(0, x.Bytes.Length)
                                                           .All(k => code[x.Offset + k] == x.Bytes[k]));
                r.Say($"  {already}/{records.Count} record(s) already present");

                if (!commit)
                {
                    r.Say($"  would write {records.Sum(x => x.Bytes.Length)} byte(s) into code.bin");
                    break;
                }

                try
                {
                    // Keep exactly the bytes this patch replaces, so revert can undo it alone. Restoring
                    // the first-ever code.bin.bak instead rolled back every later code.bin change too.
                    string undoPath = IpsUndoPath(config, recipe.IpsPath);
                    if (!File.Exists(undoPath) && already < records.Count)
                    {
                        var undo = records.Select(x => new IpsRecord(x.Offset, code.AsSpan(x.Offset, x.Bytes.Length).ToArray(), false)).ToList();
                        File.WriteAllText(undoPath, System.Text.Json.JsonSerializer.Serialize(undo));
                    }
                    byte[] patched = (byte[])code.Clone();
                    int wrote = IpsPatch.Apply(patched, records);
                    if (UhuruGuard.Active && UhuruGuard.FirstOverlap("code.bin", code, patched) is { } overlap)
                    { r.Fail("Refused on Uhuru: " + overlap); break; }
                    File.WriteAllBytes(codePath, patched);
                    r.Say($"{wrote} byte(s) written to code.bin");
                }
                catch (Exception ex) { r.Fail($"applying failed: {ex.Message}"); }
                break;
            }

            case RecipeEffectKind.Package:
            {
                var pkg = recipe.Package;
                if (pkg == null) { r.Fail("this recipe has no package attached"); break; }
                if (map == null) { r.Fail("Battle.cro is not loaded, so the package cannot be applied"); break; }

                var supplied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (values != null)
                    foreach (var kv in values)
                        if (!string.IsNullOrWhiteSpace(kv.Value)) supplied[kv.Key] = kv.Value.Trim();

                var firstParam = pkg.Parameters?.Keys.FirstOrDefault();
                if (firstParam != null && ids.Count > 0 && ids[0] >= 0 && !supplied.ContainsKey(firstParam))
                    supplied[firstParam] = ids[0].ToString();

                // Two parameters pointed at the same id is the collision that used to surface as an
                // unexplained "same key has already been added" from deep inside the installer.
                var dupes = supplied.GroupBy(kv => kv.Value).Where(g => g.Count() > 1).ToList();
                foreach (var d in dupes)
                    r.Fail($"id {d.Key} is given to more than one of this package's slots " +
                           $"({string.Join(", ", d.Select(x => x.Key))}) - each needs its own");
                if (dupes.Count > 0) break;

                var keys = (pkg.ItemNames?.Keys ?? Enumerable.Empty<string>())
                    .Concat(pkg.ItemData?.Keys ?? Enumerable.Empty<string>()).ToList();
                var literals = new HashSet<string>(keys.Where(k => !k.Contains('$')));
                var keyParams = new HashSet<string>(
                    keys.Where(k => k.Contains('$'))
                        .Select(k => k.Trim('$', '{', '}')),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var kv in supplied.Where(kv => keyParams.Contains(kv.Key)))
                {
                    foreach (string one in kv.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!literals.Contains(one)) continue;
                        string what = pkg.ItemNames != null && pkg.ItemNames.TryGetValue(one, out var nm) ? $" ('{nm}')" : "";
                        r.Fail($"'{kv.Key}' is set to {one}, which this package already claims by " +
                               $"number{what} - choose a different id");
                    }
                }
                if (!r.Ok) break;

                var tables = new Dictionary<CustomMechanicKind, string[]>
                {
                    [CustomMechanicKind.Item] = config.GetText(TextName.ItemNames),
                    [CustomMechanicKind.Ability] = config.GetText(TextName.AbilityNames),
                    [CustomMechanicKind.Move] = config.GetText(TextName.MoveNames),
                };

                PackageResult preview;
                try { preview = PatchPackageInstaller.Preview(pkg, supplied, map, tables); }
                catch (Exception ex)
                {
                    r.Fail($"this package could not be read: {ex.Message}");
                    if (pkg.Parameters is { Count: > 1 })
                        r.Fail($"it claims {pkg.Parameters.Count} ids ({string.Join(", ", pkg.Parameters.Keys)}), " +
                               "and the id box supplies only the first - the rest fall back to the package's " +
                               "defaults, which may already be taken on this ROM");
                    break;
                }

                foreach (string s in preview.Log) r.Say("  " + s);
                foreach (string e in preview.Errors) r.Fail("  " + e);
                if (!preview.Success) break;

                var problems = new List<string>();
                var resolvedValues = PatchParameters.Resolve(pkg, supplied, problems);
                var bound = PatchParameters.Bind(pkg, resolvedValues, problems);
                if (problems.Count > 0)
                {
                    foreach (var p in problems) r.Fail(p);
                    break;
                }

                // Check that cloneFrom templates exist within the current table
                if (bound.ItemData.Count > 0)
                {
                    int itemCount = config.GetGARCData("item").Files.Length;
                    foreach (var (idText, idata) in bound.ItemData)
                    {
                        if (!uint.TryParse(idText, out _))
                        { r.Fail($"invalid item id '{idText}'"); continue; }
                        if (!string.IsNullOrWhiteSpace(idata.CloneFrom) &&
                            (!uint.TryParse(idata.CloneFrom, out uint from) || from >= itemCount))
                            r.Fail($"cloneFrom '{idata.CloneFrom}' is out of range");
                    }
                    if (!r.Ok) break;
                }

                if (!commit)
                {
                    int slots = pkg.Mechanics?.Sum(m => m.Slots?.Count ?? 0) ?? 0;
                    r.Say($"  would install {slots} slot(s), {pkg.Blocks?.Count ?? 0} block(s), " +
                          $"{pkg.SitePatches?.Count ?? 0} site hook(s), {pkg.OtherCros?.Count ?? 0} other CRO(s)");
                    break;
                }

                var allPlaced = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
                // Each binary is staged and written only once the whole package has succeeded: writing
                // as soon as each was done left Battle.cro patched without its code.bin half after a
                // later failure, and wrote files whose data tables had failed.
                var pending = new List<(string Name, string Path, byte[] Output, Action Write)>();
                var record = InstallRecord.Load(config.RomFS);
                var placements = new List<InstallRecord.Placement>();

                // 1. Battle.cro (if package touches Battle.cro mechanics or blocks)
                if (bound.Mechanics.Count > 0 || bound.Blocks.Count > 0 || bound.SitePatches.Count > 0)
                {
                    string? bCro = ResolveTargetFile(ResearchTarget.BattleCro, config) ?? battleCroPath;
                    if (string.IsNullOrWhiteSpace(bCro) || !File.Exists(bCro))
                    {
                        r.Fail("Battle.cro was not found");
                        break;
                    }

                    byte[] rom = File.ReadAllBytes(bCro);
                    byte[] output;
                    var applied = PatchPackageInstaller.ApplyBattleCro(pkg, supplied, rom, db, tables, out output);
                    foreach (string s in applied.Log.Take(12)) r.Say("  " + s);
                    if (applied.Log.Count > 12) r.Say($"  … and {applied.Log.Count - 12} more");
                    foreach (string e in applied.Errors) r.Fail("  " + e);

                    if (!applied.Success || output == null) { r.Fail("the package was not applied to Battle.cro"); break; }

                    // Uhuru may reference master tables by literal address, so moving one is not safe there.
                    if (UhuruGuard.Active && applied.TableRelocated)
                    { r.Fail("Refused on Uhuru: this package would move a Battle.cro table, .data or segment"); break; }

                    pending.Add(("Battle.cro", bCro, output, () =>
                    {
                        BackupFileBeforeWrite(bCro);
                        pk3DS.Core.CTR.CROUtil.SaveCro(bCro, output);
                    }));
                    r.Say($"{recipe.Name} prepared for Battle.cro");
                }

                // 2. CodeBin (applied before OtherCros so CRO dispatch stubs can resolve cross-binary code targets)
                if (bound.CodeBin != null && (bound.CodeBin.Patches.Count > 0 || bound.CodeBin.Blocks.Count > 0))
                {
                    string codePath = pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS);
                    if (!File.Exists(codePath))
                    {
                        r.Fail("code.bin not found in ExeFS");
                    }
                    else
                    {
                        byte[] codeBytes = File.ReadAllBytes(codePath);
                        string exeFsPath = config.ExeFS;
                        string? romRoot = Directory.GetParent(Path.GetFullPath(exeFsPath))?.FullName;
                        string? exHeaderPath = romRoot == null ? null : Path.Combine(romRoot, "ExHeader.bin");
                        string? ncch0Path = romRoot == null ? null : Path.Combine(romRoot, "HeaderNCCH0.bin");
                        byte[]? exHeader = exHeaderPath != null && File.Exists(exHeaderPath)
                            ? File.ReadAllBytes(exHeaderPath) : null;
                        int textEnd = CodeGrowth.TextEndFor(exHeader, codeBytes.LongLength);
                        // Blocks earlier packages placed are off limits: a zero-filled table inside one
                        // (CenterMoveTutor's) otherwise looks like free padding to the allocator.
                        var keepOut = record.PlacedIn("code.bin")
                            .Concat(UhuruGuard.Active ? UhuruGuard.KeepOut("code.bin") : new List<(uint, int)>());
                        PortResult codeResult = PortCodeBinInstaller.Apply(codeBytes, bound.CodeBin, config.RomFS, r.Say,
                            keepOut: keepOut, textEnd: textEnd);
                        GrowResult? codeGrowth = null;
                        byte[]? stagedGrowthJournal = null;
                        string? growthJournalPath = null;

                        bool noExecutableRoom = codeResult.Errors.Any(e =>
                            e.Contains("has no executable room", StringComparison.OrdinalIgnoreCase));
                        if (noExecutableRoom && GameConfig.AllowGrowth && !UhuruGuard.Active)
                        {
                            long needed = bound.CodeBin.Blocks.Sum(b => (b.CodeBytes.LongLength + 3L) & ~3L);
                            if (exHeader == null || exHeaderPath == null || !File.Exists(exHeaderPath))
                            {
                                r.Say("  ! code.bin growth refused: ExHeader.bin was not found next to ExeFS");
                            }
                            else if (needed <= 0 || needed > int.MaxValue)
                            {
                                r.Say($"  ! code.bin growth refused: required code size {needed} is outside the supported range");
                            }
                            else
                            {
                                // Grow records an offset journal. Stage that journal too, so a later
                                // package failure leaves no code, header, or journal changes behind.
                                string stageName = "pk3ds-code-growth-" + Guid.NewGuid().ToString("N");
                                string journalStageRoot = Path.Combine(Path.GetTempPath(), stageName);
                                try
                                {
                                    Directory.CreateDirectory(journalStageRoot);
                                    File.WriteAllText(Path.Combine(journalStageRoot, ".romlab-scratch"), "staged code growth journal");
                                    string stagedExeFsPath = Path.Combine(journalStageRoot, "ExeFS");
                                    Directory.CreateDirectory(stagedExeFsPath);
                                    growthJournalPath = CodeBinExpansionJournal.GetJournalPath(exeFsPath);
                                    string stagedJournalPath = Path.Combine(stagedExeFsPath, CodeBinExpansionJournal.JournalFileName);
                                    if (File.Exists(growthJournalPath))
                                        File.Copy(growthJournalPath, stagedJournalPath, true);

                                    byte[]? ncch0 = ncch0Path != null && File.Exists(ncch0Path)
                                        ? File.ReadAllBytes(ncch0Path) : null;
                                    codeGrowth = CodeGrowth.Grow(codeBytes, exHeader, ncch0, (int)needed, stagedExeFsPath);
                                    if (codeGrowth.Ok)
                                    {
                                        var retry = PortCodeBinInstaller.Apply(codeGrowth.Code, bound.CodeBin, config.RomFS, r.Say,
                                            keepOut: keepOut, textEnd: CodeGrowth.TextEndFor(codeGrowth.ExHeader));
                                        if (retry.Success && retry.Output != null)
                                        {
                                            codeResult = retry;
                                            if (File.Exists(stagedJournalPath))
                                                stagedGrowthJournal = File.ReadAllBytes(stagedJournalPath);
                                            r.Say($"  code.bin grew by {codeGrowth.Pages} page(s)");
                                        }
                                        else
                                        {
                                            codeResult = retry;
                                            r.Say("  ! code.bin growth retry did not find executable room");
                                        }
                                    }
                                    else
                                    {
                                        r.Say("  ! code.bin growth refused: " + codeGrowth.Error);
                                        codeGrowth = null;
                                    }
                                }
                                finally
                                {
                                    string marker = Path.Combine(journalStageRoot, ".romlab-scratch");
                                    if (Directory.Exists(journalStageRoot) && File.Exists(marker))
                                        Directory.Delete(journalStageRoot, true);
                                }
                            }
                        }

                        foreach (string s in codeResult.Log) r.Say("  " + s);
                        foreach (string e in codeResult.Errors) r.Fail("code.bin: " + e);
                        if (codeResult.Success && codeResult.Output != null)
                        {
                            foreach (var kv in codeResult.Placed) allPlaced[kv.Key] = kv.Value;
                            foreach (var b in bound.CodeBin.Blocks)
                                if (codeResult.Placed.TryGetValue(b.Name, out uint loadAt))
                                    placements.Add(new() { File = "code.bin", Block = b.Name, Offset = loadAt - bound.CodeBin.LoadBase, Size = b.CodeBytes.Length });

                            var dtResult = PatchPackageInstaller.ApplyDataTables(bound, "code.bin", codeResult.Output, codeResult.Placed, allPlaced);
                            foreach (string s in dtResult.Log) r.Say("  " + s);
                            foreach (string e in dtResult.Errors) r.Fail("  " + e);

                            pending.Add(("code.bin", codePath, codeResult.Output, () =>
                            {
                                BackupFileBeforeWrite(codePath);
                                File.WriteAllBytes(codePath, codeResult.Output);
                            }));

                            GrowResult? acceptedGrowth = codeGrowth;
                            if (acceptedGrowth != null && acceptedGrowth.Ok && exHeaderPath != null)
                            {
                                pending.Add(("ExHeader.bin", exHeaderPath, acceptedGrowth.ExHeader, () =>
                                {
                                    BackupFileBeforeWrite(exHeaderPath);
                                    File.WriteAllBytes(exHeaderPath, acceptedGrowth.ExHeader);
                                }));
                                if (ncch0Path != null && File.Exists(ncch0Path) && acceptedGrowth.Ncch0.Length > 0)
                                {
                                    pending.Add(("HeaderNCCH0.bin", ncch0Path, acceptedGrowth.Ncch0, () =>
                                    {
                                        BackupFileBeforeWrite(ncch0Path);
                                        File.WriteAllBytes(ncch0Path, acceptedGrowth.Ncch0);
                                    }));
                                }
                                if (stagedGrowthJournal != null && growthJournalPath != null)
                                {
                                    pending.Add((CodeBinExpansionJournal.JournalFileName, growthJournalPath,
                                        stagedGrowthJournal, () =>
                                        {
                                            BackupFileBeforeWrite(growthJournalPath);
                                            File.WriteAllBytes(growthJournalPath, stagedGrowthJournal);
                                        }));
                                }
                            }
                        }
                    }
                }

                // 3. Other CROs (e.g. Bag.cro, FieldRo.cro)
                foreach (var (croName, croPort) in bound.OtherCros)
                {
                    string croPath = Path.Combine(config.RomFS, croName);
                    if (!File.Exists(croPath)) croPath = Path.Combine(config.RomFS, "cro", croName);
                    if (!File.Exists(croPath))
                    {
                        r.Fail($"{croName} was not found in RomFS");
                        continue;
                    }

                    byte[] croBytes = File.ReadAllBytes(croPath);
                    var croResult = PortCroSiteInstaller.Apply(croBytes, croPort, croName, r.Say);
                    foreach (string s in croResult.Log) r.Say("  " + s);
                    foreach (string e in croResult.Errors) r.Fail("  " + e);
                    if (!croResult.Success || croResult.Output == null) continue;

                    foreach (var kv in croResult.Placed) allPlaced[kv.Key] = kv.Value;
                    foreach (var b in croPort.Blocks ?? [])
                        if (croResult.Placed.TryGetValue(b.Name, out uint blockAt))
                            placements.Add(new() { File = croName, Block = b.Name, Offset = blockAt, Size = b.CodeBytes.Length });
                    foreach (var site in croPort.SitePatches ?? [])
                        placements.Add(new() { File = croName, Block = site.IsHook ? site.HookTarget ?? "" : "", Offset = site.OffsetValue });

                    // Data tables targeting this CRO (with cross-binary placed resolution)
                    var dtResult = PatchPackageInstaller.ApplyDataTables(bound, croName, croResult.Output, croResult.Placed, allPlaced);
                    foreach (string s in dtResult.Log) r.Say("  " + s);
                    foreach (string e in dtResult.Errors) r.Fail("  " + e);

                    pk3DS.Core.CTR.CROUtil.UpdateHashes(croResult.Output);
                    pending.Add((croName, croPath, croResult.Output, () =>
                    {
                        BackupFileBeforeWrite(croPath);
                        File.WriteAllBytes(croPath, croResult.Output);
                    }));
                }

                if (!r.Ok)
                {
                    r.Fail("nothing was written: every file is left as it was");
                    break;
                }
                // Uhuru: the anchors verify what they touch, but the Mechanics path does not, so check the
                // finished output against Uhuru's changed regions before a single file is written.
                if (UhuruGuard.Active)
                {
                    foreach (var (name, path, output, _) in pending)
                    {
                        string? hit = UhuruGuard.FirstOverlap(path, File.ReadAllBytes(path), output);
                        if (hit != null) r.Fail($"Refused on Uhuru: {hit}");
                    }
                    string cb = pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS);
                    byte[]? finalCode = pending.FirstOrDefault(p => p.Name == "code.bin").Output
                                        ?? (File.Exists(cb) ? File.ReadAllBytes(cb) : null);
                    if (PatchPackageInstaller.UhuruCeilingRefusal(pkg, supplied, finalCode) is { } ceiling) r.Fail(ceiling);
                    if (!r.Ok)
                    {
                        r.Fail("nothing was written: every file is left as it was");
                        break;
                    }
                }
                // code.bin and a grown exheader must land together: remember every file, and put them all back if any write fails.
                var originals = new Dictionary<string, byte[]?>();
                foreach (var (_, pendingPath, _, _) in pending)
                    originals[pendingPath] = File.Exists(pendingPath) ? File.ReadAllBytes(pendingPath) : null;
                try
                {
                    foreach (var (name, _, _, write) in pending)
                    {
                        write();
                        r.Say($"{name} written.");
                    }
                }
                catch (Exception writeEx)
                {
                    foreach (var (restorePath, restoreBytes) in originals)
                    {
                        try
                        {
                            if (restoreBytes != null) File.WriteAllBytes(restorePath, restoreBytes);
                            else if (File.Exists(restorePath)) File.Delete(restorePath);
                        }
                        catch { }
                    }
                    r.Fail($"write failed ({writeEx.Message}); every file was put back as it was");
                    break;
                }
                try
                {
                    // Merge, don't replace: a reinstall that found everything already in place places
                    // nothing, and replacing would erase the ranges the allocator must keep out of.
                    var entry = record.Packages.TryGetValue(pkg.Name, out var prior) ? prior : [];
                    foreach (var pl in placements)
                        if (!entry.Any(x => x.File.Equals(pl.File, StringComparison.OrdinalIgnoreCase) &&
                                            x.Block == pl.Block && x.Offset == pl.Offset))
                            entry.Add(pl);
                    record.Packages[pkg.Name] = entry;
                    record.Save(config.RomFS);
                }
                catch (Exception ex)
                {
                    r.Say($"! install record not saved ({ex.Message}); later installs cannot see these blocks");
                }

                // 4. Data and Text (after the binaries: it reads code.bin back to raise the item ceiling)
                var data = PatchPackageInstaller.ApplyData(pkg, supplied, config);
                foreach (string s in data.Log) r.Say("  " + s);
                foreach (string e in data.Errors) r.Fail("  " + e);

                r.Say($"{recipe.Name} installation complete.");
                break;
            }

            case RecipeEffectKind.ItemPatch:
                if (recipe.PatchName == null)
                {
                    r.Say("no handler exists for this one yet - it will appear in the bag but do nothing");
                    break;
                }

                string? itemPatchCro = ResolveTargetFile(ResearchTarget.BattleCro, config);
                if (string.IsNullOrWhiteSpace(itemPatchCro) || !File.Exists(itemPatchCro))
                    itemPatchCro = battleCroPath;   // fall back only when the ROM has no Battle.cro

                if (string.IsNullOrWhiteSpace(itemPatchCro) || !File.Exists(itemPatchCro) ||
                    !Path.GetFileName(itemPatchCro).Equals("Battle.cro", StringComparison.OrdinalIgnoreCase))
                {
                    r.Fail($"Battle.cro is needed to install the {recipe.PatchName} handler but was not found");
                    break;
                }
                r.Say($"  target: {Path.GetFileName(itemPatchCro)}");
                if (commit)
                {
                    bool ok = ResearchEngine.ApplyItemPatch(itemPatchCro, recipe.PatchName, ids[0]);
                    if (ok) r.Say($"{recipe.PatchName} handler patched to item {ids[0]}");
                    else r.Fail($"the {recipe.PatchName} patch did not match this Battle.cro - " +
                                "its signature is not present, so nothing was changed");
                }
                else
                {
                    r.Say($"would patch the {recipe.PatchName} handler to item {ids[0]}");
                }
                break;

            case RecipeEffectKind.CorpusPatch:
            {
                if (db == null) { r.Fail("the research notes are needed to find this recipe's patches"); break; }

                if (!VersionMatches(recipe, config, out string cpWanted, out string cpLoaded))
                {
                    r.Fail($"this workbook's offsets are for {cpWanted} and the loaded ROM is {cpLoaded}; " +
                           "the two builds differ, so nothing was written");
                    break;
                }

                var sheets = db.Sheets
                    .Where(s => string.Equals(Path.GetFileName(s.SourceFile ?? ""), recipe.SheetFile,
                                              StringComparison.OrdinalIgnoreCase))
                    .Where(s => s.Patches.Count > 0)
                    .ToList();

                var allRows = sheets.SelectMany(s => s.Patches).ToList();
                if (allRows.Count == 0)
                {
                    r.Fail($"'{recipe.SheetFile}' has no recorded byte writes in the notes");
                    break;
                }

                var patches = allRows.Where(p => p.Bytes is { Length: > 0 }).ToList();
                int noBytes = allRows.Count - patches.Count;

                if (patches.Count == 0)
                {
                    r.Fail($"'{recipe.SheetFile}' records {allRows.Count} step(s) but none of them carry " +
                           "assembled bytes - the sheet is working notes, not a finished patch, so there " +
                           "is nothing to write");
                    break;
                }
                if (noBytes > 0)
                    r.Say($"  {noBytes} of {allRows.Count} recorded step(s) carry no bytes and are skipped");

                var paths = new Dictionary<ResearchTarget, string>();
                foreach (var t in new[]
                {
                    ResearchTarget.BagCro, ResearchTarget.BattleCro,
                    ResearchTarget.FieldRoCro, ResearchTarget.CodeBin, ResearchTarget.EvolutionCro,
                })
                {
                    string? p = t == ResearchTarget.CodeBin
                        ? pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS)
                        : ResolveTargetFile(t, config);
                    if (p != null && File.Exists(p)) paths[t] = p;
                }
                if (paths.Count == 0) { r.Fail("none of Bag.cro, Battle.cro or code.bin could be found"); break; }

                var loaded = paths.ToDictionary(kv => kv.Key, kv => File.ReadAllBytes(kv.Value));

                var resolved = CorpusFeature.Resolve(sheets, loaded);
                if (resolved.Count == 0) { r.Fail("none of this workbook's sheets could be placed in a binary"); break; }

                if (recipe.CodeBinDeltaByVersion.Count > 0)
                {
                    string game = recipe.ForVersion ?? "";
                    var codeSheets = resolved.Where(x => x.Target == ResearchTarget.CodeBin).ToList();

                    if (codeSheets.Count > 0)
                    {
                        if (!string.IsNullOrEmpty(game) &&
                            recipe.CodeBinDeltaByVersion.TryGetValue(game, out var rules) && rules.Count > 0)
                        {
                            var bin = loaded[ResearchTarget.CodeBin];

                            // Highest matching From wins, so (0,0) + (0x341654,+4) means "nothing
                            // moves until 0x341654, then everything is 4 later".
                            int ShiftFor(uint off) => rules
                                .Where(s => off >= s.From)
                                .OrderByDescending(s => s.From)
                                .Select(s => s.Delta)
                                .FirstOrDefault();

                            bool Present(ResearchPatch p, long baseDelta)
                            {
                                long at = p.Offset + baseDelta + ShiftFor(p.Offset);
                                return at >= 0 && at + p.Bytes.Length <= bin.Length &&
                                       Enumerable.Range(0, p.Bytes.Length).All(k => bin[at + k] == p.Bytes[k]);
                            }

                            foreach (var x in codeSheets)
                            {
                                long plain = x.OffsetDelta;
                                long loadBased = x.OffsetDelta + 0x0010_0000L;
                                long best = x.Originals.Count(p => Present(p, loadBased)) >
                                            x.Originals.Count(p => Present(p, plain)) ? loadBased : plain;

                                x.PerRowShift = ShiftFor;
                                x.OffsetDelta = (uint)best;
                                x.OriginalsMatched = x.Originals.Count(p => Present(p, best));
                            }

                            if (rules.Any(s => s.Delta != 0))
                                r.Say($"  code.bin rows shifted for {game}: " +
                                      string.Join(", ", rules.Select(s => $"from 0x{s.From:X6} {s.Delta:+#;-#;0}")));
                        }
                        else if (!string.IsNullOrEmpty(game))
                        {
                            r.Fail($"this feature's code.bin offsets have not been mapped for {game} " +
                                   $"(known: {string.Join(", ", recipe.CodeBinDeltaByVersion.Keys)}) - " +
                                   "applying the other game's offsets would write into unrelated code");
                            break;
                        }
                    }
                }

                foreach (var x in resolved)
                    r.Say($"  [{x.Sheet.SheetName}] -> {Path.GetFileName(paths[x.Target])}: " +
                          $"{x.Edits.Count} write(s), {x.Originals.Count} original(s) - {x.Reason}");

                int totalOrig = resolved.Sum(x => x.Originals.Count);
                int totalHit = resolved.Sum(x => x.OriginalsMatched);

                if (totalOrig > 0)
                {
                    double rate = (double)totalHit / totalOrig;
                    r.Say($"  verification: {totalHit}/{totalOrig} recorded originals present ({rate:P0})");

                    if (rate < 0.90)
                    {
                        r.Fail($"only {totalHit} of {totalOrig} recorded original instructions are present in this " +
                               "ROM - the workbook was written against a different binary and will not be applied");
                        break;
                    }

                    // Name every row that did not match, so a slip can be told from a near-miss.
                    foreach (var x in resolved.Where(x => x.Originals.Count > 0 && !x.Verified))
                    {
                        var bin = loaded[x.Target];
                        foreach (var p in x.Originals)
                        {
                            bool hit = p.Offset + p.Bytes.Length <= bin.Length &&
                                       Enumerable.Range(0, p.Bytes.Length).All(k => bin[p.Offset + k] == p.Bytes[k]);
                            if (hit) continue;
                            r.Say($"  ! [{x.Sheet.SheetName}] 0x{p.Offset:X6} expected {p.HexBytes} " +
                                  $"but this ROM differs - the writes on that sheet are unverified");
                        }
                    }
                }

                var unchecked_ = resolved.Where(x => x.Originals.Count == 0 && x.Edits.Count > 0).ToList();
                foreach (var x in unchecked_)
                    r.Say($"  note: [{x.Sheet.SheetName}] records no original bytes, so its " +
                          $"{x.Edits.Count} write(s) cannot be checked before being made");

                foreach (var grp in resolved.Where(x => x.Edits.Count > 0).GroupBy(x => x.Target))
                {
                    if (grp.Key == ResearchTarget.CodeBin) continue;   // not a CRO; no segment header
                    var rows = grp.SelectMany(CorpusFeature.RebasedEdits)
                                  .Select(p => (p.Offset, p.Bytes)).ToList();
                    foreach (var f in PatchSanity.Check(loaded[grp.Key], rows, Recipe.FileNameOf(grp.Key)))
                    {
                        if (f.Fatal) r.Fail(f.Message);
                        else r.Say("  warning: " + f.Message);
                    }
                }
                if (!r.Ok) break;

                if (!commit)
                {
                    foreach (var x in resolved.Where(x => x.Edits.Count > 0))
                    {
                        var bin = loaded[x.Target];
                        var reb = CorpusFeature.RebasedEdits(x);
                        int present = reb.Count(p => p.Offset + p.Bytes.Length <= bin.Length &&
                            Enumerable.Range(0, p.Bytes.Length).All(k => bin[p.Offset + k] == p.Bytes[k]));
                        r.Say($"  [{x.Sheet.SheetName}] {present}/{reb.Count} already present");
                    }
                    break;
                }

                foreach (var grp in resolved.Where(x => x.Edits.Count > 0).GroupBy(x => x.Target))
                {
                    var bin = loaded[grp.Key];
                    var applied = CodePatchInstaller.ApplyRecorded(bin, grp.SelectMany(CorpusFeature.RebasedEdits).ToList());
                    foreach (string e in applied.Errors) r.Fail(e);
                    if (applied.Errors.Count > 0) continue;

                    bool isCro = grp.Key != ResearchTarget.CodeBin;
                    if (isCro) pk3DS.Core.CTR.CROUtil.UpdateHashes(bin);

                    File.WriteAllBytes(paths[grp.Key], bin);
                    r.Say($"{applied.Applied} write(s) applied to {Path.GetFileName(paths[grp.Key])}" +
                          (isCro ? "; hashes refreshed" : ""));
                }
                break;
            }

            case RecipeEffectKind.Template:
            {
                var t = FunctionTemplates.ByName(recipe.TemplateName ?? "");
                if (t == null) { r.Fail($"template '{recipe.TemplateName}' does not exist"); break; }

                if (map?.Cro == null)
                {
                    r.Fail("Battle.cro is not loaded, so the routine cannot be installed. " +
                           "Open the Research Center with a ROM loaded.");
                    break;
                }

                var def = t.Build(recipe.Name, recipe.TemplateValues);
                def.Mechanic = recipe.Kind;
                def.Target = ResearchTarget.BattleCro;
                def.MechanicIndex = ids.Count > 0 ? ids[0] : -1;
                def.MechanicName = recipe.AttachTo ?? recipe.Entries.FirstOrDefault()?.Name;
                def.Timing = recipe.Timing ?? TemplateTiming.Suggest(t, map)?.Timing ?? 0;

                r.Say($"template: {t.Name}");
                r.Say($"  installs as a {def.Mechanic} routine at timing 0x{def.Timing:X2}, id {def.MechanicIndex}");
                foreach (var kv in recipe.TemplateValues)
                    r.Say($"    {kv.Key} = {kv.Value}");

                // 'names' is the table the text stage above already loaded for this recipe's kind,
                // and is what the installer resolves MechanicName against.
                InstallPlan plan;
                try { plan = CustomFunctionInstaller.Plan(def, map.Cro, db, names, map); }
                catch (Exception ex) { r.Fail($"planning the routine failed: {ex.Message}"); break; }

                foreach (var s in plan.Steps)
                {
                    string line = $"  [{s.Severity}] {s.Stage}: {s.Message}";
                    if (s.Severity == PlanSeverity.Error) r.Fail(line); else r.Say(line);
                }

                if (plan.HasErrors)
                {
                    r.Fail("the routine was not installed - see the errors above");
                    break;
                }

                if (!commit)
                {
                    r.Say($"  would assemble {def.Assembly.Count} line(s) and install them");
                    break;
                }

                bool ok;
                var log = new List<string>();
                try { ok = CustomFunctionInstaller.Commit(plan, map.Cro, log.Add); }
                catch (Exception ex) { r.Fail($"installing the routine failed: {ex.Message}"); break; }

                foreach (string s in log.Take(12)) r.Say("  " + s);
                if (log.Count > 12) r.Say($"  … and {log.Count - 12} more");

                if (ok) r.Say("routine installed into Battle.cro");
                else r.Fail("the routine could not be written into Battle.cro");
                break;
            }
        }

        foreach (string c in recipe.Caveats) r.Say("note: " + c);
        return r;
    }

    /// <summary>Where an IPS recipe's undo bytes live: beside the install record, outside exefs.</summary>
    private static string IpsUndoPath(GameConfig config, string ipsPath) =>
        Path.Combine(Path.GetDirectoryName(InstallRecord.PathFor(config.RomFS)) ?? "",
                     $"pk3ds-undo-{Path.GetFileName(ipsPath)}.json");

    public static void BackupFileBeforeWrite(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
            string bak = filePath + ".bak";
            if (!File.Exists(bak))
            {
                File.Copy(filePath, bak, false);
            }
        }
        catch { }
    }

    /// <summary>
    /// Reverts a recipe from the ROM: restores recorded original bytes or backups.
    /// </summary>
    public static RecipeResult Revert(Recipe recipe, GameConfig config, string? battleCroPath = null,
                                      ResearchDatabase? db = null, BattleMechanicMap? map = null)
    {
        var r = new RecipeResult();
        r.Say($"Reverting recipe: {recipe.Name}");

        if (UhuruGuard.Active && recipe.EffectKind != RecipeEffectKind.DataOnly)
        {
            // A revert writes recorded bytes back into code.bin / CRO files (Bag.cro 0x15F40 is Uhuru's own code).
            r.Fail(UhuruGuard.RefusalFor("Battle.cro") + " Restore the .bak copy instead.");
            return r;
        }

        try
        {
            switch (recipe.EffectKind)
            {
                case RecipeEffectKind.ByteEdit:
                {
                    if (recipe.ByteEdits.Count == 0)
                    {
                        r.Fail("no byte edits defined for this recipe to revert");
                        break;
                    }

                    // Each edit goes back to its own file (as install does), and only where it still
                    // holds the installed value; otherwise nothing is written.
                    var files = new Dictionary<ResearchTarget, (string Path, byte[] Data)>();
                    foreach (var edit in recipe.ByteEdits)
                    {
                        var target = edit.Target ?? recipe.Target;
                        if (!files.ContainsKey(target))
                        {
                            string? tp = target == ResearchTarget.CodeBin
                                ? pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS)
                                : ResolveTargetFile(target, config);
                            if (string.IsNullOrEmpty(tp) || !File.Exists(tp))
                            { r.Fail($"{Recipe.FileNameOf(target)} was not found"); continue; }
                            files[target] = (tp, File.ReadAllBytes(tp));
                        }
                        var data = files[target].Data;
                        if (edit.Offset >= data.Length)
                        { r.Fail($"{Recipe.FileNameOf(target)} 0x{edit.Offset:X6} is past the end of the file"); continue; }
                        if (data[edit.Offset] == edit.From) continue; // already original
                        if (data[edit.Offset] != edit.To)
                        { r.Fail($"{Recipe.FileNameOf(target)} 0x{edit.Offset:X6} holds 0x{data[edit.Offset]:X2}, not this recipe's 0x{edit.To:X2}"); continue; }
                        data[edit.Offset] = edit.From;
                    }
                    if (!r.Ok) { r.Fail("nothing was reverted"); break; }

                    foreach (var (target, (path, data)) in files)
                    {
                        bool isCro = target != ResearchTarget.CodeBin;
                        if (isCro) pk3DS.Core.CTR.CROUtil.UpdateHashes(data);
                        File.WriteAllBytes(path, data);
                        r.Say($"Reverted byte edits in {Path.GetFileName(path)}" + (isCro ? "; hashes refreshed" : ""));
                    }
                    break;
                }

                case RecipeEffectKind.CorpusPatch:
                {
                    if (db == null)
                    {
                        r.Fail("Research database not loaded");
                        break;
                    }

                    string sheetName = recipe.SheetFile ?? "";
                    var sheets = db.Sheets
                        .Where(s => string.Equals(Path.GetFileName(s.SourceFile ?? ""), sheetName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var paths = new Dictionary<ResearchTarget, string>();
                    foreach (var t in new[] { ResearchTarget.BagCro, ResearchTarget.BattleCro, ResearchTarget.FieldRoCro, ResearchTarget.CodeBin, ResearchTarget.EvolutionCro })
                    {
                        string? p = t == ResearchTarget.CodeBin ? pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS) : ResolveTargetFile(t, config);
                        if (p != null && File.Exists(p)) paths[t] = p;
                    }

                    // Special known cases if sheet recorded no original rows (like Rare Candy on Level 100 or Expansion Evolution Fix)
                    if (string.Equals(sheetName, "Rare Candy on Level 100 Pokemon triggers evolution.xlsx", StringComparison.OrdinalIgnoreCase) ||
                        recipe.Name.Contains("Rare Candy", StringComparison.OrdinalIgnoreCase))
                    {
                        if (paths.TryGetValue(ResearchTarget.BagCro, out var bagPath) && File.Exists(bagPath))
                        {
                            byte[] bag = File.ReadAllBytes(bagPath);
                            if (bag.Length > 0x15FB0)
                            {
                                byte[] origBranch1 = [0x6E, 0x01, 0x00, 0x0A, 0x00, 0x10, 0xA0, 0xE3];
                                byte[] origBranch2 = [0x7C, 0x00, 0x94, 0xE5]; // ldr r0, [r4, #0x7c]
                                byte[] zeros = new byte[128];
                                Array.Copy(origBranch1, 0, bag, 0xC324, 8);
                                Array.Copy(origBranch2, 0, bag, 0xC50C, 4);
                                Array.Copy(zeros, 0, bag, 0x15F40, 128);
                                pk3DS.Core.CTR.CROUtil.UpdateHashes(bag);
                                File.WriteAllBytes(bagPath, bag);
                                r.Say($"Restored original Bag.cro instructions at 0xC324, 0xC50C, and 0x15F40; hashes refreshed.");
                            }
                        }

                        if (paths.TryGetValue(ResearchTarget.EvolutionCro, out var evoPath) && File.Exists(evoPath))
                        {
                            byte[] evo = File.ReadAllBytes(evoPath);
                            if (evo.Length > 0x6AA0)
                            {
                                byte[] origAC8 = [0xF8, 0x40, 0x2D, 0xE9]; // push {r3, r4, r5, r6, r7, lr}
                                byte[] orig978 = [0xF8, 0x43, 0x2D, 0xE9]; // push {r3, r4, r5, r6, r7, r8, sb, lr}
                                byte[] origB5C = [0xF8, 0x40, 0x2D, 0xE9]; // push {r3, r4, r5, r6, r7, lr}
                                byte[] zeros = new byte[128];
                                Array.Copy(origAC8, 0, evo, 0x0AC8, 4);
                                Array.Copy(orig978, 0, evo, 0x0978, 4);
                                Array.Copy(origB5C, 0, evo, 0x0B5C, 4);
                                Array.Copy(zeros, 0, evo, 0x6A20, 128);
                                pk3DS.Core.CTR.CROUtil.UpdateHashes(evo);
                                File.WriteAllBytes(evoPath, evo);
                                r.Say($"Restored original Evolution.cro instructions at 0xAC8, 0x978, and 0xB5C; hashes refreshed.");
                            }
                        }
                    }

                    if (sheets.Count == 0 && !r.Steps.Any(s => s.Contains("Bag.cro") || s.Contains("Evolution.cro")))
                    {
                        r.Fail($"no sheets found for workbook {sheetName}");
                        break;
                    }

                    var loaded = paths.ToDictionary(kv => kv.Key, kv => File.ReadAllBytes(kv.Value));
                    var resolved = CorpusFeature.Resolve(sheets, loaded);

                    foreach (var grp in resolved.Where(x => x.Edits.Count > 0).GroupBy(x => x.Target))
                    {
                        if (!paths.TryGetValue(grp.Key, out var targetPath) || !File.Exists(targetPath)) continue;

                        var bin = loaded[grp.Key];
                        int reverted = 0;

                        foreach (var sheet in grp)
                        {
                            if (sheet.Originals.Count > 0)
                            {
                                foreach (var orig in sheet.Originals)
                                {
                                    long at = orig.Offset + sheet.OffsetDelta + (sheet.PerRowShift != null ? sheet.PerRowShift(orig.Offset) : 0);
                                    if (at >= 0 && at + orig.Bytes.Length <= bin.Length)
                                    {
                                        Array.Copy(orig.Bytes, 0, bin, at, orig.Bytes.Length);
                                        reverted++;
                                    }
                                }
                            }
                        }

                        if (reverted > 0)
                        {
                            bool isCro = grp.Key != ResearchTarget.CodeBin;
                            if (isCro) pk3DS.Core.CTR.CROUtil.UpdateHashes(bin);
                            File.WriteAllBytes(targetPath, bin);
                            r.Say($"Restored {reverted} original write(s) in {Path.GetFileName(targetPath)}" + (isCro ? "; hashes refreshed" : ""));
                        }
                    }
                    break;
                }

                case RecipeEffectKind.IpsPatch:
                {
                    string codePath = pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS);
                    string undoPath = string.IsNullOrWhiteSpace(recipe.IpsPath) ? "" : IpsUndoPath(config, recipe.IpsPath);
                    if (!File.Exists(codePath)) { r.Fail("code.bin was not found"); break; }
                    if (!File.Exists(undoPath))
                    {
                        r.Fail("no undo was recorded for this patch (installed before per-patch undo existed). " +
                               $"Restoring code.bin.bak would also undo every later code.bin change, so it is not done automatically.");
                        break;
                    }
                    if (string.IsNullOrWhiteSpace(recipe.IpsPath) || !File.Exists(recipe.IpsPath))
                    { r.Fail("the .ips file is needed to check this patch is still in place, and was not found"); break; }

                    byte[] code = File.ReadAllBytes(codePath);
                    var applied = CodeBinExpansionJournal.TranslateIpsRecords(IpsPatch.Read(File.ReadAllBytes(recipe.IpsPath)), codePath);
                    var changed = applied.Where(x => x.End > code.Length ||
                                                     !code.AsSpan(x.Offset, x.Bytes.Length).SequenceEqual(x.Bytes)).ToList();
                    if (changed.Count > 0)
                    {
                        r.Fail($"{changed.Count} of this patch's record(s) were changed since (first at 0x{changed[0].Offset:X6}); " +
                               "reverting would overwrite the later change, so nothing was written");
                        break;
                    }
                    var undo = System.Text.Json.JsonSerializer.Deserialize<List<IpsRecord>>(File.ReadAllText(undoPath)) ?? [];
                    int restored = IpsPatch.Apply(code, undo);
                    File.WriteAllBytes(codePath, code);
                    File.Delete(undoPath);
                    r.Say($"Restored {restored} byte(s) this patch had changed in code.bin.");
                    break;
                }

                case RecipeEffectKind.Package:
                {
                    var pkg = recipe.Package;
                    if (pkg == null) { r.Fail("no package found on recipe"); break; }

                    var record = InstallRecord.Load(config.RomFS);
                    bool recorded = record.Knows(pkg.Name);

                    // Battle.cro: master-table mechanics and placed blocks cannot be taken back out, and
                    // restoring only the hooks would leave the mechanics live. Say so rather than report
                    // success (revert used to skip Battle.cro silently).
                    if ((pkg.Mechanics?.Count ?? 0) > 0 || (pkg.Blocks?.Count ?? 0) > 0 || (pkg.SitePatches?.Count ?? 0) > 0)
                        r.Warn($"Battle.cro was not reverted ({pkg.Mechanics?.Count ?? 0} mechanic(s), {pkg.Blocks?.Count ?? 0} block(s), " +
                               $"{pkg.SitePatches?.Count ?? 0} hook(s)); restore it from Battle.cro.bak if this package must be fully removed.");

                    // Revert OtherCros (e.g. Bag.cro)
                    foreach (var (croName, croPort) in pkg.OtherCros ?? [])
                    {
                        string croPath = Path.Combine(config.RomFS, croName);
                        if (!File.Exists(croPath)) croPath = Path.Combine(config.RomFS, "cro", croName);
                        if (!File.Exists(croPath)) continue;

                        byte[] bin = File.ReadAllBytes(croPath);
                        int reverted = 0;
                        foreach (var site in croPort.SitePatches ?? [])
                        {
                            byte[] orig = site.OriginalBytes;
                            uint at = site.OffsetValue;
                            if (orig == null || orig.Length == 0 || at + orig.Length > bin.Length) continue;

                            // A hook shared with other installed packages (bag-item-effects is shared by
                            // six) must stay, or reverting one package disables the rest. Without an
                            // install record nobody can tell, so hooks are left alone then.
                            if (site.IsHook && !recorded)
                            { r.Say($"  {croName} 0x{at:X6}: hook left in place (no install record says whether other packages use it)"); continue; }
                            if (record.OthersUse(pkg.Name, croName, site.IsHook ? site.HookTarget ?? "" : "", at))
                            { r.Say($"  {croName} 0x{at:X6}: still used by another installed package, left in place"); continue; }

                            // Only undo what this package wrote: a literal site must still hold its bytes,
                            // a hook must still be a branch.
                            bool ours = site.IsHook
                                ? pk3DS.Core.CTR.ARMCodec.IsBranch(BitConverter.ToUInt32(bin, (int)at))
                                : bin.AsSpan((int)at, orig.Length).SequenceEqual(site.PatchedBytes);
                            if (!ours) { r.Say($"  {croName} 0x{at:X6}: no longer holds this package's bytes, left alone"); continue; }

                            Array.Copy(orig, 0, bin, at, orig.Length);
                            reverted++;
                        }

                        if (reverted > 0)
                        {
                            pk3DS.Core.CTR.CROUtil.UpdateHashes(bin);
                            File.WriteAllBytes(croPath, bin);
                            r.Say($"Restored {reverted} site patch(es) in {croName}; hashes refreshed.");
                        }
                    }

                    // Revert CodeBin
                    bool codeBinUndone = pkg.CodeBin == null;
                    if (pkg.CodeBin != null)
                    {
                        string codePath = pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS);
                        if (File.Exists(codePath))
                        {
                            // Patch offsets are measured from a symbol, not the start of the file:
                            // writing them raw put AbilityCycle's original bytes over code.bin's entry.
                            var undo = PortCodeBinInstaller.Revert(File.ReadAllBytes(codePath), pkg.CodeBin, config.RomFS, out int reverted);
                            foreach (string s in undo.Log) r.Say(s);
                            foreach (string e in undo.Errors) r.Fail(e);
                            if (!undo.Success)
                                r.Fail("code.bin left unchanged: not every patch could be confirmed in place.");
                            else
                            {
                                codeBinUndone = true;
                                if (reverted > 0)
                                {
                                    File.WriteAllBytes(codePath, undo.Output);
                                    r.Say($"Restored {reverted} code patch(es) in code.bin.");
                                }
                            }
                        }
                    }

                    // Keep the entry while code.bin still carries the package: dropping it would let the
                    // allocator hand that live code to the next install.
                    if (recorded && codeBinUndone && record.Packages.Remove(pkg.Name))
                    {
                        try { record.Save(config.RomFS); }
                        catch (Exception ex) { r.Say($"! install record not updated ({ex.Message})"); }
                    }
                    break;
                }

                default:
                    r.Fail($"Reverting is not supported for effect kind {recipe.EffectKind}.");
                    break;
            }
        }
        catch (Exception ex)
        {
            r.Fail($"Reverting failed ({ex.GetType().Name}: {ex.Message})");
        }

        return r;
    }
}
