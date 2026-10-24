using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>What one package would do, or did.</summary>
public sealed class PackageResult
{
    public string Package { get; init; } = "";
    public bool Success { get; set; } = true;
    public bool Skipped { get; set; }
    public List<string> Log { get; } = [];
    public List<string> Errors { get; } = [];
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool TableRelocated { get; set; }

    internal void Say(string s) => Log.Add(s);
    internal void Fail(string s) { Errors.Add(s); Success = false; }
    public string Describe() => string.Join(Environment.NewLine,
        Log.Select(l => "  " + l).Concat(Errors.Select(e => "  ERROR: " + e)));
}

/// <summary>
/// Applies <see cref="PatchPackage"/>s to a ROM, resolving each package's parameters first.
/// <para>
/// Everything a package touches goes through the installers that already exist — the master-table
/// path for mechanics, the site path for CROs, the symbol-anchored path for code.bin — so a package
/// gains all of their checking rather than a second, weaker copy of it. What this adds is the layer
/// above: choosing ids, ordering by dependency, and refusing a package whose chosen id names
/// something else in the target.
/// </para>
/// </summary>
public static class PatchPackageInstaller
{
    /// <summary>
    /// Validates a package against a ROM without writing: are the parameters resolvable, do the ids
    /// name what they claim, is anything already present.
    /// </summary>
    public static PackageResult Preview(
        PatchPackage package,
        IDictionary<string, string> supplied,
        BattleMechanicMap map,
        Dictionary<CustomMechanicKind, string[]> nameTables = null)
    {
        var result = new PackageResult { Package = package.Name };
        var problems = new List<string>();
        var values = PatchParameters.Resolve(package, supplied, problems);
        foreach (string p in problems) result.Fail(p);
        foreach (var kv in values) result.Values[kv.Key] = kv.Value;
        if (!result.Success) return result;

        result.Say($"{package.Name}: " + string.Join(", ", values.Select(kv => $"{kv.Key}={kv.Value}")));

        var bound = PatchParameters.Bind(package, values, problems);
        foreach (string p in problems.Distinct()) result.Fail(p);
        if (!result.Success) return result;

        // Every chosen id must either name the thing being installed or be an unclaimed slot.
        foreach (var (key, param) in package.Parameters ?? [])
        {
            if (param.Type is not ("item" or "move" or "ability")) continue;
            if (!values.TryGetValue(key, out string raw) || !uint.TryParse(raw, out uint id)) continue;

            var kind = param.Type switch
            {
                "item" => CustomMechanicKind.Item,
                "move" => CustomMechanicKind.Move,
                _ => CustomMechanicKind.Ability,
            };

            if (nameTables != null && nameTables.TryGetValue(kind, out var table) && id < table.Length)
            {
                string named = (table[id] ?? "").Trim();
                bool blank = named is "" or "???" or "？？？" or "(?)" or "-----" or "—";
                string want = param.ExpectName
                    ?? (package.ItemNames != null && package.ItemNames.TryGetValue($"${{{key}}}", out var iname) ? iname : null)
                    ?? (package.ItemNames != null && package.ItemNames.TryGetValue(id.ToString(), out var iname2) ? iname2 : null)
                    ?? package.Name;

                if (!blank && !named.Replace('’', '\'').Equals(want.Replace('’', '\''), StringComparison.OrdinalIgnoreCase))
                    result.Fail($"Collision: {key}={id} is already occupied by '{named}' (expected '{want}' or an unnamed slot)");
                else if (blank && !param.AllowUnnamed)
                    result.Fail($"{key}={id} is an unnamed slot and this package needs a named one");
                else
                    result.Say($"  {key}={id} -> '{(blank ? "unnamed slot" : named)}'");
            }

            if (map?.Find(kind, id) != null)
                result.Say($"  {kind} {id} already has an effect entry — that part will be skipped");
        }

        int edits = bound.Mechanics.Count + bound.SitePatches.Count + bound.Blocks.Count
                  + (bound.CodeBin?.Patches.Count ?? 0)
                  + bound.OtherCros.Sum(c => c.Value.SitePatches.Count)
                  + bound.ItemData.Count + bound.ItemNames.Count;
        result.Say($"  {edits} edit(s): {bound.Mechanics.Count} mechanic(s), {bound.SitePatches.Count} site patch(es), " +
                   $"{bound.Blocks.Count} block(s), {bound.OtherCros.Count} other CRO(s), " +
                   $"{bound.ItemData.Count} item data, {bound.ItemNames.Count} name(s)");
        return result;
    }

    /// <summary>
    /// Applies the Battle.cro half of a bound package. The caller keeps ownership of the other
    /// binaries, because a package that spans files must not leave some of them written and the
    /// rest not.
    /// </summary>
    public static PackageResult ApplyBattleCro(
        PatchPackage package,
        IDictionary<string, string> supplied,
        byte[] targetRom,
        ResearchDatabase db,
        Dictionary<CustomMechanicKind, string[]> nameTables,
        out byte[] output)
    {
        output = null;
        var result = new PackageResult { Package = package.Name };
        var problems = new List<string>();
        var values = PatchParameters.Resolve(package, supplied, problems);
        foreach (string p in problems) result.Fail(p);
        if (!result.Success) return result;

        var bound = PatchParameters.Bind(package, values, problems);
        foreach (string p in problems.Distinct()) result.Fail(p);
        if (!result.Success) return result;

        if (bound.Mechanics.Count == 0 && bound.SitePatches.Count == 0 && bound.Blocks.Count == 0)
        { result.Skipped = true; result.Say("nothing for Battle.cro"); output = targetRom; return result; }

        var manifest = new PortManifest
        {
            Description = package.Name,
            Mechanics = bound.Mechanics,
            Blocks = bound.Blocks,
            SitePatches = bound.SitePatches,
        };

        var port = PortInstaller.Apply(targetRom, manifest, db, nameTables);
        foreach (string l in port.Log) result.Say(l);
        foreach (string e in port.Errors) result.Fail(e);
        result.TableRelocated = port.TableRelocated;
        if (port.Success) output = port.Output;
        return result;
    }

    /// <summary>
    /// Writes a package's data tables into blocks that have already been placed.
    /// <para>
    /// Must run after the blocks land, because a table is positioned relative to its block, not to
    /// the file. <paramref name="placed"/> is the block-name to address map the CRO or code.bin
    /// installer produced.
    /// </para>
    /// </summary>
    public static PackageResult ApplyDataTables(
        PatchPackage bound, string target, byte[] binary, IReadOnlyDictionary<string, uint> placed, IReadOnlyDictionary<string, uint> externalPlaced = null)
    {
        var result = new PackageResult { Package = bound.Name };
        foreach (var t in bound.DataTables ?? [])
        {
            if (!string.Equals(t.Target, target, StringComparison.OrdinalIgnoreCase)) continue;

            uint block;
            if (!placed.TryGetValue(t.Block, out block))
            {
                if (externalPlaced == null || !externalPlaced.TryGetValue(t.Block, out block))
                { result.Fail($"data table names block '{t.Block}', which was not placed in {target}"); continue; }
            }

            var parts = (t.Values ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var vals = new List<uint>();
            foreach (string p in parts)
            {
                // "a+b+..." of plain numbers packs several fields into one element, e.g. the bag
                // effect table's item | handler<<16 | arg<<24 built from a list parameter.
                if (p.Contains('+') && !p.StartsWith("${", StringComparison.Ordinal))
                {
                    ulong sum = 0; bool allNum = true;
                    foreach (string term in p.Split('+', StringSplitOptions.TrimEntries))
                    {
                        if (uint.TryParse(term, out uint tv) ||
                            (term.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                             uint.TryParse(term[2..], System.Globalization.NumberStyles.HexNumber, null, out tv)))
                            sum += tv;
                        else { allNum = false; break; }
                    }
                    if (allNum && sum <= uint.MaxValue) { vals.Add((uint)sum); continue; }
                }

                if (!uint.TryParse(p, out uint v) &&
                    !(p.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                      uint.TryParse(p[2..], System.Globalization.NumberStyles.HexNumber, null, out v)))
                {
                    // Check for block or externalPlaced reference: e.g. "${block:name}" or "${block:name+0x110}"
                    string token = p;
                    if (token.StartsWith("${block:", StringComparison.OrdinalIgnoreCase) && token.EndsWith("}"))
                        token = token.Substring(8, token.Length - 9).Trim();
                    else if (token.StartsWith("${", StringComparison.OrdinalIgnoreCase) && token.EndsWith("}"))
                        token = token.Substring(2, token.Length - 3).Trim();

                    uint addOffset = 0;
                    int plusIdx = token.IndexOf('+');
                    if (plusIdx > 0)
                    {
                        string offStr = token[(plusIdx + 1)..].Trim();
                        if (offStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                            uint.TryParse(offStr[2..], System.Globalization.NumberStyles.HexNumber, null, out addOffset);
                        else
                            uint.TryParse(offStr, out addOffset);
                        token = token[..plusIdx].Trim();
                    }

                    if ((externalPlaced != null && externalPlaced.TryGetValue(token, out uint resolvedAddr)) ||
                        placed.TryGetValue(token, out resolvedAddr))
                    {
                        v = resolvedAddr + addOffset;
                    }
                    else
                    {
                        result.Fail($"'{p}' is not a number or resolvable block reference");
                        continue;
                    }
                }
                vals.Add(v);
            }
            if (!result.Success) continue;

            uint fileBase = (string.Equals(target, "code.bin", StringComparison.OrdinalIgnoreCase) || block >= 0x00100000)
                ? (block >= 0x00100000 ? block - 0x00100000 : block)
                : block;
            uint at = fileBase + t.OffsetValue;

            // Merge with existing non-zero values if this is a terminated list (e.g. Bag.cro usable items table)
            // to support multiple packages contributing to a unified table without clobbering each other.
            var combinedVals = new List<uint>();
            if (t.Terminate && at + t.ElementSize <= binary.Length)
            {
                int maxScan = t.Capacity > 0 ? t.Capacity : 128;
                for (int i = 0; i < maxScan; i++)
                {
                    uint scanAt = at + (uint)(i * t.ElementSize);
                    if (scanAt + t.ElementSize > binary.Length) break;
                    uint existing = t.ElementSize switch
                    {
                        1 => binary[scanAt],
                        2 => BitConverter.ToUInt16(binary, (int)scanAt),
                        _ => BitConverter.ToUInt32(binary, (int)scanAt)
                    };
                    if (existing == 0) break;
                    if (!combinedVals.Contains(existing)) combinedVals.Add(existing);
                }
            }
            // De-duplicate ONLY for a terminated list, where the table is a SET of ids several
            // packages contribute to. A non-terminated table is positional - the Pokemon Center
            // service list is indexed by Center number and legitimately repeats the same script id -
            // so collapsing duplicates there silently shortens it and misaligns every later entry.
            foreach (uint v in vals)
            {
                if (!t.Terminate || !combinedVals.Contains(v)) combinedVals.Add(v);
            }
            if (combinedVals.Count == 0) combinedVals = vals;

            int needed = combinedVals.Count + (t.Terminate ? 1 : 0);
            if (t.Capacity > 0 && needed > t.Capacity)
            { result.Fail($"{t.Block}+0x{t.OffsetValue:X}: {combinedVals.Count} value(s) need {needed} slot(s), block reserves {t.Capacity}"); continue; }

            if (at + needed * t.ElementSize > binary.Length)
            { result.Fail($"{t.Block}+0x{t.OffsetValue:X} runs past the end of {target}"); continue; }

            uint max = t.ElementSize switch { 1 => byte.MaxValue, 2 => ushort.MaxValue, _ => uint.MaxValue };
            foreach (uint v in combinedVals)
                if (v > max) { result.Fail($"{v} does not fit in {t.ElementSize} byte(s)"); }
            if (!result.Success) continue;

            for (int i = 0; i < combinedVals.Count; i++) WriteAt(binary, at + (uint)(i * t.ElementSize), combinedVals[i], t.ElementSize);
            if (t.Terminate) WriteAt(binary, at + (uint)(combinedVals.Count * t.ElementSize), 0, t.ElementSize);

            result.Say($"{target} {t.Block}+0x{t.OffsetValue:X}: wrote {combinedVals.Count} value(s)" +
                       (t.Terminate ? " and a terminator" : "") + $" at 0x{at:X6}");
        }
        return result;
    }

    private static void WriteAt(byte[] b, uint at, uint value, int size)
    {
        switch (size)
        {
            case 1: b[at] = (byte)value; break;
            case 2: BitConverter.GetBytes((ushort)value).CopyTo(b, (int)at); break;
            default: BitConverter.GetBytes(value).CopyTo(b, (int)at); break;
        }
    }

    /// <summary>Writes a package's item data and names, using the game's own tables.</summary>
    /// <summary>
    /// On Uhuru, why <see cref="ApplyData"/> would refuse this package's item-ceiling raise, or null.
    /// RecipeInstaller asks before writing any binary, because ApplyData runs after them.
    /// </summary>
    public static string UhuruCeilingRefusal(PatchPackage package, IDictionary<string, string> supplied, byte[] codeBin)
    {
        if (!UhuruGuard.Active || codeBin == null) return null;
        var problems = new List<string>();
        var bound = PatchParameters.Bind(package, PatchParameters.Resolve(package, supplied, problems), problems);
        if (problems.Count > 0) return null; // ApplyData reports these and writes nothing
        uint max = bound.ItemData.Keys.Select(k => uint.TryParse(k, out uint v) ? v : 0u).DefaultIfEmpty(0u).Max();
        if (max < ItemCeilingPatcher.RetailCeiling) return null;
        return ItemCeilingPatcher.Raise((byte[])codeBin.Clone(), (int)max).PatchedCount > 0
            ? UhuruGuard.RefusalFor("code.bin") + " (item ceiling raise)" : null;
    }

    public static PackageResult ApplyData(PatchPackage package, IDictionary<string, string> supplied, GameConfig config)
    {
        var result = new PackageResult { Package = package.Name };
        var problems = new List<string>();
        var values = PatchParameters.Resolve(package, supplied, problems);
        foreach (string p in problems) result.Fail(p);
        if (!result.Success) return result;
        var bound = PatchParameters.Bind(package, values, problems);
        if (!result.Success) return result;

        if (bound.ItemData.Count == 0 && bound.ItemNames.Count == 0 && bound.ItemDescriptions.Count == 0)
        { result.Skipped = true; return result; }

        var garc = config.GetGARCData("item");
        var files = garc.Files;

        var existingNames = config.GetText(TextName.ItemNames) ?? [];

        // Hard structural limit, confirmed by direct disassembly of Bag.cro's own item-id reads
        // (0x0DCAC and 0x0099CC both do 'mov r1,r1,lsl #22; mov r1,r1,lsr #22'): a bag slot stores
        // the item id in a 10-BIT packed field, so 1023 is the highest id the bag can represent at
        // all. The retail item name table is exactly 1024 entries for the same reason. An id at or
        // above 1024 does not merely go unnamed - it truncates modulo 1024 and silently aliases a
        // real low item (1024 -> Master Ball), which is why such items showed "???" and reported
        // "It won't have any effect". Raising ItemCeilingPatcher's ceiling does NOT lift this; the
        // packed field would have to widen, changing save data layout. Refuse instead of aliasing.
        foreach (var idText in bound.ItemData.Keys)
        {
            if (uint.TryParse(idText, out uint bid) && bid > 1023)
                result.Fail($"item id {bid} exceeds the bag's 10-bit item-id field (max 1023) - " +
                            $"it would alias item {bid & 0x3FF}; pick a free id below 1024");
        }
        if (!result.Success) return result;

        // Collision safety guard: refuse to overwrite named items unless explicitly allowed or name matches
        foreach (var (idText, _) in bound.ItemData)
        {
            if (!uint.TryParse(idText, out uint id)) continue;
            if (id < existingNames.Length)
            {
                string named = (existingNames[id] ?? "").Trim();
                bool blank = named is "" or "???" or "？？？" or "(?)" or "-----" or "—";
                string want = bound.ItemNames.TryGetValue(idText, out var iname) ? iname : package.Name;
                if (!blank && !named.Replace('’', '\'').Equals(want.Replace('’', '\''), StringComparison.OrdinalIgnoreCase))
                {
                    result.Fail($"Collision: Item ID {id} is already occupied by '{named}' (refusing to overwrite with '{want}')");
                }
            }
        }
        if (!result.Success) return result;

        uint maxItemId = 0;
        foreach (var idText in bound.ItemData.Keys)
            if (uint.TryParse(idText, out uint kid) && kid > maxItemId) maxItemId = kid;

        if (maxItemId >= files.Length)
        {
            int oldLen = files.Length;
            Array.Resize(ref files, (int)maxItemId + 1);
            int template = oldLen > 1 ? 1 : 0;
            for (int i = oldLen; i < files.Length; i++)
                files[i] = (byte[])files[template].Clone();
            garc.Files = files;
        }

        // Everything is validated before anything is written: code.bin, the item GARC and the pocket
        // table used to be saved one after another, so a later failure (a bad pocket, a bad field)
        // left the earlier files already changed.
        var pocketWanted = new List<(int id, int pocket)>();
        foreach (var (idText, data) in bound.ItemData)
        {
            if (string.IsNullOrWhiteSpace(data.Pocket)) continue;
            if (!int.TryParse(idText, out int pid) || pid < 0 || pid >= ItemPocketTable.ItemCount) continue;
            if (!int.TryParse(data.Pocket, out int pocket) || pocket is < 0 or > 15)
            { result.Fail($"item {idText}: pocket '{data.Pocket}' is not 0-15"); continue; }
            pocketWanted.Add((pid, pocket));
        }
        if (!result.Success) return result;

        foreach (var (idText, data) in bound.ItemData)
        {
            if (!uint.TryParse(idText, out uint id) || id >= files.Length)
            { result.Fail($"item id '{idText}' is out of range"); continue; }

            if (!string.IsNullOrWhiteSpace(data.CloneFrom))
            {
                if (!uint.TryParse(data.CloneFrom, out uint from) || from >= files.Length)
                { result.Fail($"cloneFrom '{data.CloneFrom}' is out of range"); continue; }
                files[id] = (byte[])files[from].Clone();
                result.Say($"item {id}: data cloned from {from}");
            }

            var it = new pk3DS.Core.Structures.Item(files[id]);
            if (uint.TryParse(data.HeldEffect, out uint he)) { it.HeldEffect = (byte)he; result.Say($"item {id}: heldEffect {he}"); }
            if (uint.TryParse(data.HeldArgument, out uint ha)) { it.HeldArgument = (byte)ha; result.Say($"item {id}: heldArgument {ha}"); }
            if (uint.TryParse(data.ItemKind, out uint ik))
            {
                // Gate on the whole bag-use effect system - see PatchItemData.ItemKind. Without this
                // set to 1, GetHealingItemType returns 0x1C and the item silently does nothing.
                it.Unk_0xC = (byte)ik;
                result.Say($"item {id}: itemKind (Unk_0xC) {ik}" + (ik == 1 ? " - healing-item dispatch enabled" : ""));
            }
            if (uint.TryParse(data.EffectField, out uint ef)) { it.EffectField = (byte)ef; result.Say($"item {id}: effectField {ef}"); }
            if (uint.TryParse(data.FlingPower, out uint fp)) it.FlingPower = (byte)fp;

            // Arbitrary Item properties, by their real names on the struct. This is what lets a
            // package express a bag-use effect as data instead of injected code.
            foreach (var (fname, fval) in data.Fields ?? [])
            {
                var prop = typeof(pk3DS.Core.Structures.Item).GetProperty(
                    fname, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop == null || !prop.CanWrite)
                { result.Fail($"item {id}: Item has no writable property '{fname}'"); continue; }
                object boxed = it;
                try
                {
                    var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    object v = t == typeof(bool) ? bool.Parse(fval.Trim())
                             : t.IsEnum ? Enum.Parse(t, fval.Trim(), true)
                             : Convert.ChangeType(ParseNum(fval), t);
                    prop.SetValue(boxed, v);
                    it = (pk3DS.Core.Structures.Item)boxed;
                    result.Say($"item {id}: {fname} = {fval.Trim()}");
                }
                catch (Exception ex)
                { result.Fail($"item {id}: cannot set {fname}='{fval}' ({ex.Message})"); }
            }
            files[id] = it.Write();
            if (uint.TryParse(data.Price, out uint pr)) BitConverter.GetBytes((ushort)pr).CopyTo(files[id], 0);
        }
        if (!result.Success) { result.Fail("nothing was written: fix the item data errors above"); return result; }

        // Auto-raise code.bin item ceiling if new items are placed at or above the retail ceiling
        if (maxItemId >= ItemCeilingPatcher.RetailCeiling)
        {
            string codeBinPath = pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS);
            if (File.Exists(codeBinPath))
            {
                byte[] codeBin = File.ReadAllBytes(codeBinPath);
                var ceilRes = ItemCeilingPatcher.Raise(codeBin, (int)maxItemId);
                if (ceilRes.PatchedCount > 0)
                {
                    string uhuruRefusal = UhuruGuard.RefusalFor(codeBinPath);
                    if (uhuruRefusal != null)
                    { result.Fail(uhuruRefusal + " (item ceiling raise)"); return result; }
                    File.WriteAllBytes(codeBinPath, codeBin);
                    result.Say($"item ceiling auto-raised to 0x{ceilRes.AppliedCeiling:X} for item {maxItemId}");
                }
            }
        }
        garc.Save();

        // Bag pocket lives in a separate nibble table (a/1/0/3), NOT in the item data block just
        // written - so a cloned item silently inherits its TEMPLATE's pocket. Every package here
        // clones Ability Capsule, whose neighbourhood of "???" ids happens to sit in the Key
        // Items pocket, which is why medicine-shaped items (mints, Ability Patch) turned up under
        // Key Items in game. Assign the pocket explicitly for any item that asked for one.
        if (pocketWanted.Count > 0)
        {
            var table = ItemPocketTable.Load(config.RomFS);
            if (table == null)
                result.Say("! could not read a/1/0/3 - Bag pockets left as cloned");
            else
            {
                foreach (var (id, pocket) in pocketWanted)
                {
                    if (UhuruGuard.Active && UhuruGuard.ProtectedPocketItems.Contains(id))
                    { result.Say($"item {id}: pocket left alone (Uhuru uses this item for Tera)"); continue; }
                    int was = ItemPocketTable.Read(table, id);
                    ItemPocketTable.Write(table, id, pocket);
                    result.Say($"item {id}: pocket {ItemPocketTable.PocketNames[was & 0xF]} -> " +
                               $"{ItemPocketTable.PocketNames[pocket]}");
                }
                if (!ItemPocketTable.Save(config.RomFS, table))
                    result.Fail("could not write a/1/0/3 (Bag pocket table)");
            }
        }

        int langs = 0, descLangs = 0, pluralLangs = 0, pluralAltLangs = 0, textLangs = 0;
        int originalLanguage = config.Language;
        try
        {
            for (int lang = 0; lang <= 9; lang++)
            {
                try
                {
                    config.Language = lang;
                    try { config.InitializeGameText(); }
                    catch (Exception)
                    {
                        // Not every index is a language in every ROM (USUM has 9 of 0-9).
                        result.Say($"language {lang}: no text archive in this ROM, skipped");
                        continue;
                    }

                    // Raw game text lines (bag use-result messages etc.), same languages as names.
                    bool wroteLines = false;
                    foreach (var (fileText, lines) in bound.GameTextLines ?? [])
                    {
                        if (!int.TryParse(fileText, out int fi) || fi < 0 || fi >= config.GameTextStrings.Length) continue;
                        var s = config.GameTextStrings[fi];
                        int top = lines.Keys.Select(k => int.TryParse(k, out int li) ? li : -1).DefaultIfEmpty(-1).Max();
                        if (top < 0) continue;
                        if (top >= s.Length)
                        {
                            var grown = new string[top + 1];
                            Array.Copy(s, grown, s.Length);
                            for (int i = s.Length; i < grown.Length; i++) grown[i] = "";
                            s = grown;
                        }
                        foreach (var (lineText, text) in lines)
                            if (int.TryParse(lineText, out int li) && li >= 0) s[li] = text;
                        config.GameTextStrings[fi] = s;
                        config.GARCGameText.Files[fi] = TextFile.GetBytes(config, s);
                        wroteLines = true;
                    }
                    if (wroteLines) { config.GARCGameText.Save(); textLangs++; }

                    var n = config.GetText(TextName.ItemNames);
                    if (n != null && n.Length > 0 && bound.ItemNames.Count > 0)
                    {
                        uint maxNameId = 0;
                        foreach (var k in bound.ItemNames.Keys)
                            if (uint.TryParse(k, out uint kid) && kid > maxNameId) maxNameId = kid;

                        if (maxNameId >= n.Length)
                        {
                            var grown = new string[maxNameId + 1];
                            Array.Copy(n, grown, n.Length);
                            for (int i = n.Length; i < grown.Length; i++) grown[i] = "???";
                            n = grown;
                        }

                        var doable = bound.ItemNames
                            .Where(kv => uint.TryParse(kv.Key, out uint i) && i < n.Length)
                            .ToList();
                        if (doable.Count > 0)
                        {
                            foreach (var kv in doable) n[uint.Parse(kv.Key)] = kv.Value;
                            config.SetText(TextName.ItemNames, n);
                            config.SaveText(TextName.ItemNames);
                            langs++;
                        }
                    }

                    var p = config.GetText(TextName.ItemNamesPlural);
                    if (p != null && p.Length > 0 && bound.ItemNames.Count > 0)
                    {
                        uint maxPluralId = 0;
                        foreach (var k in bound.ItemNames.Keys)
                            if (uint.TryParse(k, out uint kid) && kid > maxPluralId) maxPluralId = kid;

                        if (maxPluralId >= p.Length)
                        {
                            var grown = new string[maxPluralId + 1];
                            Array.Copy(p, grown, p.Length);
                            for (int i = p.Length; i < grown.Length; i++) grown[i] = "???";
                            p = grown;
                        }

                        var doable = bound.ItemNames
                            .Where(kv => uint.TryParse(kv.Key, out uint i) && i < p.Length)
                            .ToList();
                        if (doable.Count > 0)
                        {
                            foreach (var kv in doable)
                            {
                                string plural = (bound.ItemNamesPlural != null && bound.ItemNamesPlural.TryGetValue(kv.Key, out string pl) && !string.IsNullOrWhiteSpace(pl))
                                    ? pl
                                    : ItemTextUtil.Pluralize(kv.Value);
                                p[uint.Parse(kv.Key)] = plural;
                            }
                            config.SetText(TextName.ItemNamesPlural, p);
                            config.SaveText(TextName.ItemNamesPlural);
                            pluralLangs++;
                        }
                    }

                    var pa = config.GetText(TextName.ItemNamesPluralAlt);
                    if (pa != null && pa.Length > 0 && bound.ItemNames.Count > 0)
                    {
                        uint maxPluralAltId = 0;
                        foreach (var k in bound.ItemNames.Keys)
                            if (uint.TryParse(k, out uint kid) && kid > maxPluralAltId) maxPluralAltId = kid;

                        if (maxPluralAltId >= pa.Length)
                        {
                            var grown = new string[maxPluralAltId + 1];
                            Array.Copy(pa, grown, pa.Length);
                            for (int i = pa.Length; i < grown.Length; i++) grown[i] = "???";
                            pa = grown;
                        }

                        var doable = bound.ItemNames
                            .Where(kv => uint.TryParse(kv.Key, out uint i) && i < pa.Length)
                            .ToList();
                        if (doable.Count > 0)
                        {
                            foreach (var kv in doable)
                            {
                                string pluralAlt = (bound.ItemNamesPluralAlt != null && bound.ItemNamesPluralAlt.TryGetValue(kv.Key, out string palt) && !string.IsNullOrWhiteSpace(palt))
                                    ? palt
                                    : ItemTextUtil.PluralAlt(kv.Value);
                                pa[uint.Parse(kv.Key)] = pluralAlt;
                            }
                            config.SetText(TextName.ItemNamesPluralAlt, pa);
                            config.SaveText(TextName.ItemNamesPluralAlt);
                            pluralAltLangs++;
                        }
                    }

                    var d = config.GetText(TextName.ItemFlavor);
                    if (d != null && ((bound.ItemDescriptions != null && bound.ItemDescriptions.Count > 0) || (bound.ItemNames != null && bound.ItemNames.Count > 0)))
                    {
                        uint maxDescId = 0;
                        if (bound.ItemDescriptions != null)
                        {
                            foreach (var k in bound.ItemDescriptions.Keys)
                                if (uint.TryParse(k, out uint kid) && kid > maxDescId) maxDescId = kid;
                        }
                        if (bound.ItemNames != null)
                        {
                            foreach (var k in bound.ItemNames.Keys)
                                if (uint.TryParse(k, out uint kid) && kid > maxDescId) maxDescId = kid;
                        }

                        if (maxDescId >= d.Length)
                        {
                            var grown = new string[maxDescId + 1];
                            Array.Copy(d, grown, d.Length);
                            for (int i = d.Length; i < grown.Length; i++) grown[i] = "";
                            d = grown;
                        }

                        var allDescKeys = (bound.ItemDescriptions?.Keys ?? Enumerable.Empty<string>())
                            .Union(bound.ItemNames?.Keys ?? Enumerable.Empty<string>())
                            .Where(k => uint.TryParse(k, out uint i) && i < d.Length)
                            .ToList();
                        if (allDescKeys.Count > 0)
                        {
                            bool touched = false;
                            foreach (var k in allDescKeys)
                            {
                                uint id = uint.Parse(k);
                                string desc = (bound.ItemDescriptions != null && bound.ItemDescriptions.TryGetValue(k, out string explicitDesc) && !string.IsNullOrWhiteSpace(explicitDesc))
                                    ? explicitDesc
                                    : bound.Description;
                                if (!string.IsNullOrWhiteSpace(desc))
                                {
                                    d[id] = ItemTextUtil.FormatDescription(desc);
                                    touched = true;
                                }
                            }
                            if (touched)
                            {
                                config.SetText(TextName.ItemFlavor, d);
                                config.SaveText(TextName.ItemFlavor);
                                descLangs++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Used to be swallowed, so the install reported success with this language's
                    // names or descriptions missing.
                    result.Fail($"language {lang}: text not fully written ({ex.GetType().Name}: {ex.Message})");
                }
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
        if (bound.ItemNames.Count > 0) result.Say($"names written to {langs} language(s)");
        if (bound.ItemDescriptions.Count > 0) result.Say($"descriptions written to {descLangs} language(s)");
        if (pluralLangs > 0) result.Say($"plural names written to {pluralLangs} language(s)");
        if (textLangs > 0) result.Say($"game text lines written to {textLangs} language(s)");
        return result;
    }
    private static long ParseNum(string s)
    {
        s = (s ?? "").Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt64(s[2..], 16)
            : long.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    }
}
