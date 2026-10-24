#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public enum RecipeEffectKind
{
    ItemPatch,

    Template,

    DataOnly,

    LevelCap,

    CorpusPatch,

    Package,

    IpsPatch,

    ByteEdit,
}

public sealed record ByteEdit(uint Offset, byte From, byte To, ResearchTarget? Target = null);

public sealed record RecipeAnchor(ResearchTarget Target, uint Offset, byte[] Bytes, string Describes,
                                  string? Remedy = null)
{
    public bool Present(byte[] bin) =>
        bin != null && Offset + Bytes.Length <= bin.Length &&
        System.Linq.Enumerable.Range(0, Bytes.Length).All(k => bin[Offset + k] == Bytes[k]);
}

public sealed record CodeBinShift(uint From, int Delta);

public sealed class Recipe
{
    public string Name { get; init; } = "";
    public string Summary { get; init; } = "";
    public CustomMechanicKind Kind { get; init; } = CustomMechanicKind.Item;

    public int SlotCount => Entries.Count;

    public List<RecipeEntry> Entries { get; init; } = [];

    public RecipeEffectKind EffectKind { get; init; } = RecipeEffectKind.DataOnly;

    public string? PatchName { get; init; }

    public string? TemplateName { get; init; }

    public Dictionary<string, string> TemplateValues { get; init; } = [];

    public byte? Timing { get; init; }

    public string? AttachTo { get; init; }

    public PatchPackage? Package { get; init; }

    public string? PackagePath { get; init; }

    public string? IpsPath { get; init; }

    public List<ByteEdit> ByteEdits { get; init; } = [];

    public List<RecipeAnchor> Anchors { get; init; } = [];

    public Dictionary<string, List<CodeBinShift>> CodeBinDeltaByVersion { get; init; } = [];

    public string? ForVersion { get; init; }

    public string Category => EffectKind switch
    {
        RecipeEffectKind.Package => "Items & abilities",
        RecipeEffectKind.ItemPatch => "Items & abilities",
        RecipeEffectKind.IpsPatch => "IPS patches",
        RecipeEffectKind.Template => "Templates",
        RecipeEffectKind.DataOnly => "Data only",
        _ => "Battle mechanics",
    };

    public string? SheetFile { get; init; }

    public ResearchTarget Target { get; init; } = ResearchTarget.BattleCro;

    public List<string> TargetFiles { get; init; } = [];

    public IReadOnlyList<string> ResolvedTargets =>
        TargetFiles.Count > 0 ? TargetFiles : [FileNameOf(Target)];

    public static string FileNameOf(ResearchTarget t) => t switch
    {
        ResearchTarget.BagCro => "Bag.cro",
        ResearchTarget.ShopCro => "Shop.cro",
        ResearchTarget.BoxCro => "Box.cro",
        ResearchTarget.StatusCro => "Status.cro",
        ResearchTarget.FieldRoCro => "FieldRo.cro",
        ResearchTarget.EvolutionCro => "Evolution.cro",
        ResearchTarget.CodeBin => "code.bin",
        ResearchTarget.BattleCro => "Battle.cro",
        _ => "(unknown)",
    };

    public int PatchCount { get; set; }

    public List<string> Caveats { get; init; } = [];

    public override string ToString() => $"{Name} ({SlotCount} id{(SlotCount == 1 ? "" : "s")})";
}

public sealed class RecipeEntry
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Plural { get; init; }
    public string? PluralAlt { get; init; }

    public int Id { get; set; } = -1;
}

public static class Recipes
{
    private static readonly (string Nature, string Raises, string Lowers)[] Mints =
    [
        ("Lonely",  "Attack",    "Defense"),
        ("Adamant", "Attack",    "Sp. Atk"),
        ("Naughty", "Attack",    "Sp. Def"),
        ("Brave",   "Attack",    "Speed"),
        ("Bold",    "Defense",   "Attack"),
        ("Impish",  "Defense",   "Sp. Atk"),
        ("Lax",     "Defense",   "Sp. Def"),
        ("Relaxed", "Defense",   "Speed"),
        ("Modest",  "Sp. Atk",   "Attack"),
        ("Mild",    "Sp. Atk",   "Defense"),
        ("Rash",    "Sp. Atk",   "Sp. Def"),
        ("Quiet",   "Sp. Atk",   "Speed"),
        ("Calm",    "Sp. Def",   "Attack"),
        ("Gentle",  "Sp. Def",   "Defense"),
        ("Careful", "Sp. Def",   "Sp. Atk"),
        ("Sassy",   "Sp. Def",   "Speed"),
        ("Timid",   "Speed",     "Attack"),
        ("Hasty",   "Speed",     "Defense"),
        ("Jolly",   "Speed",     "Sp. Atk"),
        ("Naive",   "Speed",     "Sp. Def"),
        ("Serious", "nothing",   "nothing"),
    ];

    private static bool HasPackageFor(string feature)
    {
        try
        {
            var packages = LoadPackages(out _);
            return packages.Any(p =>
                (p.Name ?? "").Contains(feature, StringComparison.OrdinalIgnoreCase) ||
                (p.ItemNames?.Values ?? Enumerable.Empty<string>())
                    .Any(n => (n ?? "").Contains(feature.TrimEnd('s'), StringComparison.OrdinalIgnoreCase)));
        }
        catch { return false; }
    }

    private static Recipe NatureMints() => new()
    {
        Name = "Nature Mints",
        Summary = "The twenty-one mints. Using one rewrites a Pokemon's nature, so its stats grow "
                + "as that nature's would. Needs twenty-one consecutive free item ids.",
        Kind = CustomMechanicKind.Item,
        EffectKind = RecipeEffectKind.CorpusPatch,
        SheetFile = "Nature Mints.xlsx",
        Target = ResearchTarget.BagCro,
        Entries = [.. Mints.Select(m => new RecipeEntry
        {
            Name = $"{m.Nature} Mint",
            Description = m.Serious()
                ? "A mint with a mysterious aroma. When\nused, it makes a Pokémon's stats\ngrow evenly."
                : $"A mint with a mysterious aroma. When\nused, it raises {m.Raises} and lowers\n{m.Lowers}.",
            Plural = $"{m.Nature} Mints",
            PluralAlt = $"{m.Nature} Mints",
        })],
        Caveats =
        [
            "The mints need twenty-one CONSECUTIVE item ids: the game's dispatcher walks the block "
            + "by offset from the first, so a gap silently shifts every mint after it.",
            "The handler is meant to live in Bag.cro, and Battle.cro has nothing to do with mints. "
            + "But the sheet is not finished: all 36 of its recorded steps carry assembly text and "
            + "no assembled bytes, with unresolved branch targets (bl 0x0), offsets relative to "
            + "three unnamed functions rather than the file, and a few truncated instructions. "
            + "Installing this adds the 21 names and descriptions and writes no code.",
            "code.bin supplies the routine that actually writes the nature (0x2212B0, 'Write Nature "
            + "r1 to loaded pokemon pointer r0'); Bag.cro calls into it, so that binary must be the "
            + "matching build.",
        ],
    };

    private static bool Serious(this (string Nature, string Raises, string Lowers) m) =>
        m.Raises == "nothing";

    private static readonly (string Item, string Desc, string? Plural)[] CompetitiveItems =
    [
        ("Clear Amulet",
         "An item to be held by a Pokémon.\nThis clear, curved charm prevents other\nPokémon from lowering the holder's stats.",
         "Clear Amulets"),

        ("Ability Shield",
         "An item to be held by a Pokémon.\nThis sturdy shield prevents the holder's\nAbility from being changed or suppressed.",
         "Ability Shields"),

        ("Loaded Dice",
         "An item to be held by a Pokémon.\nThese tricky dice make multistrike moves\nhit more times than usual.",
         "Loaded Dice"),

        ("Covert Cloak",
         "An item to be held by a Pokémon.\nThis cloak protects the holder from the\nadditional effects of moves.",
         "Covert Cloaks"),

        ("Throat Spray",
         "An item to be held by a Pokémon.\nIt raises Sp. Atk when the holder uses\na sound-based move.",
         "Throat Sprays"),

        ("Utility Umbrella",
         "An item to be held by a Pokémon.\nThis sturdy umbrella protects the holder\nfrom the effects of sun and rain.",
         "Utility Umbrellas"),

        ("Ability Patch",
         "An item that changes a Pokémon with a\nregular Ability to one with a rare Ability.",
         "Ability Patches"),

        ("Room Service",
         "An item to be held by a Pokémon.\nIt lowers the holder's Speed when\nTrick Room takes effect.",
         "Room Services"),

        ("Punching Glove",
         "An item to be held by a Pokémon.\nThis glove boosts punching moves and\nstops them making contact.",
         "Punching Gloves"),

        ("Mirror Herb",
         "An item to be held by a Pokémon.\nThis herb lets the holder copy an\nopponent's stat boosts, once.",
         "Mirror Herbs"),

        ("Booster Energy",
         "An item to be held by a Pokémon.\nThis capsule activates Protosynthesis\nor Quark Drive once.",
         "Booster Energies"),

        ("Blunder Policy",
         "An item to be held by a Pokémon.\nIt sharply raises Speed when the\nholder misses with a move.",
         "Blunder Policies"),
    ];

    private static readonly Lazy<List<Recipe>> All = new(Build);

    private static List<Recipe> Build()
    {
        var list = new List<Recipe>();
        if (!HasPackageFor("Nature Mints") && !HasPackageFor("Ability Patch")) list.Add(NatureMints());

        foreach (var (item, desc, plural) in CompetitiveItems)
        {
            if (HasPackageFor(item)) continue;
            var build = MapToTemplate(item);

            list.Add(new Recipe
            {
                Name = item,
                Summary = build == null
                    ? $"Adds {item} as an item with its name and description. No behaviour yet."
                    : $"Adds {item} and installs its behaviour as a custom function.",
                Kind = CustomMechanicKind.Item,
                EffectKind = build == null ? RecipeEffectKind.ItemPatch : RecipeEffectKind.Template,
                PatchName = MapToPatchName(item),
                TemplateName = build?.Template,
                TemplateValues = build?.Values ?? [],
                Timing = build?.Timing,
                Entries = [new RecipeEntry
                {
                    Name = item,
                    Description = desc,
                    Plural = plural ?? ItemTextUtil.Pluralize(item),
                    PluralAlt = plural ?? ItemTextUtil.Pluralize(item),
                }],
                Caveats = build?.Caveats
                    ?? ["No template covers this one yet: the item and its text are added, but it "
                        + "will have no effect until a routine is written for it."],
            });
        }

        return list;
    }

    private sealed record TemplateBuild(string Template, Dictionary<string, string> Values,
                                        byte Timing, List<string> Caveats);

    private static TemplateBuild? MapToTemplate(string item) => item switch
    {
        // Clear Body's routine IS Clear Amulet's mechanic: refuse an opponent's stat drop, allow
        // raises. STAT=255 protects every stat, which is what the item does.
        "Clear Amulet" => new(
            "Ability: block stat drops",
            new() { ["STAT"] = "255" },
            0x72,
            ["Blocks drops from any source, including the holder's own moves. The vanilla Clear "
             + "Body routine this copies makes no distinction between self-inflicted and opposing "
             + "drops, so Close Combat's own drop is blocked too."]),

        // Iron Fist's power-scaling shape, applied as an item at 1.1x.
        "Punching Glove" => new(
            "Item: boost moves in a flag category",
            new() { ["BIT"] = "17", ["NUM"] = "11", ["DEN"] = "10" },
            0x47,
            ["The flag bit is F18, which is unused in a stock ROM - it does not already mean "
             + "'punching move'. Tick F18 on the punching moves in the Move editor, or this boosts "
             + "nothing.",
             "Only the power boost is installed. The real item also stops punching moves making "
             + "contact, which needs a separate routine on the contact check."]),

        _ => null,
    };

    private static string? MapToPatchName(string item) => null;

    public static IReadOnlyList<Recipe> Book => All.Value;

    public static List<Recipe> Discover(ResearchDatabase? db, string? version = null)
    {
        var list = new List<Recipe>();

        // Packages FIRST, for real this time: the old code added DiscoverIps/ByteEditRecipes/
        // OtherMechanics to `list` before packages were even loaded, so book.FirstOrDefault(name)
        // always returned the built-in entry whenever a package existed with the same name (the
        // "claimed" set below only ever protected the later All.Value loop, never these three) -
        // e.g. a real, verified patch-packages/Frostbite.json never actually got used, because the
        // hardcoded OtherMechanics() "Frostbite" was already earlier in the list. Where one exists
        // for an item, it is the real implementation and the built-in entry for the same name is
        // only its text.
        var packages = LoadPackages(out string? from);
        foreach (var p in packages.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            list.Add(FromPackage(p, from));

        var claimed = new HashSet<string>(list.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var r in DiscoverIps(version).Concat(ByteEditRecipes(version)).Concat(OtherMechanics(version)))
            if (claimed.Add(r.Name)) list.Add(r);

        foreach (var r in All.Value)
            if (!claimed.Contains(r.Name)) list.Add(r);

        return list;
    }

    private static Recipe RareCandyInBagEvolution()
    {
        // Where each trampoline lives now. C keeps the workbook's address (already blank); A and B
        // move off the live routine into the same free run, packed after C.
        const uint TrampC = 0x006A70;   // serves the hook at 0x000B5C
        const uint TrampA = 0x006A90;   // serves the hook at 0x000AC8
        const uint TrampB = 0x006AB0;   // serves the hook at 0x000978

        const uint Blank = 0x00000000;

        // The three hooked prologues, and where each trampoline rejoins the function it came from.
        var edits = new List<ByteEdit>();
        edits.AddRange(Words(
            (0x000978, 0xE92D43F8, Branch(0x000978, TrampB)),
            (0x000AC8, 0xE92D40F8, Branch(0x000AC8, TrampA)),
            (0x000B5C, 0xE92D40F8, Branch(0x000B5C, TrampC))));

        // Trampoline C - hook 0x000B5C displaced PUSH / MOV R5,R1 / MOV R4,R0, resumes at 0x000B68.
        edits.AddRange(Words(
            (TrampC + 0x00, Blank, 0xE1D120B8),   // LDRH R2, [R1, #0x8]      species id
            (TrampC + 0x04, Blank, 0xE3003327),   // MOVW R3, #0x327          807
            (TrampC + 0x08, Blank, 0xE1520003),   // CMP  R2, R3
            (TrampC + 0x0C, Blank, 0x812FFF1E),   // BXHI LR                  above 807: skip
            (TrampC + 0x10, Blank, 0xE92D40F8),   // PUSH {R3,R4,R5,R6,R7,LR}
            (TrampC + 0x14, Blank, 0xE1A05001),   // MOV  R5, R1
            (TrampC + 0x18, Blank, 0xE1A04000),   // MOV  R4, R0
            (TrampC + 0x1C, Blank, Branch(TrampC + 0x1C, 0x000B68))));

        // Trampoline A - hook 0x000AC8 displaced the same three, resumes at 0x000AD4.
        edits.AddRange(Words(
            (TrampA + 0x00, Blank, 0xE1D120B8),
            (TrampA + 0x04, Blank, 0xE3003327),
            (TrampA + 0x08, Blank, 0xE1520003),
            (TrampA + 0x0C, Blank, 0x812FFF1E),
            (TrampA + 0x10, Blank, 0xE92D40F8),
            (TrampA + 0x14, Blank, 0xE1A05001),
            (TrampA + 0x18, Blank, 0xE1A04000),
            (TrampA + 0x1C, Blank, Branch(TrampA + 0x1C, 0x000AD4))));

        // Trampoline B - hook 0x000978 displaced four instructions and uses R12 for the constant,
        // because R3 is one of the arguments this function keeps. Resumes at 0x000984, as authored.
        edits.AddRange(Words(
            (TrampB + 0x00, Blank, 0xE1D130B8),   // LDRH R3, [R1, #0x8]
            (TrampB + 0x04, Blank, 0xE300C327),   // MOVW R12, #0x327
            (TrampB + 0x08, Blank, 0xE153000C),   // CMP  R3, R12
            (TrampB + 0x0C, Blank, 0x812FFF1E),   // BXHI LR
            (TrampB + 0x10, Blank, 0xE92D43F8),   // PUSH {R3,R4,R5,R6,R7,R8,R9,LR}
            (TrampB + 0x14, Blank, 0xE1A05001),   // MOV  R5, R1
            (TrampB + 0x18, Blank, 0xE1A06002),   // MOV  R6, R2
            (TrampB + 0x1C, Blank, 0xE1A04000),   // MOV  R4, R0
            (TrampB + 0x20, Blank, Branch(TrampB + 0x20, 0x000984))));

        return new Recipe
        {
            Name = "Rare Candy evolves at Lv100 (Instant In-Bag)",
            Summary = "A Rare Candy used on a maxed Pokemon evolves it from the bag. Gen 8/9 species "
                    + "skip the evolution cutscene, which has no model for them.",
            Kind = CustomMechanicKind.Item,
            EffectKind = RecipeEffectKind.ByteEdit,
            Target = ResearchTarget.EvolutionCro,
            TargetFiles = ["Evolution.cro"],
            Entries = [],
            ByteEdits = edits,
            Anchors =
            [
                new(ResearchTarget.EvolutionCro, 0x001918, [0x70, 0x40, 0x2D, 0xE9],
                    "a stock, unhooked evolution routine",
                    "Your ROM already has this feature. The Expansion Pack installs its own version - "
                    + "it hooks Evolution.cro at 0x001918 to a species check that skips the cutscene "
                    + "above species 807 - so there is nothing to add and applying this would replace "
                    + "a working implementation with a second one."),
            ],
            Caveats =
            [
                "For a STOCK Evolution.cro only. The Expansion Pack already implements this - it "
                + "hooks 0x001918 to its own species>807 cutscene skip - so this refuses there "
                + "rather than overwriting a working implementation with a second one.",
                "Every write is checked against the value it replaces, so it refuses rather than "
                + "half-applying on a build it does not fit.",
                "Corrected from the research workbook, whose hook at 0x000978 pointed at blank space "
                + "and whose trampolines overlapped the routine at 0x006A20.",
                "UNTESTED in game. Its trampolines read the species as [R1+8] - one dereference from "
                + "the second argument - while the game's own equivalent check needs three from the "
                + "first. If it faults on a stock build, that is the thing to look at.",
            ],
        };
    }

    private static Recipe ExpansionRareCandyEvolution()
    {
        const uint Blank = 0x00000000;
        const ResearchTarget Bag = ResearchTarget.BagCro;
        const ResearchTarget Evo = ResearchTarget.EvolutionCro;

        var edits = new List<ByteEdit>();

        // Bag.cro: the hook, then the Rare Candy usability handler it reaches.
        edits.AddRange(In(Bag, Words(
            (0x00C324, 0x0A00016Eu, 0x0A002705u))));                 // B(EQ) -> 0x015F40
        edits.AddRange(In(Bag, Words(
            (0x015F40, Blank, 0xE3550032u),    // CMP  R5, #0x32         item 50, Rare Candy
            (0x015F44, Blank, 0x1AFFDA66u),    // BNE  0x00C8E4          not the candy: original path
            (0x015F48, Blank, 0xE594008Cu),    // LDR  R0, [R4, #0x8C]
            (0x015F4C, Blank, 0xE59D1044u),    // LDR  R1, [SP, #0x44]
            (0x015F50, Blank, 0xEBFFAA18u),    // BL   0x0007B8
            (0x015F54, Blank, 0xE3500000u),    // CMP  R0, #0
            (0x015F58, Blank, 0x0AFFDA61u),    // BEQ  0x00C8E4          refused
            (0x015F5C, Blank, 0xEAFFD926u)))); // B    0x00C3FC          allowed

        // Evolution.cro: the hook, then the cutscene-skip trampoline.
        edits.AddRange(In(Evo, Words(
            (0x001918, 0xE92D4070u, 0xEA001440u))));                 // B 0x006A20
        edits.AddRange(In(Evo, Words(
            (0x006A20, Blank, 0xE92D4070u),    // PUSH {R4,R5,R6,LR}     displaced prologue
            (0x006A24, Blank, 0xE1A04000u),    // MOV  R4, R0
            (0x006A28, Blank, 0xE5940038u),    // LDR  R0, [R4, #0x38]
            (0x006A2C, Blank, 0xE3500000u),    // CMP  R0, #0
            (0x006A30, Blank, 0x1A00000Au),    // BNE  0x006A60          step already resolved
            (0x006A34, Blank, 0xE5940030u),    // LDR  R0, [R4, #0x30]
            (0x006A38, Blank, 0xE590003Cu),    // LDR  R0, [R0, #0x3C]
            (0x006A3C, Blank, 0xE1D000B8u),    // LDRH R0, [R0, #0x8]    species id
            (0x006A40, Blank, 0xE3001327u),    // MOVW R1, #0x327        807
            (0x006A44, Blank, 0xE1500001u),    // CMP  R0, R1
            (0x006A48, Blank, 0x9A000004u),    // BLS  0x006A60          vanilla species: play it
            (0x006A4C, Blank, 0xE1A00004u),    // MOV  R0, R4
            (0x006A50, Blank, 0xEBFFEC71u),    // BL   0x001C1C          finish without the scene
            (0x006A54, Blank, 0xE3A00006u),    // MOV  R0, #6
            (0x006A58, Blank, 0xE5840038u),    // STR  R0, [R4, #0x38]   mark the step done
            (0x006A5C, Blank, 0xE8BD8070u),    // POP  {R4,R5,R6,PC}
            (0x006A60, Blank, 0xE594004Cu),    // LDR  R0, [R4, #0x4C]   displaced third instruction
            (0x006A64, Blank, 0xEAFFEBAEu)))); // B    0x001924          resume

        return new Recipe
        {
            Name = "Rare Candy evolves at Lv100 (Expansion Pack build)",
            Summary = "A Rare Candy used on a maxed Pokemon evolves it from the bag, and species "
                    + "above 807 skip the evolution cutscene they have no model for.",
            Kind = CustomMechanicKind.Item,
            EffectKind = RecipeEffectKind.ByteEdit,
            Target = Bag,
            TargetFiles = ["Bag.cro", "Evolution.cro"],
            Entries = [],
            ByteEdits = edits,
            Anchors =
            [
                new(Bag, 0x0000B4, [0x80, 0x8E, 0x01, 0x00],
                    "an Expansion Pack Bag.cro",
                    "This is the Expansion Pack's own implementation, at the addresses its build "
                    + "uses. Your Bag.cro is a stock one, where those addresses hold different code, "
                    + "so it is refused rather than written into the wrong place. Install the "
                    + "Expansion Pack first."),
            ],
            Caveats =
            [
                "Writes to two files: the hook and item check go into Bag.cro, the cutscene skip "
                + "into Evolution.cro. Both or neither - it refuses before writing if either half "
                + "does not match.",
                "For an Expansion Pack build only. Most Expansion builds ship with this already "
                + "applied, in which case it reports every byte as already in place and changes "
                + "nothing.",
                "The bytes are read back out of a working Expansion build rather than reconstructed, "
                + "and are identical on US and UM.",
            ],
        };
    }

    private static IEnumerable<ByteEdit> In(ResearchTarget t, IEnumerable<ByteEdit> edits)
    {
        foreach (var e in edits) yield return e with { Target = t };
    }

    private static uint Branch(uint at, uint target)
    {
        long off = ((long)target - (at + 8)) / 4;
        return 0xEA000000u | (uint)(off & 0x00FFFFFF);
    }

    private static IEnumerable<ByteEdit> Words(params (uint At, uint From, uint To)[] rows)
    {
        foreach (var (at, from, to) in rows)
        {
            var f = BitConverter.GetBytes(from);
            var t = BitConverter.GetBytes(to);
            for (uint k = 0; k < 4; k++)
                yield return new ByteEdit(at + k, f[k], t[k]);
        }
    }

    private static IEnumerable<Recipe> ByteEditRecipes(string? version)
    {
        string v = (version ?? "").Trim().ToUpperInvariant();
        if (v is not ("US" or "UM")) yield break;

        yield return ExpansionRareCandyEvolution();

        yield return new Recipe
        {
            Name = "Mega Evolution keeps its forme",
            Summary = "Mega-capable Pokemon revert to the forme they entered battle in, not forme 0.",
            Kind = CustomMechanicKind.Ability,
            EffectKind = RecipeEffectKind.ByteEdit,
            Target = ResearchTarget.CodeBin,
            ForVersion = v,
            Entries = [],
            ByteEdits = [new ByteEdit(v == "UM" ? 0x344C83u : 0x344C7Fu, 0x0A, 0xEA)],
            Caveats =
            [
                "One byte in code.bin: the conditional branch that skips the forme-0 revert becomes "
                + "unconditional (BEQ -> B).",
                v == "UM"
                    ? "UM offset 0x344C83. The US build uses 0x344C7F; only the matching one is offered."
                    : "US offset 0x344C7F. The UM build uses 0x344C83; only the matching one is offered.",
            ],
        };

        yield return new Recipe
        {
            Name = "Mega Evolution without the event flag",
            Summary = "Mega Evolution works from the start, instead of waiting for the story flag "
                    + "that normally unlocks it.",
            Kind = CustomMechanicKind.Ability,
            EffectKind = RecipeEffectKind.ByteEdit,
            Target = ResearchTarget.BattleCro,
            TargetFiles = ["Battle.cro"],
            ForVersion = v,
            Entries = [],
            ByteEdits =
            [
                new ByteEdit(0x9CE24, 0x01, 0x00),
                new ByteEdit(0x9CE25, 0x00, 0xF0),
                new ByteEdit(0x9CE26, 0x00, 0x20),
                new ByteEdit(0x9CE27, 0x0A, 0xE3),
            ],
            Caveats =
            [
                "One instruction in Battle.cro at 0x9CE24: the branch that answers \"no\" when the "
                + "Mega Evolution event flag is unset becomes a NOP, so the check falls through to "
                + "the \"yes\" path.",
                "The same offset and the same original bytes (0A000001) on vanilla US, vanilla UM "
                + "and both Expansion builds, so it is offered for every one of them.",
            ],
        };

        // The four ceiling raises below share one shape: each widens a hardcoded id-bounds check in
        // code.bin from whatever this build's own baseline already is (either stock retail, or the
        // community Expansion Pack's own already-applied raise) up to 65280 (0xFF00) - the largest
        // value that is both a single ARM rotated-immediate and safely under 65536, since pk3DS's
        // own GARC writer (GARC.PackGARC) casts entry counts to ushort and would silently truncate
        // past that regardless of what code.bin allows. Every ByteEdit's "From" byte is exactly
        // what this session found live in a real ROM - RecipeInstaller refuses to write (rather than
        // guessing) if a target ROM's byte does not match either that or the already-applied "To"
        // value, so a build that genuinely differs is left alone rather than corrupted.
        // Verified against US only - UM equivalents were not independently found/tested this
        // session, so these recipes are offered for US builds only.
        if (v == "US")
        {
            yield return new Recipe
            {
                Name = "Raise the trainer-battle ceiling",
                Summary = "Lets overworld trainer battles (trdata indices past the stock 653) resolve "
                        + "to the real trainer instead of silently falling back to trdata[1].",
                Kind = CustomMechanicKind.Item,
                EffectKind = RecipeEffectKind.ByteEdit,
                Target = ResearchTarget.CodeBin,
                ForVersion = v,
                Entries = [],
                ByteEdits =
                [
                    new ByteEdit(0x2D3158, 0xA3, 0xFF), new ByteEdit(0x2D3159, 0x0F, 0x0C),
                    new ByteEdit(0x330708, 0xA3, 0xFF), new ByteEdit(0x330709, 0x0F, 0x0C),
                    new ByteEdit(0x3307C4, 0xA3, 0xFF), new ByteEdit(0x3307C5, 0x0F, 0x0C),
                    new ByteEdit(0x330F48, 0xA3, 0xFF), new ByteEdit(0x330F49, 0x0F, 0x0C),
                    new ByteEdit(0x3317C0, 0xA3, 0xFF), new ByteEdit(0x3317C1, 0x0F, 0x0C),
                    new ByteEdit(0x3318B8, 0xA3, 0xFF), new ByteEdit(0x3318B9, 0x0F, 0x0C),
                    new ByteEdit(0x331990, 0xA3, 0xFF), new ByteEdit(0x331991, 0x0F, 0x0C),
                    new ByteEdit(0x331A40, 0xA3, 0xFF), new ByteEdit(0x331A41, 0x0F, 0x0C),
                    new ByteEdit(0x331AC4, 0xA3, 0xFF), new ByteEdit(0x331AC5, 0x0F, 0x0C),
                ],
                Caveats =
                [
                    "Not community-documented - found this session via a live two-data-point gameplay "
                    + "test (trainer indices 653 and 654 both fell back to the exact same trainer, "
                    + "ruling out a modulo/wraparound and confirming a deliberate clamp) followed by a "
                    + "targeted disassembly for the exact compare-then-clamp-to-1 shape.",
                    "9 sites in code.bin, all the same shared, likely-templated \"safe array access, "
                    + "default to element 1\" helper - also used elsewhere in this binary for several "
                    + "small, unrelated bounds (0/1/2/3/5/11/13); only these 9 specific offsets are "
                    + "touched.",
                    "US only - UM offsets were not found/verified this session.",
                    "Confirmed live in-game: a trdata entry at index 653 and a second one at 654 both "
                    + "resolved correctly (name, class, team) after this patch, instead of showing as "
                    + "trdata[1].",
                ],
            };

            yield return new Recipe
            {
                Name = "Raise the move-ID ceiling further",
                Summary = "Widens the move-ID bounds checks past whatever this build already allows "
                        + "(retail 728, or the Expansion Pack's own already-applied 920) up to 65280.",
                Kind = CustomMechanicKind.Move,
                EffectKind = RecipeEffectKind.ByteEdit,
                Target = ResearchTarget.CodeBin,
                ForVersion = v,
                Entries = [],
                ByteEdits =
                [
                    new ByteEdit(0x226680, 0xE6, 0xFF), new ByteEdit(0x226681, 0x0F, 0x0C),
                    new ByteEdit(0x2267D4, 0xE6, 0xFF), new ByteEdit(0x2267D5, 0x0F, 0x0C),
                    new ByteEdit(0x226B04, 0xE6, 0xFF), new ByteEdit(0x226B05, 0x0F, 0x0C),
                    new ByteEdit(0x2D2C28, 0xE6, 0xFF), new ByteEdit(0x2D2C29, 0x0F, 0x0C),
                ],
                Caveats =
                [
                    "Builds on the community's own documented \"MOVE LIMITS\" code_map.csv rows "
                    + "(the move flag lookup, the move parameter lookup, move-record loading, and move "
                    + "name text) - this recipe only widens further, from whichever of retail (728) or "
                    + "the Expansion Pack's already-applied 920 this build currently has.",
                    "US only - the UM name-text offset (0x2D2C2C, 4 bytes later than the US one, per "
                    + "the community map) was not independently re-verified this session.",
                ],
            };

            yield return new Recipe
            {
                Name = "Raise the ability-name ceiling further",
                Summary = "Widens the ability name-text bounds check past whatever this build already "
                        + "allows (retail 234, or the Expansion Pack's own already-applied 320) up to 65280.",
                Kind = CustomMechanicKind.Ability,
                EffectKind = RecipeEffectKind.ByteEdit,
                Target = ResearchTarget.CodeBin,
                ForVersion = v,
                Entries = [],
                ByteEdits = [new ByteEdit(0x2D30BC, 0x05, 0xFF), new ByteEdit(0x2D30BD, 0x0D, 0x0C)],
                Caveats =
                [
                    "Builds on the community's own documented \"ABILITY LIMITS\" code_map.csv row - "
                    + "this only widens the name-text ceiling further, from whichever of retail (234) or "
                    + "the Expansion Pack's already-applied 320 this build currently has.",
                    "Ability IDs themselves can only reach 511 even with the separate \"NINTH ABILITY "
                    + "BIT\" patches applied (one extra bit reserves exactly that much room) - a name "
                    + "slot raised past 511 would never actually be reachable by a real ability id, so "
                    + "there is a real, lower practical ceiling here than the other three recipes.",
                    "US only - the UM offset (0x2D30C0) was not independently re-verified this session.",
                ],
            };

            yield return new Recipe
            {
                Name = "Raise the item-ID ceiling further",
                Summary = "Widens the item-ID bounds checks past whatever this build already allows "
                        + "(retail 960, or the Expansion Pack's own already-applied 1024) up to 65280.",
                Kind = CustomMechanicKind.Item,
                EffectKind = RecipeEffectKind.ByteEdit,
                Target = ResearchTarget.CodeBin,
                ForVersion = v,
                Entries = [],
                ByteEdits =
                [
                    new ByteEdit(0x276B90, 0x01, 0xFF), new ByteEdit(0x276B91, 0x0B, 0x0C),
                    new ByteEdit(0x2D2B60, 0x01, 0xFF), new ByteEdit(0x2D2B61, 0x0B, 0x0C),
                    new ByteEdit(0x34B428, 0x0F, 0xFF), new ByteEdit(0x34B429, 0x0D, 0x0C),
                    new ByteEdit(0x3BABFC, 0x01, 0xFF), new ByteEdit(0x3BABFD, 0x0B, 0x0C),
                ],
                Caveats =
                [
                    "Builds on ItemCeilingPatcher's own already-established documented sites (item "
                    + "records, item name text, field held items) plus one further site this session "
                    + "found still sitting at the plain retail ceiling (960) in the reference ROM even "
                    + "though the other three had already been raised to 1024 - the Expansion Pack's "
                    + "own item-limit patch is not applied uniformly at every site in every build.",
                    "One further documented site (a second item-name/field-item pair) exists in code.bin "
                    + "and is deliberately left untouched here because it sits inside the Expansion "
                    + "Pack's own added code, where the same comparison selects behaviour rather than "
                    + "rejecting an out-of-range id - widening it would not raise any real ceiling.",
                    "US only - UM equivalents were not independently re-verified this session.",
                ],
            };
        }
    }

    private static IEnumerable<Recipe> OtherMechanics(string? version)
    {
        (string Sheet, string Name, string Summary, string[] Caveats)[] features =
        [
            ("Vs Seeker Implementation.xlsx",
             "Vs Seeker",
             "Adds a working Vs Seeker: a held/bag item that flags rebattleable trainers.",
             ["All 11 recorded originals verified in code.bin on US.",
              "Spans three binaries - Bag.cro (text and the toggle), FieldRo.cro (the check call) "
              + "and code.bin (the battleable test). The workbook records them on one sheet; the "
              + "installer places each row in the binary it fits.",
              "Ported to UM by shifting only the code.bin rows +8; the CROs are byte-identical "
              + "between the two games, so they need no adjustment."]),

            ("Move Effectiveness Text (add 4x weakness and resistance).xlsx",
             "Move Effectiveness Text (4x)",
             "Battle text distinguishes 4x weakness and 4x resistance from the ordinary cases.",
             ["Every one of the 276 recorded original instructions was found in this ROM's Battle.cro, so the sheet matches this build exactly."]),

            ("Move Relearner Replaces Cafe.xlsx",
             "Move Relearner Replaces Cafe",
             "Turns the cafe NPC into a move relearner.",
             ["All 10 recorded originals verified in code.bin.",
              "The workbook labels these edits Battle.cro; they are code.bin offsets, and the installer corrects that from where the originals actually are."]),

            ("Frostbite.xlsx",
             "Frostbite",
             "Replaces the freeze status with Gen 9's frostbite: chip damage and halved Sp. Atk instead of a full lock.",
             ["All 37 recorded originals verified in Battle.cro on both US and UM.",
              "Two of the six sheets record no original bytes, so their 36 writes cannot be checked before being made. They are aimed at Battle.cro with the rest of the workbook - on their own they would have been resolved by size and landed in FieldRo.cro."]),

        ];

        var umShift = new Dictionary<string, List<CodeBinShift>>(StringComparer.OrdinalIgnoreCase)
        {
            // 11/11 anchors, unanimous.
            ["Vs Seeker Implementation.xlsx"] = [new(0, 8)],

            // 10/10 anchors, two regions: the Café NPC hook does not move, the relearner display
            // function and everything after it is 4 bytes later.
            ["Move Relearner Replaces Cafe.xlsx"] = [new(0, 0), new(0x341654, 4)],
        };

        yield return new Recipe
        {
            Name = "Level Cap",
            Summary = "Caps how far Pokemon can level until the story advances. Editable: pick which "
                    + "story flags raise the cap and to what.",
            Kind = CustomMechanicKind.Move,
            EffectKind = RecipeEffectKind.LevelCap,
            Entries = [],
            ForVersion = version,
            TargetFiles = ["Battle.cro", "code.bin"],
            Caveats =
            [
                "Both hook sites are byte-identical across vanilla US, vanilla UM and both Expansion "
                + "builds, and are verified before anything is written.",
                "Keyed on story flags, not the battle counter the original research also supported - "
                + "a counter can be inflated with the Vs Seeker, which would raise the cap without the "
                + "story moving.",
                "Rare Candy is capped too, but only when the ExeFS is open; with RomFS alone the "
                + "experience path is capped and candies are not.",
            ],
        };

        // Which files each of these actually writes, where it is more than the one Target says.
        var spans = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Vs Seeker Implementation.xlsx"] = ["Bag.cro", "FieldRo.cro", "code.bin"],
            ["Move Relearner Replaces Cafe.xlsx"] = ["code.bin"],
            ["Move Effectiveness Text (add 4x weakness and resistance).xlsx"] = ["Battle.cro"],
            ["Frostbite.xlsx"] = ["Battle.cro"],
            ["Rare Candy on Level 100 Pokemon triggers evolution.xlsx"] = ["Bag.cro", "Evolution.cro"],
        };

        // Preconditions for workbooks that record no original bytes, so there is otherwise nothing
        // to tell whether their offsets suit the loaded build.
        var anchors = new Dictionary<string, List<RecipeAnchor>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Rare Candy on Level 100 Pokemon triggers evolution.xlsx"] =
            [
                new(ResearchTarget.BagCro, 0x00C324, [0x05, 0x27, 0x00, 0x0A],
                    "the Expansion Pack's Rare Candy usability handler in Bag.cro"),
            ],
        };

        foreach (var (sheet, name, summary, caveats) in features)
        {
            var deltas = new Dictionary<string, List<CodeBinShift>>(StringComparer.OrdinalIgnoreCase)
            {
                ["US"] = [new(0, 0)],
            };
            if (umShift.TryGetValue(sheet, out var d)) deltas["UM"] = d;

            yield return new Recipe
            {
                Name = name,
                Summary = summary,
                Kind = CustomMechanicKind.Move,
                EffectKind = RecipeEffectKind.CorpusPatch,
                SheetFile = sheet,
                Entries = [],
                CodeBinDeltaByVersion = deltas,
                TargetFiles = spans.TryGetValue(sheet, out var files) ? [.. files] : [],
                Anchors = anchors.TryGetValue(sheet, out var anch) ? [.. anch] : [],
                ForVersion = version,
                Caveats = [.. caveats],
            };
        }
    }

    private static List<Recipe> DiscoverIps(string? version)
    {
        var list = new List<Recipe>();

        string v = (version ?? "").Trim().ToUpperInvariant();
        if (v is not ("US" or "UM")) return list;

        foreach (string root in IpsFolders())
        {
            string folder = Path.Combine(root, $"{v}_patches");
            if (!Directory.Exists(folder)) continue;

            var files = Directory.EnumerateFiles(folder, "*.ips", SearchOption.AllDirectories)
                                 .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                                 .ToList();
            if (files.Count == 0) continue;

            foreach (string f in files)
            {
                string label = Path.GetFileNameWithoutExtension(f);
                string summary;
                try
                {
                    var recs = IpsPatch.Read(File.ReadAllBytes(f));
                    summary = $"{v} code.bin patch - {IpsPatch.Describe(recs)}";
                }
                catch (Exception ex) { summary = $"{v} code.bin patch - unreadable: {ex.Message}"; }

                list.Add(new Recipe
                {
                    Name = label,
                    Summary = summary,
                    Kind = CustomMechanicKind.Item,
                    EffectKind = RecipeEffectKind.IpsPatch,
                    IpsPath = f,
                    ForVersion = v,
                    Target = ResearchTarget.CodeBin,
                    Entries = [],
                    Caveats =
                    [
                        $"Built for {v}. The other version's copy of this patch uses different "
                        + "offsets, so only the matching set is ever listed.",
                        "Writes straight into code.bin. Select several to apply them together - "
                        + "that is exactly what merging them into one .ips would do, and any "
                        + "address two of them share is reported before anything is written.",
                    ],
                });
            }
            break;   // nearest folder wins
        }
        return list;
    }

    public static IEnumerable<string> IpsFolders()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        yield return Path.Combine(baseDir, "other-ips");
        yield return Path.Combine(baseDir, "Resources", "other-ips");

        var curr = new DirectoryInfo(baseDir);
        while (curr != null)
        {
            string candidate = Path.Combine(curr.FullName, "other-ips");
            if (Directory.Exists(candidate)) yield return candidate;
            curr = curr.Parent;
        }
    }

    public static IEnumerable<string> PackageFolders()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        yield return Path.Combine(baseDir, "patch-packages");
        yield return Path.Combine(baseDir, "Resources", "patch-packages");

        var curr = new DirectoryInfo(baseDir);
        while (curr != null)
        {
            string candidate = Path.Combine(curr.FullName, "patch-packages");
            if (Directory.Exists(candidate)) yield return candidate;
            curr = curr.Parent;
        }
    }

    public static List<PatchPackage> LoadPackages(out string? loadedFrom)
    {
        loadedFrom = null;
        foreach (string folder in PackageFolders())
        {
            if (!Directory.Exists(folder)) continue;
            var problems = new List<string>();
            var found = PatchPackage.LoadFolder(folder, problems);
            if (found.Count == 0) continue;
            loadedFrom = folder;
            return PatchPackage.InDependencyOrder(found, problems);
        }
        return [];
    }

    private static Recipe FromPackage(PatchPackage p, string? from)
    {
        var slots = p.Mechanics?.SelectMany(m => m.Slots ?? []).ToList() ?? [];
        int reused = slots.Count(s => s.Reuse != null);
        int written = slots.Count - reused;

        var bits = new List<string>();
        if (slots.Count > 0) bits.Add($"{slots.Count} slot(s): {reused} reused, {written} new");
        if (p.Blocks?.Count > 0) bits.Add($"{p.Blocks.Count} code block(s)");
        if (p.SitePatches?.Count > 0) bits.Add($"{p.SitePatches.Count} site hook(s)");

        // A package names its own ids through parameters, so the recipe's id boxes describe those
        // rather than a block of consecutive slots.
        var entries = (p.ItemNames ?? [])
            .Select(kv => new RecipeEntry
            {
                Name = kv.Value,
                Description = (p.ItemDescriptions != null && p.ItemDescriptions.TryGetValue(kv.Key, out string? d) && !string.IsNullOrWhiteSpace(d))
                    ? ItemTextUtil.FormatDescription(d)
                    : ItemTextUtil.FormatDescription(p.Description),
                Plural = (p.ItemNamesPlural != null && p.ItemNamesPlural.TryGetValue(kv.Key, out string? pl) && !string.IsNullOrWhiteSpace(pl))
                    ? pl
                    : ItemTextUtil.Pluralize(kv.Value),
                PluralAlt = (p.ItemNamesPluralAlt != null && p.ItemNamesPluralAlt.TryGetValue(kv.Key, out string? pa) && !string.IsNullOrWhiteSpace(pa))
                    ? pa
                    : ItemTextUtil.PluralAlt(kv.Value),
            })
            .ToList();

        ResearchTarget target = ResearchTarget.BattleCro;
        if (p.OtherCros != null && p.OtherCros.ContainsKey("Bag.cro") && (p.Mechanics == null || p.Mechanics.Count == 0) && (p.Blocks == null || p.Blocks.Count == 0) && (p.SitePatches == null || p.SitePatches.Count == 0))
        {
            target = ResearchTarget.BagCro;
        }
        else if (p.OtherCros != null && p.OtherCros.Count > 0 && (p.Mechanics == null || p.Mechanics.Count == 0))
        {
            string first = p.OtherCros.Keys.First();
            if (first.Contains("Bag", StringComparison.OrdinalIgnoreCase)) target = ResearchTarget.BagCro;
            else if (first.Contains("Field", StringComparison.OrdinalIgnoreCase)) target = ResearchTarget.FieldRoCro;
        }
        else if (p.CodeBin != null && (p.Mechanics == null || p.Mechanics.Count == 0))
        {
            target = ResearchTarget.CodeBin;
        }

        var files = new List<string>();
        if ((p.Mechanics?.Count ?? 0) > 0 || (p.Blocks?.Count ?? 0) > 0 || (p.SitePatches?.Count ?? 0) > 0)
            files.Add("Battle.cro");
        if (p.OtherCros != null)
            files.AddRange(p.OtherCros.Keys);
        if (p.CodeBin != null && ((p.CodeBin.Patches?.Count ?? 0) > 0 || (p.CodeBin.Blocks?.Count ?? 0) > 0))
            files.Add("code.bin");

        return new Recipe
        {
            Name = p.Name,
            Summary = p.Description + (bits.Count > 0 ? "  (" + string.Join("; ", bits) + ")" : ""),
            Kind = CustomMechanicKind.Item,
            EffectKind = RecipeEffectKind.Package,
            Package = p,
            PackagePath = from,
            Entries = entries,
            Target = target,
            TargetFiles = [.. files.Distinct(StringComparer.OrdinalIgnoreCase)],
        };
    }

    public static Recipe? ByName(string name) =>
        All.Value.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<Recipe> Working =>
        All.Value.Where(r => r.EffectKind != RecipeEffectKind.ItemPatch || r.PatchName != null);
}
