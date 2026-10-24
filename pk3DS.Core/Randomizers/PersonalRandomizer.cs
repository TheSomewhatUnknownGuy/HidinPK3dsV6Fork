using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using pk3DS.Core;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using pk3DS.Core.Randomizers.Competitive;

namespace pk3DS.Core.Randomizers;

public class PersonalRandomizer : IRandomizer
{
    private readonly Random rnd = Util.Rand;

    private const decimal LearnTMPercent = 35; // Average Learnable TMs is 35.260.
    private const decimal LearnTypeTutorPercent = 2; //136 special tutor moves learnable by species in Untouched ORAS.
    private const decimal LearnMoveTutorPercent = 30; //10001 tutor moves learnable by 826 species in Untouched ORAS.
    private int tmcount => (Game != null && Game.Generation == 7 && Game.MaxSpeciesID >= 1025) ? 128 : 100;
    private const int eggGroupCount = 16;

    private readonly GameConfig Game;
    private readonly PersonalInfo[] Table;
    private readonly HashSet<int> DefaultBannedAbilities = [];

    // Randomization Settings
    public int TypeCount = 18;
    public bool ModifyCatchRate = true;
    public bool ModifyLearnsetTM = true;
    public bool ModifyLearnsetHM = true;
    public bool ModifyLearnsetTypeTutors = true;
    public bool ModifyLearnsetMoveTutors = true;
    public bool ModifyHeldItems = true;

    public bool ModifyAbilities = true;
    public bool AllowWonderGuard = true;
    public bool ModifyAbilitiesFollowEvolutions { get; set; }
    public bool ModifyAbilitiesFollowMegas { get; set; }

    public bool ModifyStats = true;
    public bool ShuffleStats = true;
    public bool PreserveBST = true;
    public decimal StatDeviation = 25;
    public bool[] StatsToRandomize = [true, true, true, true, true, true];
    public bool ModifyStatsFollowEvolutions { get; set; }
    public bool ModifyStatsFollowMegas { get; set; }
    public bool AvoidMinmaxing { get; set; } = false;

    public bool EnforceMinimumBST { get; set; } = true;
    public int MinBST3Stage1 { get; set; } = 300;
    public int MinBST3Stage2 { get; set; } = 450;
    public int MinBST3Stage3 { get; set; } = 600;
    public int MinBST2Stage1 { get; set; } = 350;
    public int MinBST2Stage2 { get; set; } = 550;
    public int MinBST1Stage { get; set; } = 500;
    public int MinBSTLegendary { get; set; } = 600;

    // Maximum BST ceilings — mirrors the Minimum BST Floors tiering exactly, but as a cap.
    public bool EnforceMaximumBST { get; set; } = false;
    public int MaxBST3Stage1 { get; set; } = 500;
    public int MaxBST3Stage2 { get; set; } = 600;
    public int MaxBST3Stage3 { get; set; } = 700;
    public int MaxBST2Stage1 { get; set; } = 550;
    public int MaxBST2Stage2 { get; set; } = 650;
    public int MaxBST1Stage { get; set; } = 600;
    public int MaxBSTLegendary { get; set; } = 780;

    public bool NoEgregiousStats { get; set; } = false;
    public int NoEgregiousStatsSingleCap { get; set; } = 200;
    public int NoEgregiousStatsBSTCapRegular { get; set; } = 600;
    public int NoEgregiousStatsBSTCapLegendary { get; set; } = 780;

    public bool ModifyTypes = true;
    public decimal SameTypeChance = 50;
    public bool ModifyTypesFollowEvolutions { get; set; }
    public bool ModifyTypesFollowMegas { get; set; }
    public bool ForceDualTypes { get; set; } = false;
    public bool ModifyEggGroup = true;
    public decimal SameEggGroupChance = 50;

    //public bool Advanced { get; set; } = false;
    public bool TMInheritance { get; set; }
    public bool ModifyLearnsetSmartly { get; set; }

    public ushort[] MoveIDsTMs { private get; set; }
    public Move[] Moves => Game?.Moves;
    private EvolutionSet[] _evosOverride;
    public EvolutionSet[] Evos { get => _evosOverride ?? Game?.Evolutions; set => _evosOverride = value; }
    private int MaxSpeciesID => Game?.MaxSpeciesID ?? (Table != null && Table.Length > 1025 ? 1025 : 807);

    private readonly int[][] OriginalStats;

    public PersonalRandomizer(PersonalInfo[] table, GameConfig game = null)
    {
        Game = game;
        Table = table;
        OriginalStats = table.Select(z => z?.Stats != null ? (int[])z.Stats.Clone() : new int[6]).ToArray();
        string[] abilityNames = Array.Empty<string>();
        try { abilityNames = Game?.GetText(TextName.AbilityNames) ?? Array.Empty<string>(); } catch { }

        string[] formLockedAbilities =
        [
            "Hunger Switch", "Disguise", "Stance Change", "Gulp Missile", "Ice Face",
            "Schooling", "Power Construct", "Shields Down", "Battle Bond", "RKS System",
            "Multitype", "Forecast", "Flower Gift", "Zen Mode", "Illusion",
            "Commander", "Zero to Hero", "As One", "Tera Shift", "Teraform Zero", "Poison Puppeteer"
        ];

        int maxAbil = Game?.Info?.MaxAbilityID ?? (abilityNames.Length - 1);
        for (int i = 1; i < abilityNames.Length && i <= maxAbil; i++)
        {
            string aName = abilityNames[i];
            if (string.IsNullOrWhiteSpace(aName) || aName == "—" || aName == "———")
            {
                DefaultBannedAbilities.Add(i);
                continue;
            }
            if (formLockedAbilities.Any(f => GameNameComparer.Instance.Equals(f, aName)) || aName.StartsWith("As One", StringComparison.OrdinalIgnoreCase))
            {
                DefaultBannedAbilities.Add(i);
            }
        }
        if (File.Exists("bannedabilities.txt"))
        {
            var data = File.ReadAllLines("bannedabilities.txt");
            var list = new List<int>(BannedAbilities);
            list.AddRange(data.Select(z => Convert.ToInt32(z)));
            BannedAbilities = list;
        }
    }

    public void Execute()
    {
        for (var i = 1; i < Table.Length; i++)
            Randomize(Table[i], i);

        if (TMInheritance)
            PropagateTMs(Table, Evos);

        ApplyStatFloors();
        ApplyMaximumBST();
        ApplyNoEgregiousStats();
        ApplyStatCap();

        if (ForceDualTypes)
            ApplyForceDualTypes(Table);
    }

    public void PropagateTMs(PersonalInfo[] table, EvolutionSet[] evos)
    {
        if (table == null || evos == null) return;
        int specCount = Math.Min(Game?.MaxSpeciesID ?? (table.Length - 1), table.Length - 1);
        var handledIndexes = new HashSet<int>();

        var isChild = new bool[table.Length];
        for (int i = 1; i <= specCount && i < evos.Length; i++)
        {
            if (evos[i]?.PossibleEvolutions == null) continue;
            foreach (var evo in evos[i].PossibleEvolutions.Where(z => z != null && z.Species != 0))
            {
                if (evo.Species >= table.Length || table[evo.Species] == null) continue;
                int evoIndex = table[evo.Species].FormeIndex(evo.Species, evo.Form < 0 ? 0 : evo.Form, specCount);
                if (evoIndex < table.Length)
                    isChild[evoIndex] = true;
            }
        }

        for (int species = 1; species <= specCount; species++)
        {
            if (table[species] == null) continue;
            if (!isChild[species])
            {
                PropagateDownIndex(table[species], species);
                for (int form = 1; form < table[species].FormeCount; form++)
                {
                    int formIndex = table[species].FormeIndex(species, form, specCount);
                    if (formIndex != species && formIndex < table.Length && !isChild[formIndex] && table[formIndex] != null)
                        PropagateDownIndex(table[formIndex], formIndex);
                }
            }
        }

        for (int species = 1; species <= specCount; species++)
        {
            if (table[species] != null && !handledIndexes.Contains(species))
            {
                PropagateDownIndex(table[species], species);
            }
        }

        void PropagateDownIndex(PersonalInfo pi, int index)
        {
            handledIndexes.Add(index);
            if (index >= evos.Length) return;
            var evoList = evos[index];
            if (evoList?.PossibleEvolutions == null) return;

            foreach (var evo in evoList.PossibleEvolutions.Where(z => z != null && z.Species != 0))
            {
                var espec = evo.Species;
                var eform = evo.Form < 0 ? 0 : evo.Form;
                if (espec >= table.Length) continue;

                var evoIndex = table[espec].FormeIndex(espec, eform, specCount);
                if (evoIndex >= table.Length) continue;

                if (!handledIndexes.Contains(evoIndex))
                {
                    if (table[evoIndex].TMHM != null && pi.TMHM != null)
                    {
                        var childTM = (bool[])table[evoIndex].TMHM.Clone();
                        for (int b = 0; b < childTM.Length && b < pi.TMHM.Length; b++)
                            childTM[b] |= pi.TMHM[b];
                        table[evoIndex].TMHM = childTM;
                    }
                    else if (pi.TMHM != null)
                    {
                        table[evoIndex].TMHM = (bool[])pi.TMHM.Clone();
                    }
                    PropagateDownIndex(table[evoIndex], evoIndex);
                }
                else
                {
                    if (table[evoIndex].TMHM != null && pi.TMHM != null)
                    {
                        for (int b = 0; b < table[evoIndex].TMHM.Length && b < pi.TMHM.Length; b++)
                            table[evoIndex].TMHM[b] |= pi.TMHM[b];
                    }
                }
            }
        }
    }

    public void Randomize(PersonalInfo z, int index)
    {
        // Fiddle with Learnsets
        if (ModifyLearnsetTM || ModifyLearnsetHM)
        {
            if (!ModifyLearnsetSmartly)
                RandomizeTMHMSimple(z);
            else
                RandomizeTMHMAdvanced(z);
        }
        if (ModifyLearnsetTypeTutors)
            RandomizeTypeTutors(z, index);
        if (ModifyLearnsetMoveTutors)
            RandomizeSpecialTutors(z);
        if (ModifyStats)
        {
            if (PreserveBST)
                RandomizeStatsBSTPreserving(z);
            else
                RandomizeStats(z);
        }
        if (ShuffleStats)
            RandomShuffledStats(z);
        if (ModifyTypes)
            RandomizeTypes(z);
        if (ModifyAbilities)
            RandomizeAbilities(z, index);
        if (ModifyEggGroup)
            RandomizeEggGroups(z);
        if (ModifyHeldItems)
            RandomizeHeldItems(z, index);
        if (ModifyCatchRate)
            z.CatchRate = rnd.Next(3, 251); // Random Catch Rate between 3 and 250.
    }

    public void ApplyTypeInheritance()
    {
        if (!ModifyTypes)
        {
            if (ForceDualTypes)
                ApplyForceDualTypes(Table);
            return;
        }

        if (ModifyTypesFollowEvolutions)
            PropagateTypes(Table, Evos);

        if (ModifyTypesFollowMegas)
            PropagateMegaTypes(Table);

        SynchronizeCalyrexRiderTypes(Table);

        if (ForceDualTypes)
            ApplyForceDualTypes(Table);
    }

    public void SynchronizeCalyrexRiderTypes(PersonalInfo[] table)
    {
        const int CalyrexSpecies = 898;
        if (CalyrexSpecies >= table.Length) return;
        int specCount = MaxSpeciesID;
        var entry = table[CalyrexSpecies];
        if (entry == null) return;

        int iceRiderIdx = entry.FormeIndex(CalyrexSpecies, 1, specCount);
        int shadowRiderIdx = entry.FormeIndex(CalyrexSpecies, 2, specCount);
        if ((iceRiderIdx == CalyrexSpecies || iceRiderIdx >= table.Length) && table.Length > 1303)
        {
            iceRiderIdx = 1302;
            shadowRiderIdx = 1303;
        }
        if (iceRiderIdx >= table.Length || shadowRiderIdx >= table.Length) return;
        if (iceRiderIdx == CalyrexSpecies || shadowRiderIdx == CalyrexSpecies) return;

        int primary = entry.Types[0];
        int sec1 = table[iceRiderIdx].Types[1];
        int sec2 = table[shadowRiderIdx].Types[1];

        int attempts = 0;
        if (sec1 == primary)
        {
            do { sec1 = GetRandomType(); } while (sec1 == primary && attempts++ < 50);
        }
        attempts = 0;
        if (sec2 == primary || sec2 == sec1)
        {
            do { sec2 = GetRandomType(); } while ((sec2 == primary || sec2 == sec1) && attempts++ < 50);
        }

        table[iceRiderIdx].Types = [primary, sec1];
        table[shadowRiderIdx].Types = [primary, sec2];
    }

    private void PropagateMegaTypes(PersonalInfo[] table)
    {
        int specCount = Math.Min(MaxSpeciesID, table.Length - 1);
        for (int species = 1; species <= specCount; species++)
        {
            if (species >= table.Length) break;
            var entry = table[species];
            if (entry == null || entry.FormeCount <= 1) continue;

            var megaIndices = CompetitiveDatabase.GetMegaFormIndices(species, table, specCount);
            foreach (int megaIdx in megaIndices)
            {
                if (megaIdx < table.Length && megaIdx != species)
                {
                    var parentEntry = entry;
                    if (species == 670)
                    {
                        int floette5Idx = entry.FormeIndex(670, 5, specCount);
                        if (floette5Idx < table.Length && floette5Idx != 670)
                            parentEntry = table[floette5Idx];
                    }
                    else if (species == 718)
                    {
                        int completeIdx = entry.FormeIndex(718, 4, specCount);
                        if (completeIdx < table.Length && completeIdx != 718)
                            parentEntry = table[completeIdx];
                    }
                    int megaSecType = ForceDualTypes
                        ? GetRandomSecondaryType(parentEntry.Types[0])
                        : (rnd.Next(0, 100) < SameTypeChance ? parentEntry.Types[0] : GetRandomType());
                    table[megaIdx].Types = [parentEntry.Types[0], megaSecType];
                }
            }
        }
    }

    public void ApplyStatInheritance()
    {
        if (ModifyStats && ModifyStatsFollowEvolutions)
        {
            PropagateStats(Table, Evos);
        }

        ApplyStatFloors();
        ApplyMaximumBST();
        ApplyNoEgregiousStats();
    }

    public void ApplyAbilityInheritance()
    {
        if (!ModifyAbilities || !ModifyAbilitiesFollowEvolutions)
            return;

        PropagateAbilities(Table, Evos);
    }

    private int GetFormOfIndex(int idx, int baseSpec, PersonalInfo[] table, int specCount)
    {
        if (idx <= specCount) return 0;
        if (baseSpec <= 0 || baseSpec >= table.Length) return 0;
        int fc = table[baseSpec].FormeCount;
        for (int f = 1; f < fc; f++)
        {
            if (table[baseSpec].FormeIndex(baseSpec, f, specCount) == idx)
                return f;
        }
        return 0;
    }

    private static bool IsValidEvolutionParent(int baseSpec, int currentForm, int targetSpec, int targetForm, PersonalInfo[] table)
    {
        int requiredParentForm = ExpansionPackEvolutions.GetSourceFormForEvolution(baseSpec, targetSpec);
        if (requiredParentForm >= 0)
        {
            return currentForm == requiredParentForm;
        }

        if (currentForm > 0 && targetForm == 0 && targetSpec < table.Length && table[targetSpec].FormeCount > currentForm)
        {
            return false;
        }
        if (currentForm == 0 && targetForm > 0 && table[baseSpec].FormeCount > 1)
        {
            if (baseSpec != 109 && baseSpec != 104 && baseSpec != 25 && baseSpec != 102)
                return false;
        }

        return true;
    }

    private void PropagateTypes(PersonalInfo[] table, EvolutionSet[] evos)
    {
        int specCount = Math.Min(MaxSpeciesID, table.Length - 1);
        var handledIndexes = new HashSet<int>();

        int GetBaseSpecies(int idx)
        {
            if (idx <= specCount) return idx;
            for (int s = 1; s <= specCount; s++)
            {
                if (s >= table.Length) break;
                int fc = table[s].FormeCount;
                for (int f = 1; f < fc; f++)
                {
                    if (table[s].FormeIndex(s, f, specCount) == idx)
                        return s;
                }
            }
            return 0;
        }

        var isChild = new bool[table.Length];
        for (int i = 1; i <= specCount; i++)
        {
            if (i >= evos.Length || evos[i]?.PossibleEvolutions == null) continue;
            foreach (var evo in evos[i].PossibleEvolutions.Where(z => z != null && z.Species != 0))
            {
                if (evo.Species >= table.Length) continue;
                int evoIndex = table[evo.Species].FormeIndex(evo.Species, evo.Form < 0 ? 0 : evo.Form, specCount);
                if (evoIndex < table.Length)
                    isChild[evoIndex] = true;
            }
        }

        for (int species = 1; species <= specCount; species++)
        {
            if (!isChild[species])
            {
                PropagateDownIndex(table[species], species);
            }
        }

        for (int i = 1; i < table.Length; i++)
        {
            if (!handledIndexes.Contains(i))
            {
                PropagateDownIndex(table[i], i);
            }
        }

        void PropagateDownIndex(PersonalInfo pi, int index)
        {
            handledIndexes.Add(index);
            int baseSpec = GetBaseSpecies(index);
            if (baseSpec == 0 || baseSpec >= evos.Length) return;
            var evoList = evos[baseSpec];
            if (evoList?.PossibleEvolutions == null) return;

            int currentForm = GetFormOfIndex(index, baseSpec, table, specCount);
            var branches = evoList.PossibleEvolutions.Where(z => z != null && z.Species != 0).ToList();
            var validBranches = branches.Where(evo =>
            {
                var espec = evo.Species;
                var eform = evo.Form < 0 ? 0 : evo.Form;
                return espec < table.Length && IsValidEvolutionParent(baseSpec, currentForm, espec, eform, table);
            }).ToList();
            bool multiBranch = validBranches.Count > 1;

            var siblingTypePairs = multiBranch ? new HashSet<(int, int)>() : null;

            foreach (var evo in validBranches)
            {
                var espec = evo.Species;
                var eform = evo.Form < 0 ? 0 : evo.Form;

                var evoIndex = table[espec].FormeIndex(espec, eform, specCount);
                if (evoIndex >= table.Length) continue;

                if (!handledIndexes.Contains(evoIndex))
                {
                    if (multiBranch)
                    {
                        int t0 = (baseSpec == 133) ? GetRandomType() : pi.Types[0];
                        int t1 = pi.Types[1];
                        int attempts = 0;
                        do
                        {
                            if (baseSpec == 133)
                            {
                                t0 = GetRandomType();
                                t1 = ForceDualTypes
                                    ? GetRandomSecondaryType(t0)
                                    : (rnd.Next(0, 100) < SameTypeChance ? t0 : GetRandomType());
                            }
                            else if (siblingTypePairs.Count > 0)
                            {
                                t1 = ForceDualTypes ? GetRandomSecondaryType(t0) : GetRandomType();
                            }
                            else if (ForceDualTypes && t1 == t0)
                            {
                                t1 = GetRandomSecondaryType(t0);
                            }
                            attempts++;
                        } while ((siblingTypePairs.Contains((t0, t1)) || (ForceDualTypes && t0 == t1)) && attempts < 30);
                        if (ForceDualTypes && t0 == t1)
                            t1 = GetRandomSecondaryType(t0);

                        siblingTypePairs.Add((t0, t1));
                        table[evoIndex].Types = new[] { t0, t1 };
                    }
                    else
                    {
                        var evoTypes = (int[])pi.Types.Clone();
                        if (ForceDualTypes && evoTypes.Length >= 2 && evoTypes[0] == evoTypes[1])
                            evoTypes[1] = GetRandomSecondaryType(evoTypes[0]);
                        table[evoIndex].Types = evoTypes;
                    }
                    PropagateDownIndex(table[evoIndex], evoIndex);
                }
            }
        }
    }

    private void PropagateStats(PersonalInfo[] table, EvolutionSet[] evos)
    {
        int specCount = Math.Min(MaxSpeciesID, table.Length - 1);
        var handledIndexes = new HashSet<int>();

        int GetBaseSpecies(int idx)
        {
            if (idx <= specCount) return idx;
            for (int s = 1; s <= specCount; s++)
            {
                if (s >= table.Length) break;
                int fc = table[s].FormeCount;
                for (int f = 1; f < fc; f++)
                {
                    if (table[s].FormeIndex(s, f, specCount) == idx)
                        return s;
                }
            }
            return 0;
        }

        var isChild = new bool[table.Length];
        for (int i = 1; i <= specCount; i++)
        {
            if (i >= evos.Length || evos[i]?.PossibleEvolutions == null) continue;
            foreach (var evo in evos[i].PossibleEvolutions.Where(z => z != null && z.Species != 0))
            {
                if (evo.Species >= table.Length) continue;
                int evoIndex = table[evo.Species].FormeIndex(evo.Species, evo.Form < 0 ? 0 : evo.Form, specCount);
                if (evoIndex < table.Length)
                    isChild[evoIndex] = true;
            }
        }

        for (int species = 1; species <= specCount; species++)
        {
            if (!isChild[species])
            {
                PropagateDownIndex(table[species], species);
            }
        }

        for (int i = 1; i < table.Length; i++)
        {
            if (!handledIndexes.Contains(i))
            {
                PropagateDownIndex(table[i], i);
            }
        }

        void PropagateDownIndex(PersonalInfo pi, int index)
        {
            handledIndexes.Add(index);
            int baseSpec = GetBaseSpecies(index);
            if (baseSpec == 0 || baseSpec >= evos.Length) return;
            var evoList = evos[baseSpec];
            if (evoList?.PossibleEvolutions == null) return;

            int currentForm = GetFormOfIndex(index, baseSpec, table, specCount);
            var branches = evoList.PossibleEvolutions.Where(z => z != null && z.Species != 0).ToList();
            bool multiBranch = branches.Count > 1;

            foreach (var evo in branches)
            {
                var espec = evo.Species;
                var eform = evo.Form < 0 ? 0 : evo.Form;
                if (espec >= table.Length) continue;

                if (!IsValidEvolutionParent(baseSpec, currentForm, espec, eform, table))
                    continue;

                var evoIndex = table[espec].FormeIndex(espec, eform, specCount);
                if (evoIndex >= table.Length) continue;

                if (!handledIndexes.Contains(evoIndex))
                {
                    int ourBST = OriginalStats[evoIndex].Sum();
                    int parentBST = OriginalStats[index].Sum();
                    double bstRatio = parentBST > 0 ? (double)ourBST / parentBST : 1.0;
                    int targetEvoBST = Math.Clamp((int)Math.Round(pi.Stats.Sum() * bstRatio), 30, 6 * 255);

                    int[] newEvo = new int[6];
                    for (int s = 0; s < 6; s++)
                    {
                        newEvo[s] = Math.Clamp((int)Math.Round(pi.Stats[s] * bstRatio), 5, 255);
                    }
                    int evoAllocated = newEvo.Sum();
                    int evoDiff = targetEvoBST - evoAllocated;
                    int evoAttempts = 100;
                    while (evoDiff > 0 && evoAttempts-- > 0)
                    {
                        int idx = rnd.Next(0, 6);
                        if (newEvo[idx] < 255)
                        {
                            newEvo[idx]++;
                            evoDiff--;
                        }
                    }
                    while (evoDiff < 0 && evoAttempts-- > 0)
                    {
                        int idx = rnd.Next(0, 6);
                        if (newEvo[idx] > 5)
                        {
                            newEvo[idx]--;
                            evoDiff++;
                        }
                    }
                    table[evoIndex].Stats = newEvo;

                    if (multiBranch)
                        RandomizeStatsBSTPreserving(table[evoIndex]);

                    PropagateDownIndex(table[evoIndex], evoIndex);
                }
            }
        }

        if (ModifyStatsFollowMegas)
        {
            for (int species = 1; species <= specCount; species++)
            {
                var entry = table[species];

                var megaFormIndices = CompetitiveDatabase.MegaSpeciesIDs.Contains(species)
                    ? new HashSet<int>(CompetitiveDatabase.GetMegaFormIndices(species, table, specCount))
                    : null;

                for (int form = 1; form < entry.FormeCount; form++)
                {
                    int formIndex = entry.FormeIndex(species, form, specCount);
                    if (formIndex >= table.Length || formIndex == species) continue;
                    if (megaFormIndices != null && megaFormIndices.Contains(formIndex)) continue;

                    int formBST = OriginalStats[formIndex].Sum();
                    int baseBST = OriginalStats[species].Sum();
                    double bstRatio = baseBST > 0 ? (double)formBST / baseBST : 1.0;
                    int targetBST = Math.Clamp((int)Math.Round(entry.Stats.Sum() * bstRatio), 6, 6 * 255);

                    int[] seedStats = new int[6];
                    int baseShare = targetBST / 6;
                    int remainder = targetBST - (baseShare * 6);
                    for (int s = 0; s < 6; s++) seedStats[s] = baseShare;
                    for (int s = 0; s < remainder; s++) seedStats[s]++;
                    table[formIndex].Stats = seedStats;
                    RandomizeStatsBSTPreserving(table[formIndex]);
                }
            }
        }
    }

    private void PropagateAbilities(PersonalInfo[] table, EvolutionSet[] evos)
    {
        int specCount = Math.Min(MaxSpeciesID, table.Length - 1);
        var handledIndexes = new HashSet<int>();

        int GetBaseSpecies(int idx)
        {
            if (idx <= specCount) return idx;
            for (int s = 1; s <= specCount; s++)
            {
                if (s >= table.Length) break;
                int fc = table[s].FormeCount;
                for (int f = 1; f < fc; f++)
                {
                    if (table[s].FormeIndex(s, f, specCount) == idx)
                        return s;
                }
            }
            return 0;
        }

        var isChild = new bool[table.Length];
        for (int i = 1; i <= specCount; i++)
        {
            if (i >= evos.Length || evos[i]?.PossibleEvolutions == null) continue;
            foreach (var evo in evos[i].PossibleEvolutions.Where(z => z != null && z.Species != 0))
            {
                if (evo.Species >= table.Length) continue;
                int evoIndex = table[evo.Species].FormeIndex(evo.Species, evo.Form < 0 ? 0 : evo.Form, specCount);
                if (evoIndex < table.Length)
                    isChild[evoIndex] = true;
            }
        }

        for (int species = 1; species <= specCount; species++)
        {
            if (!isChild[species])
            {
                PropagateDownIndex(table[species], species);
            }
        }

        for (int i = 1; i < table.Length; i++)
        {
            if (!handledIndexes.Contains(i))
            {
                PropagateDownIndex(table[i], i);
            }
        }

        void PropagateDownIndex(PersonalInfo pi, int index)
        {
            handledIndexes.Add(index);
            int baseSpec = GetBaseSpecies(index);
            if (baseSpec == 0 || baseSpec >= evos.Length) return;
            var evoList = evos[baseSpec];
            if (evoList?.PossibleEvolutions == null) return;

            int currentForm = GetFormOfIndex(index, baseSpec, table, specCount);
            foreach (var evo in evoList.PossibleEvolutions.Where(z => z != null && z.Species != 0))
            {
                var espec = evo.Species;
                var eform = evo.Form < 0 ? 0 : evo.Form;
                if (espec >= table.Length) continue;

                if (!IsValidEvolutionParent(baseSpec, currentForm, espec, eform, table))
                    continue;

                var evoIndex = table[espec].FormeIndex(espec, eform, specCount);
                if (evoIndex >= table.Length) continue;

                if (!handledIndexes.Contains(evoIndex))
                {
                    table[evoIndex].Abilities = (int[])pi.Abilities.Clone();
                    PropagateDownIndex(table[evoIndex], evoIndex);
                }
            }
        }
    }

    public bool TrulyRandomTMs { get; set; } = true;
    /// <summary>TM compatibility favors moves sharing the species' types (needs <see cref="MoveIDsTMs"/>).</summary>
    public bool PreferSameTypeTMs { get; set; }
    private int LearnTMChance => TrulyRandomTMs ? 50 : (int)LearnTMPercent;

    private void RandomizeTMHMAdvanced(PersonalInfo z)
    {
        var tms = z.TMHM;
        if (tms == null) return;

        bool CanLearn(Move move)
        {
            if (!PreferSameTypeTMs || move == null)
                return rnd.Next(0, 100) < LearnTMChance;
            // "Prefer same type": own-type TMs are likely, Normal ones even odds, the rest unlikely.
            const int NormalType = 0;
            int chance = z.Types.Contains(move.Type) ? 90 : move.Type == NormalType ? 50 : 25;
            return rnd.Next(0, 100) < chance;
        }

        if (ModifyLearnsetTM)
        {
            for (int j = 0; j < tmcount && j < tms.Length; j++)
            {
                if (MoveIDsTMs != null && j < MoveIDsTMs.Length)
                {
                    var moveID = MoveIDsTMs[j];
                    var move = Moves != null && moveID < Moves.Length ? Moves[moveID] : null;
                    tms[j] = CanLearn(move);
                }
                else
                {
                    tms[j] = rnd.Next(0, 100) < LearnTMChance;
                }
            }
        }
        if (ModifyLearnsetHM && Game.Generation == 6)
        {
            for (int j = tmcount; j < tms.Length; j++)
            {
                if (MoveIDsTMs != null && j < MoveIDsTMs.Length)
                {
                    var moveID = MoveIDsTMs[j];
                    var move = Moves != null && moveID < Moves.Length ? Moves[moveID] : null;
                    tms[j] = CanLearn(move);
                }
                else
                {
                    tms[j] = rnd.Next(0, 100) < LearnTMChance;
                }
            }
        }

        z.TMHM = tms;
    }

    private void RandomizeTMHMSimple(PersonalInfo z)
    {
        var tms = z.TMHM;
        if (tms == null) return;

        if (ModifyLearnsetTM)
        {
            for (int j = 0; j < tmcount && j < tms.Length; j++)
                tms[j] = rnd.Next(0, 100) < LearnTMChance;
        }

        if (ModifyLearnsetHM && Game.Generation == 6)
        {
            for (int j = tmcount; j < tms.Length; j++)
                tms[j] = rnd.Next(0, 100) < LearnTMChance;
        }

        z.TMHM = tms;
    }

    private void RandomizeTypeTutors(PersonalInfo z, int index)
    {
        if (z is PersonalInfoSM sm)
        {
            for (int i = 0; i < 8 && i < sm.TutorFlags.Length; i++)
                sm.TutorFlags[i] = rnd.Next(0, 100) < LearnTypeTutorPercent;
            return;
        }

        var t = z.TypeTutors;
        for (int i = 0; i < t.Length; i++)
            t[i] = rnd.Next(0, 100) < LearnTypeTutorPercent;

        // Make sure Rayquaza can learn Dragon Ascent.
        if (!Game.XY && index is 384 or 814)
            t[7] = true;

        z.TypeTutors = t;
    }

    private void RandomizeSpecialTutors(PersonalInfo z)
    {
        if (z is PersonalInfoSM sm)
        {
            for (int i = 8; i < sm.TutorFlags.Length; i++)
                sm.TutorFlags[i] = rnd.Next(0, 100) < LearnMoveTutorPercent;
            return;
        }

        var tutors = z.SpecialTutors;
        foreach (bool[] tutor in tutors)
        {
            for (int i = 0; i < tutor.Length; i++)
                tutor[i] = rnd.Next(0, 100) < LearnMoveTutorPercent;
        }

        z.SpecialTutors = tutors;
    }

    public bool MegaBSTSync { get; set; } = false;
    public int MaximumStatCap { get; set; } = 255;
    public bool UseCompetitiveAbilities { get; set; } = false;

    public void ApplyMegaBSTSync()
    {
        int specCount = MaxSpeciesID;
        for (int species = 1; species <= specCount; species++)
        {
            if (!Competitive.CompetitiveDatabase.MegaSpeciesIDs.Contains(species)) continue;
            var entry = Table[species];
            var baseEntry = entry;
            if (species == 670)
            {
                int floette5Idx = entry.FormeIndex(670, 5, specCount);
                if (floette5Idx < Table.Length && floette5Idx != 670)
                    baseEntry = Table[floette5Idx];
            }
            else if (species == 718)
            {
                int completeIdx = entry.FormeIndex(718, 4, specCount);
                if (completeIdx < Table.Length && completeIdx != 718)
                    baseEntry = Table[completeIdx];
            }

            int baseBST = baseEntry.Stats.Sum();
            int targetMegaBST = baseBST + 100;

            int[] megaFormIndices = Competitive.CompetitiveDatabase.GetMegaFormIndices(species, Table, specCount);
            if (megaFormIndices.Length == 0) continue;

            bool hasDualMegaForms = megaFormIndices.Length > 1;
            int[] physicalStats = { CompetitiveDatabase.ATK, CompetitiveDatabase.DEF };
            int[] specialStats = { CompetitiveDatabase.SPA, CompetitiveDatabase.SPD };

            for (int i = 0; i < megaFormIndices.Length; i++)
            {
                int formIndex = megaFormIndices[i];

                int[] megaStats = (int[])baseEntry.Stats.Clone();
                int currentBST = megaStats.Sum();
                int diff = targetMegaBST - currentBST;
                int attempts = 500;
                int[] biasPool = !hasDualMegaForms ? null : i == 0 ? physicalStats : specialStats;
                while (diff > 0 && attempts-- > 0)
                {
                    int statIdx = (biasPool != null && rnd.Next(100) < 80)
                        ? biasPool[rnd.Next(biasPool.Length)]
                        : rnd.Next(1, 6); // Base HP (index 0) must remain strictly invariant during Mega Evolution
                    if (megaStats[statIdx] < MaximumStatCap)
                    {
                        megaStats[statIdx]++;
                        diff--;
                    }
                }

                // Strictly enforce physical bias for Mega X (i == 0) and special bias for Mega Y (i == 1)
                if (hasDualMegaForms)
                {
                    if (i == 0 && megaStats[CompetitiveDatabase.ATK] <= megaStats[CompetitiveDatabase.SPA])
                    {
                        int higher = megaStats[CompetitiveDatabase.SPA];
                        int lower = megaStats[CompetitiveDatabase.ATK];
                        megaStats[CompetitiveDatabase.ATK] = higher;
                        megaStats[CompetitiveDatabase.SPA] = lower;
                        if (megaStats[CompetitiveDatabase.ATK] == megaStats[CompetitiveDatabase.SPA] && megaStats[CompetitiveDatabase.ATK] < MaximumStatCap)
                            megaStats[CompetitiveDatabase.ATK]++;
                    }
                    else if (i == 1 && megaStats[CompetitiveDatabase.SPA] <= megaStats[CompetitiveDatabase.ATK])
                    {
                        int higher = megaStats[CompetitiveDatabase.ATK];
                        int lower = megaStats[CompetitiveDatabase.SPA];
                        megaStats[CompetitiveDatabase.SPA] = higher;
                        megaStats[CompetitiveDatabase.ATK] = lower;
                        if (megaStats[CompetitiveDatabase.SPA] == megaStats[CompetitiveDatabase.ATK] && megaStats[CompetitiveDatabase.SPA] < MaximumStatCap)
                            megaStats[CompetitiveDatabase.SPA]++;
                    }
                }

                Table[formIndex].Stats = megaStats;

                if (ModifyTypes && ModifyTypesFollowMegas)
                {
                    int megaSecType = ForceDualTypes
                        ? GetRandomSecondaryType(baseEntry.Types[0])
                        : (rnd.Next(0, 100) < SameTypeChance ? baseEntry.Types[0] : GetRandomType());
                    Table[formIndex].Types = new[] { baseEntry.Types[0], megaSecType };
                }
            }
        }
    }

    public void ApplyStatCap()
    {
        if (MaximumStatCap >= 255) return;
        for (int i = 1; i < Table.Length; i++)
        {
            var stats = Table[i].Stats;
            if (stats == null) continue;
            for (int s = 0; s < 6; s++)
            {
                if (stats[s] > MaximumStatCap) stats[s] = MaximumStatCap;
            }
        }
    }

    private void RandomizeAbilities(PersonalInfo z, int speciesId)
    {
        var abils = z.Abilities;
        for (int i = 0; i < abils.Length; i++)
        {
            // No real species ever repeats an ability across its own slots (Ability 1/2/Hidden).
            // Each slot used to be rolled independently with nothing checking it against the
            // slots already filled, so a collision (both likely and silent under the competitive
            // bias, which draws from the same small tier list every slot) quietly shrank the
            // Pokemon to fewer effective abilities than it was meant to have. Bounded like
            // GetRandomAbility's own retry loop below, for the same reason: a heavily-banned
            // ability table must not spin forever.
            int chosen = 0;
            for (int tries = 0; tries < 64; tries++)
            {
                chosen = UseCompetitiveAbilities ? GetBiasedCompetitiveAbility(z, speciesId) : GetRandomAbility();
                bool duplicate = false;
                for (int j = 0; j < i; j++)
                {
                    if (abils[j] == chosen) { duplicate = true; break; }
                }
                if (!duplicate) break;
            }
            abils[i] = chosen;
        }
        z.Abilities = abils;
    }

    private int GetAbilityId(string name)
    {
        var abilityNames = Game.GetText(TextName.AbilityNames);
        if (abilityNames == null) return 1;
        int idx = Array.FindIndex(abilityNames, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return idx > 0 ? idx : 1;
    }

    // Whether a species has no further evolutions — used by AbilityBiasRule.FinalStageOnly.
    private bool IsFinalStageSpecies(int species)
    {
        if (Evos == null || species <= 0 || species >= Evos.Length) return true;
        var evoSet = Evos[species];
        if (evoSet?.PossibleEvolutions == null) return true;
        return !evoSet.PossibleEvolutions.Any(e => e != null && e.Species > 0 && e.Method > 0);
    }

    private int GetBiasedCompetitiveAbility(PersonalInfo z, int speciesId)
    {
        // Signature form abilities for their respective species ONLY
        if (speciesId == 681) return GetAbilityId("Stance Change");
        if (speciesId == 778) return GetAbilityId("Disguise");
        if (speciesId == 746) return GetAbilityId("Schooling");
        if (speciesId == 773) return GetAbilityId("RKS System");
        if (speciesId == 718) return GetAbilityId("Power Construct");

        var abilityNames = Game.GetText(TextName.AbilityNames);
        int[] types = z.Types;
        int atk = z.Stats[CompetitiveDatabase.ATK], spa = z.Stats[CompetitiveDatabase.SPA];
        int def = z.Stats[CompetitiveDatabase.DEF], spd = z.Stats[CompetitiveDatabase.SPD];
        int spe = z.Stats[CompetitiveDatabase.SPE], hp = z.Stats[CompetitiveDatabase.HP];
        int bst = z.Stats.Sum();
        bool finalStage = IsFinalStageSpecies(speciesId);

        // Build pre-filtered list of valid competitive ability IDs
        var validCompetitiveIds = new List<int>();
        for (int i = 1; i < abilityNames.Length && i <= Game.Info.MaxAbilityID; i++)
        {
            string aName = abilityNames[i];
            // Same bans as the plain random path: this list used to skip only the defaults, so
            // Wonder Guard and banned trapping abilities still reached competitive species.
            if (string.IsNullOrWhiteSpace(aName) || !IsAbilityAllowed(i)) continue;

            if (CompetitiveDatabase.CompetitiveAbilities.Contains(aName) ||
                CompetitiveDatabase.SituationalAbilities.Contains(aName) ||
                CompetitiveDatabase.LessCompetitiveAbilities.Contains(aName))
            {
                validCompetitiveIds.Add(i);
            }
        }

        if (validCompetitiveIds.Count == 0)
            return GetRandomAbility();

        bool PassesConstraints(string name)
        {
            // Other species' signature/form-locked abilities are never handed out at random.
            if (name.Equals("Disguise", StringComparison.OrdinalIgnoreCase) && speciesId != 778) return false;
            if (name.Equals("Stance Change", StringComparison.OrdinalIgnoreCase) && speciesId != 681) return false;
            if (name.Equals("Hunger Switch", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Equals("Gulp Missile", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Equals("Ice Face", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Equals("Schooling", StringComparison.OrdinalIgnoreCase) && speciesId != 746) return false;
            if (name.Equals("RKS System", StringComparison.OrdinalIgnoreCase) && speciesId != 773) return false;
            if (name.Equals("Power Construct", StringComparison.OrdinalIgnoreCase) && speciesId != 718) return false;
            if (name.Equals("Shields Down", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Equals("Battle Bond", StringComparison.OrdinalIgnoreCase)) return false;

            if (CompetitiveDatabase.TypeExclusiveAbilities.TryGetValue(name, out var reqTypes) &&
                !types.Any(t => reqTypes.Contains(t)))
                return false;

            // Prevent Special Attackers from rolling Physical-boosting abilities, and Physical Attackers from rolling Special-boosting abilities
            bool isPhysicalBiased = atk > spa;
            bool isSpecialBiased = spa > atk;

            if (isSpecialBiased)
            {
                if (CompetitiveDatabase.PhysicalAttackBoostingAbilities.Contains(name) ||
                    (CompetitiveDatabase.AbilityBiasRules.TryGetValue(name, out var rAtk) && rAtk.RequireHigherAtkForBias))
                    return false;
            }
            if (isPhysicalBiased)
            {
                if (CompetitiveDatabase.SpecialAttackBoostingAbilities.Contains(name) ||
                    (CompetitiveDatabase.AbilityBiasRules.TryGetValue(name, out var rSpa) && rSpa.RequireHigherSpaForBias))
                    return false;
            }

            if (!CompetitiveDatabase.AbilityBiasRules.TryGetValue(name, out var rule)) return true;

            if (rule.SpeciesLock >= 0 && speciesId != rule.SpeciesLock) return false;
            if (rule.ExcludedTypes != null && types.Any(t => rule.ExcludedTypes.Contains(t))) return false;
            if (rule.MinAtkStat >= 0 && atk < rule.MinAtkStat) return false;
            if (atk > rule.MaxAtkStat) return false;
            if (rule.MinSpaStat >= 0 && spa < rule.MinSpaStat) return false;
            if (rule.MinSpeStat >= 0 && spe < rule.MinSpeStat) return false;
            if (rule.MinDefStat >= 0 && def < rule.MinDefStat) return false;
            if (def > rule.MaxDefStat) return false;
            if (rule.MinSpdStat >= 0 && spd < rule.MinSpdStat) return false;
            if (spd > rule.MaxSpdStat) return false;
            if (rule.MinHpStat >= 0 && hp < rule.MinHpStat) return false;
            if (hp > rule.MaxHpStat) return false;
            if (bst > rule.MaxBST) return false;
            if (rule.MinAtkOrSpaStat >= 0 && Math.Max(atk, spa) < rule.MinAtkOrSpaStat) return false;
            if (rule.FinalStageOnly && !finalStage) return false;

            return true;
        }

        var eligible = validCompetitiveIds
            .Select(id => (id, name: id < abilityNames.Length ? abilityNames[id] : ""))
            .Where(x => !string.IsNullOrWhiteSpace(x.name) && PassesConstraints(x.name))
            .ToList();

        if (eligible.Count == 0)
            return validCompetitiveIds[rnd.Next(validCompetitiveIds.Count)];

        // Manifesto 0.2: Weighted preference using CompetitiveRules.Weigh
        double totalWeight = 0;
        double[] weights = new double[eligible.Count];
        for (int i = 0; i < eligible.Count; i++)
        {
            var (id, name) = eligible[i];
            int w = 1;
            if (CompetitiveDatabase.AbilityBiasRules.TryGetValue(name, out var rule))
            {
                if (rule.BiasedTypes != null && rule.BiasedTypes.Count > 0)
                    w *= CompetitiveRules.Weigh(types.Any(t => rule.BiasedTypes.Contains(t)));
                if (rule.RequireHigherAtkForBias)
                    w *= CompetitiveRules.Weigh(atk >= 90 && atk > spa);
                if (rule.RequireHigherSpaForBias)
                    w *= CompetitiveRules.Weigh(spa >= 90 && spa > atk);
            }
            if (name.Equals("Anger Shell", StringComparison.OrdinalIgnoreCase))
                w *= CompetitiveRules.Weigh(CompetitiveBuildEngine.IsFrail(z));

            weights[i] = w;
            totalWeight += w;
        }

        double roll = rnd.NextDouble() * totalWeight;
        double cum = 0;
        for (int i = 0; i < eligible.Count; i++)
        {
            cum += weights[i];
            if (roll <= cum) return eligible[i].id;
        }

        return eligible[rnd.Next(eligible.Count)].id;
    }

    private void RandomizeEggGroups(PersonalInfo z)
    {
        var egg = z.EggGroups;
        egg[0] = GetRandomEggGroup();
        egg[1] = rnd.Next(0, 100) < SameEggGroupChance ? egg[0] : GetRandomEggGroup();
        z.EggGroups = egg;
    }

    private int GetFinalEvoSpecies(int species)
    {
        if (Evos == null || species <= 0 || species >= Evos.Length) return species;
        var evoSet = Evos[species];
        if (evoSet?.PossibleEvolutions == null) return species;
        var target = evoSet.PossibleEvolutions.FirstOrDefault(e => e != null && e.Species > 0 && e.Method > 0);
        return target != null ? GetFinalEvoSpecies(target.Species) : species;
    }

    private void RandomizeHeldItems(PersonalInfo z, int speciesId)
    {
        var item = z.Items;
        var itemNames = Game.GetText(TextName.ItemNames);
        ushort GetItemId(string name)
        {
            if (itemNames == null || itemNames.Length == 0) return 0;
            int idx = Array.FindIndex(itemNames, k => Competitive.GameNameComparer.Instance.Equals(k, name));
            return idx > 0 ? (ushort)idx : (ushort)0;
        }

        string[] typeBoosters = ["Silk Scarf", "Black Belt", "Sharp Beak", "Poison Barb", "Soft Sand", "Hard Stone", "Silver Powder", "Spell Tag", "Metal Coat", "Charcoal", "Mystic Water", "Miracle Seed", "Magnet", "Twisted Spoon", "Never-Melt Ice", "Dragon Fang", "Black Glasses", "Pixie Plate"];

        int t0 = z.Types[0];
        int t1 = z.Types.Length > 1 ? z.Types[1] : t0;
        string booster0 = (t0 >= 0 && t0 < typeBoosters.Length) ? typeBoosters[t0] : "Silk Scarf";
        string booster1 = (t1 >= 0 && t1 < typeBoosters.Length) ? typeBoosters[t1] : booster0;

        // Mega Stone / Primal Orb logic (Manifesto line 296)
        ushort megaStoneId = 0;
        if (speciesId == 382) megaStoneId = GetItemId("Blue Orb");
        else if (speciesId == 383) megaStoneId = GetItemId("Red Orb");
        else if (speciesId != 384 && CompetitiveDatabase.MegaStoneMap.TryGetValue(speciesId, out var stones))
        {
            string chosenStone = stones[rnd.Next(stones.Length)];
            megaStoneId = GetItemId(chosenStone);
        }

        // Slot 0 (50% slot): 50% Mega Stone if Mega-capable, else 50% type booster
        if (megaStoneId > 0 && rnd.Next(100) < 50)
        {
            item[0] = megaStoneId;
        }
        else if (rnd.Next(100) < 50)
        {
            string chosenBooster = (t0 != t1 && rnd.Next(100) < 50) ? booster1 : booster0;
            item[0] = GetItemId(chosenBooster);
        }
        else
        {
            item[0] = GetRandomHeldItem();
        }

        // Slot 1 (5% slot): Check pre-evolution of Mega species (5% chance)
        ushort evoMegaStoneId = 0;
        int finalSpec = IsFinalStageSpecies(speciesId) ? speciesId : (Evos != null && speciesId < Evos.Length ? GetFinalEvoSpecies(speciesId) : speciesId);
        if (finalSpec != speciesId && CompetitiveDatabase.MegaStoneMap.TryGetValue(finalSpec, out var evoStones))
        {
            evoMegaStoneId = GetItemId(evoStones[rnd.Next(evoStones.Length)]);
        }

        if (evoMegaStoneId > 0 && rnd.Next(100) < 5)
        {
            item[1] = evoMegaStoneId;
        }
        else if (rnd.Next(100) < 50)
        {
            string chosenBooster = (t0 != t1 && rnd.Next(100) < 50) ? booster1 : booster0;
            item[1] = GetItemId(chosenBooster);
        }
        else
        {
            item[1] = GetRandomHeldItem();
        }

        item[2] = GetRandomHeldItem();
        z.Items = item;
    }

    private void RandomizeTypes(PersonalInfo z)
    {
        var t = z.Types;
        t[0] = GetRandomType();
        t[1] = ForceDualTypes
            ? GetRandomSecondaryType(t[0])
            : (rnd.Next(0, 100) < SameTypeChance ? t[0] : GetRandomType());
        z.Types = t;
    }

    private void RandomizeStatsBSTPreserving(PersonalInfo z)
    {
        var stats = z.Stats;
        if (stats == null || stats[0] == 1) // Shedinja or invalid
            return;

        int origBST = stats.Sum();
        if (origBST <= 0) return;

        int fixedBST = 0;
        int randomizeCount = 0;
        for (int i = 0; i < 6; i++)
        {
            if (!StatsToRandomize[i])
                fixedBST += stats[i];
            else
                randomizeCount++;
        }

        if (randomizeCount == 0) return;

        int targetTotalBST = origBST;
        if (StatDeviation > 0)
        {
            double varianceFactor = 1.0 + ((rnd.NextDouble() * 2.0 - 1.0) * ((double)StatDeviation / 100.0));
            targetTotalBST = Math.Clamp((int)Math.Round(origBST * varianceFactor), Math.Max(30, fixedBST + randomizeCount * 5), 6 * 255);
        }

        int targetBSTForRandom = Math.Max(targetTotalBST - fixedBST, randomizeCount * 5);

        // Generate random positive weights only across randomized stats
        double[] weights = new double[6];
        double totalWeight = 0;
        for (int i = 0; i < 6; i++)
        {
            if (StatsToRandomize[i])
            {
                // Generate a weighted value centered around 1.0 with variance
                weights[i] = 0.5 + (rnd.NextDouble() * 1.5);
                totalWeight += weights[i];
            }
        }

        int[] newStats = new int[6];
        int allocatedBST = 0;
        for (int i = 0; i < 6; i++)
        {
            if (StatsToRandomize[i])
            {
                int val = (int)Math.Round((weights[i] / totalWeight) * targetBSTForRandom);
                newStats[i] = Math.Clamp(val, 5, 255);
            }
            else
            {
                newStats[i] = stats[i];
            }
            allocatedBST += newStats[i];
        }

        // Adjust for any rounding discrepancy so total BST matches targetTotalBST exactly
        int diff = targetTotalBST - allocatedBST;
        int maxAttempts = 500;
        while (diff != 0 && maxAttempts-- > 0)
        {
            int idx = rnd.Next(0, 6);
            if (!StatsToRandomize[idx]) continue;
            if (diff > 0 && newStats[idx] < 255)
            {
                newStats[idx]++;
                diff--;
            }
            else if (diff < 0 && newStats[idx] > 5)
            {
                newStats[idx]--;
                diff++;
            }
        }

        z.Stats = newStats;
    }

    private void RandomizeStats(PersonalInfo z)
    {
        // Fiddle with Base Stats, don't muck with Shedinja.
        var stats = z.Stats;
        if (stats[0] == 1)
            return;
        for (int i = 0; i < stats.Length; i++)
        {
            if (!StatsToRandomize[i])
                continue;
            var l = Math.Min(255, (int)(stats[i] * (1 - (StatDeviation / 100))));
            var h = Math.Min(255, (int)(stats[i] * (1 + (StatDeviation / 100))));
            stats[i] = Math.Max(5, rnd.Next(l, h + 1));
        }
        z.Stats = stats;
    }

    private static void RandomShuffledStats(PersonalInfo z)
    {
        // Fiddle with Base Stats, don't muck with Shedinja.
        var stats = z.Stats;
        if (stats[0] == 1)
            return;

        Util.Shuffle(stats);
        z.Stats = stats;
    }

    private int GetRandomType() => rnd.Next(0, TypeCount > 0 ? TypeCount : 18);

    private int GetRandomSecondaryType(int primaryType)
    {
        int maxTypes = TypeCount > 0 ? TypeCount : 18;
        if (maxTypes <= 1) return primaryType;
        int t1;
        do
        {
            t1 = rnd.Next(0, maxTypes);
        } while (t1 == primaryType);
        return t1;
    }

    public void ApplyForceDualTypes(PersonalInfo[] table)
    {
        if (table == null || table.Length <= 1) return;
        var evos = Evos;
        int specCount = Math.Min(MaxSpeciesID, table.Length - 1);

        // If evolutionary inheritance is active and evos are available, propagate down evolution lines first
        // so that pre-evolutions and their evolutions share secondary types consistently.
        if (evos != null)
        {
            var isChild = new bool[table.Length];
            for (int i = 1; i <= specCount && i < evos.Length; i++)
            {
                if (evos[i]?.PossibleEvolutions == null) continue;
                foreach (var evo in evos[i].PossibleEvolutions.Where(z => z != null && z.Species != 0))
                {
                    if (evo.Species >= table.Length || table[evo.Species] == null) continue;
                    int evoIndex = table[evo.Species].FormeIndex(evo.Species, evo.Form < 0 ? 0 : evo.Form, specCount);
                    if (evoIndex < table.Length)
                        isChild[evoIndex] = true;
                }
            }

            var visited = new HashSet<int>();
            for (int s = 1; s <= specCount; s++)
            {
                if (!isChild[s] && table[s] != null)
                {
                    EnsureDualType(table[s]);
                    PropagateDualTypeDown(s, table[s]);
                }
            }

            void PropagateDualTypeDown(int parentIdx, PersonalInfo parentEntry)
            {
                visited.Add(parentIdx);
                int baseSpec = parentIdx <= specCount ? parentIdx : 0;
                if (baseSpec == 0 || baseSpec >= evos.Length) return;
                var evoList = evos[baseSpec];
                if (evoList?.PossibleEvolutions == null) return;

                int curForm = GetFormOfIndex(parentIdx, baseSpec, table, specCount);
                foreach (var evo in evoList.PossibleEvolutions.Where(z => z != null && z.Species != 0))
                {
                    int espec = evo.Species;
                    int eform = evo.Form < 0 ? 0 : evo.Form;
                    if (espec >= table.Length || table[espec] == null) continue;
                    if (!IsValidEvolutionParent(baseSpec, curForm, espec, eform, table)) continue;

                    int evoIdx = table[espec].FormeIndex(espec, eform, specCount);
                    if (evoIdx >= table.Length || table[evoIdx] == null || visited.Contains(evoIdx)) continue;

                    var evoEntry = table[evoIdx];
                    if (evoEntry.Types != null && evoEntry.Types.Length >= 2 && evoEntry.Types[0] == evoEntry.Types[1])
                    {
                        int inheritSec = parentEntry.Types != null && parentEntry.Types.Length >= 2 ? parentEntry.Types[1] : parentEntry.Types[0];
                        if (inheritSec != evoEntry.Types[0])
                        {
                            evoEntry.Types = new[] { evoEntry.Types[0], inheritSec };
                        }
                        else
                        {
                            evoEntry.Types = new[] { evoEntry.Types[0], GetRandomSecondaryType(evoEntry.Types[0]) };
                        }
                    }
                    else
                    {
                        EnsureDualType(evoEntry);
                    }

                    PropagateDualTypeDown(evoIdx, evoEntry);
                }
            }
        }

        if (ModifyTypesFollowMegas)
        {
            for (int species = 1; species <= specCount; species++)
            {
                if (species >= table.Length) break;
                var entry = table[species];
                if (entry == null || entry.FormeCount <= 1) continue;

                var megaIndices = CompetitiveDatabase.GetMegaFormIndices(species, table, specCount);
                foreach (int megaIdx in megaIndices)
                {
                    if (megaIdx < table.Length && megaIdx != species && table[megaIdx] != null)
                    {
                        var megaEntry = table[megaIdx];
                        if (megaEntry.Types != null && megaEntry.Types.Length >= 2 && megaEntry.Types[0] == megaEntry.Types[1])
                        {
                            int inheritSec = entry.Types != null && entry.Types.Length >= 2 ? entry.Types[1] : entry.Types[0];
                            if (inheritSec != megaEntry.Types[0])
                            {
                                megaEntry.Types = new[] { megaEntry.Types[0], inheritSec };
                            }
                            else
                            {
                                megaEntry.Types = new[] { megaEntry.Types[0], GetRandomSecondaryType(megaEntry.Types[0]) };
                            }
                        }
                    }
                }
            }
        }

        // Final exhaustive sweep over EVERY single entry in table
        for (int i = 1; i < table.Length; i++)
        {
            if (table[i] == null) continue;
            EnsureDualType(table[i]);
        }
    }

    private void EnsureDualType(PersonalInfo pi)
    {
        if (pi?.Types == null || pi.Types.Length < 2) return;
        if (pi.Types[0] == pi.Types[1])
        {
            pi.Types = new[] { pi.Types[0], GetRandomSecondaryType(pi.Types[0]) };
        }
    }

    private int GetRandomEggGroup() => rnd.Next(1, eggGroupCount);
    /// <summary>
    /// A wild held item. Never a Z-Crystal.
    /// </summary>
    private int GetRandomHeldItem()
    {
        var pool = Game.Info.HeldItems;
        ZItems ??= new ZCrystalFilter(Game.GetText(TextName.ItemNames));
        return ZItems.PickAllowed(() => pool[rnd.Next(1, pool.Length)]);
    }

    private ZCrystalFilter ZItems;
    private readonly IReadOnlyList<int> BannedAbilities = [];

    /// <summary>
    /// The user's ban toggles, resolved to ids. Null means nothing extra is banned.
    /// </summary>
    public AbilityBanList BanList { get; set; }

    /// <summary>Every user ban at once: Wonder Guard toggle, the ban list, and the defaults.</summary>
    private bool IsAbilityAllowed(int id)
    {
        const int WonderGuard = 25;
        if (id == WonderGuard && !AllowWonderGuard) return false;
        if (BannedAbilities.Contains(id) || DefaultBannedAbilities.Contains(id)) return false;
        return BanList?.IsBanned(id) != true;
    }

    private int GetRandomAbility()
    {
        int newabil;

        // Bounded rather than unbounded: banning enough categories on a ROM with a small ability
        // table could otherwise leave nothing to draw and spin here forever.
        for (int tries = 0; tries < 512; tries++)
        {
            newabil = rnd.Next(1, Game.Info.MaxAbilityID + 1);
            if (IsAbilityAllowed(newabil)) return newabil;
        }

        // Nothing satisfied every ban; fall back to the un-banned rule so a Pokemon still gets an
        // ability rather than a zero.
        do newabil = rnd.Next(1, Game.Info.MaxAbilityID + 1);
        while (DefaultBannedAbilities.Contains(newabil));
        return newabil;
    }

    public void ApplyStatFloors()
    {
        if ((!ModifyStats && !ShuffleStats) || !EnforceMinimumBST)
            return;

        int[] minBst = ComputeMinBstTable(Table, Evos);
        for (int i = 1; i < Table.Length; i++)
        {
            EnforceStatFloor(Table[i], minBst[i]);
            EnforceStatCeiling(Table[i], minBst[i]);
        }
    }

    private static void EnforceStatCeiling(PersonalInfo pi, int floorBst)
    {
        if (floorBst <= 0) return;
        int[] stats = pi.Stats;
        if (stats == null || stats[0] == 1) return;

        int maxBst = (int)(floorBst * 1.35); // 35% above floor BST ceiling
        int currentBst = stats.Sum();
        if (currentBst <= maxBst) return;

        double ratio = (double)maxBst / currentBst;
        int[] newStats = new int[6];
        for (int i = 0; i < 6; i++)
        {
            newStats[i] = Math.Clamp((int)Math.Round(stats[i] * ratio), 5, 255);
        }
        pi.Stats = newStats;
    }

    private int[] ComputeMinBstTable(PersonalInfo[] table, EvolutionSet[] evos)
    {
        int specCount = MaxSpeciesID;
        int[] minBst = new int[table.Length];

        var legendarySet = new HashSet<int>(Legal.Legendary_USUM.Concat(Legal.Mythical_USUM));

        var children = new Dictionary<int, List<int>>();
        var parents = new Dictionary<int, List<int>>();
        for (int i = 1; i <= specCount; i++)
        {
            children[i] = [];
            parents[i] = [];
        }

        for (int i = 1; i <= specCount; i++)
        {
            if (i >= evos.Length || evos[i]?.PossibleEvolutions == null) continue;
            foreach (var evo in evos[i].PossibleEvolutions.Where(z => z != null && z.Species != 0))
            {
                int targetSpec = evo.Species;
                if (targetSpec >= 1 && targetSpec <= specCount)
                {
                    if (!children[i].Contains(targetSpec)) children[i].Add(targetSpec);
                    if (!parents[targetSpec].Contains(i)) parents[targetSpec].Add(i);
                }
            }
        }

        for (int s = 1; s <= specCount; s++)
        {
            if (legendarySet.Contains(s))
            {
                minBst[s] = MinBSTLegendary;
                continue;
            }

            int stage = 1;
            if (parents[s].Count > 0)
            {
                bool parentHasParent = parents[s].Any(p => parents[p].Count > 0);
                stage = parentHasParent ? 3 : 2;
            }

            int maxDepth = stage;
            if (stage == 1)
            {
                if (children[s].Count > 0)
                {
                    bool anyGrandchild = children[s].Any(c => children[c].Count > 0);
                    maxDepth = anyGrandchild ? 3 : 2;
                }
                else
                {
                    maxDepth = 1;
                }
            }
            else if (stage == 2)
            {
                bool anyChild = children[s].Count > 0;
                maxDepth = anyChild ? 3 : 2;
            }
            else
            {
                maxDepth = 3;
            }

            if (maxDepth == 3)
            {
                minBst[s] = stage switch
                {
                    1 => MinBST3Stage1,
                    2 => MinBST3Stage2,
                    _ => MinBST3Stage3
                };
            }
            else if (maxDepth == 2)
            {
                minBst[s] = stage switch
                {
                    1 => MinBST2Stage1,
                    _ => MinBST2Stage2
                };
            }
            else
            {
                minBst[s] = MinBST1Stage;
            }
        }

        for (int s = 1; s <= specCount; s++)
        {
            if (s >= table.Length) break;
            int fc = table[s].FormeCount;
            for (int f = 1; f < fc; f++)
            {
                int formIdx = table[s].FormeIndex(s, f, specCount);
                if (formIdx < table.Length && formIdx != s)
                {
                    if (legendarySet.Contains(s) || legendarySet.Contains(formIdx))
                        minBst[formIdx] = (s == 718 && f == 4) ? Math.Max(MinBSTLegendary, 708) : MinBSTLegendary;
                    else if (s == 670 && f == 5) // Floette 5 (Eternal Floette - 551 native BST)
                        minBst[formIdx] = Math.Max(minBst[s], MinBST3Stage3);
                    else if (s == 670 && f == 6) // Floette 6 (Mega Floette)
                        minBst[formIdx] = Math.Max(minBst[s], MinBST3Stage3) + 100;
                    else
                        minBst[formIdx] = minBst[s];
                }
            }
        }

        foreach (var kvp in ComputeMegaFormeMinimumBst(table))
        {
            if (kvp.Key < minBst.Length)
                minBst[kvp.Key] = Math.Max(minBst[kvp.Key], kvp.Value);
        }

        return minBst;
    }

    private static void EnforceStatFloor(PersonalInfo pi, int floorBst)
    {
        if (floorBst <= 0) return;
        int[] stats = pi.Stats;
        if (stats == null || stats[0] == 1) return;

        int currentBst = stats.Sum();
        if (currentBst >= floorBst) return;

        double ratio = (double)floorBst / currentBst;
        int[] newStats = new int[6];
        for (int i = 0; i < 6; i++)
        {
            newStats[i] = Math.Clamp((int)Math.Round(stats[i] * ratio), 5, 255);
        }
        int diff = floorBst - newStats.Sum();
        int maxAttempts = 100;
        while (diff > 0 && maxAttempts-- > 0)
        {
            int idx = Util.Rand.Next(0, 6);
            if (newStats[idx] < 255)
            {
                newStats[idx]++;
                diff--;
            }
        }
        pi.Stats = newStats;
    }

    public void ApplyMaximumBST()
    {
        if ((!ModifyStats && !ShuffleStats) || !EnforceMaximumBST)
            return;

        int[] maxBst = ComputeMaxBstTable(Table, Evos);
        // A ceiling must never contradict an enabled floor for the same species — reconcile so
        // the floor always wins if the two happen to be misconfigured against each other.
        int[] minBst = EnforceMinimumBST ? ComputeMinBstTable(Table, Evos) : null;
        for (int i = 1; i < Table.Length; i++)
        {
            int ceiling = minBst != null ? Math.Max(maxBst[i], minBst[i]) : maxBst[i];
            EnforceStatCeilingToTarget(Table[i], ceiling);
        }
    }

    private int[] ComputeMaxBstTable(PersonalInfo[] table, EvolutionSet[] evos)
    {
        int specCount = MaxSpeciesID;
        int[] maxBst = new int[table.Length];

        var legendarySet = new HashSet<int>(Legal.Legendary_USUM.Concat(Legal.Mythical_USUM));

        var children = new Dictionary<int, List<int>>();
        var parents = new Dictionary<int, List<int>>();
        for (int i = 1; i <= specCount; i++)
        {
            children[i] = [];
            parents[i] = [];
        }

        for (int i = 1; i <= specCount; i++)
        {
            if (i >= evos.Length || evos[i]?.PossibleEvolutions == null) continue;
            foreach (var evo in evos[i].PossibleEvolutions.Where(z => z != null && z.Species != 0))
            {
                int targetSpec = evo.Species;
                if (targetSpec >= 1 && targetSpec <= specCount)
                {
                    if (!children[i].Contains(targetSpec)) children[i].Add(targetSpec);
                    if (!parents[targetSpec].Contains(i)) parents[targetSpec].Add(i);
                }
            }
        }

        for (int s = 1; s <= specCount; s++)
        {
            if (legendarySet.Contains(s))
            {
                maxBst[s] = MaxBSTLegendary;
                continue;
            }

            int stage = 1;
            if (parents[s].Count > 0)
            {
                bool parentHasParent = parents[s].Any(p => parents[p].Count > 0);
                stage = parentHasParent ? 3 : 2;
            }

            int maxDepth = stage;
            if (stage == 1)
            {
                if (children[s].Count > 0)
                {
                    bool anyGrandchild = children[s].Any(c => children[c].Count > 0);
                    maxDepth = anyGrandchild ? 3 : 2;
                }
                else
                {
                    maxDepth = 1;
                }
            }
            else if (stage == 2)
            {
                bool anyChild = children[s].Count > 0;
                maxDepth = anyChild ? 3 : 2;
            }
            else
            {
                maxDepth = 3;
            }

            if (maxDepth == 3)
            {
                maxBst[s] = stage switch
                {
                    1 => MaxBST3Stage1,
                    2 => MaxBST3Stage2,
                    _ => MaxBST3Stage3
                };
            }
            else if (maxDepth == 2)
            {
                maxBst[s] = stage switch
                {
                    1 => MaxBST2Stage1,
                    _ => MaxBST2Stage2
                };
            }
            else
            {
                maxBst[s] = MaxBST1Stage;
            }
        }

        for (int s = 1; s <= specCount; s++)
        {
            if (s >= table.Length) break;
            int fc = table[s].FormeCount;
            for (int f = 1; f < fc; f++)
            {
                int formIdx = table[s].FormeIndex(s, f, specCount);
                if (formIdx < table.Length && formIdx != s)
                {
                    if (legendarySet.Contains(s) || legendarySet.Contains(formIdx))
                        maxBst[formIdx] = (s == 718 && f == 4) ? Math.Max(MaxBSTLegendary, 708) : MaxBSTLegendary;
                    else if (s == 670 && f == 5) // Floette 5 (Eternal Floette - 551 native BST)
                        maxBst[formIdx] = Math.Max(maxBst[s], MaxBST3Stage3);
                    else if (s == 670 && f == 6) // Floette 6 (Mega Floette)
                        maxBst[formIdx] = Math.Max(maxBst[s], MaxBST3Stage3) + 100;
                    else
                        maxBst[formIdx] = maxBst[s];
                }
            }
        }

        foreach (var kvp in ComputeMegaFormeMinimumBst(table))
        {
            if (kvp.Key < maxBst.Length)
                maxBst[kvp.Key] = Math.Max(maxBst[kvp.Key], kvp.Value);
        }

        return maxBst;
    }

    private Dictionary<int, int> ComputeMegaFormeMinimumBst(PersonalInfo[] table)
    {
        var result = new Dictionary<int, int>();
        int specCount = MaxSpeciesID;
        foreach (int species in CompetitiveDatabase.MegaSpeciesIDs)
        {
            if (species <= 0 || species >= table.Length) continue;
            var entry = table[species];
            if (species == 670)
            {
                int floette5Idx = entry.FormeIndex(670, 5, specCount);
                if (floette5Idx < table.Length && floette5Idx != 670)
                    entry = table[floette5Idx];
            }
            else if (species == 718)
            {
                int completeIdx = entry.FormeIndex(718, 4, specCount);
                if (completeIdx < table.Length && completeIdx != 718)
                    entry = table[completeIdx];
            }

            int[] baseStats = entry.Stats;
            if (baseStats == null || baseStats[0] == 1) continue;

            int targetMegaBst = baseStats.Sum() + 100;
            foreach (int formIdx in CompetitiveDatabase.GetMegaFormIndices(species, table, specCount))
                result[formIdx] = targetMegaBst;
        }
        return result;
    }

    private HashSet<int> ComputeLegendaryFormeIndices()
    {
        var legendarySet = new HashSet<int>(Legal.Legendary_USUM.Concat(Legal.Mythical_USUM));
        var result = new HashSet<int>(legendarySet);
        int specCount = MaxSpeciesID;
        for (int s = 1; s <= specCount; s++)
        {
            if (s >= Table.Length) break;
            int fc = Table[s].FormeCount;
            for (int f = 1; f < fc; f++)
            {
                int formIdx = Table[s].FormeIndex(s, f, specCount);
                if (formIdx < Table.Length && formIdx != s && (legendarySet.Contains(s) || legendarySet.Contains(formIdx)))
                    result.Add(formIdx);
            }
        }
        return result;
    }

    private static void EnforceStatCeilingToTarget(PersonalInfo pi, int ceilingBst)
    {
        if (ceilingBst <= 0) return;
        int[] stats = pi.Stats;
        if (stats == null || stats[0] == 1) return;

        int currentBst = stats.Sum();
        if (currentBst <= ceilingBst) return;

        double ratio = (double)ceilingBst / currentBst;
        int[] newStats = new int[6];
        for (int i = 0; i < 6; i++)
        {
            newStats[i] = Math.Clamp((int)Math.Round(stats[i] * ratio), 5, 255);
        }
        int diff = newStats.Sum() - ceilingBst;
        int maxAttempts = 100;
        while (diff > 0 && maxAttempts-- > 0)
        {
            int idx = Util.Rand.Next(0, 6);
            if (newStats[idx] > 5)
            {
                newStats[idx]--;
                diff--;
            }
        }
        pi.Stats = newStats;
    }

    public void ApplyNoEgregiousStats()
    {
        if (!NoEgregiousStats) return;

        var legendaryFormeIndices = ComputeLegendaryFormeIndices();
        var megaFormeMinBst = ComputeMegaFormeMinimumBst(Table);
        // A cap must never contradict an enabled Minimum BST Floor for the same species.
        bool floorActive = (ModifyStats || ShuffleStats) && EnforceMinimumBST;
        int[] minBst = floorActive ? ComputeMinBstTable(Table, Evos) : null;

        for (int i = 1; i < Table.Length; i++)
        {
            int[] stats = Table[i].Stats;
            if (stats == null || stats[0] == 1) continue;

            bool changed = false;
            for (int s = 0; s < stats.Length; s++)
            {
                if (stats[s] > NoEgregiousStatsSingleCap)
                {
                    stats[s] = NoEgregiousStatsSingleCap;
                    changed = true;
                }
            }

            int cap = legendaryFormeIndices.Contains(i) ? NoEgregiousStatsBSTCapLegendary : NoEgregiousStatsBSTCapRegular;
            // Mega Evolution forms must never be capped below their required base+100 BST sync
            // (see ApplyMegaBSTSync) — that addition is "enforced," so it wins over this cap.
            if (megaFormeMinBst.TryGetValue(i, out int megaFloor))
                cap = Math.Max(cap, megaFloor);
            if (minBst != null && i < minBst.Length)
                cap = Math.Max(cap, minBst[i]);

            int currentBst = stats.Sum();
            if (currentBst > cap)
            {
                double ratio = (double)cap / currentBst;
                for (int s = 0; s < stats.Length; s++)
                    stats[s] = Math.Clamp((int)Math.Round(stats[s] * ratio), 5, NoEgregiousStatsSingleCap);
                int diff = stats.Sum() - cap;
                int maxAttempts = 100;
                while (diff > 0 && maxAttempts-- > 0)
                {
                    int idx = Util.Rand.Next(0, 6);
                    if (stats[idx] > 5)
                    {
                        stats[idx]--;
                        diff--;
                    }
                }
                changed = true;
            }

            if (changed) Table[i].Stats = stats;
        }
    }

    public void ApplyAvoidMinmaxing()
    {
        if (!AvoidMinmaxing) return;

        int specCount = MaxSpeciesID;
        var megaParents = new Dictionary<int, PersonalInfo>();
        foreach (int species in CompetitiveDatabase.MegaSpeciesIDs)
        {
            if (species <= 0 || species >= Table.Length) continue;
            var entry = Table[species];
            var parentEntry = entry;
            if (species == 670)
            {
                int floette5Idx = entry.FormeIndex(670, 5, specCount);
                if (floette5Idx < Table.Length && floette5Idx != 670)
                    parentEntry = Table[floette5Idx];
            }
            else if (species == 718)
            {
                int completeIdx = entry.FormeIndex(718, 4, specCount);
                if (completeIdx < Table.Length && completeIdx != 718)
                    parentEntry = Table[completeIdx];
            }

            foreach (int megaIdx in CompetitiveDatabase.GetMegaFormIndices(species, Table, specCount))
            {
                if (megaIdx < Table.Length && megaIdx != species)
                    megaParents[megaIdx] = parentEntry;
            }
        }

        for (int i = 1; i < Table.Length; i++)
        {
            if (megaParents.ContainsKey(i)) continue;
            int[] stats = Table[i].Stats;
            if (stats == null || stats[0] == 1) continue;
            SmoothStatSpread(stats);
            Table[i].Stats = stats;
        }

        foreach (var (megaIdx, parentEntry) in megaParents)
        {
            int[] stats = Table[megaIdx].Stats;
            if (stats == null || stats[0] == 1) continue;
            int fixedHp = parentEntry.Stats[0];
            SmoothStatSpread(stats, fixedHp);
            Table[megaIdx].Stats = stats;
        }
    }

    private static void SmoothStatSpread(int[] stats, int fixedHp = -1)
    {
        int total = stats.Sum();
        if (total <= 0) return;

        if (fixedHp >= 0)
        {
            stats[0] = fixedHp;
            int nonHpTotal = total - fixedHp;
            if (nonHpTotal <= 0) return;
            double avg = nonHpTotal / 5.0;
            double maxDeviation = avg * 0.75;
            int[] target = new int[6];
            target[0] = fixedHp;
            for (int s = 1; s < 6; s++)
            {
                double delta = stats[s] - avg;
                double clampedDelta = Math.Clamp(delta, -maxDeviation, maxDeviation);
                target[s] = Math.Clamp((int)Math.Round(avg + clampedDelta), 5, 255);
            }

            int diff = total - target.Sum();
            int attempts = 200;
            while (diff != 0 && attempts-- > 0)
            {
                int idx = Util.Rand.Next(1, 6);
                if (diff > 0 && target[idx] < 255) { target[idx]++; diff--; }
                else if (diff < 0 && target[idx] > 5) { target[idx]--; diff++; }
            }

            Array.Copy(target, stats, 6);
            return;
        }

        double fullAvg = total / 6.0;
        double fullMaxDeviation = fullAvg * 0.75;
        int[] fullTarget = new int[6];
        for (int s = 0; s < 6; s++)
        {
            double delta = stats[s] - fullAvg;
            double clampedDelta = Math.Clamp(delta, -fullMaxDeviation, fullMaxDeviation);
            fullTarget[s] = Math.Clamp((int)Math.Round(fullAvg + clampedDelta), 5, 255);
        }

        int fullDiff = total - fullTarget.Sum();
        int fullAttempts = 200;
        while (fullDiff != 0 && fullAttempts-- > 0)
        {
            int idx = Util.Rand.Next(6);
            if (fullDiff > 0 && fullTarget[idx] < 255) { fullTarget[idx]++; fullDiff--; }
            else if (fullDiff < 0 && fullTarget[idx] > 5) { fullTarget[idx]--; fullDiff++; }
        }

        Array.Copy(fullTarget, stats, 6);
    }
}
