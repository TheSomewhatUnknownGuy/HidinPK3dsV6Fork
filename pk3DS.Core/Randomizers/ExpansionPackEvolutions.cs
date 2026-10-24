using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.Structures;

namespace pk3DS.Core.Randomizers;

/// <summary>
/// The Expansion Pack mod (which adds Gen 8/9 species 808-1025, plus Galarian/Hisuian regional
/// forms and White-Striped Basculin, to a Gen 6/7 ROM) doesn't give a number of these Pokemon a
/// working evolution method. This adds one for each, every time a seed is generated — matching
/// their real mainline-game evolution method wherever the ROM's evolution format can represent
/// it (Method 4 = level up, 8 = use item, 21 = level up knowing a move, 22 = level up with a
/// specific species in the party, 33 = level up at night), and falling back to an equivalent
/// level requirement where it can't (move-use counters, step counters, battle counters, etc. —
/// each such substitution is called out below).
///
/// Every species/move/item is resolved BY NAME at runtime against this ROM's own text tables
/// rather than hardcoded IDs, since Expansion Pack species IDs can vary slightly by build. The
/// one exception is species from Rellor (species ~953) onward: pk3DS's own Gen8/9 name table
/// (Gen89SpeciesNames.Names808_1025) has a known off-by-one gap starting at Rellor's slot, so
/// name lookup for species at/after that point falls back to a hardcoded National Dex number.
/// Regional-form entries assume the added form is form index 1 (2 for White-Striped Basculin,
/// since Basculin already has 2 vanilla forms) — this matches how every existing Gen 7 regional
/// form (Alolan Raichu, etc.) is indexed, but wasn't verifiable against a live ROM; spot-check
/// in the Evolution Editor if an entry looks off.
/// </summary>
public static class ExpansionPackEvolutions
{
    private sealed class EvoDef
    {
        public string SourceName, TargetName;
        public int SourceForm = -1;
        public int TargetForm = 0;
        public int Method, Level;
        public string ArgMoveName, ArgItemName, ArgPartySpeciesName;
        public int FallbackSourceId = -1, FallbackTargetId = -1; // used only if name lookup fails
    }

    private const int MethodLevelUp = 4;
    private const int MethodUseItem = 8;
    private const int MethodLevelUpKnowingMove = 21;
    private const int MethodLevelUpWithPartySpecies = 22;
    private const int MethodLevelUpAtNight = 33;

    private static readonly Dictionary<(int Parent, int Child), int> SourceFormMap = new()
    {
        { (83, 865), 1 },   // Farfetch'd (Galarian: 1) -> Sirfetch'd (0)
        { (263, 264), 1 },  // Zigzagoon (Galarian: 1) -> Linoone (Galarian: 1)
        { (264, 862), 1 },  // Linoone (Galarian: 1) -> Obstagoon (0)
        { (562, 867), 1 },  // Yamask (Galarian: 1) -> Runerigus (0)
        { (211, 904), 1 },  // Qwilfish (Hisuian: 1) -> Overqwil (0)
        { (215, 903), 1 },  // Sneasel (Hisuian: 1) -> Sneasler (0)
        { (550, 902), 2 },  // Basculin (White-Striped: 2) -> Basculegion (0)
        { (52, 863), 2 },   // Meowth (Galarian: 2) -> Perrserker (0)
        { (222, 864), 1 },  // Corsola (Galarian: 1) -> Cursola (0)
        { (122, 866), 1 },  // Mr. Mime (Galarian: 1) -> Mr. Rime (0)
        { (79, 80), 1 },    // Slowpoke (Galarian: 1) -> Galarian Slowbro (2)
        { (79, 199), 1 },   // Slowpoke (Galarian: 1) -> Galarian Slowking (1)
        { (77, 78), 1 },    // Ponyta (Galarian: 1) -> Rapidash (Galarian: 1)
        { (554, 555), 1 },  // Darumaka (Galarian: 1) -> Darmanitan (Galarian: 1)
        { (58, 59), 1 },    // Growlithe (Hisuian: 1) -> Arcanine (Hisuian: 1)
        { (100, 101), 1 },  // Voltorb (Hisuian: 1) -> Electrode (Hisuian: 1)
        { (570, 571), 1 },  // Zorua (Hisuian: 1) -> Zoroark (Hisuian: 1)
        { (705, 706), 1 },  // Sliggoo (Hisuian: 1) -> Goodra (Hisuian: 1)
    };

    public static int GetSourceFormForEvolution(int parentSpecies, int childSpecies)
    {
        if (SourceFormMap.TryGetValue((parentSpecies, childSpecies), out int form))
            return form;
        return -1;
    }

    private static readonly EvoDef[] Evolutions =
    [
        // --- Non-form species (official method matched where representable) ---
        new() { SourceName = "Primeape", TargetName = "Annihilape", Method = MethodLevelUp, Level = 35, FallbackSourceId = 57, FallbackTargetId = 979 },
        new() { SourceName = "Girafarig", TargetName = "Farigiraf", Method = MethodLevelUpWithPartySpecies, ArgPartySpeciesName = "Dudunsparce", Level = 1, FallbackSourceId = 203, FallbackTargetId = 981 },
        new() { SourceName = "Dunsparce", TargetName = "Dudunsparce", Method = MethodLevelUp, Level = 32, FallbackSourceId = 206, FallbackTargetId = 982 },
        new() { SourceName = "Stantler", TargetName = "Wyrdeer", Method = MethodLevelUpKnowingMove, ArgMoveName = "Psyshield Bash", Level = 1, FallbackSourceId = 234, FallbackTargetId = 899 },
        new() { SourceName = "Bisharp", TargetName = "Kingambit", Method = MethodLevelUp, Level = 52, FallbackSourceId = 625, FallbackTargetId = 983 },
        new() { SourceName = "Scyther", TargetName = "Kleavor", Method = MethodUseItem, ArgItemName = "Black Augurite", Level = 36, FallbackSourceId = 123, FallbackTargetId = 900 },
        new() { SourceName = "Ursaring", TargetName = "Ursaluna", Method = MethodUseItem, ArgItemName = "Peat Block", Level = 40, FallbackSourceId = 217, FallbackTargetId = 901 },
        new() { SourceName = "Applin", TargetName = "Dipplin", Method = MethodUseItem, ArgItemName = "Syrupy Apple", Level = 25, FallbackSourceId = 840, FallbackTargetId = 1011 },
        new() { SourceName = "Applin", TargetName = "Flapple", Method = MethodUseItem, ArgItemName = "Tart Apple", Level = 25, FallbackSourceId = 840, FallbackTargetId = 841 },
        new() { SourceName = "Applin", TargetName = "Appletun", Method = MethodUseItem, ArgItemName = "Sweet Apple", Level = 25, FallbackSourceId = 840, FallbackTargetId = 842 },
        new() { SourceName = "Meltan", TargetName = "Melmetal", Method = MethodLevelUp, Level = 40, FallbackSourceId = 808, FallbackTargetId = 809 },
        new() { SourceName = "Toxel", TargetName = "Toxtricity", Method = MethodLevelUp, Level = 30, FallbackSourceId = 848, FallbackTargetId = 849 },
        new() { SourceName = "Clobbopus", TargetName = "Grapploct", Method = MethodLevelUpKnowingMove, ArgMoveName = "Taunt", Level = 1, FallbackSourceId = 852, FallbackTargetId = 853 },
        new() { SourceName = "Snom", TargetName = "Frosmoth", Method = MethodLevelUpAtNight, Level = 20, FallbackSourceId = 872, FallbackTargetId = 873 },
        new() { SourceName = "Duraludon", TargetName = "Archaludon", Method = MethodLevelUp, Level = 35, FallbackSourceId = 884, FallbackTargetId = 1018 },
        new() { SourceName = "Lechonk", TargetName = "Oinkologne", Method = MethodLevelUp, Level = 18, FallbackSourceId = 915, FallbackTargetId = 916 },
        new() { SourceName = "Pawmi", TargetName = "Pawmo", Method = MethodLevelUp, Level = 18, FallbackSourceId = 921, FallbackTargetId = 922 },
        new() { SourceName = "Pawmo", TargetName = "Pawmot", Method = MethodLevelUp, Level = 25, FallbackSourceId = 922, FallbackTargetId = 923 },
        new() { SourceName = "Tandemaus", TargetName = "Maushold", Method = MethodLevelUp, Level = 25, FallbackSourceId = 924, FallbackTargetId = 925 },
        new() { SourceName = "Tadbulb", TargetName = "Bellibolt", Method = MethodLevelUp, Level = 30, FallbackSourceId = 938, FallbackTargetId = 939 },
        new() { SourceName = "Bramblin", TargetName = "Brambleghast", Method = MethodLevelUp, Level = 25, FallbackSourceId = 946, FallbackTargetId = 947 },
        new() { SourceName = "Capsakid", TargetName = "Scovillain", Method = MethodLevelUp, Level = 30, FallbackSourceId = 950, FallbackTargetId = 951 },
        new() { SourceName = "Rellor", TargetName = "Rabsca", Method = MethodLevelUp, Level = 25, FallbackSourceId = 953, FallbackTargetId = 954 },
        new() { SourceName = "Finizen", TargetName = "Palafin", Method = MethodLevelUp, Level = 38, FallbackSourceId = 963, FallbackTargetId = 964 },
        new() { SourceName = "Greavard", TargetName = "Houndstone", Method = MethodLevelUpAtNight, Level = 30, FallbackSourceId = 971, FallbackTargetId = 972 },
        new() { SourceName = "Cetoddle", TargetName = "Cetitan", Method = MethodLevelUp, Level = 30, FallbackSourceId = 974, FallbackTargetId = 975 },
        new() { SourceName = "Dipplin", TargetName = "Hydrapple", Method = MethodLevelUpKnowingMove, ArgMoveName = "Dragon Cheer", Level = 1, FallbackSourceId = 1011, FallbackTargetId = 1019 },
        new() { SourceName = "Charcadet", TargetName = "Armarouge", Method = MethodUseItem, ArgItemName = "Auspicious Armor", Level = 30, FallbackSourceId = 935, FallbackTargetId = 936 },
        new() { SourceName = "Charcadet", TargetName = "Ceruledge", Method = MethodUseItem, ArgItemName = "Malicious Armor", Level = 30, FallbackSourceId = 935, FallbackTargetId = 937 },
        new() { SourceName = "Nacli", TargetName = "Naclstack", Method = MethodLevelUp, Level = 24, FallbackSourceId = 931, FallbackTargetId = 932 },
        new() { SourceName = "Naclstack", TargetName = "Garganacl", Method = MethodLevelUp, Level = 38, FallbackSourceId = 932, FallbackTargetId = 933 },
        new() { SourceName = "Wattrel", TargetName = "Kilowattrel", Method = MethodLevelUp, Level = 25, FallbackSourceId = 940, FallbackTargetId = 941 },
        new() { SourceName = "Flittle", TargetName = "Espathra", Method = MethodLevelUp, Level = 35, FallbackSourceId = 955, FallbackTargetId = 956 },
        new() { SourceName = "Wiglett", TargetName = "Wugtrio", Method = MethodLevelUp, Level = 26, FallbackSourceId = 960, FallbackTargetId = 961 },
        new() { SourceName = "Smoliv", TargetName = "Dolliv", Method = MethodLevelUp, Level = 25, FallbackSourceId = 928, FallbackTargetId = 929 },
        new() { SourceName = "Dolliv", TargetName = "Arboliva", Method = MethodLevelUp, Level = 35, FallbackSourceId = 929, FallbackTargetId = 930 },
        new() { SourceName = "Shroodle", TargetName = "Grafaiai", Method = MethodLevelUp, Level = 28, FallbackSourceId = 944, FallbackTargetId = 945 },
        new() { SourceName = "Fidough", TargetName = "Dachsbun", Method = MethodLevelUp, Level = 26, FallbackSourceId = 926, FallbackTargetId = 927 },
        new() { SourceName = "Maschiff", TargetName = "Mabosstiff", Method = MethodLevelUp, Level = 30, FallbackSourceId = 942, FallbackTargetId = 943 },
        new() { SourceName = "Nymble", TargetName = "Lokix", Method = MethodLevelUp, Level = 24, FallbackSourceId = 919, FallbackTargetId = 920 },
        new() { SourceName = "Tarountula", TargetName = "Spidops", Method = MethodLevelUp, Level = 15, FallbackSourceId = 917, FallbackTargetId = 918 },
        new() { SourceName = "Fuecoco", TargetName = "Crocalor", Method = MethodLevelUp, Level = 16, FallbackSourceId = 909, FallbackTargetId = 910 },
        new() { SourceName = "Crocalor", TargetName = "Skeledirge", Method = MethodLevelUp, Level = 36, FallbackSourceId = 910, FallbackTargetId = 911 },
        new() { SourceName = "Sprigatito", TargetName = "Floragato", Method = MethodLevelUp, Level = 16, FallbackSourceId = 906, FallbackTargetId = 907 },
        new() { SourceName = "Floragato", TargetName = "Meowscarada", Method = MethodLevelUp, Level = 36, FallbackSourceId = 907, FallbackTargetId = 908 },
        new() { SourceName = "Quaxly", TargetName = "Quaxwell", Method = MethodLevelUp, Level = 16, FallbackSourceId = 912, FallbackTargetId = 913 },
        new() { SourceName = "Quaxwell", TargetName = "Quaquaval", Method = MethodLevelUp, Level = 36, FallbackSourceId = 913, FallbackTargetId = 914 },
        new() { SourceName = "Rookidee", TargetName = "Corvisquire", Method = MethodLevelUp, Level = 18, FallbackSourceId = 821, FallbackTargetId = 822 },
        new() { SourceName = "Corvisquire", TargetName = "Corviknight", Method = MethodLevelUp, Level = 38, FallbackSourceId = 822, FallbackTargetId = 823 },
        new() { SourceName = "Blipbug", TargetName = "Dottler", Method = MethodLevelUp, Level = 10, FallbackSourceId = 824, FallbackTargetId = 825 },
        new() { SourceName = "Dottler", TargetName = "Orbeetle", Method = MethodLevelUp, Level = 30, FallbackSourceId = 825, FallbackTargetId = 826 },
        new() { SourceName = "Nickit", TargetName = "Thievul", Method = MethodLevelUp, Level = 18, FallbackSourceId = 827, FallbackTargetId = 828 },
        new() { SourceName = "Gossifleur", TargetName = "Eldegoss", Method = MethodLevelUp, Level = 20, FallbackSourceId = 829, FallbackTargetId = 830 },
        new() { SourceName = "Wooloo", TargetName = "Dubwool", Method = MethodLevelUp, Level = 24, FallbackSourceId = 831, FallbackTargetId = 832 },
        new() { SourceName = "Chewtle", TargetName = "Drednaw", Method = MethodLevelUp, Level = 22, FallbackSourceId = 833, FallbackTargetId = 834 },
        new() { SourceName = "Yamper", TargetName = "Boltund", Method = MethodLevelUp, Level = 25, FallbackSourceId = 835, FallbackTargetId = 836 },
        new() { SourceName = "Rolycoly", TargetName = "Carkol", Method = MethodLevelUp, Level = 18, FallbackSourceId = 837, FallbackTargetId = 838 },
        new() { SourceName = "Carkol", TargetName = "Coalossal", Method = MethodLevelUp, Level = 34, FallbackSourceId = 838, FallbackTargetId = 839 },
        new() { SourceName = "Silicobra", TargetName = "Sandaconda", Method = MethodLevelUp, Level = 36, FallbackSourceId = 843, FallbackTargetId = 844 },
        new() { SourceName = "Arrokuda", TargetName = "Barraskewda", Method = MethodLevelUp, Level = 26, FallbackSourceId = 846, FallbackTargetId = 847 },
        new() { SourceName = "Sizzlipede", TargetName = "Centiskorch", Method = MethodLevelUp, Level = 28, FallbackSourceId = 850, FallbackTargetId = 851 },
        new() { SourceName = "Sinistea", TargetName = "Polteageist", Method = MethodUseItem, ArgItemName = "Cracked Pot", Level = 30, FallbackSourceId = 854, FallbackTargetId = 855 },
        new() { SourceName = "Hatenna", TargetName = "Hattrem", Method = MethodLevelUp, Level = 32, FallbackSourceId = 856, FallbackTargetId = 857 },
        new() { SourceName = "Hattrem", TargetName = "Hatterene", Method = MethodLevelUp, Level = 42, FallbackSourceId = 857, FallbackTargetId = 858 },
        new() { SourceName = "Impidimp", TargetName = "Morgrem", Method = MethodLevelUp, Level = 32, FallbackSourceId = 859, FallbackTargetId = 860 },
        new() { SourceName = "Morgrem", TargetName = "Grimmsnarl", Method = MethodLevelUp, Level = 42, FallbackSourceId = 860, FallbackTargetId = 861 },
        new() { SourceName = "Milcery", TargetName = "Alcremie", Method = MethodUseItem, ArgItemName = "Strawberry Sweet", Level = 25, FallbackSourceId = 868, FallbackTargetId = 869 },
        new() { SourceName = "Cufant", TargetName = "Copperajah", Method = MethodLevelUp, Level = 34, FallbackSourceId = 878, FallbackTargetId = 879 },
        new() { SourceName = "Dreepy", TargetName = "Drakloak", Method = MethodLevelUp, Level = 50, FallbackSourceId = 885, FallbackTargetId = 886 },
        new() { SourceName = "Drakloak", TargetName = "Dragapult", Method = MethodLevelUp, Level = 60, FallbackSourceId = 886, FallbackTargetId = 887 },
        new() { SourceName = "Poltchageist", TargetName = "Sinistcha", Method = MethodUseItem, ArgItemName = "Unremarkable Cup", Level = 30, FallbackSourceId = 1012, FallbackTargetId = 1013 },
        new() { SourceName = "Varoom", TargetName = "Revavroom", Method = MethodLevelUp, Level = 40, FallbackSourceId = 965, FallbackTargetId = 966 },
        new() { SourceName = "Gimmighoul", TargetName = "Gholdengo", Method = MethodLevelUp, Level = 38, FallbackSourceId = 999, FallbackTargetId = 1000 },
        new() { SourceName = "Tinkatink", TargetName = "Tinkatuff", Method = MethodLevelUp, Level = 24, FallbackSourceId = 957, FallbackTargetId = 958 },
        new() { SourceName = "Tinkatuff", TargetName = "Tinkaton", Method = MethodLevelUp, Level = 38, FallbackSourceId = 958, FallbackTargetId = 959 },

        // --- Regional / alternate forms (Form index assumed — see class remarks) ---
        new() { SourceName = "Ponyta", SourceForm = 1, TargetForm = 1, TargetName = "Rapidash", Method = MethodLevelUp, Level = 40, FallbackSourceId = 77, FallbackTargetId = 78 },
        new() { SourceName = "Farfetch'd", SourceForm = 1, TargetForm = 0, TargetName = "Sirfetch'd", Method = MethodLevelUp, Level = 25, FallbackSourceId = 83, FallbackTargetId = 865 },
        new() { SourceName = "Zigzagoon", SourceForm = 1, TargetForm = 1, TargetName = "Linoone", Method = MethodLevelUp, Level = 20, FallbackSourceId = 263, FallbackTargetId = 264 },
        new() { SourceName = "Linoone", SourceForm = 1, TargetForm = 0, TargetName = "Obstagoon", Method = MethodLevelUpAtNight, Level = 35, FallbackSourceId = 264, FallbackTargetId = 862 },
        new() { SourceName = "Darumaka", SourceForm = 1, TargetForm = 1, TargetName = "Darmanitan", Method = MethodUseItem, ArgItemName = "Ice Stone", Level = 0, FallbackSourceId = 554, FallbackTargetId = 555 },
        new() { SourceName = "Yamask", SourceForm = 1, TargetForm = 0, TargetName = "Runerigus", Method = MethodLevelUp, Level = 30, FallbackSourceId = 562, FallbackTargetId = 867 },
        new() { SourceName = "Growlithe", SourceForm = 1, TargetForm = 1, TargetName = "Arcanine", Method = MethodUseItem, ArgItemName = "Fire Stone", Level = 0, FallbackSourceId = 58, FallbackTargetId = 59 },
        new() { SourceName = "Voltorb", SourceForm = 1, TargetForm = 1, TargetName = "Electrode", Method = MethodUseItem, ArgItemName = "Leaf Stone", Level = 0, FallbackSourceId = 100, FallbackTargetId = 101 },
        new() { SourceName = "Qwilfish", SourceForm = 1, TargetForm = 0, TargetName = "Overqwil", Method = MethodLevelUpKnowingMove, ArgMoveName = "Barb Barrage", Level = 1, FallbackSourceId = 211, FallbackTargetId = 904 },
        new() { SourceName = "Sneasel", SourceForm = 1, TargetForm = 0, TargetName = "Sneasler", Method = MethodUseItem, ArgItemName = "Razor Claw", Level = 0, FallbackSourceId = 215, FallbackTargetId = 903 },
        new() { SourceName = "Zorua", SourceForm = 1, TargetForm = 1, TargetName = "Zoroark", Method = MethodLevelUp, Level = 30, FallbackSourceId = 570, FallbackTargetId = 571 },
        new() { SourceName = "Sliggoo", SourceForm = 1, TargetForm = 1, TargetName = "Goodra", Method = MethodLevelUp, Level = 50, FallbackSourceId = 705, FallbackTargetId = 706 },
        new() { SourceName = "Basculin", SourceForm = 2, TargetForm = 0, TargetName = "Basculegion", Method = MethodLevelUp, Level = 30, FallbackSourceId = 550, FallbackTargetId = 902 },
        new() { SourceName = "Meowth", SourceForm = 2, TargetForm = 0, TargetName = "Perrserker", Method = MethodLevelUp, Level = 28, FallbackSourceId = 52, FallbackTargetId = 863 },
        new() { SourceName = "Corsola", SourceForm = 1, TargetForm = 0, TargetName = "Cursola", Method = MethodLevelUp, Level = 38, FallbackSourceId = 222, FallbackTargetId = 864 },
        new() { SourceName = "Koffing", SourceForm = 0, TargetForm = 1, TargetName = "Weezing", Method = MethodLevelUp, Level = 35, FallbackSourceId = 109, FallbackTargetId = 110 },
        new() { SourceName = "Mime Jr.", SourceForm = 0, TargetForm = 1, TargetName = "Mr. Mime", Method = MethodLevelUp, Level = 28, FallbackSourceId = 439, FallbackTargetId = 122 },
        new() { SourceName = "Mr. Mime", SourceForm = 1, TargetForm = 0, TargetName = "Mr. Rime", Method = MethodLevelUp, Level = 42, FallbackSourceId = 122, FallbackTargetId = 866 },
        new() { SourceName = "Slowpoke", SourceForm = 1, TargetForm = 2, TargetName = "Slowbro", Method = MethodUseItem, ArgItemName = "Galarica Cuff", Level = 37, FallbackSourceId = 79, FallbackTargetId = 80 },
        new() { SourceName = "Slowpoke", SourceForm = 1, TargetForm = 1, TargetName = "Slowking", Method = MethodUseItem, ArgItemName = "Galarica Wreath", Level = 37, FallbackSourceId = 79, FallbackTargetId = 199 },
    ];

    /// <summary>
    /// Adds the missing evolution methods to <paramref name="config"/>'s in-memory evolution sets.
    /// </summary>
    /// <returns>How many entries were added. Zero means nothing needed changing.</returns>
    public static int Apply(GameConfig config) => Apply(config, config?.Evolutions);

    /// <summary>
    /// As above, against an explicit set of evolutions rather than the one on the config.
    /// </summary>
    public static int Apply(GameConfig config, EvolutionSet[] evolutions)
    {
        int added = 0;
        if (config == null || evolutions == null) return 0;
        // Only meaningful once the Expansion Pack's Gen 8/9 species are actually present.
        if (config.MaxSpeciesID < 1025) return 0;

        string[] speciesNames = config.GetText(TextName.SpeciesNames);
        string[] moveNames = config.GetText(TextName.MoveNames);
        string[] itemNames = config.GetText(TextName.ItemNames);

        int FindByName(string[] names, string target)
        {
            if (names == null || string.IsNullOrEmpty(target)) return -1;
            return Array.FindIndex(names, n => n != null && Competitive.GameNameComparer.Instance.Equals(n, target));
        }

        foreach (var def in Evolutions)
        {
            int sourceId = FindByName(speciesNames, def.SourceName);
            if (sourceId <= 0) sourceId = def.FallbackSourceId;
            int targetId = FindByName(speciesNames, def.TargetName);
            if (targetId <= 0) targetId = def.FallbackTargetId;
            if (sourceId <= 0 || targetId <= 0 || sourceId >= evolutions.Length) continue;

            int argument = 0;
            if (def.Method == MethodLevelUpKnowingMove)
            {
                int moveId = FindByName(moveNames, def.ArgMoveName);
                if (moveId <= 0) continue; // can't represent this evolution without the move — skip rather than guess
                argument = moveId;
            }
            else if (def.Method == MethodUseItem)
            {
                int itemId = FindByName(itemNames, def.ArgItemName);
                if (itemId <= 0) continue;
                argument = itemId;
            }
            else if (def.Method == MethodLevelUpWithPartySpecies)
            {
                int partyId = FindByName(speciesNames, def.ArgPartySpeciesName);
                if (partyId <= 0) continue;
                argument = partyId;
            }

            int sourceIndex = SourceIndex(config, sourceId, def.SourceForm);
            if (sourceIndex < 0 || sourceIndex >= evolutions.Length) continue;
            var evoSet = evolutions[sourceIndex];
            if (evoSet?.PossibleEvolutions == null) continue;

            int targetForm = def.TargetForm;

            // Don't duplicate if this exact evolution is already present (e.g. re-running on an
            // already-patched ROM), and only write into a genuinely empty (Method == 0) slot.
            bool alreadyPresent = evoSet.PossibleEvolutions.Any(e =>
                e != null && e.Method == def.Method && e.Species == targetId && e.Form == targetForm);
            if (alreadyPresent) continue;

            int emptySlot = Array.FindIndex(evoSet.PossibleEvolutions, e => e != null && e.Method == 0);
            if (emptySlot < 0) continue; // no room — leave the species' existing evolutions untouched

            evoSet.PossibleEvolutions[emptySlot] = new EvolutionMethod
            {
                Method = def.Method,
                Argument = argument,
                Species = targetId,
                Form = targetForm,
                Level = def.Level,
            };
            added++;
        }

        return added;
    }

    /// <summary>Writes the in-memory evolution sets back to the ROM's evolution GARC.</summary>
    public static bool Save(GameConfig config)
    {
        try
        {
            var g = config?.GetGARCData("evolution");
            if (g?.Files == null || config.Evolutions == null) return false;

            int n = Math.Min(g.Files.Length, config.Evolutions.Length);
            for (int i = 0; i < n; i++)
                g.Files[i] = config.Evolutions[i].Write();

            g.Save();
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Applies the missing methods and writes them out in one step.
    /// </summary>
    /// <returns>How many entries were added and saved.</returns>
    public static int ApplyAndSave(GameConfig config)
    {
        int added = Apply(config);
        if (added > 0) Save(config);
        return added;
    }

    /// <summary>
    /// How many of these evolutions are missing from the ROM right now, without changing anything.
    /// </summary>
    public static int CountMissing(GameConfig config) => CountMissing(config, config?.Evolutions);

    /// <summary>As above, against an explicit set of evolutions.</summary>
    public static int CountMissing(GameConfig config, EvolutionSet[] evolutions)
    {
        if (config == null || evolutions == null || config.MaxSpeciesID < 1025) return 0;

        string[] speciesNames = config.GetText(TextName.SpeciesNames);
        int missing = 0;

        foreach (var def in Evolutions)
        {
            int sourceId = Array.FindIndex(speciesNames ?? [],
                n => n != null && n.Equals(def.SourceName, StringComparison.OrdinalIgnoreCase));
            if (sourceId <= 0) sourceId = def.FallbackSourceId;
            int targetId = Array.FindIndex(speciesNames ?? [],
                n => n != null && n.Equals(def.TargetName, StringComparison.OrdinalIgnoreCase));
            if (targetId <= 0) targetId = def.FallbackTargetId;
            if (sourceId <= 0 || targetId <= 0 || sourceId >= evolutions.Length) continue;

            int sourceIndex = SourceIndex(config, sourceId, def.SourceForm);
            if (sourceIndex < 0 || sourceIndex >= evolutions.Length) continue;
            var evoSet = evolutions[sourceIndex];
            if (evoSet?.PossibleEvolutions == null) continue;

            // Compare the field Apply writes (the target's form), not the source's.
            bool present = evoSet.PossibleEvolutions.Any(e =>
                e != null && e.Method == def.Method && e.Species == targetId && e.Form == def.TargetForm);
            if (!present) missing++;
        }
        return missing;
    }

    /// <summary>
    /// The evolution entry a definition belongs to. Gen 7 keeps one evolution file per personal
    /// entry, forms included, so Galarian Farfetch'd's evolution must go in its form's entry: written
    /// to the base entry it made Kantonian Farfetch'd evolve into Sirfetch'd. Returns -1 when this ROM
    /// has no such form, rather than falling back to the base species.
    /// </summary>
    private static int SourceIndex(GameConfig config, int sourceId, int sourceForm)
    {
        if (sourceForm <= 0) return sourceId;
        int index = config.Personal?.GetFormIndex(sourceId, sourceForm) ?? sourceId;
        return index == sourceId ? -1 : index;
    }

    /// <summary>Every species this class can add an evolution for, for reporting.</summary>
    public static int DefinedCount => Evolutions.Length;
}
