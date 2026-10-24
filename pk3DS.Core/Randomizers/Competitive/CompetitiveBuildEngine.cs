using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;

namespace pk3DS.Core.Randomizers.Competitive;

public class CompetitiveBuildEngine
{
    private readonly GameConfig Config;
    private readonly LegalLearnsetAggregator LearnsetAggregator;
    private readonly Random Rnd = Util.Rand;

    // Stat index constants for PersonalInfo.Stats: [HP, ATK, DEF, SPE, SPA, SPD]
    private const int HP = CompetitiveDatabase.HP;
    private const int ATK = CompetitiveDatabase.ATK;
    private const int DEF = CompetitiveDatabase.DEF;
    private const int SPE = CompetitiveDatabase.SPE;
    private const int SPA = CompetitiveDatabase.SPA;
    private const int SPD = CompetitiveDatabase.SPD;

    // EV index constants for TrainerPoke7.EVs: [HP, ATK, DEF, SPA, SPD, SPE]
    private const int EV_HP = 0, EV_ATK = 1, EV_DEF = 2, EV_SPA = 3, EV_SPD = 4, EV_SPE = 5;

    private string[] _cachedMoveNames;
    public string[] GetMoveNames() => _cachedMoveNames ??= (Config?.GetText(TextName.MoveNames) ?? Array.Empty<string>());

    public CompetitiveBuildEngine(GameConfig config)
    {
        Config = config;
        LearnsetAggregator = new LegalLearnsetAggregator(config);
    }

    public LegalLearnsetAggregator Learnsets => LearnsetAggregator;

    /// <summary>
    /// Resolves a trainer Pokemon's ability *slot index* (0-3, matching TrainerPoke7.Ability)
    /// to the actual ability name via the species' PersonalInfo.Abilities array.
    /// </summary>
    public static string ResolveAbilityName(PersonalInfo pi, int abilitySlot, string[] abilityNames)
    {
        if (pi?.Abilities == null || pi.Abilities.Length == 0 || abilityNames == null) return "";
        int arrIdx = abilitySlot switch
        {
            1 => 0,
            2 => pi.Abilities.Length > 1 ? 1 : 0,
            3 => pi.Abilities.Length > 2 ? 2 : 0,
            _ => 0, // "Any" (0) — resolve to Ability 1 for naming purposes; the caller should
                    // have already committed to a concrete slot before this is used for building.
        };
        if (arrIdx >= pi.Abilities.Length) arrIdx = 0;
        int abilId = pi.Abilities[arrIdx];
        return (abilId > 0 && abilId < abilityNames.Length) ? (abilityNames[abilId] ?? "") : "";
    }

    /// <summary>
    /// Picks the best ability slot (1, 2, or 3=Hidden — all fully eligible) for a competitive
    /// trainer Pokemon, preferring higher-tier competitive abilities. Returns a concrete slot
    /// (never 0/"Any") so downstream moveset/item logic has a definite ability to build around.
    /// </summary>
    /// <summary>
    /// The user's ability bans, honoured when picking a trainer Pokemon's ability slot.
    /// </summary>
    public AbilityBanList BanList { get; set; }

    /// <summary>Every tunable number (spec §4); the randomizer sets it from the weights file.</summary>
    public CompetitiveWeights Weights { get; set; } = new();

    public int ChooseCompetitiveAbilitySlot(PersonalInfo pi, string[] abilityNames)
    {
        if (pi?.Abilities == null || pi.Abilities.Length == 0 || abilityNames == null) return 1;

        var candidates = new List<(int slot, string name)>();
        for (int i = 0; i < pi.Abilities.Length && i < 3; i++)
        {
            int abilId = pi.Abilities[i];
            if (abilId <= 0 || abilId >= abilityNames.Length) continue;
            string name = abilityNames[abilId];
            if (string.IsNullOrEmpty(name)) continue;
            candidates.Add((i + 1, name));
        }
        if (candidates.Count == 0) return 1;

        // A species whose every ability is banned keeps its options rather than losing one, since
        // it has to end up with something.
        if (BanList != null)
        {
            var allowed = candidates.Where(c => !BanList.IsBanned(c.name)).ToList();
            if (allowed.Count > 0) candidates = allowed;
        }

        int Tier(string name)
        {
            if (CompetitiveDatabase.CompetitiveAbilities.Contains(name)) return 3;
            if (CompetitiveDatabase.SituationalAbilities.Contains(name)) return 2;
            if (CompetitiveDatabase.LessCompetitiveAbilities.Contains(name)) return 1;
            return 0;
        }

        int atk = pi.Stats[ATK];
        int spa = pi.Stats[SPA];
        bool isSpecialBiased = spa > atk;
        bool isPhysicalBiased = atk > spa;

        int Score(string name)
        {
            if (isSpecialBiased)
            {
                if (CompetitiveDatabase.PhysicalAttackBoostingAbilities.Contains(name) ||
                    (CompetitiveDatabase.AbilityBiasRules.TryGetValue(name, out var r) && r.RequireHigherAtkForBias))
                    return -1;
            }
            if (isPhysicalBiased)
            {
                if (CompetitiveDatabase.SpecialAttackBoostingAbilities.Contains(name) ||
                    (CompetitiveDatabase.AbilityBiasRules.TryGetValue(name, out var r) && r.RequireHigherSpaForBias))
                    return -1;
            }
            return Tier(name);
        }

        int bestScore = candidates.Max(c => Score(c.name));
        var bestCandidates = candidates.Where(c => Score(c.name) == bestScore).ToList();
        return bestCandidates[Rnd.Next(bestCandidates.Count)].slot;
    }

    /// <summary>How many of a finished set's moves are physical, and how many special.</summary>
    public (int Physical, int Special) CountAttackCategories(int[] moves)
    {
        var data = Config?.Moves;
        if (moves == null || data == null) return (0, 0);

        int phys = 0, spec = 0;
        foreach (int m in moves)
        {
            if (m <= 0 || m >= data.Length) continue;
            if (data[m].Category == 1) phys++;
            else if (data[m].Category == 2) spec++;
        }
        return (phys, spec);
    }

    /// <summary>Manifesto 0.7: Frail iff HP + Def + SpD <= 200.</summary>
    public static bool IsFrail(PersonalInfo pi)
    {
        if (pi?.Stats == null || pi.Stats.Length < 6) return false;
        return (pi.Stats[HP] + pi.Stats[DEF] + pi.Stats[SPD]) <= 200;
    }

    public (int[] evs, byte nature) BuildEVsAndNature(PersonalInfo pi, CompetitiveRole role, int[] moves, bool isTrickRoom = false, string[] moveNames = null, int level = 100)
    {
        int atk = pi.Stats[ATK], def = pi.Stats[DEF], spe = pi.Stats[SPE], spa = pi.Stats[SPA], spd = pi.Stats[SPD];

        var (physCount, specCount) = CountAttackCategories(moves);
        bool physicalSet = physCount != specCount ? physCount > specCount : atk >= spa;

        // Check for Gyro Ball: low-speed users want 0 Spe EVs and speed-hindering nature
        bool hasGyroBall = false;
        bool hasPivotMove = false;
        var allMoveNames = moveNames ?? GetMoveNames();
        if (moves != null && allMoveNames != null && allMoveNames.Length > 0)
        {
            foreach (int m in moves)
            {
                if (m <= 0 || m >= allMoveNames.Length) continue;
                string mName = allMoveNames[m];
                if (GameNameComparer.Instance.Equals(mName, "Gyro Ball"))
                    hasGyroBall = true;
                if (CompetitiveDatabase.PivotMoves.Contains(mName))
                    hasPivotMove = true;
            }
        }

        // Slow Bulky Pivot: per Smogon VoltTurn momentum, base Spe <= 65 moving LAST absorbs hits safely
        bool isSlowBulkyPivot = (role is CompetitiveRole.BulkyPivot or CompetitiveRole.DefensivePivot ||
                                 IsDefensiveRole(role) || IsBulkyRole(role)) && hasPivotMove && spe <= 65;

        // Trick Room, slow Gyro Ball user, or slow bulky pivot: 0 Spe EVs and IV, Speed-lowering nature
        bool slow = isTrickRoom || (hasGyroBall && spe <= 80) || isSlowBulkyPivot;

        // Which stats a role invests in is the old role table; how much is the optimizer's job (spec §1).
        bool isWall = role is CompetitiveRole.BulkyPivot or CompetitiveRole.BulkyUtility
                           or CompetitiveRole.DefensiveWall or CompetitiveRole.DefensivePivot;
        bool supportLead = role == CompetitiveRole.BulkyLead ||
                           (role == CompetitiveRole.OffensiveLead && atk < 90 && spa < 90);
        bool defensiveSpread = slow ? IsDefensiveRole(role) || isSlowBulkyPivot : isWall;
        bool fillAttack = !defensiveSpread && (slow || !supportLead);

        // Offensive roles and leads beat the benchmark; bulky and tanky attackers creep past an
        // uninvested Pokemon of their own base Speed; everyone else puts nothing in Speed.
        bool benchmark = !slow && (IsOffensiveRole(role) || role == CompetitiveRole.BulkyLead);
        bool creep = !slow && !benchmark && !defensiveSpread;
        var w = Weights;
        int target = benchmark
            ? CompetitiveMath.Stat(CompetitiveMath.SPE, w.SpeedBenchmarkBase, w.SpeedBenchmarkIv, w.SpeedBenchmarkEv, level, w.SpeedBenchmarkNaturePercent)
            : creep ? CompetitiveMath.Stat(CompetitiveMath.SPE, spe, 31, 0, level, 100) : 0;

        var goal = !defensiveSpread ? BulkGoal.Mixed : def >= spd ? BulkGoal.Physical : BulkGoal.Special;
        int attackStat = physicalSet ? CompetitiveMath.ATK : CompetitiveMath.SPA;
        // A set with no attacks lowers Atk, matching BuildIvs (Foul Play, confusion damage).
        int unusedAttack = physCount + specCount == 0 || !physicalSet ? CompetitiveMath.ATK : CompetitiveMath.SPA;
        int[] ivs = [31, 31, 31, 31, 31, slow ? 0 : 31];

        // Stealth Rock takes 1/4 of a 2x Rock-weak Pokemon's HP and 1/2 of a 4x one's; an HP stat it
        // doesn't divide evenly survives one more entry.
        float rockWeakness = TypeEffectivenessChart.GetEffectiveness(PokemonTypes.Rock, pi.Types);
        int hazardModulus = rockWeakness >= 4f ? 2 : rockWeakness >= 2f ? 4 : 0;

        return CompetitiveMath.OptimizeSpread(new SpreadRequest(
            CompetitiveMath.ToEvOrder(pi.Stats), ivs, level,
            fillAttack ? attackStat : -1, unusedAttack, target, slow, goal,
            HazardHpModulus: hazardModulus,
            DenyDownloadSpA: benchmark && fillAttack && def >= spd,
            SpeedCreep: creep));
    }

    /// <summary>
    /// Generates optimal competitive IVs dynamically based on moves, role, and speed stat:
    /// - 0 Spe IV for Trick Room, Gyro Ball users (Spe <= 80), or slow bulky pivots (Spe <= 65)
    /// - 0 Atk IV for pure special attackers or defensive walls with 0 physical attacks (mitigates Foul Play, confusion self-damage, Strength Sap)
    /// - 31 (or random when fullIvs is false) for remaining stats.
    /// Format matches TrainerPoke7.IVs: [HP, Atk, Def, SpA, SpD, Spe]
    /// </summary>
    public int[] BuildIvs(PersonalInfo pi, CompetitiveRole role, int[] moves, bool isTrickRoom = false, string[] moveNames = null, bool fullIvs = true)
    {
        int[] ivs = fullIvs
            ? [31, 31, 31, 31, 31, 31]
            : [Rnd.Next(32), Rnd.Next(32), Rnd.Next(32), Rnd.Next(32), Rnd.Next(32), Rnd.Next(32)];

        var allMoveNames = moveNames ?? GetMoveNames();
        bool hasGyroBall = false;
        bool hasPivotMove = false;
        if (moves != null && allMoveNames != null && allMoveNames.Length > 0)
        {
            foreach (int m in moves)
            {
                if (m <= 0 || m >= allMoveNames.Length) continue;
                string mName = allMoveNames[m];
                if (GameNameComparer.Instance.Equals(mName, "Gyro Ball"))
                    hasGyroBall = true;
                if (CompetitiveDatabase.PivotMoves.Contains(mName))
                    hasPivotMove = true;
            }
        }

        int spe = pi?.Stats != null ? pi.Stats[SPE] : 100;
        bool isSlowBulkyPivot = (role is CompetitiveRole.BulkyPivot or CompetitiveRole.DefensivePivot ||
                                 IsDefensiveRole(role) || IsBulkyRole(role)) &&
                                hasPivotMove && spe <= 65;

        // Trick Room teams, slow Gyro Ball users, and slow bulky pivots need 0 Speed IV
        if (isTrickRoom || (hasGyroBall && spe <= 80) || isSlowBulkyPivot)
            ivs[5] = 0;

        // Pure special or status attackers (0 physical attacks): 0 Atk IV to minimize Foul Play damage & confusion
        var (physAttacks, _) = CountAttackCategories(moves);
        if (physAttacks == 0)
            ivs[1] = 0;

        return ivs;
    }

    /// <summary>
    /// Dynamically adapts a team role to match the Pokemon's actual base stat distribution.
    /// Prevents high-offense titans or Megas from being trapped in 0-offense defensive roles,
    /// and prevents frail Pokemon from being assigned wall/tank roles.
    /// </summary>
    public static CompetitiveRole AdaptRoleForPokemon(PersonalInfo pi, CompetitiveRole baseRole, bool isMega = false)
    {
        if (pi?.Stats == null) return baseRole;
        int hp = pi.Stats[HP], atk = pi.Stats[ATK], def = pi.Stats[DEF];
        int spe = pi.Stats[SPE], spa = pi.Stats[SPA], spd = pi.Stats[SPD];
        int maxOffense = Math.Max(atk, spa);
        int bulk = hp + def + spd;

        // 1. Offensive Titans (Max Atk or SpA >= 130, or holding offensive Mega):
        // Relegate away from purely passive defensive wall roles into bulky sweeper or breaker
        if ((maxOffense >= 130 || isMega) && baseRole is CompetitiveRole.DefensiveWall or CompetitiveRole.DefensivePivot)
        {
            return bulk >= 240 ? CompetitiveRole.BulkySweeper : CompetitiveRole.OffensiveSweeper;
        }

        // 2. Frail Pokemon (Bulk <= 200, e.g. Deoxys, Pheromosa, Sharpedo):
        // Never allow frail Pokemon to be assigned defensive or bulky utility roles
        if (bulk <= 200 && (baseRole is CompetitiveRole.DefensiveWall or CompetitiveRole.DefensivePivot or CompetitiveRole.BulkyUtility or CompetitiveRole.BulkyLead))
        {
            return (spe >= 90) ? CompetitiveRole.OffensiveSweeper : CompetitiveRole.OffensiveBreaker;
        }

        return baseRole;
    }

    public ushort AssignItem(
        int species, PersonalInfo pi, string abilityName, int[] moves, CompetitiveRole role,
        string[] itemNames, string[] moveNames, Move[] moveData,
        ref bool assignedMega, ref bool assignedZCrystal,
        TeamArchetype archetype = TeamArchetype.Balance,
        List<string> teamAbilities = null,
        bool isDoubles = false)
    {
        ushort GetItemId(string name)
        {
            if (itemNames == null || itemNames.Length == 0 || string.IsNullOrEmpty(name)) return 0;
            int idx = Array.FindIndex(itemNames, z => GameNameComparer.Instance.Equals(z, name));
            return idx > 0 ? (ushort)idx : (ushort)0;
        }

        bool HasItem(string name) => GetItemId(name) > 0;

        ushort ReturnItem(string primary, string fallback = "Leftovers")
        {
            ushort id = GetItemId(primary);
            if (id > 0) return id;
            ushort fallbackId = GetItemId(fallback);
            return fallbackId > 0 ? fallbackId : GetItemId("Sitrus Berry");
        }

        var currentMoveNames = ResolveMoveNames(moves, moveNames);
        int atk = pi.Stats[ATK], spa = pi.Stats[SPA], spe = pi.Stats[SPE];
        int def = pi.Stats[DEF], spd = pi.Stats[SPD], hp = pi.Stats[HP];
        bool isPhysical = atk >= spa;
        int physCount = 0, specCount = 0, statusCount = 0;
        if (moveData != null && moves != null)
        {
            foreach (int m in moves)
            {
                if (m <= 0 || m >= moveData.Length) continue;
                switch (moveData[m].Category)
                {
                    case 0: statusCount++; break;
                    case 1: physCount++; break;
                    case 2: specCount++; break;
                }
            }
        }

        // === 0. MEGA STONE (max 1 per team) ===
        if (!assignedMega && species > 0)
        {
            ushort megaItem = TryAssignMegaStone(species, itemNames, ref assignedMega, pi);
            if (megaItem > 0) return megaItem;
        }

        // === 0b. Z-CRYSTAL (max 1 per team, Gen 7) ===
        bool abilityBlocksZCrystal =
            abilityName.Equals("Unburden", StringComparison.OrdinalIgnoreCase) ||
            abilityName.Equals("Poison Heal", StringComparison.OrdinalIgnoreCase) ||
            abilityName.Equals("Toxic Boost", StringComparison.OrdinalIgnoreCase) ||
            abilityName.Equals("Guts", StringComparison.OrdinalIgnoreCase) ||
            abilityName.Equals("Flare Boost", StringComparison.OrdinalIgnoreCase);

        if (!assignedZCrystal && !abilityBlocksZCrystal && species > 0 && Config != null && Config.Generation == 7 && IsOffensiveRole(role))
        {
            ushort zItem = TryAssignZCrystal(species, pi, moves, itemNames, moveNames, moveData, ref assignedZCrystal);
            if (zItem > 0) return zItem;
        }

        // === 1. STRICT ABILITY-SPECIFIC ITEMS ===

        // Poison Heal / Toxic Boost -> Toxic Orb
        if (abilityName.Equals("Poison Heal", StringComparison.OrdinalIgnoreCase) ||
            abilityName.Equals("Toxic Boost", StringComparison.OrdinalIgnoreCase))
            return ReturnItem("Toxic Orb");

        // Guts / Flare Boost -> Flame Orb
        if (abilityName.Equals("Guts", StringComparison.OrdinalIgnoreCase) ||
            abilityName.Equals("Flare Boost", StringComparison.OrdinalIgnoreCase))
            return ReturnItem("Flame Orb");

        // Anger Shell -> Focus Sash
        if (abilityName.Equals("Anger Shell", StringComparison.OrdinalIgnoreCase))
            return ReturnItem("Focus Sash");

        // Super Luck -> Scope Lens
        if (abilityName.Equals("Super Luck", StringComparison.OrdinalIgnoreCase))
            return ReturnItem("Scope Lens");

        // Gorilla Tactics -> Choice Band or Choice Scarf
        if (abilityName.Equals("Gorilla Tactics", StringComparison.OrdinalIgnoreCase))
        {
            if (atk + spe > 160) return ReturnItem("Choice Scarf");
            return ReturnItem(spe > atk ? "Choice Scarf" : "Choice Band");
        }

        // Protosynthesis / Quark Drive -> Booster Energy (prioritized for offensive)
        if (abilityName.Equals("Protosynthesis", StringComparison.OrdinalIgnoreCase) ||
            abilityName.Equals("Quark Drive", StringComparison.OrdinalIgnoreCase))
        {
            if (IsOffensiveRole(role)) return ReturnItem("Booster Energy", "Life Orb");
        }

        // Harvest -> Sitrus Berry (defensive) or Starf Berry (offensive)
        if (abilityName.Equals("Harvest", StringComparison.OrdinalIgnoreCase))
        {
            bool isSunTeam = archetype == TeamArchetype.Sun;
            if (IsDefensiveRole(role) || !IsOffensiveRole(role))
                return ReturnItem("Sitrus Berry");
            return ReturnItem("Starf Berry", "Sitrus Berry");
        }

        // Life Orb synergy: Magic Guard and Sheer Force attackers take 0 recoil damage (Smogon staple)
        if (IsOffensiveRole(role) && !string.IsNullOrEmpty(abilityName) && CompetitiveDatabase.LifeOrbSynergyAbilities.Contains(abilityName))
            return ReturnItem(IsFrail(pi) && abilityName.Equals("Magic Guard", StringComparison.OrdinalIgnoreCase) ? "Focus Sash" : "Life Orb");

        // Contact damage punishment: Rough Skin and Iron Barbs -> Rocky Helmet
        if (!string.IsNullOrEmpty(abilityName) && (abilityName.Equals("Rough Skin", StringComparison.OrdinalIgnoreCase) || abilityName.Equals("Iron Barbs", StringComparison.OrdinalIgnoreCase)))
            return ReturnItem("Rocky Helmet", (pi.Types != null && Array.IndexOf(pi.Types, PokemonTypes.Poison) >= 0) ? "Black Sludge" : "Leftovers");

        // Moody -> Leftovers (manifesto: Substitute + Protect moveset)
        if (abilityName.Equals("Moody", StringComparison.OrdinalIgnoreCase))
            return ReturnItem("Leftovers");

        // === 2. SCREEN / AURORA VEIL + LIGHT CLAY ===
        bool hasDualScreens = currentMoveNames.Contains("Light Screen") && currentMoveNames.Contains("Reflect");
        bool hasSnowVeil = abilityName.Equals("Snow Warning", StringComparison.OrdinalIgnoreCase) && currentMoveNames.Contains("Aurora Veil");
        if (hasDualScreens || hasSnowVeil)
            return ReturnItem("Light Clay");

        // Snow Warning without Aurora Veil -> Icy Rock (weather teams)
        if (abilityName.Equals("Snow Warning", StringComparison.OrdinalIgnoreCase) && archetype == TeamArchetype.Snow)
            return ReturnItem("Icy Rock");

        // Drought -> Heat Rock (Sun team lead)
        if (abilityName.Equals("Drought", StringComparison.OrdinalIgnoreCase) && archetype == TeamArchetype.Sun)
            return ReturnItem("Heat Rock");

        // Drizzle -> Damp Rock (Rain team lead)
        if (abilityName.Equals("Drizzle", StringComparison.OrdinalIgnoreCase) && archetype == TeamArchetype.Rain)
            return ReturnItem("Damp Rock");

        // Electric/Grassy/Psychic/Misty Surge lead -> Terrain Extender (extends any terrain)
        if ((abilityName.Equals("Electric Surge", StringComparison.OrdinalIgnoreCase) ||
             abilityName.Equals("Grassy Surge", StringComparison.OrdinalIgnoreCase) ||
             abilityName.Equals("Psychic Surge", StringComparison.OrdinalIgnoreCase) ||
             abilityName.Equals("Misty Surge", StringComparison.OrdinalIgnoreCase)) &&
            archetype == TeamArchetype.TerrainTeam && role == CompetitiveRole.BulkyLead)
            return ReturnItem("Terrain Extender", "Heavy-Duty Boots");

        // === 3. UNBURDEN -> consumable items ===
        if (abilityName.Equals("Unburden", StringComparison.OrdinalIgnoreCase))
        {
            // Terrain seed if teammate has Surge
            if (teamAbilities != null)
            {
                if (teamAbilities.Any(a => a.Equals("Electric Surge", StringComparison.OrdinalIgnoreCase)))
                    return ReturnItem("Electric Seed", "Air Balloon");
                if (teamAbilities.Any(a => a.Equals("Grassy Surge", StringComparison.OrdinalIgnoreCase)))
                    return ReturnItem("Grassy Seed", "Air Balloon");
                if (teamAbilities.Any(a => a.Equals("Psychic Surge", StringComparison.OrdinalIgnoreCase)))
                    return ReturnItem("Psychic Seed", "Air Balloon");
                if (teamAbilities.Any(a => a.Equals("Misty Surge", StringComparison.OrdinalIgnoreCase)))
                    return ReturnItem("Misty Seed", "Air Balloon");
            }
            // Fallback consumable
            if (IsFrail(pi)) return ReturnItem("Focus Sash");
            if (currentMoveNames.Overlaps(CompetitiveDatabase.StatDropMoves)) return ReturnItem("White Herb");
            if (currentMoveNames.Overlaps(CompetitiveDatabase.TwoTurnMoves)) return ReturnItem("Power Herb");
            return ReturnItem("Air Balloon");
        }

        // === 4. MOVE-SPECIFIC & PATCH ITEMS ===

        // Doubles: Redirection -> Safety Goggles (immune to Spore/Sleep Powder and weather chip)
        if (isDoubles && currentMoveNames.Overlaps(CompetitiveDatabase.RedirectionMoves) && HasItem("Safety Goggles"))
            return ReturnItem("Safety Goggles", "Sitrus Berry");

        // Trick Room Setter / Disruptor -> Mental Herb (prevents Taunt shutdown)
        if (currentMoveNames.Contains("Trick Room") && HasItem("Mental Herb") && (archetype == TeamArchetype.TrickRoom || isDoubles))
            return ReturnItem("Mental Herb", "Focus Sash");

        // Weakness Policy: strictly for bulky offensive sweepers/attackers with direct attacking moves and >= 2 weaknesses
        if (HasItem("Weakness Policy") &&
            (role is CompetitiveRole.BulkySweeper or CompetitiveRole.TankySweeper or CompetitiveRole.BulkyAttacker or CompetitiveRole.OffensiveSweeper) &&
            (CompetitiveDatabase.WeaknessPolicyAbilities.Contains(abilityName) || (hp + def + spd >= 210)))
        {
            int weaknessCount = 0;
            if (pi.Types != null)
            {
                for (int t = 0; t < 18; t++)
                {
                    if (TypeEffectivenessChart.GetEffectiveness(t, pi.Types) > 1.0f)
                        weaknessCount++;
                }
            }

            int investedCat = isPhysical ? 1 : 2;
            int directAttacks = moves?.Count(m => m > 0 && m < (moveData?.Length ?? 0) &&
                                                  moveData[m].Category == investedCat &&
                                                  moveData[m].Power > 0 &&
                                                  !CompetitiveDatabase.FixedDamageMoves.Contains(moveNames[m]) &&
                                                  !moveNames[m].Equals("Foul Play", StringComparison.OrdinalIgnoreCase)) ?? 0;

            if (weaknessCount >= 2 && directAttacks >= 2 && Rnd.Next(100) < (isDoubles ? 60 : 35))
                return ReturnItem("Weakness Policy", "Life Orb");
        }

        // Punching Glove (Gen 9)
        if (currentMoveNames.Overlaps(CompetitiveDatabase.PunchingMoves) && isPhysical && HasItem("Punching Glove"))
            return ReturnItem("Punching Glove");

        // Throat Spray (Gen 8)
        if (currentMoveNames.Overlaps(CompetitiveDatabase.SoundMoves) && !isPhysical && HasItem("Throat Spray"))
            return ReturnItem("Throat Spray");

        // Loaded Dice (Gen 9)
        if (currentMoveNames.Overlaps(CompetitiveDatabase.MultiHitMoves) && HasItem("Loaded Dice"))
            return ReturnItem("Loaded Dice");

        // Power Herb
        if (currentMoveNames.Overlaps(CompetitiveDatabase.TwoTurnMoves))
            return ReturnItem("Power Herb");

        // Clear Amulet (Gen 9)
        if (isPhysical && (currentMoveNames.Overlaps(CompetitiveDatabase.AtkSetupMoves) || role == CompetitiveRole.OffensiveBreaker) && HasItem("Clear Amulet") && Rnd.Next(100) < 35)
            return ReturnItem("Clear Amulet");

        // Covert Cloak (Gen 9)
        if ((IsDefensiveRole(role) || IsBulkyRole(role)) && HasItem("Covert Cloak") && Rnd.Next(100) < 25)
            return ReturnItem("Covert Cloak");

        // Eject Pack (Gen 8)
        if (currentMoveNames.Overlaps(CompetitiveDatabase.StatDropMoves) && (role == CompetitiveRole.OffensivePivot || role == CompetitiveRole.BulkyPivot || role == CompetitiveRole.OffensiveLead) && HasItem("Eject Pack"))
            return ReturnItem("Eject Pack");

        // Blunder Policy (Gen 8)
        bool hasLowAccMove = moves != null && moveData != null && moves.Any(m => m > 0 && m < moveData.Length && moveData[m].Accuracy > 0 && moveData[m].Accuracy < 90);
        if (hasLowAccMove && IsOffensiveRole(role) && HasItem("Blunder Policy") && Rnd.Next(100) < 30)
            return ReturnItem("Blunder Policy");

        // Stealth Rock weakness -> Heavy-Duty Boots (Gen 8)
        float rockWeakness = 1f;
        if (pi.Types != null)
        {
            foreach (int t in pi.Types)
                rockWeakness *= TypeEffectivenessChart.GetEffectiveness(PokemonTypes.Rock, t);
        }
        if (rockWeakness >= 2f && HasItem("Heavy-Duty Boots") && Rnd.Next(100) < 65)
            return ReturnItem("Heavy-Duty Boots");

        // === 5. MEGA STONE (max 1 per team) ===
        if (!assignedMega && pi is PersonalInfo megaPi)
        {
            // Check if this species has a Mega Stone in the game
            // Species ID isn't directly available from PersonalInfo; handled by caller
        }

        // Belly Drum -> Sitrus Berry (recovers 25% immediately after 50% HP deduction)
        if (currentMoveNames.Contains("Belly Drum") && HasItem("Sitrus Berry"))
            return ReturnItem("Sitrus Berry");

        // 4x / 2x Fatal Weakness Neutralization via Type-Resist Berries:
        // Dynamically evaluate all 18 attacking types against pi.Types using TypeEffectivenessChart.
        // Setup sweepers, offensive breakers, and bulky attackers have a ~45% chance to run the resist berry.
        if (pi.Types != null && (currentMoveNames.Overlaps(CompetitiveDatabase.SetupMoves) || IsOffensiveRole(role) || role == CompetitiveRole.BulkyAttacker))
        {
            int fatalAtkType = -1;
            float maxWeakness = 1.0f;
            for (int t = 0; t <= 17; t++)
            {
                float eff = TypeEffectivenessChart.GetEffectiveness(t, pi.Types);
                if (eff > maxWeakness)
                {
                    maxWeakness = eff;
                    fatalAtkType = t;
                }
            }
            if (maxWeakness >= 4.0f && fatalAtkType >= 0 && CompetitiveDatabase.TypeToResistBerry.TryGetValue(fatalAtkType, out string resistBerry) && HasItem(resistBerry) && Rnd.Next(100) < 45)
            {
                return ReturnItem(resistBerry);
            }
        }

        // Doubles: bulky attackers / support with high composite bulk -> Sitrus Berry
        if (isDoubles && (hp + def + spd >= 210) && (role == CompetitiveRole.BulkyAttacker || role == CompetitiveRole.BulkyUtility) && HasItem("Sitrus Berry") && Rnd.Next(100) < 35)
            return ReturnItem("Sitrus Berry");

        string defaultDefensiveItem = (pi.Types != null && Array.IndexOf(pi.Types, PokemonTypes.Poison) >= 0) ? "Black Sludge" : "Leftovers";

        // Eviolite for non-final stage defensive/bulky Pokemon
        if (species > 0 && Legal.CanSpeciesEvolve(species, Config) &&
            (IsDefensiveRole(role) || IsBulkyRole(role) || role == CompetitiveRole.BulkyLead))
        {
            return ReturnItem("Eviolite", defaultDefensiveItem);
        }


        // Strict competitive viability checks for Choice and Assault Vest items
        bool hasProtect = currentMoveNames.Overlaps(CompetitiveDatabase.ProtectMoves);
        bool hasSetup = currentMoveNames.Overlaps(CompetitiveDatabase.SetupMoves);
        bool hasRecovery = currentMoveNames.Overlaps(CompetitiveDatabase.RecoveryMoves);
        bool canUseChoice = !hasProtect && !hasSetup && !hasRecovery && (statusCount == 0 || currentMoveNames.Contains("Trick") || currentMoveNames.Contains("Switcheroo"));
        bool canUseAssaultVest = statusCount == 0 && !hasProtect;

        // === 6. ROLE-BASED ITEMS ===
        switch (role)
        {
            case CompetitiveRole.OffensiveLead:
                return ReturnItem(IsFrail(pi) ? "Focus Sash" : "Heavy-Duty Boots");

            case CompetitiveRole.BulkyLead:
                if (currentMoveNames.Contains("Trick Room"))
                    return ReturnItem("Mental Herb", "Focus Sash");
                if (currentMoveNames.Overlaps(CompetitiveDatabase.HazardMoves) && Rnd.Next(100) < 25)
                    return ReturnItem("Mental Herb");
                return ReturnItem("Heavy-Duty Boots", defaultDefensiveItem);

            case CompetitiveRole.OffensiveSweeper:
            case CompetitiveRole.OffensiveBreaker:
                if (statusCount == 0 && physCount + specCount == 4)
                {
                    // All attacks: Choice item viable only if eligible
                    if (canUseChoice && isPhysical) return ReturnItem("Choice Band");
                    return ReturnItem("Life Orb");
                }
                if (currentMoveNames.Overlaps(CompetitiveDatabase.SetupMoves))
                {
                    // Setup sweeper: Lum Berry or Life Orb
                    if (Rnd.Next(100) < 40) return ReturnItem("Lum Berry");
                    return ReturnItem("Life Orb");
                }
                return ReturnItem("Life Orb");

            case CompetitiveRole.OffensiveCleaner:
                return ReturnItem("Life Orb");

            case CompetitiveRole.SpeedControl:
                if (canUseChoice && (statusCount == 0 || currentMoveNames.Contains("Trick") || currentMoveNames.Contains("Switcheroo")))
                    return ReturnItem("Choice Scarf");
                return ReturnItem("Focus Sash", "Life Orb");

            case CompetitiveRole.OffensivePivot:
                if (physCount + specCount >= 3) return ReturnItem("Expert Belt");
                return ReturnItem("Heavy-Duty Boots", "Life Orb");

            case CompetitiveRole.OffensiveUtility:
                if (physCount + specCount >= 3) return ReturnItem("Life Orb");
                return ReturnItem("Heavy-Duty Boots", "Life Orb");

            case CompetitiveRole.TankySweeper:
            case CompetitiveRole.TankyCleaner:
                if (currentMoveNames.Overlaps(CompetitiveDatabase.SetupMoves))
                    return ReturnItem(defaultDefensiveItem);
                if (canUseChoice)
                    return ReturnItem(isPhysical ? "Choice Band" : "Choice Specs");
                return ReturnItem(defaultDefensiveItem);

            case CompetitiveRole.BulkyAttacker:
                if (canUseAssaultVest) return ReturnItem("Assault Vest");
                return ReturnItem(defaultDefensiveItem);

            case CompetitiveRole.BulkySweeper:
                return ReturnItem(defaultDefensiveItem);

            case CompetitiveRole.BulkyPivot:
            case CompetitiveRole.BulkyUtility:
                // Check for Stealth Rock weakness -> Heavy-Duty Boots
                if (Rnd.Next(100) < 30) return ReturnItem("Heavy-Duty Boots", defaultDefensiveItem);
                return ReturnItem(defaultDefensiveItem);

            case CompetitiveRole.DefensiveWall:
            case CompetitiveRole.DefensivePivot:
                if (hp + Math.Max(def, spd) > 160 && Rnd.Next(100) < 40)
                    return ReturnItem("Rocky Helmet");
                return ReturnItem(defaultDefensiveItem);

            default:
                return ReturnItem(hp + Math.Max(def, spd) > 150 ? defaultDefensiveItem : "Life Orb");
        }
    }

    // Mega Stone assignment as a separate pass (called by the team builder)
    public ushort TryAssignMegaStone(int species, string[] itemNames, ref bool assignedMega, PersonalInfo pi = null)
    {
        if (assignedMega) return 0;
        if (!CompetitiveDatabase.MegaStoneMap.TryGetValue(species, out var stones)) return 0;

        ushort GetItemId(string name)
        {
            if (itemNames == null || itemNames.Length == 0) return 0;
            int idx = Array.FindIndex(itemNames, z => GameNameComparer.Instance.Equals(z, name));
            return idx > 0 ? (ushort)idx : (ushort)0;
        }

        // For dual Mega species (Charizard, Mewtwo), pick X for physical bias, Y for special bias
        string[] candidateStones;
        if (stones.Length > 1 && pi?.Stats != null)
        {
            bool wantsPhysical = pi.Stats[ATK] > pi.Stats[SPA];
            candidateStones = wantsPhysical
                ? stones.OrderByDescending(s => s.EndsWith(" X", StringComparison.OrdinalIgnoreCase)).ToArray()
                : stones.OrderByDescending(s => s.EndsWith(" Y", StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        else
        {
            candidateStones = stones.Length > 1 ? stones.OrderBy(_ => Util.Rand.Next()).ToArray() : stones;
        }

        foreach (var stone in candidateStones)
        {
            ushort id = GetItemId(stone);
            if (id > 0)
            {
                assignedMega = true;
                return id;
            }
        }
        return 0;
    }

    // Z-Crystal assignment as a separate pass
    public ushort TryAssignZCrystal(int species, PersonalInfo pi, int[] moves, string[] itemNames, string[] moveNames, Move[] moveData, ref bool assignedZCrystal)
    {
        if (assignedZCrystal) return 0;

        ushort GetItemId(string name)
        {
            if (itemNames == null || itemNames.Length == 0) return 0;
            int idx = Array.FindIndex(itemNames, z => GameNameComparer.Instance.Equals(z, name));
            return idx > 0 ? (ushort)idx : (ushort)0;
        }

        // Species-specific Z-Crystals first
        if (CompetitiveDatabase.SpeciesZCrystals.TryGetValue(species, out var specZ))
        {
            ushort zId = GetItemId(specZ.Crystal);
            if (zId > 0)
            {
                assignedZCrystal = true;
                return zId;
            }
        }

        if (pi != null && moves != null && moveData != null && moveNames != null)
        {
            bool isPhysical = pi.Stats[ATK] >= pi.Stats[SPA];
            int bestPower = 0;
            int bestType = -1;
            foreach (int m in moves)
            {
                if (m <= 0 || m >= moveData.Length) continue;
                var md = moveData[m];
                if (md.Category == 0) continue; // Skip status
                bool matchesCat = (isPhysical && md.Category == 1) || (!isPhysical && md.Category == 2);
                if (!matchesCat) continue;
                int moveType = md.Type;
                bool isSTAB = Array.IndexOf(pi.Types, moveType) >= 0;
                if (isSTAB && md.Power >= 80 && md.Power > bestPower)
                {
                    bestPower = md.Power;
                    bestType = moveType;
                }
            }
            if (bestType >= 0 && CompetitiveDatabase.TypeZCrystals.TryGetValue(bestType, out var crystalName))
            {
                ushort zId = GetItemId(crystalName);
                if (zId > 0)
                {
                    assignedZCrystal = true;
                    return zId;
                }
            }
        }

        return 0;
    }

    public int[] BuildCompetitiveMoveset(
        int species, CompetitiveRole role, PersonalInfo pi,
        MoveRandomizer fallbackMoveRand, string abilityName = "",
        TeamArchetype archetype = TeamArchetype.Balance,
        Move[] moveData = null, List<string> teamAbilities = null,
        bool isDoubles = false,
        string[] moveNames = null,
        int level = 100)
    {
        if (moveNames == null || moveNames.Length == 0)
            moveNames = Config?.GetText(TextName.MoveNames) ?? Array.Empty<string>();

        var legal = LearnsetAggregator?.GetLegalMoves(species) ?? new HashSet<int>();
        if (legal.Count < 4)
        {
            var broadMoveNames = moveNames.Length > 0 ? moveNames : Config?.GetText(TextName.MoveNames);
            var broadMoveData = moveData ?? Config?.Moves;
            if (broadMoveData != null)
            {
                int maxId = Config?.Info != null ? Config.Info.MaxMoveID : (broadMoveData.Length - 1);
                int limit = broadMoveNames != null ? Math.Min(maxId, Math.Min(broadMoveNames.Length, broadMoveData.Length) - 1) : Math.Min(maxId, broadMoveData.Length - 1);
                legal = Enumerable.Range(1, Math.Max(0, limit))
                    .Where(m => (broadMoveNames == null ? !Legal.Z_Moves.Contains(m) : Legal.IsUsableMove(m, broadMoveNames)) &&
                                broadMoveData[m].PP != 1)
                    .ToHashSet();
            }
            if (legal.Count < 4)
                return fallbackMoveRand?.GetRandomMoveset(species) ?? new int[4];
        }

        if (moveData == null) moveData = Config?.Moves;
        int[] types = pi?.Types ?? new int[0];
        int spa = pi != null ? pi.Stats[SPA] : 0;

        var pool = legal.ToList();

        // In Singles battles, strictly purge all doubles-only moves (Wide Guard, Quick Guard, Redirection, Helping Hand)
        if (!isDoubles && moveNames.Length > 0)
        {
            pool.RemoveAll(m => m > 0 && m < moveNames.Length && CompetitiveDatabase.DoublesOnlyMoves.Contains(moveNames[m]));
        }

        var selected = new List<int>();
        bool isPhysicalAttacker = pi != null && pi.Stats[ATK] >= spa;

        // Same rule the STAB and coverage phases use: once one attacking stat clearly dominates, a
        // move on the weaker side is not worth a slot whatever else it does for the role.
        int strongStat = pi != null ? Math.Max(pi.Stats[ATK], spa) : 0;
        int weakStat = pi != null ? Math.Min(pi.Stats[ATK], spa) : 0;
        bool statGapDecisive = pi != null && weakStat * 4 < strongStat * 3;

        // Spec §2's S is the final attacking stat from §1. Moves come first, so it's read from a
        // provisional spread built without them (Trick Room is known; Gyro Ball and pivots aren't yet).
        double physStat = 1, specStat = 1;
        if (pi != null)
        {
            var (provEvs, provNature) = BuildEVsAndNature(pi, role, null, archetype == TeamArchetype.TrickRoom, moveNames, level);
            int[] evBase = CompetitiveMath.ToEvOrder(pi.Stats);
            physStat = CompetitiveMath.Stat(CompetitiveMath.ATK, evBase[CompetitiveMath.ATK], 31, provEvs[CompetitiveMath.ATK], level, CompetitiveMath.NaturePercent(provNature, CompetitiveMath.ATK));
            specStat = CompetitiveMath.Stat(CompetitiveMath.SPA, evBase[CompetitiveMath.SPA], 31, provEvs[CompetitiveMath.SPA], level, CompetitiveMath.NaturePercent(provNature, CompetitiveMath.SPA));
        }
        bool adaptability = GameNameComparer.Instance.Equals(abilityName, "Adaptability");

        double ScoreMove(int m)
        {
            if (moveData == null || m <= 0 || m >= moveData.Length) return 1;
            var md = moveData[m];
            string name = m < moveNames.Length ? moveNames[m] ?? "" : "";
            double weight = CompetitiveDatabase.CompetitiveMoves.Contains(name) ? Weights.ShouldWeight
                          : CompetitiveDatabase.SituationalMoves.Contains(name) ? Weights.WeakShouldWeight : 1;
            if (md.Category == 0) return CompetitiveMath.StatusScore(Math.Max(physStat, specStat), weight, Weights);
            if (CompetitiveDatabase.WeakPriorityAttacks.Contains(name)) weight *= Weights.WeakPriorityFactor;
            if (isDoubles && CompetitiveDatabase.DoublesSpreadMoves.Contains(name)) weight *= Weights.SpreadMoveFactor;

            double damage = CompetitiveMath.ExpectedDamage(md.Power, md.Accuracy, md.HitMin, md.HitMax,
                Array.IndexOf(types, md.Type) >= 0, adaptability, md.Category == 1 ? physStat : specStat,
                NeedsChargeTurn(name, abilityName, teamAbilities), Weights);
            var setTypes = selected.Where(s => s > 0 && s < moveData.Length && moveData[s].Category != 0)
                                   .Select(s => moveData[s].Type).ToList();
            return CompetitiveMath.Score(damage, CompetitiveMath.CoverageGain(md.Type, setTypes, Weights), weight, Weights);
        }
        bool Banned(int m) => m > 0 && m < moveNames.Length && IsBannedMove(moveNames[m], pi, selected, pool, moveNames);

        void TryAddFakeOut()
        {
            if (selected.Count >= 4 || moveNames.Length == 0) return;
            int fakeOutId = pool.FirstOrDefault(m => m > 0 && m < moveNames.Length && moveNames[m].Equals("Fake Out", StringComparison.OrdinalIgnoreCase));
            if (fakeOutId > 0 && Rnd.Next(100) < 75)
            {
                selected.Add(fakeOutId);
                pool.Remove(fakeOutId);
            }
        }

        void TryAdd(string moveName)
        {
            if (selected.Count >= 4 || moveNames.Length == 0 || string.IsNullOrEmpty(moveName)) return;
            int moveId = pool.FirstOrDefault(m => m > 0 && m < moveNames.Length && GameNameComparer.Instance.Equals(moveNames[m], moveName));
            if (moveId > 0 && !selected.Contains(moveId))
            {
                selected.Add(moveId);
                pool.Remove(moveId);
            }
        }

        // Helper: Try to add moves from a named category, prioritizing STAB
        void TryAddCategory(HashSet<string> categorySet, int maxAdd = 1, bool preferSTAB = false)
        {
            if (moveNames.Length == 0) return;

            // Sort pool: STAB first if preferred
            var candidates = new List<(int id, bool isStab, bool matchesCat, int power, int hazardWeight)>();
            foreach (int moveId in pool)
            {
                if (moveId <= 0 || moveId >= moveNames.Length) continue;
                string name = moveNames[moveId];
                if (string.IsNullOrEmpty(name) || !categorySet.Contains(name)) continue;
                if (selected.Contains(moveId)) continue;

                // Ban Sticky Web on Stall, Semi-Stall, and Trick Room archetypes
                if (categorySet == CompetitiveDatabase.HazardMoves && name.Equals("Sticky Web", StringComparison.OrdinalIgnoreCase))
                {
                    if (archetype is TeamArchetype.Stall or TeamArchetype.SemiStall or TeamArchetype.TrickRoom)
                        continue;
                }

                if (IsBannedMove(name, pi, selected, pool, moveNames)) continue;

                // For Special Attackers, do not give physical U-turn unless Bug STAB with adequate Attack
                if (categorySet == CompetitiveDatabase.PivotMoves && name.Equals("U-turn", StringComparison.OrdinalIgnoreCase))
                {
                    if (!isPhysicalAttacker && !(types != null && types.Contains(PokemonTypes.Bug) && pi.Stats[ATK] >= 80))
                        continue;
                }

                bool isStab = false;
                bool matchesCat = true; // status moves (Category == 0) have no phys/spec mismatch to worry about
                int power = 0;
                if (moveId < (moveData?.Length ?? 0))
                {
                    var md = moveData[moveId];
                    isStab = Array.IndexOf(types, md.Type) >= 0;
                    power = md.Power;
                    if (md.Category != 0)
                        matchesCat = (isPhysicalAttacker && md.Category == 1) || (!isPhysicalAttacker && md.Category == 2);
                }

                // Enforce category: attacks must match physical/special specialization
                if (!matchesCat) continue;

                int hazardWeight = 0;
                if (categorySet == CompetitiveDatabase.HazardMoves)
                {
                    if (name.Equals("Stealth Rock", StringComparison.OrdinalIgnoreCase)) hazardWeight = 1000;
                    else if (name.Equals("Toxic Spikes", StringComparison.OrdinalIgnoreCase)) hazardWeight = 950;
                    else if (name.Equals("Spikes", StringComparison.OrdinalIgnoreCase)) hazardWeight = 900;
                    else if (name.Equals("Ceaseless Edge", StringComparison.OrdinalIgnoreCase) || name.Equals("Stone Axe", StringComparison.OrdinalIgnoreCase)) hazardWeight = 850;
                    else if (name.Equals("Sticky Web", StringComparison.OrdinalIgnoreCase)) hazardWeight = 700;
                }

                candidates.Add((moveId, isStab, matchesCat, power, hazardWeight));
            }

            // Sort: hazardWeight first if hazard category, else STAB first (if preferred), then category-match, then by power descending
            if (categorySet == CompetitiveDatabase.HazardMoves)
                candidates.Sort((a, b) => b.hazardWeight.CompareTo(a.hazardWeight));
            else if (preferSTAB)
                candidates.Sort((a, b) => a.isStab != b.isStab ? (b.isStab ? 1 : -1) :
                                           a.matchesCat != b.matchesCat ? (b.matchesCat ? 1 : -1) :
                                           b.power.CompareTo(a.power));
            else
                candidates.Sort((a, b) => a.matchesCat != b.matchesCat ? (b.matchesCat ? 1 : -1) : b.power.CompareTo(a.power));

            int count = 0;
            foreach (var (id, _, _, _, _) in candidates)
            {
                if (selected.Count >= 4 || count >= maxAdd) break;
                selected.Add(id);
                pool.Remove(id);
                count++;
            }
        }

        // --- Phase 1: ABILITY-SPECIFIC MOVE REQUIREMENTS (Manifesto lines 75-122) ---
        if (!string.IsNullOrEmpty(abilityName))
        {
            ApplyAbilityMoveRequirements(abilityName, selected, pool, moveNames, moveData, types, role, archetype, pi);
        }

        // --- Phase 2: ROLE-SPECIFIC MOVES ---
        switch (role)
        {
            case CompetitiveRole.OffensiveLead:
                if (isDoubles)
                {
                    TryAddFakeOut();
                    TryAddCategory(CompetitiveDatabase.DoublesSpeedControlMoves, 1);
                }
                else
                {
                    if (archetype == TeamArchetype.HyperOffenseScreens)
                    {
                        TryAdd("Reflect"); TryAdd("Light Screen");
                    }
                    else
                    {
                        // Setting hazards is top priority for leads in Singles
                        TryAddCategory(CompetitiveDatabase.HazardMoves, 1);
                        if (!selected.Any(m => CompetitiveDatabase.HazardMoves.Contains(moveNames[m])))
                        {
                            TryAddCategory(CompetitiveDatabase.UtilityStatusMoves, 1);
                        }
                    }
                }
                if (selected.Count < 4 && Rnd.Next(100) < 35) TryAddCategory(CompetitiveDatabase.PivotMoves, 1);
                break;

            case CompetitiveRole.BulkyLead:
                if (isDoubles)
                {
                    TryAddFakeOut();
                    TryAddCategory(CompetitiveDatabase.WideQuickGuardMoves, 1);
                    TryAddCategory(CompetitiveDatabase.RedirectionMoves, 1);
                    TryAddCategory(CompetitiveDatabase.DoublesSpeedControlMoves, 1);
                }
                else
                {
                    if (archetype == TeamArchetype.HyperOffenseScreens)
                    {
                        TryAdd("Reflect"); TryAdd("Light Screen");
                    }
                    else
                    {
                        // Setting hazards is top priority for leads in Singles
                        TryAddCategory(CompetitiveDatabase.HazardMoves, 1);
                        if (!selected.Any(m => CompetitiveDatabase.HazardMoves.Contains(moveNames[m])))
                        {
                            TryAddCategory(CompetitiveDatabase.UtilityStatusMoves, 1);
                        }
                    }
                }
                if (selected.Count < 4 && Rnd.Next(100) < 35) TryAddCategory(CompetitiveDatabase.PivotMoves, 1);
                break;

            case CompetitiveRole.OffensiveSweeper:
            case CompetitiveRole.BulkySweeper:
            case CompetitiveRole.TankySweeper:
                if (pi.Stats[ATK] >= spa)
                    TryAddCategory(CompetitiveDatabase.AtkSetupMoves, 1);
                else
                    TryAddCategory(CompetitiveDatabase.SpaSetupMoves, 1);
                break;

            case CompetitiveRole.OffensivePivot:
            case CompetitiveRole.BulkyPivot:
                TryAddCategory(CompetitiveDatabase.PivotMoves, 1, true);
                break;

            case CompetitiveRole.DefensiveWall:
            case CompetitiveRole.DefensivePivot:
                TryAddCategory(CompetitiveDatabase.RecoveryMoves, 1);
                if (role == CompetitiveRole.DefensivePivot)
                    TryAddCategory(CompetitiveDatabase.PivotMoves, 1);
                // Hazards prioritized on defensive roles in Singles
                if (!isDoubles && selected.Count < 4 && Rnd.Next(100) < 65)
                    TryAddCategory(CompetitiveDatabase.HazardMoves, 1);
                if (selected.Count < 4 && Rnd.Next(100) < 60)
                    TryAddCategory(CompetitiveDatabase.UtilityStatusMoves, 1);
                // Hazard removal is less prioritized: low probability (15%) in Singles only
                if (!isDoubles && selected.Count < 4 && Rnd.Next(100) < 15)
                    TryAddCategory(CompetitiveDatabase.HazardRemovalMoves, 1);
                break;

            case CompetitiveRole.OffensiveBreaker:
                if (isDoubles)
                {
                    TryAddFakeOut();
                }
                break;

            case CompetitiveRole.OffensiveCleaner:
                // Cleaners want attacks; maybe one setup
                if (Rnd.Next(100) < 30)
                {
                    if (pi.Stats[ATK] >= spa)
                        TryAddCategory(CompetitiveDatabase.AtkSetupMoves, 1);
                    else
                        TryAddCategory(CompetitiveDatabase.SpaSetupMoves, 1);
                }
                break;

            case CompetitiveRole.SpeedControl:
                if (isDoubles)
                {
                    TryAddCategory(CompetitiveDatabase.DoublesSpeedControlMoves, 1);
                    TryAddCategory(CompetitiveDatabase.WideQuickGuardMoves, 1);
                }
                break;

            case CompetitiveRole.OffensiveUtility:
                if (isDoubles)
                {
                    TryAddFakeOut();
                    TryAddCategory(CompetitiveDatabase.DoublesSpeedControlMoves, 1);
                }
                else
                {
                    // Hazards prioritized on offensive utility in Singles
                    if (Rnd.Next(100) < 70)
                        TryAddCategory(CompetitiveDatabase.HazardMoves, 1);
                    else
                        TryAddCategory(CompetitiveDatabase.UtilityStatusMoves, 1);

                    // Hazard removal is less prioritized: low probability (15%) in Singles only
                    if (selected.Count < 4 && Rnd.Next(100) < 15)
                        TryAddCategory(CompetitiveDatabase.HazardRemovalMoves, 1);
                }
                if (selected.Count < 4 && Rnd.Next(100) < 35) TryAddCategory(CompetitiveDatabase.PivotMoves, 1);
                break;

            case CompetitiveRole.BulkyUtility:
                if (isDoubles)
                {
                    TryAddFakeOut();
                    TryAddCategory(CompetitiveDatabase.WideQuickGuardMoves, 1);
                    TryAddCategory(CompetitiveDatabase.RedirectionMoves, 1);
                }
                TryAddCategory(CompetitiveDatabase.RecoveryMoves, 1);
                // Hazards prioritized on bulky utility in Singles
                if (!isDoubles && selected.Count < 4 && Rnd.Next(100) < 65)
                    TryAddCategory(CompetitiveDatabase.HazardMoves, 1);
                if (selected.Count < 4 && Rnd.Next(100) < 50)
                    TryAddCategory(CompetitiveDatabase.UtilityStatusMoves, 1);
                // Hazard removal is less prioritized: low probability (15%) in Singles only
                if (!isDoubles && selected.Count < 4 && Rnd.Next(100) < 15)
                    TryAddCategory(CompetitiveDatabase.HazardRemovalMoves, 1);
                break;

            case CompetitiveRole.BulkyAttacker:
                if (isDoubles)
                {
                    TryAddFakeOut();
                    if (Rnd.Next(100) < 50) TryAddCategory(CompetitiveDatabase.WideQuickGuardMoves, 1);
                }
                break;

            case CompetitiveRole.TankyCleaner:
                // Wants attacks primarily
                break;
        }

        // --- Phase 3: STAB ATTACKS fill ---
        // Ensure at least 2 STAB attacking moves for offensive roles
        if (IsOffensiveRole(role))
        {
            FillSTABAttacks(selected, pool, moveNames, moveData, types, pi, 2, ScoreMove, Banned);
        }
        else
        {
            FillSTABAttacks(selected, pool, moveNames, moveData, types, pi, 1, ScoreMove, Banned);
        }

        // --- Phase 3b: DOUBLES PROTECT & WIDE/QUICK GUARD INJECTION ---
        if (isDoubles && selected.Count < 4)
        {
            if (role != CompetitiveRole.OffensiveBreaker || Rnd.Next(100) < 60)
            {
                // If species has Wide Guard or Quick Guard available, give 50% chance to select it (Smogon VGC staple)
                if (Rnd.Next(100) < 50 && pool.Any(m => m > 0 && m < moveNames.Length && CompetitiveDatabase.WideQuickGuardMoves.Contains(moveNames[m])))
                {
                    TryAddCategory(CompetitiveDatabase.WideQuickGuardMoves, 1);
                }
                if (selected.Count < 4)
                {
                    TryAddCategory(CompetitiveDatabase.ProtectMoves, 1);
                }
            }
        }

        // --- Phase 4: COVERAGE fill ---
        // Fill remaining with coverage moves (non-STAB attacks that provide good type coverage)
        FillCoverageAttacks(selected, pool, moveNames, moveData, pi, role, ScoreMove, Banned);

        bool isPhysicalFill = pi.Stats[ATK] >= spa;
        bool CategoryOk(int m) => moveData == null || m >= moveData.Length ||
            moveData[m].Category == 0 ||
            (isPhysicalFill && moveData[m].Category == 1) ||
            (!isPhysicalFill && moveData[m].Category == 2);
        bool IsCompetitiveMove(int m)
        {
            if (m <= 0 || m >= moveNames.Length) return false;
            string name = moveNames[m];
            if (string.IsNullOrEmpty(name) || IsBannedMove(name, pi, selected, pool, moveNames)) return false;
            return CompetitiveDatabase.CompetitiveMoves.Contains(name) || CompetitiveDatabase.SituationalMoves.Contains(name);
        }
        bool IsAttack(int m) => m > 0 && moveData != null && m < moveData.Length && moveData[m].Category != 0;
        bool IsViable(int m) => CompetitiveValidator.IsViableAttack(m, moveData, moveNames);

        while (selected.Count < 4 && pool.Count > 0)
        {
            var remaining = pool.Where(m => m > 0 && m < moveNames.Length).ToList();
            if (remaining.Count == 0) break;

            var tier = remaining.Where(m => CategoryOk(m) && IsCompetitiveMove(m) && (!IsAttack(m) || IsViable(m))).ToList();
            if (tier.Count == 0) tier = remaining.Where(m => IsCompetitiveMove(m) && (!IsAttack(m) || IsViable(m))).ToList();
            if (tier.Count == 0) tier = remaining.Where(m => CategoryOk(m) && (!IsAttack(m) || IsViable(m))).ToList();
            if (tier.Count == 0) tier = remaining.Where(m => !moveNames[m].Equals("Last Resort", StringComparison.OrdinalIgnoreCase)).ToList();
            if (tier.Count == 0) tier = remaining;

            // Spec §2: weighted by score through the seeded Rnd, so a seed still reproduces the set.
            int pick = tier[CompetitiveMath.WeightedIndex(tier.Select(ScoreMove).ToList(), Rnd)];
            selected.Add(pick);
            pool.Remove(pick);
        }

        while (selected.Count < 4) selected.Add(1); // Fallback: Pound

        // --- Phase 6: CATEGORY REPAIR ---
        EnforceAttackCategory(selected, pool, moveNames, moveData, types, pi, statGapDecisive, isPhysicalAttacker);

        return selected.Take(4).ToArray();
    }

    /// <summary>
    /// Last pass over a finished set: swaps out attacks that use the Pokemon's weaker attacking
    /// stat, whenever the legal pool still holds a same-category replacement.
    /// </summary>
    private void EnforceAttackCategory(
        List<int> selected, List<int> pool,
        string[] moveNames, Move[] moveData, int[] types,
        PersonalInfo pi, bool statGapDecisive, bool isPhysicalAttacker)
    {
        if (moveData == null || pi == null) return;

        int wantedCategory = isPhysicalAttacker ? 1 : 2;

        bool Attacking(int m) => m > 0 && m < moveData.Length && moveData[m].Category != 0;
        bool RightSide(int m) => m > 0 && m < moveData.Length && moveData[m].Category == wantedCategory;

        // Keeping the set's type spread is the point of coverage, so a replacement is scored the
        // way the coverage phase scores: STAB and an uncovered type first, raw power last.
        var coveredTypes = selected.Where(Attacking).Select(m => moveData[m].Type).ToHashSet();

        int Score(int m)
        {
            string name = m < moveNames.Length ? moveNames[m] : "";
            bool isCompetitive = !string.IsNullOrEmpty(name) && CompetitiveDatabase.CompetitiveMoves.Contains(name);
            bool isSituational = !isCompetitive && !string.IsNullOrEmpty(name) && CompetitiveDatabase.SituationalMoves.Contains(name);
            bool isStab = types != null && Array.IndexOf(types, moveData[m].Type) >= 0;
            bool newType = !coveredTypes.Contains(moveData[m].Type);
            return (isStab ? 8000 : 0) + (newType ? 4000 : 0)
                 + (isCompetitive ? 2000 : isSituational ? 1000 : 0) + moveData[m].Power;
        }

        for (int i = 0; i < selected.Count; i++)
        {
            int current = selected[i];
            if (!Attacking(current) || RightSide(current)) continue;

            int replacement = pool
                .Where(m => RightSide(m) && CompetitiveValidator.IsViableAttack(m, moveData, moveNames) && !selected.Contains(m))
                .OrderByDescending(Score)
                .DefaultIfEmpty(0)
                .First();

            if (replacement <= 0) continue;   // nothing on the better side is learnable; leave it

            coveredTypes.Remove(moveData[current].Type);
            coveredTypes.Add(moveData[replacement].Type);
            selected[i] = replacement;
            pool.Remove(replacement);
            pool.Add(current);
        }
    }

    // Applies ability-specific move requirements from the manifesto
    private void ApplyAbilityMoveRequirements(
        string ability, List<int> selected, List<int> pool,
        string[] moveNames, Move[] moveData, int[] types, CompetitiveRole role,
        TeamArchetype archetype, PersonalInfo pi)
    {
        bool TryAdd(string moveName)
        {
            if (selected.Count >= 4 || moveNames.Length == 0) return false;
            int id = Array.FindIndex(moveNames, n => n.Equals(moveName, StringComparison.OrdinalIgnoreCase));
            if (id <= 0 || !pool.Contains(id) || selected.Contains(id)) return false;
            selected.Add(id); pool.Remove(id); return true;
        }

        bool isPhysicalForCategory = pi.Stats[ATK] >= pi.Stats[SPA];

        void AddFromCategory(HashSet<string> cat, int count, bool preferSTAB = true)
        {
            var candidates = pool.Where(m =>
                m > 0 && m < moveNames.Length &&
                cat.Contains(moveNames[m]) &&
                !selected.Contains(m)).ToList();

            if (moveData != null)
                candidates.Sort((a, b) =>
                {
                    bool aMatches = a < moveData.Length && ((isPhysicalForCategory && moveData[a].Category == 1) || (!isPhysicalForCategory && moveData[a].Category == 2));
                    bool bMatches = b < moveData.Length && ((isPhysicalForCategory && moveData[b].Category == 1) || (!isPhysicalForCategory && moveData[b].Category == 2));
                    if (aMatches != bMatches) return bMatches ? 1 : -1;

                    if (preferSTAB)
                    {
                        bool aStab = a < moveData.Length && Array.IndexOf(types, moveData[a].Type) >= 0;
                        bool bStab = b < moveData.Length && Array.IndexOf(types, moveData[b].Type) >= 0;
                        if (aStab != bStab) return bStab ? 1 : -1;
                    }
                    int aPow = a < moveData.Length ? moveData[a].Power : 0;
                    int bPow = b < moveData.Length ? moveData[b].Power : 0;
                    return bPow.CompareTo(aPow);
                });

            int added = 0;
            foreach (int id in candidates)
            {
                if (selected.Count >= 4 || added >= count) break;
                selected.Add(id); pool.Remove(id); added++;
            }
        }

        string abilLower = ability.ToLowerInvariant();

        // Aerilate/Pixilate/Galvanize/Dragonize/Refrigerate -> 2+ Normal attacks
        if (abilLower is "aerilate" or "pixilate" or "galvanize" or "dragonize" or "refrigerate")
            AddFromCategory(CompetitiveDatabase.NormalAttacks, 2);

        // Adaptability -> 2+ STAB attacks (handled by STAB fill with higher priority)
        // Already handled in Phase 3

        // Contrary -> stat-drop move
        else if (abilLower == "contrary")
            AddFromCategory(CompetitiveDatabase.ContraryMoves, 1, true);

        // Mega Launcher -> 2+ pulse moves
        else if (abilLower == "mega launcher")
            AddFromCategory(CompetitiveDatabase.PulseMoves, 2, true);

        // Punk Rock -> 1+ sound move
        else if (abilLower == "punk rock")
            AddFromCategory(CompetitiveDatabase.SoundMoves, 1, true);

        // Iron Fist -> 2+ punch moves
        else if (abilLower == "iron fist")
            AddFromCategory(CompetitiveDatabase.PunchingMoves, 2, true);

        // Reckless -> 1+ recoil move
        else if (abilLower == "reckless")
            AddFromCategory(CompetitiveDatabase.RecoilMoves, 1, true);

        // Sharpness -> 1+ slicing move
        else if (abilLower == "sharpness")
            AddFromCategory(CompetitiveDatabase.SlicingMoves, 1, true);

        // Strong Jaw -> 2+ biting moves
        else if (abilLower == "strong jaw")
            AddFromCategory(CompetitiveDatabase.BitingMoves, 2, true);

        // Triage -> 1+ healing attack
        else if (abilLower == "triage")
            AddFromCategory(CompetitiveDatabase.HealingAttackMoves, 1, true);

        // Super Luck -> 1+ high crit move
        else if (abilLower == "super luck")
            AddFromCategory(CompetitiveDatabase.HighCritMoves, 1, true);

        // Simple -> setup move
        else if (abilLower == "simple")
            AddFromCategory(CompetitiveDatabase.SetupMoves, 1);

        // Tough Claws -> 2+ contact moves
        else if (abilLower == "tough claws")
            AddFromCategory(CompetitiveDatabase.ContactMoves, 2, true);

        // Moody -> Substitute + Protect
        else if (abilLower == "moody")
        {
            TryAdd("Substitute"); TryAdd("Protect");
        }

        // Speed Boost -> Protect (common, offensive)
        else if (abilLower == "speed boost" && IsOffensiveRole(role))
            TryAdd("Protect");

        // Prankster -> 2+ status moves
        else if (abilLower == "prankster")
            AddFromCategory(CompetitiveDatabase.UtilityStatusMoves, 2);

        // Stamina -> Body Press
        else if (abilLower == "stamina")
            TryAdd("Body Press");

        // Snow Warning -> Blizzard (if SpA >= Atk), Aurora Veil handled by item logic
        else if (abilLower == "snow warning")
        {
            if (pi.Stats[SPA] >= pi.Stats[ATK]) TryAdd("Blizzard");
        }

        // Shed Skin + offensive/utility -> Rest
        else if (abilLower == "shed skin")
        {
            if (Rnd.Next(100) < 60) TryAdd("Rest");
        }

        // Grassy Surge + physical -> Grassy Glide
        else if (abilLower == "grassy surge" && pi.Stats[ATK] >= pi.Stats[SPA])
            TryAdd("Grassy Glide");

        // Electric Surge + special -> Rising Voltage
        else if (abilLower == "electric surge" && pi.Stats[SPA] >= pi.Stats[ATK])
            TryAdd("Rising Voltage");

        // Psychic Surge + special -> Expanding Force
        else if (abilLower == "psychic surge" && pi.Stats[SPA] >= pi.Stats[ATK])
            TryAdd("Expanding Force");

        bool isPhysicalMon = pi.Stats[ATK] >= pi.Stats[SPA];
        IEnumerable<int> OrderByBetterCategoryThenPower(IEnumerable<int> candidates) =>
            candidates.OrderByDescending(m =>
            {
                bool matchesCat = (isPhysicalMon && moveData[m].Category == 1) ||
                                  (!isPhysicalMon && moveData[m].Category == 2);
                return (matchesCat ? 100000 : 0) + moveData[m].Power;
            });

        // Dark Aura -> 2+ Dark moves (favors the mon's higher attacking stat)
        if (abilLower == "dark aura")
        {
            var darkMoves = pool.Where(m =>
                m > 0 && m < (moveData?.Length ?? 0) &&
                moveData[m].Type == PokemonTypes.Dark &&
                moveData[m].Category != 0).ToList();
            int added = 0;
            foreach (int id in OrderByBetterCategoryThenPower(darkMoves))
            {
                if (selected.Count >= 4 || added >= 2) break;
                selected.Add(id); pool.Remove(id); added++;
            }
        }

        // Fairy Aura -> 2+ Fairy moves (favors the mon's higher attacking stat)
        else if (abilLower == "fairy aura")
        {
            var fairyMoves = pool.Where(m =>
                m > 0 && m < (moveData?.Length ?? 0) &&
                moveData[m].Type == PokemonTypes.Fairy &&
                moveData[m].Category != 0).ToList();
            int added = 0;
            foreach (int id in OrderByBetterCategoryThenPower(fairyMoves))
            {
                if (selected.Count >= 4 || added >= 2) break;
                selected.Add(id); pool.Remove(id); added++;
            }
        }

        // Dragon's Maw -> 2+ Dragon moves (favors the mon's higher attacking stat)
        else if (abilLower == "dragon's maw")
        {
            var dragonMoves = pool.Where(m =>
                m > 0 && m < (moveData?.Length ?? 0) &&
                moveData[m].Type == PokemonTypes.Dragon &&
                moveData[m].Category != 0).ToList();
            int added = 0;
            foreach (int id in OrderByBetterCategoryThenPower(dragonMoves))
            {
                if (selected.Count >= 4 || added >= 2) break;
                selected.Add(id); pool.Remove(id); added++;
            }
        }

        // Desolate Land / Drought -> 1+ Fire attack (favors the mon's higher attacking stat)
        else if (abilLower is "desolate land" or "drought")
        {
            var fireMoves = pool.Where(m =>
                m > 0 && m < (moveData?.Length ?? 0) &&
                moveData[m].Type == PokemonTypes.Fire &&
                moveData[m].Category != 0).ToList();
            if (fireMoves.Count > 0)
            {
                int best = OrderByBetterCategoryThenPower(fireMoves).First();
                selected.Add(best); pool.Remove(best);
            }
        }

        // Drizzle / Primordial Sea -> 1+ Water attack (favors the mon's higher attacking stat)
        else if (abilLower is "drizzle" or "primordial sea")
        {
            var waterMoves = pool.Where(m =>
                m > 0 && m < (moveData?.Length ?? 0) &&
                moveData[m].Type == PokemonTypes.Water &&
                moveData[m].Category != 0).ToList();
            if (waterMoves.Count > 0)
            {
                int best = OrderByBetterCategoryThenPower(waterMoves).First();
                selected.Add(best); pool.Remove(best);
            }
            // Electric-type with rain -> Thunder
            if (Array.IndexOf(types, PokemonTypes.Electric) >= 0 && pi.Stats[SPA] >= pi.Stats[ATK])
                TryAdd("Thunder");
            // Flying-type with rain -> Hurricane
            if (Array.IndexOf(types, PokemonTypes.Flying) >= 0 && pi.Stats[SPA] >= pi.Stats[ATK])
                TryAdd("Hurricane");
        }

        // Water Bubble -> 1+ Water attack
        else if (abilLower == "water bubble")
        {
            var waterMoves = pool.Where(m =>
                m > 0 && m < (moveData?.Length ?? 0) &&
                moveData[m].Type == PokemonTypes.Water &&
                moveData[m].Category != 0).ToList();
            if (waterMoves.Count > 0)
            {
                int best = OrderByBetterCategoryThenPower(waterMoves).First();
                selected.Add(best); pool.Remove(best);
            }
        }

        // Beads of Ruin -> 3+ Special attacks
        else if (abilLower == "beads of ruin")
        {
            var specAttacks = pool.Where(m =>
                m > 0 && m < (moveData?.Length ?? 0) &&
                moveData[m].Category == 2).OrderByDescending(m => moveData[m].Power).ToList();
            int added = 0;
            foreach (int id in specAttacks)
            {
                if (selected.Count >= 4 || added >= 3) break;
                selected.Add(id); pool.Remove(id); added++;
            }
        }

        // Physical Attack boosting abilities -> 2+ Physical attacks
        else if (abilLower is "chilling neigh" or "moxie" or "defiant" or "hustle" or "intrepid sword" or "orichalcum pulse" or "sword of ruin")
        {
            var physAttacks = pool.Where(m =>
                m > 0 && m < (moveData?.Length ?? 0) &&
                moveData[m].Category == 1).OrderByDescending(m => moveData[m].Power).ToList();
            int added = 0;
            foreach (int id in physAttacks)
            {
                if (selected.Count >= 4 || added >= 2) break;
                selected.Add(id); pool.Remove(id); added++;
            }
        }

        // Special Attack boosting abilities -> 2+ Special attacks
        else if (abilLower is "soul-heart" or "competitive" or "hadron engine" or "berserk" or "solar power" or "grim neigh")
        {
            var specAttacks = pool.Where(m =>
                m > 0 && m < (moveData?.Length ?? 0) &&
                moveData[m].Category == 2).OrderByDescending(m => moveData[m].Power).ToList();
            int added = 0;
            foreach (int id in specAttacks)
            {
                if (selected.Count >= 4 || added >= 2) break;
                selected.Add(id); pool.Remove(id); added++;
            }
        }

        // No Guard -> 1+ high-power, low-accuracy attack (<= 80% acc, >= 100 power)
        else if (abilLower == "no guard")
        {
            var noGuardMoves = pool.Where(m =>
                m > 0 && m < (moveData?.Length ?? 0) &&
                moveData[m].Category != 0 &&
                moveData[m].Power >= 100 &&
                moveData[m].Accuracy <= 80 && moveData[m].Accuracy > 0).ToList();
            if (noGuardMoves.Count > 0)
            {
                int best = OrderByBetterCategoryThenPower(noGuardMoves).First();
                selected.Add(best); pool.Remove(best);
            }
        }

        // Protean / Libero -> all different-type attacks
        else if (abilLower is "protean" or "libero")
        {
            // Handled in coverage fill — ensure all attack types differ
            // Mark for the fill phase
        }

        // Download / Neuroforce -> 3+ attacks
        // Handled by ensuring minimum attacks in role-based logic
    }

    // Fills STAB attacks, each time taking the best-scoring legal one (spec §2)
    private static void FillSTABAttacks(
        List<int> selected, List<int> pool, string[] moveNames, Move[] moveData, int[] types,
        PersonalInfo pi, int minSTAB, Func<int, double> score, Func<int, bool> banned)
    {
        if (moveData == null || types.Length == 0) return;
        bool isPhysical = pi.Stats[ATK] >= pi.Stats[SPA];

        int needed = minSTAB - selected.Count(m =>
            m > 0 && m < moveData.Length &&
            Array.IndexOf(types, moveData[m].Type) >= 0 &&
            moveData[m].Category != 0);

        while (needed > 0 && selected.Count < 4)
        {
            int pick = pool.Where(m =>
                    m > 0 && m < moveData.Length &&
                    Array.IndexOf(types, moveData[m].Type) >= 0 &&
                    CompetitiveValidator.IsViableAttack(m, moveData, moveNames) &&
                    !selected.Contains(m) && !banned(m) &&
                    ((isPhysical && moveData[m].Category == 1) || (!isPhysical && moveData[m].Category == 2)) &&
                    // Never a redundant attack of the exact same type and category (e.g. Sludge Bomb + Sludge Wave)
                    !selected.Any(sm => sm > 0 && sm < moveData.Length &&
                                        moveData[sm].Category == moveData[m].Category &&
                                        moveData[sm].Type == moveData[m].Type))
                .OrderByDescending(score)
                .DefaultIfEmpty(0)
                .First();
            if (pick <= 0) break;
            selected.Add(pick); pool.Remove(pick); needed--;
        }
    }

    // Fills coverage attacks: each pick is the best score among viable attacks of a type the set lacks
    private static void FillCoverageAttacks(
        List<int> selected, List<int> pool, string[] moveNames, Move[] moveData,
        PersonalInfo pi, CompetitiveRole role, Func<int, double> score, Func<int, bool> banned)
    {
        if (moveData == null) return;
        if (IsDefensiveRole(role)) return; // Defensive roles don't need coverage fill
        bool isPhysical = pi.Stats[ATK] >= pi.Stats[SPA];

        while (selected.Count < 4)
        {
            var coveredTypes = selected.Where(m => m > 0 && m < moveData.Length && moveData[m].Category != 0)
                                       .Select(m => moveData[m].Type).ToHashSet();
            int pick = pool.Where(m =>
                    m > 0 && m < moveData.Length &&
                    CompetitiveValidator.IsViableAttack(m, moveData, moveNames) &&
                    !coveredTypes.Contains(moveData[m].Type) &&
                    !selected.Contains(m) && !banned(m) &&
                    ((isPhysical && moveData[m].Category == 1) || (!isPhysical && moveData[m].Category == 2)))
                .OrderByDescending(score)
                .DefaultIfEmpty(0)
                .First();
            if (pick <= 0) break;
            selected.Add(pick); pool.Remove(pick);
        }
    }

    /// <summary>The manifesto's MUST NOT move rules, shared by every phase that picks a move.</summary>
    private static bool IsBannedMove(string name, PersonalInfo pi, List<int> selected, List<int> pool, string[] moveNames)
    {
        if (string.IsNullOrEmpty(name)) return false;
        // Trick Room on a fast Pokemon (Spe > 65)
        if (pi != null && pi.Stats[SPE] > 65 && GameNameComparer.Instance.Equals(name, "Trick Room")) return true;
        // Fixed damage (Seismic Toss / Night Shade) on an attacker (Atk or SpA >= 75)
        if (pi != null && (pi.Stats[ATK] >= 75 || pi.Stats[SPA] >= 75) && CompetitiveDatabase.FixedDamageMoves.Contains(name)) return true;
        // Rest alongside instant recovery, or with no Sleep Talk to pair it with
        if (GameNameComparer.Instance.Equals(name, "Rest"))
            return selected.Any(m => m > 0 && m < moveNames.Length && CompetitiveDatabase.RecoveryMoves.Contains(moveNames[m]))
                || !pool.Any(m => m > 0 && m < moveNames.Length && GameNameComparer.Instance.Equals(moveNames[m], "Sleep Talk"));
        return false;
    }

    // Power Herb is picked after the moveset (AssignItem), so only weather can be seen here.
    private static bool NeedsChargeTurn(string move, string ability, List<string> teamAbilities)
    {
        if (CompetitiveDatabase.RechargeMoves.Contains(move)) return true;
        if (!CompetitiveDatabase.TwoTurnMoves.Contains(move)) return false;
        bool Has(string a) => GameNameComparer.Instance.Equals(ability, a) ||
                              teamAbilities?.Any(t => GameNameComparer.Instance.Equals(t, a)) == true;
        if (GameNameComparer.Instance.Equals(move, "Solar Beam") || GameNameComparer.Instance.Equals(move, "Solar Blade"))
            return !(Has("Drought") || Has("Desolate Land"));
        if (GameNameComparer.Instance.Equals(move, "Electro Shot"))
            return !(Has("Drizzle") || Has("Primordial Sea"));
        return true;
    }

    private static HashSet<string> ResolveMoveNames(int[] moves, string[] moveNames)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (moves == null || moveNames == null) return set;
        foreach (int m in moves)
        {
            if (m > 0 && m < moveNames.Length)
            {
                string name = moveNames[m];
                if (!string.IsNullOrEmpty(name)) set.Add(name);
            }
        }
        return set;
    }

    public static bool IsOffensiveRole(CompetitiveRole role) => role switch
    {
        CompetitiveRole.OffensiveSweeper or CompetitiveRole.OffensiveBreaker or
        CompetitiveRole.OffensiveCleaner or CompetitiveRole.OffensivePivot or
        CompetitiveRole.OffensiveLead or CompetitiveRole.OffensiveUtility or
        CompetitiveRole.SpeedControl => true,
        _ => false
    };

    public static bool IsDefensiveRole(CompetitiveRole role) => role switch
    {
        CompetitiveRole.DefensiveWall or CompetitiveRole.DefensivePivot => true,
        _ => false
    };

    public static bool IsBulkyRole(CompetitiveRole role) => role switch
    {
        CompetitiveRole.BulkyAttacker or CompetitiveRole.BulkySweeper or
        CompetitiveRole.BulkyPivot or CompetitiveRole.BulkyLead or
        CompetitiveRole.BulkyUtility or CompetitiveRole.TankySweeper or
        CompetitiveRole.TankyCleaner => true,
        _ => false
    };

    /// <summary>Which team jobs a set of moves can do (v2 spec §3 role coverage).</summary>
    public static TeamRoles RolesOf(IEnumerable<int> moves, string[] moveNames, bool trickRoomTeam)
    {
        var roles = TeamRoles.None;
        foreach (int m in moves)
        {
            if (m <= 0 || m >= moveNames.Length) continue;
            string n = moveNames[m];
            if (CompetitiveDatabase.HazardMoves.Contains(n)) roles |= TeamRoles.Hazards;
            if (CompetitiveDatabase.HazardRemovalMoves.Contains(n)) roles |= TeamRoles.HazardRemoval;
            if (CompetitiveDatabase.PivotMoves.Contains(n)) roles |= TeamRoles.Pivot;
            if (CompetitiveDatabase.TeamSpeedControlMoves.Contains(n) ||
                (trickRoomTeam && GameNameComparer.Instance.Equals(n, "Trick Room")))
                roles |= TeamRoles.SpeedControl;
        }
        return roles;
    }
}
