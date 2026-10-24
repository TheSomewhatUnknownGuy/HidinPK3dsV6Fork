namespace pk3DS.Core;

public enum TextName
{
    AbilityNames,
    MoveNames,
    MoveFlavor,

    ItemNames,
    ItemFlavor,
    ItemNamesPlural,
    ItemNamesPluralAlt,

    SpeciesNames,
    Types,
    Natures,
    Forms,

    TrainerNames,
    TrainerClasses,
    TrainerText,
    metlist_000000,
    OPowerFlavor,
    MaisonTrainerNames,
    SuperTrainerNames,
    BattleRoyalNames,
    BattleTreeNames,

    SpeciesClassifications,
    PokedexEntry1,
    PokedexEntry2,

    // Added from a direct dump of the real USUM ROM's English (lang 2) gametext GARC, cross-checked
    // against the user's own numbered index list rather than guessed from the SM-vs-USUM offset
    // pattern alone (that pattern turned out to not be perfectly uniform - see TrainerNames below).
    BagCategoryNames,
    SongNames,
    OnlinePlayMenuText,
    BattleTeamText,
    VsRecorderText,
    BoxNames,
    MoveActionText,
    ZMoveActionText,
    BattleMessages,
    WeatherFieldEffectText,
    BattleEncounterText,
    ZMoveNames,
    TutorialText,
    UltraBeastMissionText,
    MenuText,
    BlackoutText,
    GTSText,
    IntroCutsceneText,
    FestivalPhrases,
    FestivalPlazaFacilityNames,
    SalonText,
    ColorNames,
    ClothingCategories,
    MantineSurfText,
    GiftText,
    SpecialTrainerNames,
    OnlineBattleRulesText,
    BerryNames,
    SettingsText,
    RotoPhotoText,
    PokePelagoText,
    IslandScanText,
    OnlineRulesetText,
    RibbonNames,
    RidePokemonNames,
    RidePokemonNotifications,
    PlazaGreetingText,
    CurrencyText,
    CreditsText,
    AbilityFlavor,
    LocationNames,
    TrainerCardText,
    NaturesDisplayText,
    PokemonHeightText,
    PokemonWeightText,
    PokedexCompletionText,
}

public class TextData(string[] lines)
{
    public readonly string[] Lines = lines;
    public bool Modified { get; private set; }

    public string this[int line]
    {
        get => Lines[line];
        set
        {
            if (Lines[line] == value)
                return;
            Modified = true;
            Lines[line] = value;
        }
    }
}

public sealed class TextReference
{
    public readonly int Index;
    public readonly TextName Name;

    private TextReference(int index, TextName name)
    {
        Index = index;
        Name = name;
    }

    public static readonly TextReference[] GameText_XY =
    [
        new(005, TextName.Forms),
        new(013, TextName.MoveNames),
        new(015, TextName.MoveFlavor),
        new(017, TextName.Types),
        new(020, TextName.TrainerClasses),
        new(021, TextName.TrainerNames),
        new(022, TextName.TrainerText),
        new(034, TextName.AbilityNames),
        new(047, TextName.Natures),
        new(072, TextName.metlist_000000),
        new(080, TextName.SpeciesNames),
        new(096, TextName.ItemNames),
        new(097, TextName.ItemNamesPlural),
        new(098, TextName.ItemNamesPluralAlt),
        new(099, TextName.ItemFlavor),
        new(130, TextName.MaisonTrainerNames),
        new(131, TextName.SuperTrainerNames),
        new(141, TextName.OPowerFlavor),
    ];

    public static readonly TextReference[] GameText_AO =
    [
        new(005, TextName.Forms),
        new(014, TextName.MoveNames),
        new(016, TextName.MoveFlavor),
        new(018, TextName.Types),
        new(021, TextName.TrainerClasses),
        new(022, TextName.TrainerNames),
        new(023, TextName.TrainerText),
        new(037, TextName.AbilityNames),
        new(051, TextName.Natures),
        new(090, TextName.metlist_000000),
        new(098, TextName.SpeciesNames),
        new(114, TextName.ItemNames),
        new(115, TextName.ItemNamesPlural),
        new(116, TextName.ItemNamesPluralAlt),
        new(117, TextName.ItemFlavor),
        new(153, TextName.MaisonTrainerNames),
        new(154, TextName.SuperTrainerNames),
        new(165, TextName.OPowerFlavor),
    ];

    public static readonly TextReference[] GameText_SMDEMO =
    [
        new(020, TextName.ItemFlavor),
        new(021, TextName.ItemNames),
        new(022, TextName.ItemNamesPlural),
        new(023, TextName.ItemNamesPluralAlt),
        new(026, TextName.SpeciesNames),
        new(030, TextName.metlist_000000),
        new(044, TextName.Forms),
        new(044, TextName.Natures),
        new(046, TextName.AbilityNames),
        new(049, TextName.TrainerText),
        new(050, TextName.TrainerNames),
        new(051, TextName.TrainerClasses),
        new(052, TextName.Types),
        new(054, TextName.MoveFlavor),
        new(055, TextName.MoveNames),
    ];

    public static readonly TextReference[] GameText_SM =
    [
        new(035, TextName.ItemFlavor),
        new(036, TextName.ItemNames),
        new(037, TextName.ItemNamesPlural),
        new(038, TextName.ItemNamesPluralAlt),
        new(055, TextName.SpeciesNames),
        new(067, TextName.metlist_000000),
        new(086, TextName.BattleRoyalNames),
        new(087, TextName.Natures),
        new(096, TextName.AbilityNames),
        new(099, TextName.BattleTreeNames),
        new(104, TextName.TrainerText),
        new(105, TextName.TrainerNames),
        new(106, TextName.TrainerClasses),
        new(107, TextName.Types),
        new(112, TextName.MoveFlavor),
        new(113, TextName.MoveNames),
        new(114, TextName.Forms),
        new(116, TextName.SpeciesClassifications),
        new(119, TextName.PokedexEntry1),
        new(120, TextName.PokedexEntry2),
    ];

    public static readonly TextReference[] GameText_USUM =
    [
        new(002, TextName.BagCategoryNames),
        new(003, TextName.SongNames),
        new(005, TextName.OnlinePlayMenuText),
        new(006, TextName.BattleTeamText),
        new(008, TextName.VsRecorderText),
        new(011, TextName.BoxNames),
        new(013, TextName.MoveActionText),
        new(014, TextName.ZMoveActionText),
        new(015, TextName.BattleMessages),
        new(016, TextName.WeatherFieldEffectText),
        new(017, TextName.BattleEncounterText),
        new(019, TextName.ZMoveNames),
        new(020, TextName.TutorialText),
        new(022, TextName.UltraBeastMissionText),
        new(024, TextName.MenuText),
        new(030, TextName.BlackoutText),
        new(033, TextName.GTSText),
        new(038, TextName.IntroCutsceneText),
        new(039, TextName.ItemFlavor),
        new(040, TextName.ItemNames),
        new(041, TextName.ItemNamesPlural),
        new(042, TextName.ItemNamesPluralAlt),
        new(046, TextName.FestivalPhrases),
        new(050, TextName.FestivalPlazaFacilityNames),
        new(052, TextName.SalonText),
        new(053, TextName.ColorNames),
        new(054, TextName.ClothingCategories),
        new(057, TextName.MantineSurfText),
        new(060, TextName.SpeciesNames),
        new(061, TextName.GiftText),
        new(063, TextName.SpecialTrainerNames),
        new(065, TextName.OnlineBattleRulesText),
        new(070, TextName.BerryNames),
        new(071, TextName.SettingsText),
        new(072, TextName.metlist_000000),
        new(076, TextName.RotoPhotoText),
        new(077, TextName.RotoPhotoText),
        new(081, TextName.PokePelagoText),
        new(082, TextName.IslandScanText),
        new(084, TextName.OnlineRulesetText),
        new(086, TextName.RibbonNames),
        new(087, TextName.RidePokemonNames),
        new(088, TextName.RidePokemonNotifications),
        new(091, TextName.BattleRoyalNames),
        new(092, TextName.Natures),
        new(093, TextName.PlazaGreetingText),
        new(095, TextName.CurrencyText),
        new(096, TextName.CreditsText),
        new(101, TextName.AbilityNames),
        new(102, TextName.AbilityFlavor),
        // A prior pass swapped these two based on reading their CONTENT (104 looked like a plain
        // name pool, 110 looked like a curated cameo-heavy roster) - that reasoning turned out
        // backwards, confirmed by a real crash (SMTE indexes trName[i] up to trdata.Length-1, which
        // threw once TrainerNames pointed at the shorter 210-entry list). The real, load-bearing
        // check is array LENGTH against the actual consumer, not a guess about content: real GARC
        // file counts are trdata/trpoke=653, maisontrN (Battle Tree roster)=210, maisontrS (Battle
        // Royal roster)=51 - which line up exactly with gametext index 110 (653), 104 (210), and 91
        // (51, already correctly BattleRoyalNames) respectively. So 104 is Battle Tree's own name
        // pool and 110 is the real per-trainer name table SMTE needs - the original mapping, restored.
        new(104, TextName.BattleTreeNames),
        new(105, TextName.LocationNames),
        new(107, TextName.TrainerCardText),
        new(108, TextName.NaturesDisplayText),
        new(109, TextName.TrainerText),
        new(110, TextName.TrainerNames),
        new(111, TextName.TrainerClasses),
        new(112, TextName.Types),
        new(117, TextName.MoveFlavor),
        new(118, TextName.MoveNames),
        new(119, TextName.Forms),
        new(120, TextName.PokemonHeightText),
        new(121, TextName.SpeciesClassifications),
        new(122, TextName.PokemonWeightText),
        new(123, TextName.PokedexCompletionText),
        new(124, TextName.PokedexEntry1),
        new(125, TextName.PokedexEntry2),
    ];
}