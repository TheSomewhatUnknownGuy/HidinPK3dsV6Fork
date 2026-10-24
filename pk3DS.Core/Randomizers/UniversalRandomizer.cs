using System;
using System.Collections.Generic;
using System.IO;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using pk3DS.Core.CTR;
using pk3DS.Core.Randomizers.Competitive;

namespace pk3DS.Core.Randomizers;

public class UniversalRandomizer
{
    private readonly GameConfig Config;
    private readonly UniversalSettings Settings;
    public string RomFSPath { get; set; }

    private AbilityBanList AbilityBans { get; set; }

    private byte[] _codeBin;
    private string _codeBinPath;
    private bool _codeBinDirty;

    // TM expansion whose item data is written once its code.bin is (see SyncCodeBinToDisk / FlushCodeBin).
    private ushort[] _pendingTMItemIds;
    private ushort[] _pendingTMMoves;
    private int _pendingTMFirstSlot;

    public UniversalRandomizer(GameConfig config, UniversalSettings settings, string romfsPath = "")
    {
        Config = config;
        Settings = settings;
        RomFSPath = string.IsNullOrWhiteSpace(romfsPath) ? (config?.RomFS ?? "") : romfsPath;
        AbilityBans = new AbilityBanList(config?.GetText(TextName.AbilityNames), settings);
    }

    private bool EnsureCodeBin()
    {
        if (_codeBin != null) return true;
        _codeBinPath = pk3DS.Core.CTR.ExeFS.ResolveCodeBin(Config?.ExeFS);
        if (string.IsNullOrEmpty(_codeBinPath) || !File.Exists(_codeBinPath)) return false;
        if (pk3DS.Core.Modding.UhuruGuard.RefusalFor(_codeBinPath) != null) return false; // Uhuru: every step that patches code.bin is skipped
        _codeBin = File.ReadAllBytes(_codeBinPath);
        _codeBinDirty = false;
        return _codeBin != null && _codeBin.Length > 0;
    }

    private void SyncCodeBinToDisk()
    {
        if (_codeBin != null && _codeBinDirty && !string.IsNullOrEmpty(_codeBinPath) && pk3DS.Core.Modding.UhuruGuard.RefusalFor(_codeBinPath) == null)
        {
            File.WriteAllBytes(_codeBinPath, _codeBin);
            _codeBinDirty = false;
            WritePendingTMItems();
        }
    }

    private void WritePendingTMItems()
    {
        if (_pendingTMItemIds == null) return;
        try
        {
            pk3DS.Core.Modding.Research.ExpandedTMItems.WriteItemData(Config, _pendingTMItemIds, _pendingTMMoves, _pendingTMFirstSlot);
        }
        catch (Exception ex)
        {
            LastTMError = "TM item names/descriptions not written: " + ex.Message;
        }
        _pendingTMItemIds = null;
        _pendingTMMoves = null;
        _pendingTMFirstSlot = 0;
    }

    private void ReloadCodeBinFromDisk()
    {
        // Uhuru: no shared buffer, so the code.bin steps after a package install stay skipped (and say so) instead of
        // patching a buffer FlushCodeBin would drop.
        if (!string.IsNullOrEmpty(_codeBinPath) && File.Exists(_codeBinPath) && pk3DS.Core.Modding.UhuruGuard.RefusalFor(_codeBinPath) == null)
        {
            _codeBin = File.ReadAllBytes(_codeBinPath);
            _codeBinDirty = false;
        }
    }

    private void FlushCodeBin()
    {
        if (_codeBin != null && _codeBinDirty && !string.IsNullOrEmpty(_codeBinPath) && pk3DS.Core.Modding.UhuruGuard.RefusalFor(_codeBinPath) == null)
        {
            File.WriteAllBytes(_codeBinPath, _codeBin);
            WritePendingTMItems();
        }
        _codeBin = null;
        _codeBinDirty = false;
    }

    public void Execute(Action<string, int> progressCallback = null, Action<SpeciesRandomizer, FormRandomizer> wildRandomizerAction = null, Action<SpeciesRandomizer, FormRandomizer> staticsRandomizerAction = null)
    {
        // Per-run TM state: an error or pending item data from an earlier run must not be reported or written again.
        LastTMError = null;
        _pendingTMItemIds = null;
        _pendingTMMoves = null;
        _pendingTMFirstSlot = 0;

        // 1. Initialize Seed
        int seed = GenerateSeed(Settings.Seed);
        Util.ReseedRand(seed);
        progressCallback?.Invoke("Initializing RNG seed and pool filters...", 5);

        bool applyExpansionEvos = Config.MaxSpeciesID >= 1025;
        if (applyExpansionEvos)
        {
            int added = ExpansionPackEvolutions.ApplyAndSave(Config);
            if (added > 0)
                progressCallback?.Invoke($"Added {added} missing Expansion Pack evolution method(s).", 6);
        }

        // 2. Setup Species Pool (Supports Expansion Pack 808-1025!)
        var speciesRand = new SpeciesRandomizer(Config)
        {
            G1 = Settings.Gen1,
            G2 = Settings.Gen2,
            G3 = Settings.Gen3,
            G4 = Settings.Gen4,
            G5 = Settings.Gen5,
            G6 = Settings.Gen6,
            G7 = Settings.Gen7,
            G8 = Settings.Gen8,
            G9 = Settings.Gen9,

            L = Settings.AllowLegendaries,
            E = Settings.AllowMythicals,
            Shedinja = Settings.AllowShedinja,

            rBST = Settings.MatchBST,
            rType = Settings.MatchType,
            rEXP = Settings.MatchEXP
        };
        speciesRand.Initialize();
        speciesRand.GenerateGlobalMapping(Config.Evolutions);

        // 2b. TM move table. Settled before personal data so TM compatibility below is rolled against
        // the moves the TMs will actually teach ("prefer same type" needs their types). With TM moves
        // unchanged this only reads the vanilla table.
        EnsureCodeBin();
        ushort[] tmMoveListForInjection = null;
        // The MaxSpeciesID term is there so trainers get TM moves on Expansion and Uhuru ROMs; it only reads the table.
        if (Settings.TMHMMovesMode > 0 || Settings.CompetitiveRandomizer || Settings.ExpandTMs ||
            Config.MaxSpeciesID >= 1025 || Settings.TMHMCompatibilityMode == 1)
        {
            if (Settings.TMHMMovesMode > 0)
                progressCallback?.Invoke("Randomizing TM move table...", 15);
            tmMoveListForInjection = RandomizeTMMoveTable();
            if (Settings.TMHMMovesMode > 0 && pk3DS.Core.Modding.UhuruGuard.Active)
                progressCallback?.Invoke(tmMoveListForInjection != null
                    ? "WARNING: TM move randomization skipped on Uhuru (code.bin is not patched); trainers use the existing TMs."
                    : "WARNING: TM move randomization skipped on Uhuru, and the TM table was not found, so trainers get no TM moves.", 15);
        }

        // 3. Personal Data (Base Stats, Abilities, Types, Catch Rates, Held Items, Egg Groups)
        if (Settings.BaseStatsMode > 0 || Settings.AbilitiesMode > 0 || Settings.TypesMode > 0 ||
            Settings.WildSetMinCatchRate || Settings.FieldItemsBanBadItems || Settings.TotemRandomizeHeldItems ||
            Settings.EnforceMaximumBST || Settings.NoEgregiousStats ||
            Settings.FastEggHatching || Settings.NoEVsFromPokemon ||
            Settings.ForceDualTypes ||
            // Read only inside this block, so selecting just these used to do nothing.
            Settings.WildRandomizeHeldItems || Settings.TMHMCompatibilityMode > 0 ||
            Settings.MoveTutorCompatibilityMode > 0 || Settings.FullHMCompatibility)
        {
            progressCallback?.Invoke("Randomizing Pokémon Traits (Base Stats, Abilities, Types)...", 20);

            var pRandomizer = new PersonalRandomizer(Config.Personal.Table, Config)
            {
                ModifyStats = Settings.BaseStatsMode == 2,
                ShuffleStats = Settings.BaseStatsMode == 1,
                PreserveBST = Settings.StatVariancePercent == 0,
                StatDeviation = Settings.StatVariancePercent,
                TypeCount = Math.Max(1, Config.GetText(pk3DS.Core.TextName.Types).Length),
                ModifyStatsFollowEvolutions = Settings.BaseStatsFollowEvolutions,
                ModifyStatsFollowMegas = Settings.BaseStatsFollowMegas,

                EnforceMinimumBST = Settings.EnforceMinimumBST,
                MinBST3Stage1 = Settings.MinBST3Stage1,
                MinBST3Stage2 = Settings.MinBST3Stage2,
                MinBST3Stage3 = Settings.MinBST3Stage3,
                MinBST2Stage1 = Settings.MinBST2Stage1,
                MinBST2Stage2 = Settings.MinBST2Stage2,
                MinBST1Stage = Settings.MinBST1Stage,
                MinBSTLegendary = Settings.MinBSTLegendary,

                EnforceMaximumBST = Settings.EnforceMaximumBST,
                MaxBST3Stage1 = Settings.MaxBST3Stage1,
                MaxBST3Stage2 = Settings.MaxBST3Stage2,
                MaxBST3Stage3 = Settings.MaxBST3Stage3,
                MaxBST2Stage1 = Settings.MaxBST2Stage1,
                MaxBST2Stage2 = Settings.MaxBST2Stage2,
                MaxBST1Stage = Settings.MaxBST1Stage,
                MaxBSTLegendary = Settings.MaxBSTLegendary,

                NoEgregiousStats = Settings.NoEgregiousStats,
                NoEgregiousStatsSingleCap = Settings.NoEgregiousStatsSingleCap,
                NoEgregiousStatsBSTCapRegular = Settings.NoEgregiousStatsBSTCapRegular,
                NoEgregiousStatsBSTCapLegendary = Settings.NoEgregiousStatsBSTCapLegendary,
                AvoidMinmaxing = Settings.AvoidMinmaxing,

                ModifyAbilities = Settings.AbilitiesMode > 0,
                AllowWonderGuard = Settings.AllowWonderGuard,
                BanList = AbilityBans,
                ModifyAbilitiesFollowEvolutions = Settings.AbilitiesFollowEvolutions,
                ModifyAbilitiesFollowMegas = Settings.AbilitiesFollowMegas,

                ModifyTypes = Settings.TypesMode > 0,
                SameTypeChance = Settings.TypesMode == 1 ? 75 : 50,
                ModifyTypesFollowEvolutions = Settings.TypesMode == 1,
                ModifyTypesFollowMegas = Settings.TypesFollowMegas,
                ForceDualTypes = Settings.ForceDualTypes,

                // "Minimum catch rate" is a floor, applied further down; re-rolling every species'
                // rate here first threw away the vanilla values it was meant to protect.
                ModifyCatchRate = false,
                ModifyHeldItems = Settings.WildRandomizeHeldItems,
                ModifyEggGroup = false,

                // Only with TM compatibility randomized: otherwise this re-propagated vanilla TMs down
                // evolution lines whenever any other trait was randomized.
                TMInheritance = Settings.TMFollowEvolutions && Settings.TMHMCompatibilityMode is 1 or 2,

                // Full HM compatibility is applied on its own below; it must not re-roll every TM.
                ModifyLearnsetTM = Settings.TMHMCompatibilityMode is 1 or 2,
                ModifyLearnsetHM = Settings.TMHMCompatibilityMode is 1 or 2,
                ModifyLearnsetSmartly = true,
                PreferSameTypeTMs = Settings.TMHMCompatibilityMode == 1,
                MoveIDsTMs = tmMoveListForInjection,
                ModifyLearnsetTypeTutors = Settings.MoveTutorCompatibilityMode is 1 or 2,
                ModifyLearnsetMoveTutors = Settings.MoveTutorCompatibilityMode is 1 or 2,

                MegaBSTSync = Settings.CompetitiveRandomizer || Settings.BaseStatsFollowMegas,
                UseCompetitiveAbilities = Settings.CompetitiveRandomizer
            };

            pRandomizer.Execute();
            pRandomizer.ApplyTypeInheritance();
            if (Settings.ForceDualTypes)
                pRandomizer.ApplyForceDualTypes(Config.Personal.Table);
            pRandomizer.ApplyStatInheritance();
            pRandomizer.ApplyAbilityInheritance();
            pRandomizer.SynchronizeCalyrexRiderTypes(Config.Personal.Table);

            if (pRandomizer.MegaBSTSync)
                pRandomizer.ApplyMegaBSTSync();

            if (Settings.ForceDualTypes)
                pRandomizer.ApplyForceDualTypes(Config.Personal.Table);

            // Runs absolute last so it smooths the final shape of every stat spread, Mega forms
            // included, without touching any Pokemon's total BST.
            pRandomizer.ApplyAvoidMinmaxing();

            if (Settings.TMHMCompatibilityMode == 3)
                ApplyFullTMCompatibility(Config.Personal.Table);

            if (Settings.FullHMCompatibility)
                ApplyFullHMCompatibility(Config.Personal.Table);

            if (Settings.MoveTutorCompatibilityMode == 3)
                ApplyFullTutorCompatibility(Config.Personal.Table);

            if (Settings.WildSetMinCatchRate)
            {
                byte minCatch = (byte)(Settings.WildMinCatchRate * 50);
                for (int i = 1; i < Config.Personal.Table.Length; i++)
                    if (Config.Personal.Table[i].CatchRate < minCatch)
                        Config.Personal.Table[i].CatchRate = minCatch;
            }

            if (Settings.FastEggHatching)
            {
                // 1 is the floor the games treat as "hatches almost immediately"; 0 is not used by
                // any vanilla species and is not worth being the first to try.
                for (int i = 1; i < Config.Personal.Table.Length; i++)
                    Config.Personal.Table[i].HatchCycles = 1;
            }

            if (Settings.NoEVsFromPokemon)
            {
                for (int i = 1; i < Config.Personal.Table.Length; i++)
                {
                    var p = Config.Personal.Table[i];
                    p.EV_HP = p.EV_ATK = p.EV_DEF = p.EV_SPE = p.EV_SPA = p.EV_SPD = 0;
                }
            }

            // Write back to GARC (including the reconstructed Master Table for Gen 7 / Gen 6)
            var table = Config.Personal;
            int count = table.Table.Length;
            byte[][] allFiles = new byte[count + 1][];
            for (int i = 0; i < count; i++)
            {
                allFiles[i] = table.Table[i].Write();
            }

            allFiles[count] = (byte[])Config.GARCPersonal.Files[^1].Clone(); // keeps Uhuru's junk entries and tail
            pk3DS.Core.Modding.TMCompat.SyncMaster(allFiles);

            Config.GARCPersonal.Files = allFiles;
            Config.GARCPersonal.Save();
            Config.InitializePersonal();
        }

        if (Settings.EvolutionsMode > 0 || Settings.ChangeImpossibleEvos || Settings.MakeEvosEasier || Settings.RemoveTimeBasedEvos || applyExpansionEvos)
        {
            progressCallback?.Invoke("Randomizing Evolutions & Trade Fixes...", 40);

            var eRandomizer = new EvolutionRandomizer(Config, Config.Evolutions, speciesRand);

            if (Settings.ChangeImpossibleEvos)
                eRandomizer.ExecuteTrade();

            if (Settings.MakeEvosEasier)
                eRandomizer.ExecuteMakeEvosEasier();

            if (Settings.RemoveTimeBasedEvos)
                eRandomizer.ExecuteRemoveTimeBased();

            if (Settings.EvolutionsMode == 2)
            {
                // Every-level sets each target to species 1 as a placeholder; randomizing right after
                // fills them in (as EvolutionEditor7 does). Without it everything evolves into Bulbasaur.
                eRandomizer.ExecuteEvolveEveryLevel();
                eRandomizer.Execute();
            }

            if (Settings.EvolutionsMode == 1)
                eRandomizer.Execute();

            var g = Config.GetGARCData("evolution");
            if (g != null)
            {
                for (int i = 0; i < Config.Evolutions.Length; i++)
                {
                    g.Files[i] = Config.Evolutions[i].Write();
                }
                g.Save();
            }
        }

        // 5. Movesets / Learnsets
        if (Settings.MovesetsMode > 0 || Settings.GuaranteedLevel1Moves)
        {
            progressCallback?.Invoke("Randomizing Pokémon Movesets...", 55);

            const int Metronome = 118;
            if (Settings.MovesetsMode == 3)
            {
                // "Metronome Only": every species learns exactly Metronome, at level 1.
                foreach (var set in Config.Learnsets.Where(z => z != null))
                {
                    set.Moves = [Metronome];
                    set.Levels = [1];
                }
            }
            else if (Settings.MovesetsMode > 0)
            {
                var lRandomizer = new LearnsetRandomizer(Config, Config.Learnsets)
                {
                    STABPercent = Settings.MovesetsMode == 1 ? 70 : 40,
                    STABFirst = true,
                    Learn4Level1 = Settings.GuaranteedLevel1Moves,
                    Level1Count = Settings.GuaranteedLevel1MovesCount,
                    OrderByPower = Settings.ReorderDamagingMoves,
                    Expand = true,
                    ExpandTo = 25
                };
                lRandomizer.Execute();
            }
            else
            {
                // Movesets unchanged: "guaranteed level-1 moves" only moves the vanilla learnset's
                // first N entries to level 1. It used to run the full randomizer at 40% STAB.
                foreach (var set in Config.Learnsets.Where(z => z?.Levels != null))
                {
                    var levels = set.Levels;
                    for (int i = 0; i < Math.Min(Settings.GuaranteedLevel1MovesCount, levels.Length); i++)
                        levels[i] = 1;
                    set.Levels = levels;
                }
            }

            var g = Config.GARCLearnsets;
            if (g != null)
            {
                for (int i = 0; i < Config.Learnsets.Length; i++)
                {
                    g.Files[i] = Config.Learnsets[i].Write();
                }
                g.Save();
            }
        }

        // Load code.bin once before any feature that modifies it. Every writer below operates
        // on this shared buffer; a single flush at the end persists all changes atomically.
        EnsureCodeBin();

        ushort[][] tutorMoveListsForInjection = null;
        if (Settings.MoveTutorMovesMode > 0 || Settings.CompetitiveRandomizer)
        {
            if (Settings.MoveTutorMovesMode > 0)
                progressCallback?.Invoke("Randomizing Move Tutor Moves...", 67);
            var tutorRand = new TutorRandomizer(RomFSPath, Settings.MoveTutorMovesMode);
            var tutorMoveRand = new MoveRandomizer(Config);
            tutorMoveListsForInjection = tutorRand.Execute(Config.Info.MaxMoveID, tutorMoveRand, Config, Settings.CompetitiveRandomizer);
            if (tutorRand.UhuruReadOnly)
                progressCallback?.Invoke("WARNING: move tutor randomization skipped on Uhuru (Shop.cro is not patched); trainers use the existing tutor moves.", 67);
        }

        // 5b. Starters (processed before trainers so RivalCarriesStarter has chosen starters)
        int[] chosenStarters = null;
        try
        {
            if (Settings.StartersMode > 0)
                progressCallback?.Invoke("Randomizing Starters...", 68);
            chosenStarters = RandomizeStarters(speciesRand);
        }
        catch { }

        // 6. Trainers
        if (Settings.TrainerPokemonMode > 0 || Settings.TrainerLevelModifierPercent != 0 || Settings.UpgradeVanillaTrainers)
        {
            progressCallback?.Invoke("Randomizing Trainer Teams & Difficulty...", 70);

            var trdata = Config.GetGARCData("trdata");
            var trpoke = Config.GetGARCData("trpoke");
            var moveRand = new MoveRandomizer(Config);
            var teamGen = new Competitive.TeamGenerator();
            var buildEngine = new Competitive.CompetitiveBuildEngine(Config) { BanList = AbilityBans, Weights = Settings.Weights ?? new Competitive.CompetitiveWeights() };
            if (tmMoveListForInjection != null)
                buildEngine.Learnsets.InjectTMMoves(tmMoveListForInjection);
            if (tutorMoveListsForInjection != null)
                buildEngine.Learnsets.InjectTutorMoves(tutorMoveListsForInjection);
            string[] itemNames = Config.GetText(TextName.ItemNames);

            bool trainerAllowLegendaries = !Settings.TrainerDontUseLegendaries && Settings.AllowLegendaries;
            bool trainerAllowMythicals = !Settings.TrainerDontUseLegendaries && Settings.AllowMythicals;

            var trainerSpeciesRand = new SpeciesRandomizer(Config)
            {
                G1 = Settings.Gen1,
                G2 = Settings.Gen2,
                G3 = Settings.Gen3,
                G4 = Settings.Gen4,
                G5 = Settings.Gen5,
                G6 = Settings.Gen6,
                G7 = Settings.Gen7,
                G8 = Settings.Gen8,
                G9 = Settings.Gen9,

                L = trainerAllowLegendaries,
                E = trainerAllowMythicals,
                Shedinja = Settings.AllowShedinja,

                rBST = Settings.TrainerTrySimilarStrength,
                rType = Settings.MatchType,
                rEXP = Settings.MatchEXP
            };
            trainerSpeciesRand.Initialize();
            trainerSpeciesRand.GenerateGlobalMapping(Config.Evolutions);

            var legendarySet = Legal.GetLegendaryAndMythicalSet();
            var allowedLegendaries = trainerSpeciesRand.AllowedSpecies.Where(s => legendarySet.Contains(s)).ToList();

            Dictionary<string, List<int>> abilityToFinalSpecies = null;
            if (Settings.CompetitiveRandomizer)
            {
                abilityToFinalSpecies = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                string[] abilityNamesForIndex = Config.GetText(TextName.AbilityNames);
                int maxSpecForIndex = Config.MaxSpeciesID;
                for (int s = 1; s <= maxSpecForIndex; s++)
                {
                    int finalSpec = GetFinalEvolution(s);
                    if (!trainerAllowLegendaries && legendarySet.Contains(finalSpec)) continue;
                    var fpi = Config.Personal[finalSpec];
                    if (fpi?.Abilities == null) continue;
                    foreach (int abilId in fpi.Abilities)
                    {
                        if (abilId <= 0 || abilId >= abilityNamesForIndex.Length) continue;
                        string abilNameForIndex = abilityNamesForIndex[abilId];
                        if (string.IsNullOrEmpty(abilNameForIndex)) continue;
                        if (!abilityToFinalSpecies.TryGetValue(abilNameForIndex, out var list))
                            abilityToFinalSpecies[abilNameForIndex] = list = new List<int>();
                        if (!list.Contains(finalSpec)) list.Add(finalSpec);
                    }
                }
            }

            if (trdata != null && trpoke != null)
            {
                byte[][] trd = trdata.Files;
                byte[][] trp = trpoke.Files;

                if (Config.Generation == 7)
                {
                    // Uhuru (b > 0): trdata has 5 blocks, trpoke 7, index = block * b + id. One team per trainer:
                    // block 0 is randomized, every other block copies its species slot by slot (docs/uhuru/trainer-format.md).
                    int b = Config.Info.TrainerBaseCount;
                    // Only when species can change: a level-only run must keep Uhuru's own per-difficulty teams.
                    bool oneTeam = b > 0 && Settings.UhuruOneTeam && (Settings.TrainerPokemonMode > 0 || Settings.CompetitiveRandomizer);
                    HashSet<int> keepPwt = b > 0 && Settings.UhuruKeepPwtCast ? [.. TrainerBlocks.UhuruPwtTrainers] : null;
                    var baseTeams = new Dictionary<int, (int Species, int Form)[]>();
                    // Blocks that start out identical to block 0 (block 1 on every trainer): computed on the ORIGINAL
                    // arrays before the loop mutates anything, so they can reuse block 0's output as is.
                    bool[] sameAsBase = null;
                    if (oneTeam)
                    {
                        sameAsBase = new bool[trd.Length];
                        for (int k = b; k < trd.Length && k < trp.Length; k++)
                            sameAsBase[k] = trd[k].AsSpan().SequenceEqual(trd[k % b]) && trp[k].AsSpan().SequenceEqual(trp[k % b]);
                    }
                    int loopEnd = b > 0 ? trp.Length : Math.Min(trd.Length, trp.Length);

                    for (int i = 1; i < loopEnd; i++)
                    {
                        if (trp[i].Length < 4) continue;
                        int id = TrainerBlocks.BaseId(i, b), blk = TrainerBlocks.Block(i, b);
                        if (b > 0 && (id == 0 || (keepPwt != null && keepPwt.Contains(id)))) continue;
                        if (sameAsBase != null && i < sameAsBase.Length && sameAsBase[i])
                        {
                            trd[i] = trd[id];
                            trp[i] = trp[id];
                            continue;
                        }

                        // trpoke blocks 5/6 have no trdata record of their own: use block 3/4's (this run's output) with the team length.
                        bool ownRecord = i < trd.Length;
                        TrainerData7 trainer;
                        int di = i;
                        if (ownRecord)
                            trainer = new TrainerData7(trd[i], trp[i]);
                        else
                        {
                            di = TrainerBlocks.TrdataBlockFor(blk) * b + id;
                            int n = TrainerBlocks.TeamCount(trp[i].Length);
                            var rec = (byte[])trd[di].Clone();
                            rec[3] = (byte)n;
                            trainer = new TrainerData7(rec, trp[i], n);
                        }
                        if (trainer.Pokemon == null || trainer.Pokemon.Count == 0) continue;

                        // Block 0's species for this trainer, when this is a later block (null = pick normally).
                        (int Species, int Form)[] forced = null;
                        if (oneTeam && blk > 0) baseTeams.TryGetValue(id, out forced);

                        if (Settings.BetterTrainerMovesets || Settings.CompetitiveRandomizer || Settings.MasterAIMode > 0)
                            trainer.Flag = true;

                        bool fullAI = Settings.CompetitiveRandomizer
                                   || Settings.MasterAIMode == 2
                                   || (Settings.MasterAIMode == 1 && TrainerTiers.Of(trainer, id) != TrainerTiers.Tier.Regular);

                        if (fullAI)
                        {
                            trainer.AI |= (int)(TrainerAI.Basic | TrainerAI.Strong | TrainerAI.Expert
                                              | TrainerAI.PokeChange | TrainerAI.UseItem);
                        }

                        bool shouldAddHeldItems = Settings.AddHeldItemsBossTrainers || Settings.AddHeldItemsImportantTrainers || Settings.AddHeldItemsRegularTrainers || Settings.CompetitiveRandomizer;
                        int[] sensibleItems = [217, 229, 158, 270, 268, 297, 287, 275, 538, 540, 640, 269, 271, 272, 231, 232, 233, 234, 235];

                        bool isTutorialBattle = FirstRivalBattle.Matches(id, trainer);

                        // 1. Team Expansion FIRST so full team size is known
                        int addCount = 0;
                        if (Settings.AddPokemonBossTrainers) addCount = Math.Max(addCount, Settings.AddPokemonBossCount);
                        if (Settings.AddPokemonImportantTrainers) addCount = Math.Max(addCount, Settings.AddPokemonImportantCount);
                        if (Settings.AddPokemonRegularTrainers) addCount = Math.Max(addCount, Settings.AddPokemonRegularCount);
                        if (isTutorialBattle) addCount = 0;

                        if (addCount > 0 && trainer.Pokemon.Count > 0 && trainer.Pokemon.Count < 6)
                        {
                            byte avgLevel = (byte)trainer.Pokemon.Average(p => p.Level);
                            int targetCount = Math.Min(6, trainer.Pokemon.Count + addCount);
                            while (trainer.Pokemon.Count < targetCount)
                            {
                                ushort extraSpecies = (ushort)trainerSpeciesRand.GetRandomSpecies(trainer.Pokemon[0].Species);
                                if ((Settings.ForceFullyEvolvedAtLevel && avgLevel >= Settings.ForceFullyEvolvedLevel) || Settings.CompetitiveRandomizer)
                                    extraSpecies = GetFinalEvolution(extraSpecies);
                                var newPk = new TrainerPoke7
                                {
                                    Species = extraSpecies,
                                    Level = avgLevel,
                                };
                                trainer.Pokemon.Add(newPk);
                            }
                            trainer.NumPokemon = trainer.Pokemon.Count;
                        }

                        // Apply Battle Style (e.g. Doubles) FIRST so team format & size are known to team builder
                        if (ownRecord) ApplyBattleStyle(trainer);
                        else if (trainer.Mode == BattleMode.Doubles && trainer.Pokemon.Count == 1)
                        {
                            // Blocks 5/6 follow the block 3/4 record's style (the game reads that one); only the missing partner is added.
                            trainer.Pokemon.Add(trainer.Pokemon[0].Clone());
                            trainer.NumPokemon = trainer.Pokemon.Count;
                        }
                        bool isDoubles = trainer.Mode == BattleMode.Doubles;

                        // 2. Generate Archetype & Roles for full team
                        var archetype = teamGen.GetRandomArchetype();
                        var roles = teamGen.GetRolesForArchetype(archetype, trainer.Pokemon.Count, isDoubles);
                        var archetypeSpec = teamGen.GetSpecification(archetype);
                        bool assignedMega = Settings.NoMegaEvolution;
                        bool assignedZCrystal = Settings.NoZMoves;
                        var teamAbilitiesCovered = new List<string>();

                        var teamTypesCovered = new HashSet<int>();

                        // Per-trainer bookkeeping for "avoid duplicates" and "force diverse types".
                        // Both reset for every trainer: they are about one team, not the whole game.
                        var placedSpecies = new HashSet<int>();
                        var teamTypes = new HashSet<int>();
                        var tier = TrainerTiers.Of(trainer, id);
                        bool isBoss = tier == TrainerTiers.Tier.Boss;
                        bool isImportant = tier == TrainerTiers.Tier.Important;
                        bool forceDiverse = TrainerTiers.Pick(trainer, id,
                            Settings.TrainerDiverseTypesBoss,
                            Settings.TrainerDiverseTypesImportant,
                            Settings.TrainerDiverseTypesRegular);

                        int guaranteedBossLegendarySlot = -1;
                        if (trainerAllowLegendaries && allowedLegendaries.Count > 0 && isBoss)
                        {
                            bool rivalStarterOnLast = Settings.RivalCarriesStarter && chosenStarters != null && TryGetRivalStarter(id, out _);
                            if (trainer.Pokemon.Count >= 2)
                                guaranteedBossLegendarySlot = rivalStarterOnLast ? trainer.Pokemon.Count - 2 : trainer.Pokemon.Count - 1;
                            else if (trainer.Pokemon.Count == 1)
                                guaranteedBossLegendarySlot = rivalStarterOnLast ? -1 : 0;
                        }

                        int trainerTheme = -1;
                        if (TrainerThemes.UsesThemes(Settings.TrainerPokemonMode))
                        {
                            var speciesBefore = trainer.Pokemon.Select(p => p.Species).ToList();
                            trainerTheme = TrainerThemes.ThemeFor(
                                Config, Settings.TrainerPokemonMode, trainer, id, speciesBefore);
                        }

                        string[] abilityNames = Config.GetText(TextName.AbilityNames);
                        string[] moveNames = Config.GetText(TextName.MoveNames);

                        // 3. Process every Pokemon in the team
                        for (int pkIdx = 0; pkIdx < trainer.Pokemon.Count; pkIdx++)
                        {
                            var pk = trainer.Pokemon[pkIdx];
                            var role = (pkIdx < roles.Length) ? roles[pkIdx] : Competitive.CompetitiveRole.OffensiveSweeper;
                            int preMapSpecies = pk.Species;

                            int rivalStarterSlot = -1;
                            // A later block's slots already carry block 0's starter (its evolution stage too: that is the "same species" rule).
                            bool pinned = forced != null && pkIdx < forced.Length;
                            bool isRivalStarter = forced == null && Settings.RivalCarriesStarter && chosenStarters != null && chosenStarters.Length == 3 &&
                                                  pkIdx == trainer.Pokemon.Count - 1 && TryGetRivalStarter(id, out rivalStarterSlot);

                            bool isLegendarySlot = false;
                            if (trainerAllowLegendaries && allowedLegendaries.Count > 0 && !isRivalStarter)
                            {
                                bool isAce = (pkIdx == trainer.Pokemon.Count - 1);
                                if (isBoss)
                                {
                                    if (pkIdx == guaranteedBossLegendarySlot)
                                        isLegendarySlot = true;
                                    else
                                        isLegendarySlot = (Util.Rand.Next(100) < 35);
                                }
                                else if (isImportant)
                                {
                                    if (isAce)
                                        isLegendarySlot = (Util.Rand.Next(100) < 70);
                                    else
                                        isLegendarySlot = (Util.Rand.Next(100) < 20);
                                }
                            }

                            if (pinned)
                            {
                                pk.Species = (ushort)forced[pkIdx].Species;
                                pk.Form = forced[pkIdx].Form;
                            }
                            else
                            {
                                if (isRivalStarter)
                                {
                                    pk.Species = (ushort)GetEvolvedStarterForLevel(chosenStarters[rivalStarterSlot], pk.Level);
                                }
                                else if (Settings.TrainerPokemonMode > 0 || Settings.CompetitiveRandomizer)
                                {
                                    // Extra slots in a later block: re-pick (up to 20 times) until the base stat total reaches
                                    // the weakest Pokemon of block 0's team. The competitive reselect below may still override this.
                                    int floorBst = forced == null ? 0 : forced.Min(f => Config.Personal[f.Species]?.BST ?? 0);
                                    for (int tries = 0; ; tries++)
                                    {
                                        pk.Species = (ushort)PickTrainerSpecies(trainerSpeciesRand, pk.Species, trainerTheme, isLegendarySlot, allowedLegendaries);
                                        pk.Species = (ushort)RefineTrainerPick(
                                            trainerSpeciesRand, preMapSpecies, pk.Species, placedSpecies, teamTypes, forceDiverse, trainerTheme, isLegendarySlot, allowedLegendaries);
                                        if (forced == null || tries >= 20 || (Config.Personal[pk.Species]?.BST ?? 0) >= floorBst) break;
                                    }
                                }

                                // A new species must not inherit the old slot's form: Alolan Raticate (form 1)
                                // replaced by Charizard became form 1 = Mega Charizard X without a stone, and
                                // the stat lookups below read the base form anyway.
                                if (pk.Species != preMapSpecies)
                                    pk.Form = 0;
                            }

                            int requiredAbilityId = -1;
                            if (!pinned && Settings.CompetitiveRandomizer && archetypeSpec != null)
                            {
                                string[] abilityNamesForBias = Config.GetText(TextName.AbilityNames);
                                var wantedAbilityIds = new List<int>();
                                var wantedNames = new List<string>();
                                if (pkIdx == 0)
                                {
                                    if (!string.IsNullOrEmpty(archetypeSpec.RequiredLeadAbility))
                                        wantedNames.Add(archetypeSpec.RequiredLeadAbility);
                                    if (archetypeSpec.AlternateLeadAbilities?.Length > 0)
                                        wantedNames.AddRange(archetypeSpec.AlternateLeadAbilities);
                                }
                                else if (archetypeSpec.RequiredTeammateAbilities.Count > 0)
                                {
                                    string stillNeeded = archetypeSpec.RequiredTeammateAbilities.FirstOrDefault(
                                        a => !teamAbilitiesCovered.Any(c => c.Equals(a, StringComparison.OrdinalIgnoreCase)));
                                    if (stillNeeded != null) wantedNames.Add(stillNeeded);
                                }

                                foreach (string n in wantedNames)
                                {
                                    int abilId = Array.FindIndex(abilityNamesForBias, a => a.Equals(n, StringComparison.OrdinalIgnoreCase));
                                    if (abilId > 0) wantedAbilityIds.Add(abilId);
                                }

                                var typesStillNeeded = new HashSet<int>(archetypeSpec.RequiredTypes);
                                typesStillNeeded.ExceptWith(teamTypesCovered);

                                int winningSpecies = GetFinalEvolution(pk.Species);
                                bool foundWantedAbility = false;

                                if (wantedAbilityIds.Count > 0 && abilityToFinalSpecies != null)
                                {
                                    var shuffledWanted = wantedNames.OrderBy(_ => Util.Rand.Next()).ToList();
                                    foreach (string wanted in shuffledWanted)
                                    {
                                        if (!abilityToFinalSpecies.TryGetValue(wanted, out var matches) || matches.Count == 0)
                                            continue;

                                        var pick = matches
                                            .Where(s => Competitive.TeamGenerator.IsStatSuitableForRole(Config.Personal[s], role, archetypeSpec))
                                            .ToList();

                                        var unplaced = pick.Where(s => !placedSpecies.Contains(s)).ToList();
                                        if (unplaced.Count > 0) pick = unplaced;

                                        if (isLegendarySlot && allowedLegendaries.Count > 0)
                                        {
                                            var legPick = pick.Where(s => allowedLegendaries.Contains(s)).ToList();
                                            if (legPick.Count > 0) pick = legPick;
                                        }

                                        if (archetypeSpec.RequiredTypes.Count > 0 && typesStillNeeded.Count > 0)
                                        {
                                            var typed = pick
                                                .Where(s => Config.Personal[s]?.Types?.Any(typesStillNeeded.Contains) == true)
                                                .ToList();
                                            if (typed.Count > 0) pick = typed;
                                        }

                                        if (pick.Count == 0)
                                        {
                                            var unplacedMatches = matches.Where(s => !placedSpecies.Contains(s)).ToList();
                                            pick = unplacedMatches.Count > 0 ? unplacedMatches : matches;
                                        }

                                        winningSpecies = pick[Util.Rand.Next(pick.Count)];
                                        requiredAbilityId = Array.FindIndex(abilityNamesForBias, a => a.Equals(wanted, StringComparison.OrdinalIgnoreCase));
                                        foundWantedAbility = true;
                                        break;
                                    }
                                }

                                if (!foundWantedAbility)
                                {
                                    // Manifesto 0.6's retry budget; the default lives in CompetitiveRules, the live value in the weights file.
                                    int maxArchetypeFitAttempts = buildEngine.Weights.ConstrainedPickAttempts;
                                    int candidateSpecies = pk.Species;
                                    double bestScore = double.NegativeInfinity;
                                    int bestCandidate = GetFinalEvolution(candidateSpecies);

                                    var existingTeamTypes = trainer.Pokemon.Take(pkIdx)
                                        .Select(p => Config.Personal[p.Species]?.Types)
                                        .Where(t => t != null)
                                        .ToList();
                                    bool trickRoomTeam = archetype == Competitive.TeamArchetype.TrickRoom;
                                    var teamRoles = Competitive.CompetitiveBuildEngine.RolesOf(
                                        trainer.Pokemon.Take(pkIdx).SelectMany(p => p.Moves), moveNames, trickRoomTeam);

                                    for (int attempt = 0; attempt < maxArchetypeFitAttempts; attempt++)
                                    {
                                        int evaluatedSpecies = GetFinalEvolution(candidateSpecies);
                                        var candidatePI = Config.Personal[evaluatedSpecies];
                                        winningSpecies = evaluatedSpecies;

                                        if (placedSpecies.Contains(evaluatedSpecies) && attempt < maxArchetypeFitAttempts - 1)
                                        {
                                            goto NextAttempt;
                                        }

                                        if (wantedAbilityIds.Count > 0)
                                        {
                                            int matchAbilId = candidatePI?.Abilities?.FirstOrDefault(a => wantedAbilityIds.Contains(a)) ?? 0;
                                            if (matchAbilId > 0)
                                            {
                                                requiredAbilityId = matchAbilId;
                                                bestCandidate = evaluatedSpecies;
                                                break;
                                            }
                                        }
                                        else if (Competitive.TeamGenerator.IsStatSuitableForRole(candidatePI, role, archetypeSpec) &&
                                                 (typesStillNeeded.Count == 0 ||
                                                  candidatePI?.Types?.Any(typesStillNeeded.Contains) == true ||
                                                  attempt >= maxArchetypeFitAttempts / 2))
                                        {
                                            var candidateRoles = Competitive.CompetitiveBuildEngine.RolesOf(
                                                buildEngine.Learnsets.GetLegalMoves(evaluatedSpecies), moveNames, trickRoomTeam);
                                            double totalScore = Competitive.TeamSynergy.Score(
                                                candidatePI?.Types, candidateRoles, existingTeamTypes, teamRoles,
                                                Competitive.CompetitiveDatabase.CompetitiveThreeTypeCores, buildEngine.Weights);

                                            if (totalScore > bestScore)
                                            {
                                                bestScore = totalScore;
                                                bestCandidate = evaluatedSpecies;
                                            }
                                            // Good enough synergy is taken at once; otherwise keep the best seen
                                            if (totalScore >= buildEngine.Weights.SynergyAcceptScore || attempt >= maxArchetypeFitAttempts / 2)
                                            {
                                                winningSpecies = bestCandidate;
                                                break;
                                            }
                                        }

                                        NextAttempt:
                                        if (isLegendarySlot && allowedLegendaries.Count > 0)
                                        {
                                            candidateSpecies = allowedLegendaries[Util.Rand.Next(allowedLegendaries.Count)];
                                        }
                                        else
                                        {
                                            candidateSpecies = (Settings.TrainerPokemonMode == TrainerThemes.RandomEvenDistribution)
                                                ? trainerSpeciesRand.GetEvenDistributionSpecies()
                                                : trainerSpeciesRand.GetRandomSpecies(preMapSpecies);
                                        }
                                    }
                                    if (bestScore > double.NegativeInfinity)
                                        winningSpecies = bestCandidate;
                                }
                                pk.Species = (ushort)winningSpecies;
                            }

                            if (!pinned && ((Settings.ForceFullyEvolvedAtLevel && pk.Level >= Settings.ForceFullyEvolvedLevel) || Settings.CompetitiveRandomizer))
                                pk.Species = GetFinalEvolution(pk.Species);

                            if (Settings.TrainerLevelModifierPercent != 0)
                                pk.Level = (byte)Math.Min(100, (int)Math.Max(1, pk.Level * (1 + (Settings.TrainerLevelModifierPercent / 100m))));

                            if (Settings.TrainerRandomShiny)
                                pk.Shiny = (int)(Util.Random32() % 100) < 5;

                            var pi = Config.Personal[pk.Species];

                            if (Settings.CompetitiveRandomizer)
                            {
                                // If the archetype-fit search above locked in a required lead
                                // ability, force that exact slot; otherwise pick the best tier.
                                int forcedSlot = requiredAbilityId > 0 ? FindAbilitySlot(pi, requiredAbilityId) : 0;
                                pk.Ability = forcedSlot > 0 ? forcedSlot : buildEngine.ChooseCompetitiveAbilitySlot(pi, abilityNames);
                            }
                            string abilName = Competitive.CompetitiveBuildEngine.ResolveAbilityName(pi, pk.Ability, abilityNames);
                            if (Settings.CompetitiveRandomizer && !string.IsNullOrEmpty(abilName))
                                teamAbilitiesCovered.Add(abilName);
                            if (Settings.CompetitiveRandomizer && pi?.Types != null)
                                foreach (int t in pi.Types) teamTypesCovered.Add(t);

                            if (Settings.CompetitiveRandomizer)
                            {
                                role = Competitive.CompetitiveBuildEngine.AdaptRoleForPokemon(pi, role);
                                pk.Moves = buildEngine.BuildCompetitiveMoveset(pk.Species, role, pi, moveRand, abilName, archetype, Config.Moves, isDoubles: isDoubles, level: pk.Level, teamAbilities: teamAbilitiesCovered);
                                var (evs, nature) = buildEngine.BuildEVsAndNature(pi, role, pk.Moves, archetype == Competitive.TeamArchetype.TrickRoom, level: pk.Level);
                                pk.EVs = evs.Select(b => (int)b).ToArray();
                                pk.Nature = nature;

                                pk.IVs = buildEngine.BuildIvs(pi, role, pk.Moves, archetype == Competitive.TeamArchetype.TrickRoom, moveNames, Settings.TrainerFullIVs);

                                pk.Item = (int)buildEngine.AssignItem(pk.Species, pi, abilName, pk.Moves, role, itemNames, moveNames, Config.Moves, ref assignedMega, ref assignedZCrystal, archetype, teamAbilitiesCovered, isDoubles: isDoubles);

                                string heldItemName = pk.Item > 0 && pk.Item < itemNames.Length ? itemNames[pk.Item] : "";
                                if (Competitive.CompetitiveValidator.IsMegaStone(heldItemName) &&
                                    Competitive.CompetitiveDatabase.MegaStoneMap.TryGetValue(pk.Species, out var heldStones))
                                {
                                    int stoneIdx = Array.FindIndex(heldStones, s => GameNameComparer.Instance.Equals(s, heldItemName));
                                    var megaFormIdxs = Competitive.CompetitiveDatabase.GetMegaFormIndices(pk.Species, Config.Personal.Table);
                                    if (stoneIdx >= 0 && stoneIdx < megaFormIdxs.Length)
                                    {
                                        var megaPi = Config.Personal[megaFormIdxs[stoneIdx]];

                                        string megaAbilName = Competitive.CompetitiveBuildEngine.ResolveAbilityName(megaPi, 1, abilityNames);
                                        if (!string.IsNullOrEmpty(megaAbilName))
                                        {
                                            teamAbilitiesCovered.Remove(abilName);
                                            teamAbilitiesCovered.Add(megaAbilName);
                                            abilName = megaAbilName;
                                        }

                                        role = Competitive.CompetitiveBuildEngine.AdaptRoleForPokemon(megaPi, role, isMega: true);
                                        pk.Moves = buildEngine.BuildCompetitiveMoveset(pk.Species, role, megaPi, moveRand, abilName, archetype, Config.Moves, isDoubles: isDoubles, level: pk.Level, teamAbilities: teamAbilitiesCovered);
                                        var (megaEvs, megaNature) = buildEngine.BuildEVsAndNature(megaPi, role, pk.Moves, archetype == Competitive.TeamArchetype.TrickRoom, level: pk.Level);
                                        pk.EVs = megaEvs.Select(b => (int)b).ToArray();
                                        pk.Nature = megaNature;
                                        pi = megaPi;

                                        pk.IVs = buildEngine.BuildIvs(megaPi, role, pk.Moves, archetype == Competitive.TeamArchetype.TrickRoom, moveNames, Settings.TrainerFullIVs);
                                    }
                                }

                                Competitive.CompetitiveValidator.ValidateAndSanitize(
                                    pk, pi, abilName, role, itemNames,
                                    moveNames, Config.Moves, buildEngine.Learnsets, isDoubles: isDoubles);
                            }
                            else
                            {
                                // A slot the user didn't change keeps its vanilla build unless they opted in:
                                // "Trainer level +20%" alone used to rewrite gym leaders' items and moves.
                                bool slotChanged = pk.Species != preMapSpecies;
                                bool upgrade = slotChanged || Settings.UpgradeVanillaTrainers;
                                if (Settings.BetterTrainerMovesets && upgrade)
                                    pk.Moves = moveRand.GetRandomMoveset(pk.Species);
                                if (shouldAddHeldItems && (pk.Item == 0 || Settings.WildRandomizeHeldItems))
                                {
                                    pk.Item = sensibleItems[Util.Random32() % sensibleItems.Length];
                                    upgrade = true;
                                }

                                if (upgrade)
                                    Competitive.CompetitiveValidator.ValidateAndSanitize(
                                        pk, pi, abilName, role, itemNames,
                                        Config.GetText(TextName.MoveNames), Config.Moves, buildEngine.Learnsets, isDoubles: isDoubles);
                            }

                            // Recorded at the end, using whatever the slot finally settled on - the
                            // competitive path above can still change it after the initial pick.
                            placedSpecies.Add(pk.Species);
                            foreach (int t in TypeRestrictions.TypesOf(Config, pk.Species))
                                teamTypes.Add(t);

                            // Rom Nueva + passives were chosen for the old species, so a re-rolled slot starts with none. A slot the
                            // run kept keeps its passives (the record is edited in place), and Pokémon added above start with none.
                            // Passives are not randomized themselves (follow-up F3).
                            if (pk3DS.Core.Modding.PassiveGuard.Enabled && pk.Species != preMapSpecies)
                                pk.Passives = [0, 0, 0];
                        }

                        // Ensure Mega Stone and/or Z-Crystal on competitive / boss teams if not assigned yet
                        if (Settings.CompetitiveRandomizer && trainer.Pokemon.Count >= 2)
                        {
                            if (!assignedMega)
                            {
                                int megaIdx = trainer.Pokemon.FindIndex(p => Competitive.CompetitiveDatabase.MegaStoneMap.ContainsKey(p.Species));
                                if (megaIdx >= 0)
                                {
                                    var megaPk = trainer.Pokemon[megaIdx];
                                    ushort megaItem = buildEngine.TryAssignMegaStone(megaPk.Species, itemNames, ref assignedMega);
                                    if (megaItem > 0)
                                    {
                                        megaPk.Item = megaItem;
                                        string heldItemName = itemNames.ElementAtOrDefault(megaItem) ?? "";
                                        if (Competitive.CompetitiveDatabase.MegaStoneMap.TryGetValue(megaPk.Species, out var heldStones))
                                        {
                                            int stoneIdx = Array.FindIndex(heldStones, s => GameNameComparer.Instance.Equals(s, heldItemName));
                                            var megaFormIdxs = Competitive.CompetitiveDatabase.GetMegaFormIndices(megaPk.Species, Config.Personal.Table);
                                            if (stoneIdx >= 0 && stoneIdx < megaFormIdxs.Length)
                                            {
                                                var megaPi = Config.Personal[megaFormIdxs[stoneIdx]];
                                                string megaAbilName = Competitive.CompetitiveBuildEngine.ResolveAbilityName(megaPi, 1, abilityNames);
                                                var adaptedMegaRole = Competitive.CompetitiveBuildEngine.AdaptRoleForPokemon(megaPi, roles[megaIdx], isMega: true);
                                                megaPk.Moves = buildEngine.BuildCompetitiveMoveset(megaPk.Species, adaptedMegaRole, megaPi, moveRand, megaAbilName, archetype, Config.Moves, isDoubles: isDoubles, level: megaPk.Level, teamAbilities: teamAbilitiesCovered);
                                                var (megaEvs, megaNature) = buildEngine.BuildEVsAndNature(megaPi, adaptedMegaRole, megaPk.Moves, archetype == Competitive.TeamArchetype.TrickRoom, level: megaPk.Level);
                                                megaPk.EVs = megaEvs.Select(b => (int)b).ToArray();
                                                megaPk.Nature = megaNature;

                                                megaPk.IVs = buildEngine.BuildIvs(megaPi, adaptedMegaRole, megaPk.Moves, archetype == Competitive.TeamArchetype.TrickRoom, moveNames, Settings.TrainerFullIVs);

                                                Competitive.CompetitiveValidator.ValidateAndSanitize(
                                                    megaPk, megaPi, megaAbilName, adaptedMegaRole, itemNames,
                                                    moveNames, Config.Moves, buildEngine.Learnsets, isDoubles: isDoubles);
                                            }
                                        }
                                    }
                                }
                            }

                            if (!assignedZCrystal && Config.Generation == 7)
                            {
                                var zPk = trainer.Pokemon.FirstOrDefault(p => p.Item == 0);
                                if (zPk != null)
                                {
                                    int zIdx = trainer.Pokemon.IndexOf(zPk);
                                    var zpi = Config.Personal[zPk.Species];
                                    ushort zItem = buildEngine.TryAssignZCrystal(zPk.Species, zpi, zPk.Moves, itemNames, moveNames, Config.Moves, ref assignedZCrystal);
                                    if (zItem > 0)
                                    {
                                        zPk.Item = zItem;
                                        string zAbilName = (zIdx >= 0 && zIdx < roles.Length) ? Competitive.CompetitiveBuildEngine.ResolveAbilityName(zpi, zPk.Ability, abilityNames) : "";
                                        Competitive.CompetitiveValidator.ValidateAndSanitize(
                                            zPk, zpi, zAbilName, (zIdx >= 0 && zIdx < roles.Length) ? roles[zIdx] : CompetitiveRole.OffensiveSweeper,
                                            itemNames, moveNames, Config.Moves, buildEngine.Learnsets, isDoubles: isDoubles);
                                    }
                                }
                            }
                        }

                        if (Settings.CompetitiveRandomizer)
                        {
                            var teamPIs = trainer.Pokemon.Select(p => Config.Personal[p.Species]).ToArray();
                            Competitive.CompetitiveValidator.ValidateTeam(
                                trainer.Pokemon.ToArray(),
                                teamPIs,
                                teamAbilitiesCovered.ToArray(),
                                roles,
                                itemNames,
                                moveNames,
                                Config.Moves,
                                archetype,
                                buildEngine.Learnsets,
                                isDoubles: isDoubles);
                        }

                        if (isTutorialBattle) FirstRivalBattle.Enforce(trainer);

                        if (oneTeam && blk == 0)
                            baseTeams[id] = trainer.Pokemon.Select(p => (p.Species, p.Form)).ToArray();

                        if (!ownRecord && trainer.Pokemon.Count > 0)
                            trainer.Pokemon[0].Friendship = TrainerBlocks.TeamSizeOverride(trainer.Pokemon.Count, trd[di][3]);

                        trainer.Write(out byte[] outTrData, out byte[] outTrPoke);
                        if (ownRecord) trd[i] = outTrData; // blocks 5/6 never write a trdata record: they share block 3/4's
                        trp[i] = outTrPoke;
                    }
                }
                else if (Config.Generation == 6)
                {
                    for (int i = 1; i < trd.Length && i < trp.Length; i++)
                    {
                        if (trp[i].Length < 4) continue;
                        var trainer = new TrainerData6(trd[i], trp[i], Config.ORAS);
                        if (trainer.Team == null || trainer.Team.Length == 0) continue;

                        foreach (var pk in trainer.Team)
                        {
                            if (pk == null) continue;
                            if (Settings.TrainerPokemonMode > 0 || Settings.CompetitiveRandomizer)
                                pk.Species = (ushort)trainerSpeciesRand.GetMappedSpecies(pk.Species);

                            if ((Settings.ForceFullyEvolvedAtLevel && pk.Level >= Settings.ForceFullyEvolvedLevel) || Settings.CompetitiveRandomizer)
                                pk.Species = GetFinalEvolution(pk.Species);

                            if (Settings.TrainerLevelModifierPercent != 0)
                                pk.Level = (ushort)Math.Min(100, (int)Math.Max(1, pk.Level * (1 + (Settings.TrainerLevelModifierPercent / 100m))));

                            if (Settings.BetterTrainerMovesets)
                            {
                                int[] moves = moveRand.GetRandomMoveset(pk.Species);
                                pk.Moves[0] = (ushort)moves[0];
                                pk.Moves[1] = (ushort)moves[1];
                                pk.Moves[2] = (ushort)moves[2];
                                pk.Moves[3] = (ushort)moves[3];
                            }
                        }

                        // Team Expansion
                        int addCount = 0;
                        if (Settings.AddPokemonBossTrainers) addCount = Math.Max(addCount, Settings.AddPokemonBossCount);
                        if (Settings.AddPokemonImportantTrainers) addCount = Math.Max(addCount, Settings.AddPokemonImportantCount);
                        if (Settings.AddPokemonRegularTrainers) addCount = Math.Max(addCount, Settings.AddPokemonRegularCount);

                        if (addCount > 0 && trainer.Team.Length > 0 && trainer.Team.Length < 6)
                        {
                            ushort avgLevel = (ushort)trainer.Team.Where(p => p != null).Average(p => p.Level);
                            var teamList = trainer.Team.Where(p => p != null).ToList();
                            int targetCount = Math.Min(6, teamList.Count + addCount);
                            while (teamList.Count < targetCount)
                            {
                                ushort extraSpecies = (ushort)trainerSpeciesRand.GetRandomSpecies(teamList[0].Species);
                                if ((Settings.ForceFullyEvolvedAtLevel && avgLevel >= Settings.ForceFullyEvolvedLevel) || Settings.CompetitiveRandomizer)
                                    extraSpecies = GetFinalEvolution(extraSpecies);
                                var newPk = new TrainerData6.Pokemon(new byte[trainer.Team[0].Write(trainer.Item, trainer.Moves).Length], trainer.Item, trainer.Moves)
                                {
                                    Species = extraSpecies,
                                    Level = avgLevel
                                };
                                if (Settings.BetterTrainerMovesets)
                                {
                                    int[] moves = moveRand.GetRandomMoveset(extraSpecies);
                                    newPk.Moves[0] = (ushort)moves[0];
                                    newPk.Moves[1] = (ushort)moves[1];
                                    newPk.Moves[2] = (ushort)moves[2];
                                    newPk.Moves[3] = (ushort)moves[3];
                                }
                                teamList.Add(newPk);
                            }
                            trainer.Team = teamList.ToArray();
                            trainer.NumPokemon = (byte)trainer.Team.Length;
                        }

                        trd[i] = trainer.Write();
                        trp[i] = trainer.WriteTeam();
                    }
                }

                trdata.Save();
                trpoke.Save();
            }
        }

        // 6b. Uhuru: shuffle the PWT pool (script 217). The trainer set is unchanged, so "Keep PWT cast" stays right.
        if (Settings.UhuruShufflePwtPool && Config.Info.TrainerBaseCount > 0)
        {
            progressCallback?.Invoke("Shuffling the PWT pool...", 80);
            string pwtResult = ShufflePwtPool();
            if (!string.IsNullOrEmpty(pwtResult))
                progressCallback?.Invoke(pwtResult, 80);
        }

        // 7. Wild Encounters
        if (Settings.WildPokemonMode > 0 && wildRandomizerAction != null)
        {
            progressCallback?.Invoke("Randomizing Wild Encounters...", 85);
            var formRand = new FormRandomizer(Config)
            {
                AllowMega = Settings.WildAllowAltFormes,
                AllowAlolanForm = Settings.WildAllowAltFormes
            };
            wildRandomizerAction.Invoke(speciesRand, formRand);
        }

        // 8. Static Encounters
        if (Settings.StaticsMode > 0 && staticsRandomizerAction != null)
        {
            progressCallback?.Invoke("Randomizing Static Encounters...", 88);
            var formRand = new FormRandomizer(Config)
            {
                AllowMega = Settings.StaticsAllowAltFormes,
                AllowAlolanForm = Settings.StaticsAllowAltFormes
            };
            staticsRandomizerAction.Invoke(speciesRand, formRand);
        }

        if (Settings.SpecialShopsMode > 0 || Settings.RandomizeAllShops)
        {
            progressCallback?.Invoke("Randomizing Special Shops & Poké Marts...", 95);

            var martRand = new MartRandomizer(RomFSPath, Settings.SpecialShopsMode, Settings.ShopBanBadItems, Settings.RandomizeAllShops);
            if (Settings.CompetitiveRandomizer || Settings.RandomizeAllShops)
            {
                martRand.ExecuteCompetitive(Config.Info.MaxItemID, Config);

                // Expansion failing is not fatal - the shops are still filled - but it changes what
                // was asked for, so it is said out loud rather than left to be noticed in game.
                if (!string.IsNullOrEmpty(MartRandomizer.ExpansionSkipped))
                    progressCallback?.Invoke("WARNING: " + MartRandomizer.ExpansionSkipped, 95);
            }
            else
                martRand.Execute(Config.Info.MaxItemID);
        }

        // 9a1. Cheap Rare Candies
        if (Settings.CheapRareCandies)
        {
            progressCallback?.Invoke("Making Rare Candies cheap and stocking them...", 93);
            string candyResult = ApplyCheapRareCandies();
            if (!string.IsNullOrEmpty(candyResult))
                progressCallback?.Invoke(candyResult, 93);
        }

        // 9a2. Level caps
        if (Settings.EnableLevelCaps)
        {
            progressCallback?.Invoke("Installing Level Caps...", 94);
            string capResult = InstallLevelCaps();
            if (!string.IsNullOrEmpty(capResult))
                progressCallback?.Invoke(capResult, 94);
        }

        // 9a2b. Research Center recipes the user asked for
        if (Settings.InstallPatches is { Count: > 0 })
        {
            SyncCodeBinToDisk();
            progressCallback?.Invoke("Installing selected patches...", 94);
            foreach (string line in InstallSelectedRecipes())
                progressCallback?.Invoke(line, 94);
            ReloadCodeBinFromDisk();
        }

        // 9a2c. Custom EXP Multiplier
        if (Settings.CustomExpMultiplier && Settings.ExpMultiplier >= 1)
        {
            progressCallback?.Invoke($"Applying {Settings.ExpMultiplier}x EXP Multiplier patch...", 94);
            var expLog = new List<string>();
            ApplyExpMultiplierShared(Settings.ExpMultiplier, expLog);
            foreach (string line in expLog)
                progressCallback?.Invoke(line, 94);
        }

        // 9a3. Type effectiveness chart
        if (Settings.TypeEffectivenessMode > 0)
        {
            progressCallback?.Invoke("Rewriting the type effectiveness chart...", 94);
            string typeResult = InstallTypeEffectiveness();
            if (!string.IsNullOrEmpty(typeResult))
                progressCallback?.Invoke(typeResult, 94);
        }

        // Flush the shared code.bin buffer. Every feature above operated on the same in-memory
        // copy; this single write persists all of their changes without any stomping.
        FlushCodeBin();
        // A TM error from the TM step, or from the item-data write in SyncCodeBinToDisk or the flush. Reported once, here.
        if (LastTMError != null) progressCallback?.Invoke("WARNING: " + LastTMError, 94);

        // 9b. Starters (already processed before trainers)

        // 9c. In-game trades
        if (Settings.TradesMode > 0)
        {
            progressCallback?.Invoke("Randomizing In-Game Trades...", 93);
            RandomizeTrades(speciesRand);
        }

        // 9d. Overworld Field Items
        if (Settings.FieldItemsMode > 0)
        {
            progressCallback?.Invoke("Randomizing Overworld Field Items...", 95);
            RandomizeFieldItems();
        }

        // 10. Pickup items
        if (Settings.PickupItemsMode > 0)
        {
            progressCallback?.Invoke("Randomizing Pickup Items...", 97);
            RandomizePickup();
        }

        progressCallback?.Invoke("Universal Randomization finished successfully!", 100);
    }

    private string ShufflePwtPool()
    {
        try
        {
            var g = Config.GetGARCData("amxscript");
            if (g?.Files == null || g.Files.Length <= 217) return "WARNING: PWT shuffle skipped. This ROM has no script 217.";
            var fp = new pk3DS.Core.Modding.FacilityPools(g.Files[217], 217);
            if (pk3DS.Core.Modding.FacilityPoolShuffle.ShufflePwt(fp, Util.Rand) == 0) return null;
            g.Files[217] = fp.ToBytes();
            g.Save();
            return null;
        }
        catch (Exception ex)
        {
            return "WARNING: PWT shuffle skipped. " + ex.Message;
        }
    }

    private int[] RandomizeStarters(SpeciesRandomizer speciesRand)
    {
        const int stride = 0x14;
        try
        {
            var g = Config.GetGARCData("encounterstatic");
            if (g?.Files == null || g.Files.Length == 0) return null;

            byte[] data = g.Files[0];
            if (data == null || data.Length < 3 * stride) return null;

            int[] custom = [Settings.CustomStarter1, Settings.CustomStarter2, Settings.CustomStarter3];
            int[] currents = [BitConverter.ToUInt16(data, 0), BitConverter.ToUInt16(data, stride), BitConverter.ToUInt16(data, 2 * stride)];

            if (Settings.StartersMode == 0)
                return currents;

            // Custom starters are exactly what the user typed; a type restriction on top of that
            // would be overriding the one mode that exists to not be random.
            int[] chosen = Settings.StartersMode == 1
                ? custom
                : PickStarterTrio(speciesRand, currents);

            for (int i = 0; i < 3; i++)
            {
                int species = chosen[i];
                if (species <= 0 || species > Config.MaxSpeciesID) continue;

                int offset = i * stride;
                BitConverter.GetBytes((ushort)species).CopyTo(data, offset);
                data[offset + 2] = 0;   // base forme: an alternate forme here can be a species the game cannot show yet
            }

            g.Files[0] = data;
            g.Save();

            UpdateStarterStoryText(chosen);
            return chosen;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateStarterStoryText(int[] chosen)
    {
        if (chosen == null || chosen.Length < 3 || Config == null) return;
        try
        {
            string[] speciesNames = Config.GetText(TextName.SpeciesNames);
            string[] typeNames = Config.GetText(TextName.Types);

            if (Config.Generation == 7)
            {
                var storyGarc = Config.GetGARCData("storytext");
                if (storyGarc?.Files == null || storyGarc.Files.Length == 0) return;

                int textFileIndex = Config.USUM ? 41 : 39;
                if (textFileIndex >= storyGarc.Files.Length) return;

                string[] lines = TextFile.GetStrings(Config, storyGarc.Files[textFileIndex]);
                if (lines == null || lines.Length == 0) return;

                if (Config.USUM)
                {
                    // Descriptors (lines 1, 2, 3)
                    if (lines.Length > 3)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            int spec = chosen[i];
                            if (spec <= 0 || spec >= speciesNames.Length) continue;
                            string sName = speciesNames[spec];
                            int lineIdx = i + 1;
                            int sep = lines[lineIdx].IndexOfAny([':', ',']);
                            if (sep >= 0)
                                lines[lineIdx] = sName + lines[lineIdx].Substring(sep);
                            else
                                lines[lineIdx] = sName;
                        }
                    }

                    // Confirmation dialogues (lines 7, 8, 9) and Menu options (lines 14, 15, 16)
                    for (int i = 0; i < 3; i++)
                    {
                        int spec = chosen[i];
                        if (spec <= 0 || spec >= speciesNames.Length) continue;
                        string sName = speciesNames[spec];
                        int typeId = Config.Personal?.Table != null && spec < Config.Personal.Table.Length
                            ? Config.Personal.Table[spec].Types[0]
                            : 0;
                        string tName = typeId < typeNames.Length ? typeNames[typeId] : "Normal";

                        int confirmIdx = i + 7;
                        if (confirmIdx < lines.Length)
                        {
                            string orig = lines[confirmIdx];
                            string varTag = orig.Contains("[VAR") ? orig.Substring(orig.IndexOf("[VAR")) : "";
                            lines[confirmIdx] = $"So, you wanna go with the {tName}-type Pokémon\\n{sName}?{varTag}";
                        }

                        int optionIdx = i + 14;
                        if (optionIdx < lines.Length)
                        {
                            lines[optionIdx] = sName;
                        }
                    }
                }
                else if (Config.SM)
                {
                    // Descriptors (lines 11, 12, 13)
                    if (lines.Length > 13)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            int spec = chosen[i];
                            if (spec <= 0 || spec >= speciesNames.Length) continue;
                            string sName = speciesNames[spec];
                            int lineIdx = i + 11;
                            int sep = lines[lineIdx].IndexOfAny([':', ',']);
                            if (sep >= 0)
                                lines[lineIdx] = sName + lines[lineIdx].Substring(sep);
                            else
                                lines[lineIdx] = sName;
                        }
                    }

                    // Menu options (lines 1, 2, 3), Confirmations (lines 4, 5, 6), Flavor (lines 35, 36, 37)
                    for (int i = 0; i < 3; i++)
                    {
                        int spec = chosen[i];
                        if (spec <= 0 || spec >= speciesNames.Length) continue;
                        string sName = speciesNames[spec];
                        int typeId = Config.Personal?.Table != null && spec < Config.Personal.Table.Length
                            ? Config.Personal.Table[spec].Types[0]
                            : 0;
                        string tName = typeId < typeNames.Length ? typeNames[typeId] : "Normal";

                        int optionIdx = i + 1;
                        if (optionIdx < lines.Length)
                        {
                            lines[optionIdx] = $"The {tName}-type {sName}";
                        }

                        int confirmIdx = i + 4;
                        if (confirmIdx < lines.Length)
                        {
                            string orig = lines[confirmIdx];
                            string varTag = orig.Contains("[VAR") ? orig.Substring(orig.IndexOf("[VAR")) : "";
                            lines[confirmIdx] = $"Will you choose the {tName}-type Pokémon\\n{sName}?{varTag}";
                        }

                        int flavorIdx = i + 35;
                        if (flavorIdx < lines.Length && lines[flavorIdx].Contains("\\n"))
                        {
                            string flavorSub = lines[flavorIdx].Substring(lines[flavorIdx].IndexOf("\\n"));
                            lines[flavorIdx] = $"The {tName}-type {sName}{flavorSub}";
                        }
                    }
                }

                storyGarc.Files[textFileIndex] = TextFile.GetBytes(Config, lines);
                storyGarc.Save();
            }
            else if (Config.Generation == 6)
            {
                var gameTextGarc = Config.GARCGameText ?? Config.GetGARCData("gametext");
                if (gameTextGarc?.Files != null && gameTextGarc.Files.Length > 0)
                {
                    int textFileIndex = Config.XY ? 63 : 77;
                    if (textFileIndex < gameTextGarc.Files.Length)
                    {
                        string[] lines = TextFile.GetStrings(Config, gameTextGarc.Files[textFileIndex]);
                        if (lines != null && lines.Length > 3)
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                int spec = chosen[i];
                                if (spec <= 0 || spec >= speciesNames.Length) continue;
                                lines[i + 1] = speciesNames[spec];
                            }
                            gameTextGarc.Files[textFileIndex] = TextFile.GetBytes(Config, lines);
                            gameTextGarc.Save();
                        }
                    }
                }
            }
        }
        catch
        {
            // Non-critical, continue
        }
    }

    private static bool IsImportantTrainer(int index) => TrainerTiers.IsImportant(index);

    private void ApplyBattleStyle(TrainerData7 trainer)
    {
        if (trainer == null) return;

        BattleMode? want = Settings.BattleStyleMode switch
        {
            1 => Util.Rand.Next(2) == 0 ? BattleMode.Singles : BattleMode.Doubles,
            2 => Settings.BattleStyleChoice == 1 ? BattleMode.Doubles : BattleMode.Singles,
            _ => Settings.DoubleBattleMode ? BattleMode.Doubles : null,
        };
        if (want is not { } mode) return;

        // Leave multi battles alone whatever the setting says: they are scripted pairs, and
        // rewriting one to Singles orphans the partner trainer the script still expects.
        if (trainer.Mode == BattleMode.Multi) return;

        trainer.Mode = mode;

        if (mode == BattleMode.Doubles && trainer.Pokemon.Count == 1)
        {
            trainer.Pokemon.Add(trainer.Pokemon[0].Clone());
            trainer.NumPokemon = trainer.Pokemon.Count;
        }
    }

    private int PickTrainerSpecies(SpeciesRandomizer speciesRand, int original, int theme,
                                   bool isLegendarySlot = false, List<int> allowedLegendaries = null)
    {
        if (isLegendarySlot && allowedLegendaries != null && allowedLegendaries.Count > 0)
        {
            if (theme >= 0)
            {
                var typed = allowedLegendaries.Where(s => Config.Personal[s]?.Types?.Contains(theme) == true).ToList();
                if (typed.Count > 0) return typed[Util.Rand.Next(typed.Count)];
            }
            return allowedLegendaries[Util.Rand.Next(allowedLegendaries.Count)];
        }

        // A themed trainer's slots must land on the theme, so that wins over how the mode would
        // otherwise pick: an even-distribution draw that ignored the theme would simply undo it.
        if (theme >= 0)
            return speciesRand.GetRandomSpeciesType(original, theme);

        return Settings.TrainerPokemonMode switch
        {
            TrainerThemes.RandomEvenDistribution => speciesRand.GetEvenDistributionSpecies(),
            _ => speciesRand.GetMappedSpecies(original),
        };
    }

    private int RefineTrainerPick(SpeciesRandomizer speciesRand, int original, int chosen,
                                  HashSet<int> placed, HashSet<int> teamTypes, bool diverse, int theme = -1,
                                  bool isLegendarySlot = false, List<int> allowedLegendaries = null)
    {
        bool avoid = Settings.TrainerAvoidDuplicates;
        if (theme >= 0) diverse = false;
        if (!avoid && !diverse) return chosen;

        int Reroll()
        {
            if (isLegendarySlot && allowedLegendaries != null && allowedLegendaries.Count > 0)
            {
                if (theme >= 0)
                {
                    var typed = allowedLegendaries.Where(s => Config.Personal[s]?.Types?.Contains(theme) == true).ToList();
                    if (typed.Count > 0) return typed[Util.Rand.Next(typed.Count)];
                }
                return allowedLegendaries[Util.Rand.Next(allowedLegendaries.Count)];
            }
            if (theme >= 0)
                return speciesRand.GetRandomSpeciesType(original, theme);
            if (Settings.TrainerPokemonMode == TrainerThemes.RandomEvenDistribution)
                return speciesRand.GetEvenDistributionSpecies();
            return speciesRand.GetRandomSpecies(original);
        }

        bool InRange(int s) => s > 0 && s <= Config.MaxSpeciesID;
        bool NotDuplicate(int s) => !avoid || !placed.Contains(s);
        bool AddsType(int s) => !diverse || TypeRestrictions.TypesOf(Config, s).Any(t => !teamTypes.Contains(t));

        if (InRange(chosen) && NotDuplicate(chosen) && AddsType(chosen)) return chosen;

        // Both constraints first.
        for (int attempt = 0; attempt < 60; attempt++)
        {
            int c = Reroll();
            if (InRange(c) && NotDuplicate(c) && AddsType(c)) return c;
        }

        // Then just the duplicate rule, which is the one a player actually notices.
        if (avoid)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                int c = Reroll();
                if (InRange(c) && !placed.Contains(c)) return c;
            }
        }

        return chosen;
    }

    private int[] PickStarterTrio(SpeciesRandomizer speciesRand, int[] currents)
    {
        int Pick(int current) => Settings.StartersMode == 3
            ? PickStarterWithEvolutions(speciesRand, current)
            : speciesRand.GetRandomSpecies(current);

        bool Acceptable(int species) =>
            species > 0 && species <= Config.MaxSpeciesID &&
            (!Settings.StarterNoDualTypes || TypeRestrictions.IsMonoType(Config, species));

        // A species of a required type, or 0 when the pool has none after a bounded search.
        int PickOfType(int current, int type)
        {
            for (int attempt = 0; attempt < 300; attempt++)
            {
                int c = Pick(current);
                if (Acceptable(c) && TypeRestrictions.HasType(Config, c, type)) return c;
            }
            return 0;
        }

        int PickAny(int current)
        {
            for (int attempt = 0; attempt < 300; attempt++)
            {
                int c = Pick(current);
                if (Acceptable(c)) return c;
            }
            return speciesRand.GetRandomSpecies(current);
        }

        switch (Settings.StarterTypeRestriction)
        {
            case 1: // Fire, Water, Grass
            case 2: // any type triangle
            {
                var triangles = Settings.StarterTypeRestriction == 1
                    ? [[Competitive.PokemonTypes.Fire, Competitive.PokemonTypes.Water, Competitive.PokemonTypes.Grass]]
                    : TypeRestrictions.FindTypeTriangles();

                // Shuffled so a failed triangle is not retried in the same order every run.
                var order = triangles.OrderBy(_ => Util.Rand.Next()).ToList();
                foreach (var triangle in order)
                {
                    int[] picks = new int[3];
                    bool ok = true;
                    for (int i = 0; i < 3 && ok; i++)
                    {
                        picks[i] = PickOfType(currents[i], triangle[i]);
                        if (picks[i] == 0) ok = false;
                    }
                    if (ok) return picks;
                }
                break;
            }

            case 3: // unique - no two starters share a type
            {
                for (int attempt = 0; attempt < 400; attempt++)
                {
                    int[] picks = [PickAny(currents[0]), PickAny(currents[1]), PickAny(currents[2])];
                    if (!TypeRestrictions.SharesType(Config, picks[0], picks[1]) &&
                        !TypeRestrictions.SharesType(Config, picks[0], picks[2]) &&
                        !TypeRestrictions.SharesType(Config, picks[1], picks[2]))
                        return picks;
                }
                break;
            }

            case 4: // all three of one chosen type
            {
                int type = Settings.StarterSingleType;
                int[] picks = new int[3];
                bool ok = true;
                for (int i = 0; i < 3 && ok; i++)
                {
                    picks[i] = PickOfType(currents[i], type);
                    if (picks[i] == 0) ok = false;
                }
                if (ok) return picks;
                break;
            }
        }

        // No restriction, or none that could be met.
        return [PickAny(currents[0]), PickAny(currents[1]), PickAny(currents[2])];
    }

    private int PickStarterWithEvolutions(SpeciesRandomizer speciesRand, int current)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            int candidate = speciesRand.GetRandomSpecies(current);
            if (candidate > 0 && ChainLength(candidate) >= 3) return candidate;
        }
        return speciesRand.GetRandomSpecies(current);
    }

    private int ChainLength(int species)
    {
        int best = 1;
        try
        {
            if (Config.Evolutions == null || species < 0 || species >= Config.Evolutions.Length) return 1;
            var set = Config.Evolutions[species];
            if (set?.PossibleEvolutions == null) return 1;

            foreach (var evo in set.PossibleEvolutions)
            {
                if (evo == null || evo.Method == 0 || evo.Species <= 0 || evo.Species == species) continue;
                if (evo.Species >= Config.Evolutions.Length) continue;

                // One level deeper only: three stages is the question being asked, and following
                // arbitrary depth risks a cycle on a randomized evolution table.
                int next = 1;
                var nextSet = Config.Evolutions[evo.Species];
                if (nextSet?.PossibleEvolutions != null &&
                    nextSet.PossibleEvolutions.Any(e => e != null && e.Method != 0 && e.Species > 0 && e.Species != evo.Species))
                    next = 2;

                best = Math.Max(best, 1 + next);
            }
        }
        catch { return 1; }
        return best;
    }

    private void RandomizeTrades(SpeciesRandomizer speciesRand)
    {
        const int stride = 0x34;
        try
        {
            var g = Config.GetGARCData("encounterstatic");
            if (g?.Files == null || g.Files.Length <= 4) return;

            byte[] data = g.Files[4];
            if (data == null || data.Length < stride) return;

            int count = data.Length / stride;
            for (int i = 0; i < count; i++)
            {
                int offset = i * stride;
                if (offset + stride > data.Length) break;

                int given = BitConverter.ToUInt16(data, offset);
                if (given > 0 && given <= Config.MaxSpeciesID)
                {
                    int newGiven = speciesRand.GetRandomSpecies(given);
                    if (newGiven > 0 && newGiven <= Config.MaxSpeciesID)
                    {
                        BitConverter.GetBytes((ushort)newGiven).CopyTo(data, offset);
                        data[offset + 4] = 0;   // base forme
                    }
                }

                // Mode 1 changes only what you receive; mode 2 also changes what is asked for.
                if (Settings.TradesMode < 2) continue;

                int wanted = BitConverter.ToUInt16(data, offset + 0x2C);
                if (wanted <= 0 || wanted > Config.MaxSpeciesID) continue;

                int newWanted = speciesRand.GetRandomSpecies(wanted);
                if (newWanted > 0 && newWanted <= Config.MaxSpeciesID)
                    BitConverter.GetBytes((ushort)newWanted).CopyTo(data, offset + 0x2C);
            }

            g.Files[4] = data;
            g.Save();
        }
        catch
        {
            // As above: a malformed or missing trade file must not take the run down.
        }
    }

    private void RandomizePickup()
    {
        const int columns = 10;
        const int stride = columns + 2;

        try
        {
            var g = Config.GetlzGARCData("pickup");
            if (g == null || g.FileCount == 0) return;

            byte[] data = g[0];
            if (data == null || data.Length < 4) return;

            int rows = BitConverter.ToUInt16(data, 0) - 1;
            if (rows <= 0) return;

            var itemNames = Config.GetText(TextName.ItemNames);
            int maxItem = Config.Info.MaxItemID > 0 ? Config.Info.MaxItemID : 800;
            if (itemNames != null && itemNames.Length > 0)
                maxItem = Math.Min(maxItem, itemNames.Length - 1);
            if (maxItem < 1) return;

            // Pickup can hand this out from the first route, so no Z-Crystals here.
            var zFilter = new ZCrystalFilter(itemNames);

            bool Usable(int id)
            {
                if (Settings.PickupBanBadItems && Array.IndexOf(PickupBannedItems, id) >= 0) return false;
                if (zFilter.IsZCrystal(id)) return false;
                if (itemNames == null || id >= itemNames.Length) return true;
                string nm = itemNames[id]?.Trim();
                return !string.IsNullOrEmpty(nm) && nm is not ("???" or "-----");
            }

            for (int i = 0; i < rows; i++)
            {
                int offset = 4 + (i * stride);
                if (offset + 2 > data.Length) break;

                int item = 0;
                // Bounded rather than do/while: if a ROM has almost nothing nameable left, an
                // unbounded loop here would hang the whole randomization.
                for (int tries = 0; tries < 256; tries++)
                {
                    int candidate = (int)(Util.Random32() % (uint)maxItem) + 1;
                    if (!Usable(candidate)) continue;
                    item = candidate;
                    break;
                }
                if (item == 0) continue;   // leave the original entry alone

                BitConverter.GetBytes((ushort)item).CopyTo(data, offset);
            }

            g[0] = data;
            g.Save();
        }
        catch
        {
            // A missing or short pickup GARC must not take the whole randomization down with it;
            // every other step has already written by this point.
        }
    }

    private static readonly int[] PickupBannedItems =
        [0x1B, 0x4B, 0x4C, 0x4D, 0x12, 0x121, 0x122, 0x123, 0x124];

    private static int GenerateSeed(string seedStr)
    {
        if (string.IsNullOrWhiteSpace(seedStr))
            return Environment.TickCount;
            
        if (int.TryParse(seedStr, out int numSeed))
            return numSeed;
            
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(seedStr));
        return BitConverter.ToInt32(hash, 0);
    }

    private void ApplyFullTMCompatibility(PersonalInfo[] table)
    {
        // The located TM count (128 once expanded). _codeBin is null on Uhuru, where the stock table is 100.
        int maxTm = _codeBin == null ? 100 : (pk3DS.Core.Modding.TMTable.Locate(_codeBin, out _)?.Count ?? 100);
        for (int i = 1; i < table.Length; i++)
        {
            var tmhm = table[i].TMHM;
            if (tmhm == null)
                continue;

            for (int j = 0; j < Math.Min(maxTm, tmhm.Length); j++)
                tmhm[j] = true;

            table[i].TMHM = tmhm;
        }
    }

    private void ApplyFullHMCompatibility(PersonalInfo[] table)
    {
        if (Config.Generation != 6) return;
        for (int i = 1; i < table.Length; i++)
        {
            var tmhm = table[i].TMHM;
            if (tmhm == null || tmhm.Length <= 100)
                continue;

            for (int j = 100; j < tmhm.Length; j++)
                tmhm[j] = true;

            table[i].TMHM = tmhm;
        }
    }

    private static bool TryGetRivalStarter(int trainerIndex, out int starterIndex)
    {
        // Hau's variants (variant 0: Grass player, variant 1: Fire player, variant 2: Water player)
        // Hau takes the weak starter: (variant + 2) % 3
        int[] hauBases = [491, 12, 74, 77, 215, 218, 438, 447, 450, 494, 350, 356];
        foreach (int b in hauBases)
        {
            if (trainerIndex >= b && trainerIndex <= b + 2)
            {
                int variant = trainerIndex - b;
                starterIndex = (variant + 2) % 3;
                return true;
            }
        }

        // Kukui's variants (variant 0, 1, 2)
        // Kukui takes the strong starter: (variant + 1) % 3
        int[] kukuiBases = [477];
        foreach (int b in kukuiBases)
        {
            if (trainerIndex >= b && trainerIndex <= b + 2)
            {
                int variant = trainerIndex - b;
                starterIndex = (variant + 1) % 3;
                return true;
            }
        }

        starterIndex = -1;
        return false;
    }

    private int GetEvolvedStarterForLevel(int baseStarter, int level)
    {
        if (baseStarter <= 0 || baseStarter > Config.MaxSpeciesID) return baseStarter;
        var evos = Config.Evolutions;
        if (evos == null || baseStarter >= evos.Length) return baseStarter;

        int stage = level >= 34 ? 2 : (level >= 16 ? 1 : 0);
        int current = baseStarter;
        for (int i = 0; i < stage; i++)
        {
            if (current >= evos.Length) break;
            var next = evos[current]?.PossibleEvolutions?.FirstOrDefault(e => e != null && e.Species > 0 && e.Species != current);
            if (next == null) break;
            current = next.Species;
        }
        return current;
    }

    private void RandomizeFieldItems()
    {
        if (Config.Generation != 7) return;
        try
        {
            var ed = Config.GetlzGARCData("encdata");
            if (ed == null || ed.FileCount < 11) return;

            var itemNames = Config.GetText(TextName.ItemNames);
            int maxItem = Config.Info.MaxItemID > 0 ? Config.Info.MaxItemID : 800;
            if (itemNames != null && itemNames.Length > 0)
                maxItem = Math.Min(maxItem, itemNames.Length - 1);
            var zFilter = new ZCrystalFilter(itemNames);

            // Collect all existing field items if in shuffle mode
            var collectedItems = new List<ushort>();
            int worldCount = ed.FileCount / 11;

            if (Settings.FieldItemsMode == 1) // Shuffle
            {
                for (int w = 0; w < worldCount; w++)
                {
                    int index = w * 11;
                    if (index >= ed.FileCount) break;
                    byte[] edFile = ed[index];
                    if (edFile == null || edFile.Length < 4) continue;
                    byte[][] envData = Mini.UnpackMini(edFile, "ED");
                    if (envData == null || envData.Length <= 10) continue;
                    byte[][] itemDataFull = Mini.UnpackMini(envData[10], "EI");
                    if (itemDataFull == null || itemDataFull.Length == 0) continue;

                    foreach (var itemData in itemDataFull)
                    {
                        if (itemData == null || itemData.Length < 4) continue;
                        int count = BitConverter.ToInt32(itemData, 0);
                        for (int i = 0; i < count; i++)
                        {
                            int dataId = 4 + i * 64 + 48;
                            if (dataId + 2 <= itemData.Length)
                            {
                                ushort it = BitConverter.ToUInt16(itemData, dataId);
                                if (it > 0) collectedItems.Add(it);
                            }
                        }
                    }
                }
                collectedItems = collectedItems.OrderBy(_ => Util.Rand.Next()).ToList();
            }

            int shuffleIdx = 0;
            for (int w = 0; w < worldCount; w++)
            {
                int index = w * 11;
                if (index >= ed.FileCount) break;
                byte[] edFile = ed[index];
                if (edFile == null || edFile.Length < 4) continue;
                byte[][] envData = Mini.UnpackMini(edFile, "ED");
                if (envData == null || envData.Length <= 10) continue;
                byte[][] itemDataFull = Mini.UnpackMini(envData[10], "EI");
                if (itemDataFull == null || itemDataFull.Length == 0) continue;

                bool modified = false;
                foreach (var itemData in itemDataFull)
                {
                    if (itemData == null || itemData.Length < 4) continue;
                    int count = BitConverter.ToInt32(itemData, 0);
                    for (int i = 0; i < count; i++)
                    {
                        int dataId = 4 + i * 64 + 48;
                        if (dataId + 2 <= itemData.Length)
                        {
                            ushort currentItem = BitConverter.ToUInt16(itemData, dataId);
                            if (currentItem == 0) continue;

                            ushort newItem = currentItem;
                            if (Settings.FieldItemsMode == 1) // Shuffle
                            {
                                if (shuffleIdx < collectedItems.Count)
                                    newItem = collectedItems[shuffleIdx++];
                            }
                            else // Random
                            {
                                newItem = (ushort)GetRandomFieldItem(maxItem, zFilter, itemNames);
                            }

                            BitConverter.GetBytes(newItem).CopyTo(itemData, dataId);
                            modified = true;
                        }
                    }
                }

                if (modified)
                {
                    envData[10] = Mini.PackMini(itemDataFull, "EI");
                    ed[index] = Mini.PackMini(envData, "ED");
                }
            }

            ed.Save();
        }
        catch
        {
            // A missing or malformed encdata GARC must not halt the overall process
        }
    }

    private int GetRandomFieldItem(int maxItem, ZCrystalFilter zFilter, string[] itemNames)
    {
        for (int tries = 0; tries < 200; tries++)
        {
            int item = Util.Rand.Next(1, maxItem + 1);
            if (zFilter != null && zFilter.IsZCrystal(item)) continue;
            if (Legal.Pouch_Key_SM.Contains((ushort)item)) continue;
            if (Settings.FieldItemsBanBadItems)
            {
                if (Array.IndexOf(PickupBannedItems, item) >= 0) continue;
            }
            if (itemNames != null && item < itemNames.Length)
            {
                string nm = itemNames[item]?.Trim();
                if (string.IsNullOrEmpty(nm) || nm is "???" or "-----") continue;
            }
            return item;
        }
        return 1; // Master Ball fallback
    }

    private static void ApplyFullTutorCompatibility(PersonalInfo[] table)
    {
        for (int i = 1; i < table.Length; i++)
        {
            if (table[i] is pk3DS.Core.Structures.PersonalInfo.PersonalInfoSM sm)
            {
                for (int j = 0; j < sm.TutorFlags.Length; j++)
                    sm.TutorFlags[j] = true;
                continue;
            }

            if (table[i].SpecialTutors == null)
                continue;

            foreach (var tutorGroup in table[i].SpecialTutors)
            {
                if (tutorGroup == null)
                    continue;

                for (int j = 0; j < tutorGroup.Length; j++)
                    tutorGroup[j] = true;
            }
        }
    }

    private string ApplyCheapRareCandies()
    {
        try
        {
            var names = Config.GetText(TextName.ItemNames);
            int candy = Array.FindIndex(names, n => string.Equals(n?.Trim(), "Rare Candy", StringComparison.OrdinalIgnoreCase));
            if (candy <= 0) return "WARNING: Rare Candy was not found in the item names, so nothing was changed.";

            // --- price ---
            int price = Math.Max(0, Settings.CheapRareCandyPrice);
            var itemGarc = Config.GetGARCData("item");
            var files = itemGarc.Files;
            if (candy >= files.Length) return $"WARNING: Rare Candy (id {candy}) is outside the item table.";

            var record = new pk3DS.Core.Structures.Item(files[candy]) { BuyPrice = price };
            files[candy] = record.Write();
            itemGarc.Save();

            // --- stocking ---
            string croPath = Path.Combine(RomFSPath, "Shop.cro");
            if (!File.Exists(croPath))
                return $"Rare Candy now costs {price}. Shop.cro was not found, so no mart was stocked.";

            byte[] cro = File.ReadAllBytes(croPath);
            var layout = MartLayout.Read(cro);
            if (!layout.Valid)
                return $"Rare Candy now costs {price}. Shop.cro's shop table could not be read, so no mart was stocked.";

            var lists = new List<int[]>();
            int stocked = 0, already = 0;
            for (int i = 0; i < layout.ShopOffsets.Length; i++)
            {
                var current = new List<int>();
                for (int k = 0; k < layout.ShopCounts[i]; k++)
                {
                    int o = layout.ShopOffsets[i] + (k * 2);
                    if (o + 1 < cro.Length) current.Add(BitConverter.ToUInt16(cro, o));
                }

                if (current.Contains(candy)) already++;
                else { current.Add(candy); stocked++; }

                lists.Add([.. current]);
            }

            if (pk3DS.Core.Modding.UhuruGuard.RefusalFor(croPath) is { } uhuruShop) return "WARNING: cheap Rare Candies skipped. " + uhuruShop;
            byte[] rebuilt = MartLayout.Rebuild(cro, lists, [], out _);
            var after = MartLayout.Read(rebuilt);
            if (!after.Valid || after.Validate(rebuilt).Count > 0)
                return $"Rare Candy now costs {price}, but the rewritten Shop.cro did not validate, so it was not saved.";

            pk3DS.Core.CTR.CROUtil.SaveCro(croPath, rebuilt);
            return $"Rare Candy now costs {price} and is stocked in {stocked} mart(s) ({already} already sold it).";
        }
        catch (Exception ex)
        {
            return $"WARNING: cheap Rare Candies could not be applied ({ex.GetType().Name}: {ex.Message}).";
        }
    }

    private List<string> InstallSelectedRecipes()
    {
        var log = new List<string>();
        try
        {
            string version = pk3DS.Core.Modding.Research.ResearchVersion.Resolve(Config);
            string battleCro = Path.Combine(RomFSPath ?? Config?.RomFS ?? "", "Battle.cro");
            if (!File.Exists(battleCro))
            {
                log.Add("WARNING: Battle.cro was not found, so no patches were installed.");
                return log;
            }

            // NOTE: a pre-expansion step was tried here (CROUtil.ExpandSegment('c', 0x10000) before
            // the install loop) to fix "reserve exhausted" refusals for large InstallPatches
            // selections. Reverted 2026-09-12 after direct testing showed it actively introduces the
            // relocation corruption that crashes the game in battle. The exact cause is still
            // unproven; see docs/uhuru/cro-growth-diagnosis.md. Do not restore blanket growth here.

            var db = pk3DS.Core.Modding.Research.ResearchDatabase.LoadEmbedded(version);
            var map = pk3DS.Core.Modding.Research.BattleMechanicMap.Build(File.ReadAllBytes(battleCro), db, battleCro);
            var book = pk3DS.Core.Modding.Research.Recipes.Discover(db, version);

            int done = 0, skipped = 0;
            bool warnedAboutSpace = false;

            // Ids this loop has already handed out, even though the id-name table on disk will not
            // show them as taken until Apply for that recipe actually returns. Without this, two
            // recipes early in a long InstallPatches list can both be offered the same "free" id if
            // GameConfig's in-memory text tables do not turn around between iterations as fast as
            // this loop does - the same reserved-set fix RunRecipeBatch uses in the Research Center,
            // here for the exact same reason: an unattended randomizer run has no one watching to
            // notice a silent collision.
            var reserved = new HashSet<int>();
            var installedNames = new List<string>();
#nullable enable
            byte[]? codeBinNow = null;
            try
            {
                string? exeFs = Config?.ExeFS;
                if (exeFs != null)
                {
                    string codePath = pk3DS.Core.CTR.ExeFS.ResolveCodeBin(exeFs);
                    if (File.Exists(codePath)) codeBinNow = File.ReadAllBytes(codePath);
                }
            }
            catch { /* best-effort; a failed read just skips the on-disk half of the check */ }
#nullable restore

            foreach (string name in Settings.InstallPatches)
            {
                var recipe = book.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (recipe == null)
                {
                    log.Add($"WARNING: '{name}' is not available on this build and was skipped.");
                    skipped++;
                    continue;
                }

                // PC Anywhere and Move Relearner Replaces Cafe each overwrite LIVE pristine code.bin
                // functions at their own injection site (confirmed: neither's trampoline lands in
                // free space), so each corrupts the base game even installed completely alone -
                // observed as Evolution-sequence crashes. Checked unconditionally, before the
                // pairwise conflict below.
                if (pk3DS.Core.Modding.Research.RecipeConflicts.TryFindUnsafe(name, out string unsafeReason))
                {
                    log.Add($"WARNING: '{name}' was skipped - unsafe on its own: {unsafeReason}");
                    skipped++;
                    continue;
                }

                // A recipe can pass every byte-range/branch-target check on its own and still corrupt
                // a sibling that's already installed, if it patches the body of a vanilla function the
                // sibling calls into by address (see RecipeConflicts - confirmed for PC Anywhere +
                // Move Relearner Replaces Cafe, which together crash the Bag). Checked against names
                // actually installed so far this run AND the current on-disk code.bin (catches the
                // other side having been installed manually before this randomizer run), so installing
                // just one side of a known-bad pair is never blocked by the other merely being requested.
                if (pk3DS.Core.Modding.Research.RecipeConflicts.TryFindConflict(name, installedNames, codeBinNow, out string conflictReason))
                {
                    log.Add($"WARNING: '{name}' was skipped - {conflictReason}");
                    skipped++;
                    continue;
                }

                // Ids for anything that claims a slot. Shared with the Research Center so an
                // unattended install places things exactly where the editor would have.
                var problems = new List<string>();
                var values = pk3DS.Core.Modding.Research.RecipeIdAllocator.AssignIds(recipe, Config, problems, reserved);
                if (problems.Count > 0)
                {
                    log.Add($"WARNING: '{name}' was skipped - {problems[0]}.");
                    skipped++;
                    continue;
                }

                var plan = pk3DS.Core.Modding.Research.RecipeInstaller.Plan(recipe, Config, battleCro, db, map, values);
                if (!plan.Ok)
                {
                    log.Add($"WARNING: '{name}' was refused: " +
                            (plan.Describe().Split('\n').LastOrDefault(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase))?.Trim()
                             ?? "see the Research Center for details"));
                    skipped++;
                    continue;
                }

                var applied = pk3DS.Core.Modding.Research.RecipeInstaller.Apply(recipe, Config, battleCro, db, map, values);
                if (applied.Ok)
                {
                    done++;
                    installedNames.Add(name);
                }
                else
                {
                    skipped++;
                    var errors = applied.Describe().Split('\n')
                        .Where(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                        .Select(l => l.Replace("ERROR:", "").Trim())
                        .Where(l => l.Length > 0)
                        .ToList();
                    string why = errors.FirstOrDefault();
                    log.Add($"WARNING: '{name}' was not installed" + (why != null ? $": {why}" : "."));

                    bool spaceProblem = why != null &&
                        (why.Contains("reserve exhausted", StringComparison.OrdinalIgnoreCase) ||
                         why.Contains("structural verification", StringComparison.OrdinalIgnoreCase));
                    if (spaceProblem && !warnedAboutSpace)
                    {
                        warnedAboutSpace = true;
                        log.Add("NOTE: Battle.cro is out of room for more item packages (a stock one fits about four). " +
                                "Installing them one at a time does not help - enable Options: Allow code.bin and CRO growth to expand its code segment.");
                    }
                }

                // The map describes the binary as it was, so it is rebuilt between recipes; the
                // next one has to see what the last one wrote.
                if (applied.Ok && File.Exists(battleCro))
                    map = pk3DS.Core.Modding.Research.BattleMechanicMap.Build(File.ReadAllBytes(battleCro), db, battleCro);
            }

            log.Add($"Patches installed: {done} of {Settings.InstallPatches.Count}" +
                    (skipped > 0 ? $" ({skipped} skipped)" : "") + ".");
        }
        catch (Exception ex)
        {
            log.Add($"WARNING: patches could not be installed ({ex.GetType().Name}: {ex.Message}).");
        }
        return log;
    }

    private bool ApplyExpMultiplierShared(int multiplier, List<string> log)
    {
        if (multiplier < 1 || multiplier > 255) return false;
        if (_codeBin == null || _codeBin.Length < 0x100000)
        {
            log?.Add("WARNING: code.bin not loaded; EXP multiplier patch skipped.");
            return false;
        }
        return ApplyExpMultiplier(_codeBin, Config, multiplier, log, markDirty: () => _codeBinDirty = true);
    }

    public static bool ApplyExpMultiplier(GameConfig config, int multiplier, List<string> log)
    {
        if (config == null || multiplier < 1 || multiplier > 255) return false;
        string codePath = pk3DS.Core.CTR.ExeFS.ResolveCodeBin(config.ExeFS);
        if (string.IsNullOrEmpty(codePath) || !File.Exists(codePath))
        {
            log?.Add("WARNING: ExeFS/code.bin not found; EXP multiplier patch skipped.");
            return false;
        }
        if (pk3DS.Core.Modding.UhuruGuard.RefusalFor(codePath) is { } uhuruExp)
        {
            log?.Add("WARNING: EXP multiplier skipped. " + uhuruExp);
            return false;
        }
        byte[] code = File.ReadAllBytes(codePath);
        bool ok = ApplyExpMultiplier(code, config, multiplier, log, markDirty: null);
        if (ok) File.WriteAllBytes(codePath, code);
        return ok;
    }

    private static bool ApplyExpMultiplier(byte[] code, GameConfig config, int multiplier, List<string> log, Action markDirty)
    {
        string v = pk3DS.Core.Modding.Research.ResearchVersion.Resolve(config);
        int hookOfs;
        if (v == "US")
            hookOfs = 0x3AB188;
        else if (v == "UM")
            hookOfs = 0x3AB190;
        else
        {
            log?.Add($"WARNING: EXP Multiplier patch is not supported for {v}.");
            return false;
        }

        byte multByte = (byte)multiplier;
        byte[] payloadBytes = [
            0xB2, 0x02, 0xD0, 0xE1,     // ldrh r0, [r0, #0x2b2]
            0x02, 0x40, 0x2D, 0xE9,     // push {r1, lr}
            multByte, 0x10, 0xA0, 0xE3, // mov r1, #multiplier
            0x90, 0x01, 0x00, 0xE0,     // mul r0, r0, r1
            0x02, 0x80, 0xBD, 0xE8      // pop {r1, pc}
        ];

        // The one allocator picks the place (first-fit in .text; on Uhuru it keeps out of Uhuru's code).
        var grant = pk3DS.Core.Modding.Research.CodeSpaceAllocator.ForCodeBin(code).Allocate(payloadBytes.Length, "EXP multiplier");
        int payloadOfs = grant.Success ? (int)grant.Offset : -1;

        if (payloadOfs < 0 || hookOfs + 4 > code.Length || payloadOfs + payloadBytes.Length > code.Length)
        {
            log?.Add("WARNING: code.bin has no free space for EXP multiplier payload.");
            return false;
        }

        // Encode BL from hook to the dynamically chosen payload address.
        uint blInst = pk3DS.Core.Modding.CodeBinPatcher.SynthesizeBranch((uint)hookOfs, (uint)payloadOfs, link: true);
        byte[] branchBytes = BitConverter.GetBytes(blInst);

        Array.Copy(branchBytes, 0, code, hookOfs, 4);
        Array.Copy(payloadBytes, 0, code, payloadOfs, payloadBytes.Length);
        markDirty?.Invoke();
        log?.Add($"EXP Multiplier: {multiplier}x applied at 0x{payloadOfs:X} in code.bin.");
        return true;
    }

    private string InstallTypeEffectiveness()
    {
        try
        {
            byte[] code = _codeBin;
            if (code == null || code.Length == 0)
                return "WARNING: code.bin was not loaded, so the type chart was not changed.";

            int offset = TypeEffectivenessTable.Locate(code);
            if (offset < 0)
                return "WARNING: the type chart could not be found in code.bin, so it was not changed.";

            var original = TypeEffectivenessTable.Read(code, offset);
            var log = new List<string>();
            var updated = TypeEffectivenessRandomizer.Apply(
                original, Settings.TypeEffectivenessMode,
                Settings.TypeEffectivenessAddRandomImmunities, log);

            var problems = updated.Validate();
            if (problems.Count > 0)
                return $"WARNING: the new type chart was rejected ({problems[0]}); nothing was changed.";

            updated.WriteTo(code, offset);
            _codeBinDirty = true;

            return log.Count > 0 ? log[0] : "Type chart rewritten.";
        }
        catch (Exception ex)
        {
            return $"WARNING: the type chart could not be changed ({ex.GetType().Name}: {ex.Message}).";
        }
    }

    private string InstallLevelCaps()
    {
        try
        {
            string croDir = !string.IsNullOrWhiteSpace(RomFSPath) ? RomFSPath : (Config?.RomFS ?? "");
            string croPath = Path.Combine(croDir, "Battle.cro");

            if (!File.Exists(croPath))
                return "WARNING: Battle.cro was not found, so level caps were not installed.";

            byte[] cro = File.ReadAllBytes(croPath);
            // Use the shared code.bin buffer rather than reading from disk independently.
            byte[] code = _codeBin;

            bool ultraMoon = string.Equals(
                pk3DS.Core.Modding.Research.ResearchVersion.Resolve(Config), "UM", StringComparison.OrdinalIgnoreCase);

            var stock = pk3DS.Core.Modding.Research.LevelCapTable.Default(ultraMoon);
            pk3DS.Core.Modding.Research.LevelCapTable table;

            if (Settings.LevelCapCaps is { Count: > 0 } caps && caps.Count == stock.Entries.Count)
            {
                for (int i = 0; i < caps.Count; i++)
                    stock.Entries[i] = stock.Entries[i] with { Cap = (byte)Math.Clamp(caps[i], 1, pk3DS.Core.Modding.Research.LevelCapTable.HardCeiling) };
                table = stock;
            }
            else if (Settings.LevelCapMatchTrainers)
            {
                var levels = pk3DS.Core.Modding.Research.TrainerLevelSampler.Collect(Config);
                table = pk3DS.Core.Modding.Research.LevelCapPresets.BuildFromTrainerLevels(
                    levels, (byte)Settings.LevelCapFinal, ultraMoon);
            }
            else
            {
                table = pk3DS.Core.Modding.Research.LevelCapPresets.Build(Settings.LevelCapShift, (byte)Settings.LevelCapFinal, ultraMoon);
            }
            pk3DS.Core.Modding.Research.LevelCapSettings.Table = table;

            if (code != null && !pk3DS.Core.Modding.Research.CodeSpaceBudget.Measure(code).Fits(pk3DS.Core.Modding.Research.CodeSpaceBudget.LevelCapBytes))
            {
                return "WARNING: " + pk3DS.Core.Modding.Research.CodeSpaceBudget.ExplainShortfall(
                    code, pk3DS.Core.Modding.Research.CodeSpaceBudget.LevelCapBytes, "Level caps");
            }

            if (pk3DS.Core.Modding.UhuruGuard.RefusalFor(croPath) is { } uhuruCap) return "WARNING: level caps skipped. " + uhuruCap;
            var sites = pk3DS.Core.Modding.Research.LevelCapPatch.Install(cro, code, table);
            if (sites.Count == 0)
                return "WARNING: no level cap hook site was found, so nothing was installed.";

            pk3DS.Core.CTR.CROUtil.SaveCro(croPath, cro);
            if (code != null) _codeBinDirty = true;

            return $"Level caps installed: {sites.Count} site(s), {table.Entries.Count} checkpoints, final cap {Settings.LevelCapFinal}.";
        }
        catch (Exception ex)
        {
            return $"WARNING: level caps could not be installed ({ex.GetType().Name}: {ex.Message}).";
        }
    }

    private ushort[] RandomizeTMMoveTable()
    {
        LastTMError = null;
        byte[] code = _codeBin;
        // Uhuru: EnsureCodeBin refuses the shared (writable) buffer, but the TM table is still read for trainer movesets.
        bool readOnly = code == null && pk3DS.Core.Modding.UhuruGuard.Active && File.Exists(_codeBinPath);
        if (readOnly) code = File.ReadAllBytes(_codeBinPath);
        if (code == null || code.Length < 0x100000)
        {
            if (Settings.ExpandTMs) LastTMError = "TM expansion skipped: code.bin is missing or too small.";
            return null;
        }

        var t = pk3DS.Core.Modding.TMTable.Locate(code, out string err);
        if (t == null)
        {
            LastTMError = err;
            return null;
        }

        ushort[] tmlist = t.ReadMoves(code);
        if (readOnly)
        {
            if (Settings.ExpandTMs)
                LastTMError = "TM expansion skipped on Uhuru: the TM table is read-only here.";
            return tmlist;
        }
        if (Settings.TMHMMovesMode == 0 && !Settings.ExpandTMs)
            return tmlist;

        // The pool the TM slots are drawn from. Expansion's new slots draw from the same pool.
        List<int> pool;
        if (Settings.TMHMMovesMode > 0)
        {
            var moveNames = Config.GetText(TextName.MoveNames);
            int maxMoveID = Config.Info.MaxMoveID;

            List<int> validMoves;
            if (Settings.CompetitiveRandomizer)
            {
                validMoves = Enumerable.Range(1, Math.Min(maxMoveID, moveNames.Length - 1))
                    .Where(m => Legal.IsUsableMove(m, moveNames) &&
                                (Competitive.CompetitiveDatabase.CompetitiveMoves.Contains(moveNames[m]) ||
                                 Competitive.CompetitiveDatabase.SituationalMoves.Contains(moveNames[m])))
                    .Distinct()
                    .ToList();
            }
            else
            {
                validMoves = Enumerable.Range(1, Math.Min(maxMoveID, moveNames.Length - 1))
                    .Where(m => Legal.IsUsableMove(m, moveNames))
                    .Distinct()
                    .ToList();

                if (Settings.TMHMMovesMode == 2)
                {
                    HashSet<int> banned = [12, 32, 90, 105, 166, 329, 462, 479];
                    validMoves = validMoves.Where(m => !banned.Contains(m)).ToList();
                }
            }

            if (validMoves.Count < t.Count)
            {
                var fallbackMoves = Enumerable.Range(1, Math.Min(maxMoveID, moveNames.Length - 1))
                    .Where(m => Legal.IsUsableMove(m, moveNames) && !validMoves.Contains(m))
                    .Distinct()
                    .ToList();
                Util.Shuffle(fallbackMoves);
                validMoves.AddRange(fallbackMoves.Take(t.Count - validMoves.Count));
            }

            if (validMoves.Count > 0)
            {
                Util.Shuffle(validMoves);
                for (int i = 0; i < t.Count; i++)
                {
                    tmlist[i] = (ushort)validMoves[i % validMoves.Count];
                }
            }
            pool = validMoves;

            t.Write(code, tmlist, t.ReadItems(code));
            _codeBinDirty = true;
        }
        else
        {
            // Mode 0 is "Unchanged": the vanilla 100 stay put (shuffling them used to re-point every
            // species' TM compatibility bits at different moves); only the new slots are filled.
            var moveNames = Config.GetText(TextName.MoveNames);
            int maxMoveID = Config.Info.MaxMoveID;
            pool = Enumerable.Range(1, Math.Min(maxMoveID, moveNames.Length - 1))
                .Where(m => Legal.IsUsableMove(m, moveNames))
                .Distinct()
                .ToList();
        }

        if (!Settings.ExpandTMs)
            return tmlist;
        if (t.Layout != pk3DS.Core.Modding.TMTable.Kind.Stock)
        {
            LastTMError = $"TM expansion skipped: the TM table is already expanded ({t.Count} TMs).";
            return tmlist;
        }

        // The new TMs take moves from the same pool, skipping the ones the table already holds.
        int target = pk3DS.Core.Modding.TMExpansion.EffectiveMax; // 104 while passive abilities own TM bits 104-127
        int need = target - t.Count;
        if (need <= 0) { LastTMError = $"TM expansion skipped: the TM table already holds {t.Count} TMs, the most allowed right now ({target})."; return tmlist; }
        var used = new HashSet<int>(tmlist.Select(m => (int)m));
        var fresh = pool.Where(m => !used.Contains(m)).ToList();
        if (fresh.Count < need)
        {
            LastTMError = $"TM expansion skipped: {fresh.Count} unused moves in the pool, {need} needed.";
            return tmlist;
        }
        Util.Shuffle(fresh);
        ushort[] newMoves = fresh.Take(need).Select(m => (ushort)m).ToArray();

        byte[] expanded = pk3DS.Core.Modding.Research.ExpandedTMItems.ExpandCode(
            Config, code, target, newMoves, out ushort[] newItemIds, out string error);
        if (expanded == null)
        {
            LastTMError = "TM expansion skipped: " + error;
            return tmlist;
        }

        // ExpandCode returns a fresh buffer of the same length; copy it over the shared one.
        if (_codeBin.Length == expanded.Length) Array.Copy(expanded, _codeBin, expanded.Length);
        else _codeBin = expanded;
        _codeBinDirty = true;
        _pendingTMItemIds = newItemIds;
        _pendingTMMoves = newMoves;
        _pendingTMFirstSlot = t.Count;

        return tmlist.Concat(newMoves).ToArray();
    }

    public string LastTMError { get; private set; }

    // Finds which ability slot (1, 2, or 3=Hidden) holds the given ability ID on this species,
    // or 0 if none of its slots do.
    private static int FindAbilitySlot(PersonalInfo pi, int abilityId)
    {
        if (pi?.Abilities == null || abilityId <= 0) return 0;
        for (int i = 0; i < pi.Abilities.Length && i < 3; i++)
        {
            if (pi.Abilities[i] == abilityId) return i + 1;
        }
        return 0;
    }

    public ushort GetFinalEvolution(int species)
    {
        if (species <= 0) return 1;

        var legendarySet = Legal.GetLegendaryAndMythicalSet();
        if (legendarySet.Contains(species)) return (ushort)species;

        var finalEvos = Config?.Generation == 6 ? Legal.FinalEvolutions_6 : Legal.FinalEvolutions_7;
        if (finalEvos.Contains(species)) return (ushort)species;

        if (Config?.Evolutions != null && species < Config.Evolutions.Length)
        {
            var evoSet = Config.Evolutions[species];
            bool hasValidEvolutions = evoSet?.PossibleEvolutions != null &&
                evoSet.PossibleEvolutions.Any(e => e.Species > 0 && e.Species != species && e.Method > 0);

            if (!hasValidEvolutions)
                return (ushort)species;

            int current = species;
            var visited = new HashSet<int> { current };

            while (current < Config.Evolutions.Length)
            {
                if (finalEvos.Contains(current) || legendarySet.Contains(current))
                    return (ushort)current;

                var curEvoSet = Config.Evolutions[current];
                if (curEvoSet?.PossibleEvolutions == null) break;

                var evo = curEvoSet.PossibleEvolutions.FirstOrDefault(e => e.Species > 0 && e.Species != current && e.Method > 0);
                if (evo == null || visited.Contains(evo.Species)) break;

                current = evo.Species;
                visited.Add(current);
            }
            if (current > 0) return (ushort)current;
        }

        // If species is beyond the vanilla range and has no evolution entry, it is standalone/final
        if (species > 807) return (ushort)species;

        // Deterministic fallback: map to a final evolution species from the game's final evolution list
        int idx = Math.Abs(species * 37) % finalEvos.Length;
        return (ushort)finalEvos[idx];
    }
}
