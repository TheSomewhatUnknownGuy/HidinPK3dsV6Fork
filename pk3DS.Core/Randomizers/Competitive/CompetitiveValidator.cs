using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.Structures.PersonalInfo;
using pk3DS.Core.Structures;

namespace pk3DS.Core.Randomizers.Competitive;

public static class CompetitiveValidator
{
    // Stat index constants for PersonalInfo.Stats: [HP, ATK, DEF, SPE, SPA, SPD]
    private const int HP = CompetitiveDatabase.HP;
    private const int ATK = CompetitiveDatabase.ATK;
    private const int DEF = CompetitiveDatabase.DEF;
    private const int SPE = CompetitiveDatabase.SPE;
    private const int SPA = CompetitiveDatabase.SPA;
    private const int SPD = CompetitiveDatabase.SPD;

    public static void ValidateAndSanitize(
        TrainerPoke7 pk,
        PersonalInfo pi,
        string abilityName,
        CompetitiveRole role,
        string[] itemNames,
        string[] moveNames,
        Move[] moveData = null,
        LegalLearnsetAggregator learnsets = null,
        bool isDoubles = false)
    {
        if (pk == null || pi == null) return;

        ushort GetItemId(string name)
        {
            if (itemNames == null || itemNames.Length == 0 || string.IsNullOrEmpty(name)) return 0;
            int idx = Array.FindIndex(itemNames, z => z.Equals(name, StringComparison.OrdinalIgnoreCase));
            return idx > 0 ? (ushort)idx : (ushort)0;
        }

        ushort ReturnItem(string primary, string fallback = "Leftovers")
        {
            ushort id = GetItemId(primary);
            if (id > 0) return id;
            ushort fallbackId = GetItemId(fallback);
            return fallbackId > 0 ? fallbackId : GetItemId("Sitrus Berry");
        }

        string GetItemName(int id)
        {
            if (itemNames == null || id < 0 || id >= itemNames.Length) return "";
            return itemNames[id] ?? "";
        }

        string GetMoveName(int id)
        {
            if (moveNames == null || id < 0 || id >= moveNames.Length) return "";
            return moveNames[id] ?? "";
        }

        var currentMoveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int physCount = 0, specCount = 0, statusCount = 0;
        if (pk.Moves != null && moveNames != null)
        {
            foreach (int m in pk.Moves)
            {
                if (m <= 0) continue;
                if (m < moveNames.Length)
                {
                    string mName = moveNames[m];
                    if (!string.IsNullOrEmpty(mName)) currentMoveNames.Add(mName);
                }
                if (moveData != null && m < moveData.Length)
                {
                    switch (moveData[m].Category)
                    {
                        case 0: statusCount++; break;
                        case 1: physCount++; break;
                        case 2: specCount++; break;
                    }
                }
            }
        }

        string currentItem = GetItemName(pk.Item);
        string defaultDefensive = (pi.Types != null && Array.IndexOf(pi.Types, PokemonTypes.Poison) >= 0) ? "Black Sludge" : "Leftovers";

        // 1. LIGHT CLAY: only with (Light Screen + Reflect) or (Snow Warning + Aurora Veil)
        if (currentItem.Equals("Light Clay", StringComparison.OrdinalIgnoreCase))
        {
            bool hasDualScreens = currentMoveNames.Contains("Light Screen") && currentMoveNames.Contains("Reflect");
            bool hasSnowVeil = abilityName.Equals("Snow Warning", StringComparison.OrdinalIgnoreCase) && currentMoveNames.Contains("Aurora Veil");
            if (!hasDualScreens && !hasSnowVeil)
            {
                pk.Item = (int)ReturnItem(CompetitiveBuildEngine.IsFrail(pi) ? "Focus Sash" : "Heavy-Duty Boots");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 2. LUM BERRY: only with setup move or Outrage/Raging Fury
        if (currentItem.Equals("Lum Berry", StringComparison.OrdinalIgnoreCase))
        {
            bool hasSetup = currentMoveNames.Overlaps(CompetitiveDatabase.SetupMoves);
            bool hasOutrage = currentMoveNames.Contains("Outrage") || currentMoveNames.Contains("Raging Fury");
            if (!hasSetup && !hasOutrage)
            {
                pk.Item = (int)GetItemId(pi.Stats[HP] + Math.Max(pi.Stats[DEF], pi.Stats[SPD]) > 150 ? defaultDefensive : "Life Orb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 3. ASSAULT VEST: no status moves allowed, no Protect moves
        if (currentItem.Equals("Assault Vest", StringComparison.OrdinalIgnoreCase) &&
            (statusCount > 0 || currentMoveNames.Overlaps(CompetitiveDatabase.ProtectMoves)))
        {
            pk.Item = (int)GetItemId(pi.Stats[HP] + Math.Max(pi.Stats[DEF], pi.Stats[SPD]) > 150 ? defaultDefensive : "Life Orb");
            currentItem = GetItemName(pk.Item);
        }

        // 4. CHOICE ITEMS: no Protect/Setup/Recovery, no status moves except Trick/Switcheroo
        if (currentItem.Equals("Choice Band", StringComparison.OrdinalIgnoreCase) ||
            currentItem.Equals("Choice Specs", StringComparison.OrdinalIgnoreCase) ||
            currentItem.Equals("Choice Scarf", StringComparison.OrdinalIgnoreCase))
        {
            bool hasInvalidStatus = currentMoveNames.Overlaps(CompetitiveDatabase.ProtectMoves) ||
                                    currentMoveNames.Overlaps(CompetitiveDatabase.SetupMoves) ||
                                    currentMoveNames.Overlaps(CompetitiveDatabase.RecoveryMoves);
            if (!hasInvalidStatus && moveData != null && pk.Moves != null)
            {
                foreach (int m in pk.Moves)
                {
                    if (m <= 0 || m >= moveData.Length || moveData[m].Category != 0) continue;
                    string mName = GetMoveName(m);
                    if (!mName.Equals("Trick", StringComparison.OrdinalIgnoreCase) &&
                        !mName.Equals("Switcheroo", StringComparison.OrdinalIgnoreCase))
                    {
                        hasInvalidStatus = true;
                        break;
                    }
                }
            }
            if (hasInvalidStatus)
            {
                pk.Item = (int)GetItemId(pi.Stats[HP] + Math.Max(pi.Stats[DEF], pi.Stats[SPD]) > 150 ? defaultDefensive : "Life Orb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 5. Z-CRYSTALS: MUST have matching move/type
        if (IsZCrystal(currentItem))
        {
            bool isZValid = false;
            if (CompetitiveDatabase.SpeciesZCrystals.TryGetValue(pk.Species, out var specZ) &&
                currentItem.Equals(specZ.Crystal, StringComparison.OrdinalIgnoreCase))
            {
                isZValid = currentMoveNames.Contains(specZ.Move);
            }
            else
            {
                int zType = -1;
                foreach (var kvp in CompetitiveDatabase.TypeZCrystals)
                {
                    if (kvp.Value.Equals(currentItem, StringComparison.OrdinalIgnoreCase))
                    {
                        zType = kvp.Key;
                        break;
                    }
                }
                if (zType >= 0 && moveData != null && pk.Moves != null)
                {
                    foreach (int m in pk.Moves)
                    {
                        if (m > 0 && m < moveData.Length && moveData[m].Category != 0 && moveData[m].Type == zType)
                        {
                            isZValid = true;
                            break;
                        }
                    }
                }
            }
            if (!isZValid)
            {
                pk.Item = (int)GetItemId("Life Orb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 6. EVIOLITE: pre-evolutions only
        if (currentItem.Equals("Eviolite", StringComparison.OrdinalIgnoreCase))
        {
            if (!Legal.CanSpeciesEvolve(pk.Species, learnsets?.GameConfig))
            {
                pk.Item = (int)GetItemId(pi.Stats[HP] + Math.Max(pi.Stats[DEF], pi.Stats[SPD]) > 150 ? defaultDefensive : "Life Orb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 7. BLACK SLUDGE: Poison-type only
        if (currentItem.Equals("Black Sludge", StringComparison.OrdinalIgnoreCase))
        {
            if (!pi.Types.Contains(PokemonTypes.Poison))
            {
                pk.Item = (int)GetItemId("Leftovers");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 8. AIR BALLOON: not redundant (no Flying type / Levitate / Earth Eater)
        if (currentItem.Equals("Air Balloon", StringComparison.OrdinalIgnoreCase))
        {
            if (pi.Types.Contains(PokemonTypes.Flying) ||
                abilityName.Equals("Levitate", StringComparison.OrdinalIgnoreCase) ||
                abilityName.Equals("Earth Eater", StringComparison.OrdinalIgnoreCase))
            {
                pk.Item = (int)GetItemId("Life Orb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 9. LOADED DICE: must have a multi-hit move
        if (currentItem.Equals("Loaded Dice", StringComparison.OrdinalIgnoreCase))
        {
            if (!currentMoveNames.Overlaps(CompetitiveDatabase.MultiHitMoves))
            {
                pk.Item = (int)GetItemId("Life Orb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // PUNCHING GLOVE: must have a punching move
        if (currentItem.Equals("Punching Glove", StringComparison.OrdinalIgnoreCase))
        {
            if (!currentMoveNames.Overlaps(CompetitiveDatabase.PunchingMoves))
            {
                pk.Item = (int)GetItemId("Life Orb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // THROAT SPRAY: must have a sound move
        if (currentItem.Equals("Throat Spray", StringComparison.OrdinalIgnoreCase))
        {
            if (!currentMoveNames.Overlaps(CompetitiveDatabase.SoundMoves))
            {
                pk.Item = (int)GetItemId("Life Orb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // EJECT PACK: must have a stat-drop move
        if (currentItem.Equals("Eject Pack", StringComparison.OrdinalIgnoreCase))
        {
            if (!currentMoveNames.Overlaps(CompetitiveDatabase.StatDropMoves))
            {
                pk.Item = (int)GetItemId("Heavy-Duty Boots");
                currentItem = GetItemName(pk.Item);
            }
        }

        // BLUNDER POLICY: must have a move with accuracy < 90%
        if (currentItem.Equals("Blunder Policy", StringComparison.OrdinalIgnoreCase))
        {
            bool hasLowAcc = pk.Moves != null && moveData != null && pk.Moves.Any(m => m > 0 && m < moveData.Length && moveData[m].Accuracy > 0 && moveData[m].Accuracy < 90);
            if (!hasLowAcc)
            {
                pk.Item = (int)GetItemId("Life Orb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 10. POWER HERB: must have a two-turn move
        if (currentItem.Equals("Power Herb", StringComparison.OrdinalIgnoreCase))
        {
            if (!currentMoveNames.Overlaps(CompetitiveDatabase.TwoTurnMoves))
            {
                pk.Item = (int)GetItemId("White Herb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 11. TOXIC ORB: must have Poison Heal, Toxic Boost, or Trick/Switcheroo
        if (currentItem.Equals("Toxic Orb", StringComparison.OrdinalIgnoreCase))
        {
            if (!abilityName.Equals("Poison Heal", StringComparison.OrdinalIgnoreCase) &&
                !abilityName.Equals("Toxic Boost", StringComparison.OrdinalIgnoreCase) &&
                !currentMoveNames.Contains("Trick") && !currentMoveNames.Contains("Switcheroo"))
            {
                pk.Item = (int)GetItemId(defaultDefensive);
                currentItem = GetItemName(pk.Item);
            }
        }

        // 12. FLAME ORB: must have Guts, Flare Boost, or Trick/Switcheroo
        if (currentItem.Equals("Flame Orb", StringComparison.OrdinalIgnoreCase))
        {
            if (!abilityName.Equals("Guts", StringComparison.OrdinalIgnoreCase) &&
                !abilityName.Equals("Flare Boost", StringComparison.OrdinalIgnoreCase) &&
                !currentMoveNames.Contains("Trick") && !currentMoveNames.Contains("Switcheroo"))
            {
                pk.Item = (int)GetItemId(defaultDefensive);
                currentItem = GetItemName(pk.Item);
            }
        }

        // 9. WISE GLASSES: must have 3+ special attacks
        if (currentItem.Equals("Wise Glasses", StringComparison.OrdinalIgnoreCase) && specCount < 3)
        {
            pk.Item = (int)GetItemId("Life Orb");
            currentItem = GetItemName(pk.Item);
        }

        // 10. MUSCLE BAND: must have 3+ physical attacks
        if (currentItem.Equals("Muscle Band", StringComparison.OrdinalIgnoreCase) && physCount < 3)
        {
            pk.Item = (int)GetItemId("Life Orb");
            currentItem = GetItemName(pk.Item);
        }

        // 11. NORMAL GEM: must have Explosion and Speed >= 90
        if (currentItem.Equals("Normal Gem", StringComparison.OrdinalIgnoreCase))
        {
            if (!currentMoveNames.Contains("Explosion") || pi.Stats[SPE] < 90)
            {
                pk.Item = (int)GetItemId("Focus Sash");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 12. ROCKY HELMET: must have HP + Def > 160
        if (currentItem.Equals("Rocky Helmet", StringComparison.OrdinalIgnoreCase))
        {
            if (pi.Stats[HP] + pi.Stats[DEF] <= 160)
            {
                pk.Item = (int)GetItemId(defaultDefensive);
                currentItem = GetItemName(pk.Item);
            }
        }

        // 13. SALAC BERRY: must have a setup move or Substitute
        if (currentItem.Equals("Salac Berry", StringComparison.OrdinalIgnoreCase))
        {
            if (!currentMoveNames.Overlaps(CompetitiveDatabase.SetupMoves) && !currentMoveNames.Contains("Substitute"))
            {
                pk.Item = (int)GetItemId("Life Orb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 14. EXPERT BELT: must have 3+ attacking moves
        if (currentItem.Equals("Expert Belt", StringComparison.OrdinalIgnoreCase))
        {
            if (physCount + specCount < 3)
            {
                pk.Item = (int)GetItemId("Life Orb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 15. SCOPE LENS: must have Super Luck and a high-crit move
        if (currentItem.Equals("Scope Lens", StringComparison.OrdinalIgnoreCase))
        {
            if (!abilityName.Equals("Super Luck", StringComparison.OrdinalIgnoreCase) ||
                !currentMoveNames.Overlaps(CompetitiveDatabase.HighCritMoves))
            {
                pk.Item = (int)GetItemId("Life Orb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 16. BOOSTER ENERGY: must have Protosynthesis or Quark Drive
        if (currentItem.Equals("Booster Energy", StringComparison.OrdinalIgnoreCase))
        {
            if (!abilityName.Equals("Protosynthesis", StringComparison.OrdinalIgnoreCase) &&
                !abilityName.Equals("Quark Drive", StringComparison.OrdinalIgnoreCase))
            {
                pk.Item = (int)GetItemId("Life Orb");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 17. TERRAIN SEEDS: validated at team level (see ValidateTeam)

        // 18. TERRAIN EXTENDER: must have a terrain-setting ability
        if (currentItem.Equals("Terrain Extender", StringComparison.OrdinalIgnoreCase))
        {
            bool hasTerrainAbility = abilityName.Equals("Electric Surge", StringComparison.OrdinalIgnoreCase) ||
                                      abilityName.Equals("Grassy Surge", StringComparison.OrdinalIgnoreCase) ||
                                      abilityName.Equals("Psychic Surge", StringComparison.OrdinalIgnoreCase) ||
                                      abilityName.Equals("Misty Surge", StringComparison.OrdinalIgnoreCase);
            if (!hasTerrainAbility)
            {
                pk.Item = (int)ReturnItem("Heavy-Duty Boots");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 19. HEAT ROCK / DAMP ROCK / ICY ROCK: must have the matching weather ability
        if (currentItem.Equals("Heat Rock", StringComparison.OrdinalIgnoreCase) &&
            !abilityName.Equals("Drought", StringComparison.OrdinalIgnoreCase))
        {
            pk.Item = (int)ReturnItem("Leftovers");
            currentItem = GetItemName(pk.Item);
        }
        if (currentItem.Equals("Damp Rock", StringComparison.OrdinalIgnoreCase) &&
            !abilityName.Equals("Drizzle", StringComparison.OrdinalIgnoreCase))
        {
            pk.Item = (int)ReturnItem("Leftovers");
            currentItem = GetItemName(pk.Item);
        }
        if (currentItem.Equals("Icy Rock", StringComparison.OrdinalIgnoreCase) &&
            !abilityName.Equals("Snow Warning", StringComparison.OrdinalIgnoreCase))
        {
            pk.Item = (int)ReturnItem("Leftovers");
            currentItem = GetItemName(pk.Item);
        }
        if (currentItem.Equals("Smooth Rock", StringComparison.OrdinalIgnoreCase) &&
            !abilityName.Equals("Sand Stream", StringComparison.OrdinalIgnoreCase))
        {
            pk.Item = (int)ReturnItem("Leftovers");
            currentItem = GetItemName(pk.Item);
        }

        // 20. ANGER SHELL: must hold Focus Sash
        if (abilityName.Equals("Anger Shell", StringComparison.OrdinalIgnoreCase))
        {
            pk.Item = (int)ReturnItem("Focus Sash");
            currentItem = GetItemName(pk.Item);
        }

        // 21. HARVEST: must hold a berry (Sitrus Berry or Starf Berry)
        if (abilityName.Equals("Harvest", StringComparison.OrdinalIgnoreCase))
        {
            if (!currentItem.EndsWith("Berry", StringComparison.OrdinalIgnoreCase))
            {
                pk.Item = (int)ReturnItem("Sitrus Berry");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 22. SUPER LUCK: must hold Scope Lens
        if (abilityName.Equals("Super Luck", StringComparison.OrdinalIgnoreCase))
        {
            pk.Item = (int)ReturnItem("Scope Lens");
            currentItem = GetItemName(pk.Item);
        }

        // 23. UNBURDEN: must hold a consumable item to trigger speed boost
        if (abilityName.Equals("Unburden", StringComparison.OrdinalIgnoreCase))
        {
            bool isNonConsumable =
                currentItem.EndsWith("ium Z", StringComparison.OrdinalIgnoreCase) ||
                (currentItem.EndsWith("ite", StringComparison.OrdinalIgnoreCase) && !currentItem.Equals("Eviolite", StringComparison.OrdinalIgnoreCase)) ||
                currentItem.StartsWith("Choice", StringComparison.OrdinalIgnoreCase) ||
                currentItem.Equals("Life Orb", StringComparison.OrdinalIgnoreCase) ||
                currentItem.Equals("Leftovers", StringComparison.OrdinalIgnoreCase) ||
                currentItem.Equals("Assault Vest", StringComparison.OrdinalIgnoreCase) ||
                currentItem.Equals("Rocky Helmet", StringComparison.OrdinalIgnoreCase) ||
                currentItem.Equals("Heavy-Duty Boots", StringComparison.OrdinalIgnoreCase) ||
                currentItem.Equals("Black Sludge", StringComparison.OrdinalIgnoreCase) ||
                currentItem.Equals("Eviolite", StringComparison.OrdinalIgnoreCase);

            if (isNonConsumable)
            {
                if (CompetitiveBuildEngine.IsFrail(pi))
                    pk.Item = (int)ReturnItem("Focus Sash", "Air Balloon");
                else if (currentMoveNames.Overlaps(CompetitiveDatabase.StatDropMoves))
                    pk.Item = (int)ReturnItem("White Herb", "Sitrus Berry");
                else if (currentMoveNames.Overlaps(CompetitiveDatabase.TwoTurnMoves))
                    pk.Item = (int)ReturnItem("Power Herb", "Sitrus Berry");
                else
                    pk.Item = (int)ReturnItem("Sitrus Berry", "Air Balloon");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 24. SAFETY GOGGLES: redirection users or anti-spore pivots; invalid on Grass or Overcoat
        if (currentItem.Equals("Safety Goggles", StringComparison.OrdinalIgnoreCase))
        {
            bool isGrass = pi.Types != null && pi.Types.Contains(PokemonTypes.Grass);
            bool hasOvercoat = abilityName.Equals("Overcoat", StringComparison.OrdinalIgnoreCase);
            if (isGrass || hasOvercoat)
            {
                pk.Item = (int)ReturnItem(defaultDefensive, "Sitrus Berry");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 25. WEAKNESS POLICY: must have sufficient bulk (>= 210) or synergy ability, offensive role, >= 2 weaknesses, and >= 2 direct matching attacks
        if (currentItem.Equals("Weakness Policy", StringComparison.OrdinalIgnoreCase))
        {
            bool hasBulk = (pi.Stats[HP] + pi.Stats[DEF] + pi.Stats[SPD]) >= 210;
            bool hasSynergyAbility = CompetitiveDatabase.WeaknessPolicyAbilities.Contains(abilityName);
            bool isOffensiveRole = role is CompetitiveRole.BulkySweeper
                or CompetitiveRole.TankySweeper
                or CompetitiveRole.BulkyAttacker
                or CompetitiveRole.OffensiveSweeper;

            int weaknessCount = 0;
            if (pi.Types != null)
            {
                for (int t = 0; t <= 17; t++)
                {
                    if (TypeEffectivenessChart.GetEffectiveness(t, pi.Types) > 1.0f)
                        weaknessCount++;
                }
            }

            int directAttacks = 0;
            bool isPhysical = pi.Stats[ATK] >= pi.Stats[SPA];
            if (pk.EVs != null && pk.EVs.Length >= 6)
            {
                if (pk.EVs[ATK] >= 100 && pk.EVs[SPA] == 0) isPhysical = true;
                else if (pk.EVs[SPA] >= 100 && pk.EVs[ATK] == 0) isPhysical = false;
            }

            if (pk.Moves != null && moveData != null)
            {
                foreach (int m in pk.Moves)
                {
                    if (m <= 0 || m >= moveData.Length) continue;
                    string mName = moveNames != null && m < moveNames.Length ? moveNames[m] : "";
                    if (CompetitiveDatabase.FixedDamageMoves.Contains(mName) || mName.Equals("Foul Play", StringComparison.OrdinalIgnoreCase)) continue;
                    if (moveData[m].Category == 1 && isPhysical) directAttacks++;
                    else if (moveData[m].Category == 2 && !isPhysical) directAttacks++;
                }
            }

            if ((!hasBulk && !hasSynergyAbility) || !isOffensiveRole || weaknessCount < 2 || directAttacks < 2)
            {
                bool isDef = (pi.Stats[HP] + Math.Max(pi.Stats[DEF], pi.Stats[SPD]) > 150);
                pk.Item = (int)ReturnItem(isDef ? defaultDefensive : "Life Orb", "Sitrus Berry");
                currentItem = GetItemName(pk.Item);
            }
        }

        // 26. MENTAL HERB: must have Trick Room, Tailwind, Hazard, Setup, or Redirection move
        if (currentItem.Equals("Mental Herb", StringComparison.OrdinalIgnoreCase))
        {
            bool hasTauntTargetMove = currentMoveNames.Contains("Trick Room") ||
                                      currentMoveNames.Contains("Tailwind") ||
                                      currentMoveNames.Overlaps(CompetitiveDatabase.HazardMoves) ||
                                      currentMoveNames.Overlaps(CompetitiveDatabase.SetupMoves) ||
                                      currentMoveNames.Overlaps(CompetitiveDatabase.RedirectionMoves);
            if (!hasTauntTargetMove)
            {
                pk.Item = (int)ReturnItem(defaultDefensive, "Focus Sash");
                currentItem = GetItemName(pk.Item);
            }
        }

        // Adaptability: verify 2+ STAB attacks
        if (abilityName.Equals("Adaptability", StringComparison.OrdinalIgnoreCase))
        {
            ValidateMinSTABAttacks(pk, pi, moveNames, moveData, learnsets, 2);
        }

        // Aerilate/Pixilate/Galvanize/Dragonize: verify 2+ Normal attacks
        if (abilityName.Equals("Aerilate", StringComparison.OrdinalIgnoreCase) ||
            abilityName.Equals("Pixilate", StringComparison.OrdinalIgnoreCase) ||
            abilityName.Equals("Galvanize", StringComparison.OrdinalIgnoreCase) ||
            abilityName.Equals("Dragonize", StringComparison.OrdinalIgnoreCase))
        {
            ValidateMinMovesFromCategory(pk, pi, moveNames, moveData, learnsets, CompetitiveDatabase.NormalAttacks, 2);
        }

        // Contrary: verify 1+ stat-drop move
        if (abilityName.Equals("Contrary", StringComparison.OrdinalIgnoreCase))
            ValidateMinMovesFromCategory(pk, pi, moveNames, moveData, learnsets, CompetitiveDatabase.ContraryMoves, 1);

        // Iron Fist: 2+ punch moves
        if (abilityName.Equals("Iron Fist", StringComparison.OrdinalIgnoreCase))
            ValidateMinMovesFromCategory(pk, pi, moveNames, moveData, learnsets, CompetitiveDatabase.PunchingMoves, 2);

        // Mega Launcher: 2+ pulse moves
        if (abilityName.Equals("Mega Launcher", StringComparison.OrdinalIgnoreCase))
            ValidateMinMovesFromCategory(pk, pi, moveNames, moveData, learnsets, CompetitiveDatabase.PulseMoves, 2);

        // Punk Rock: 1+ sound move
        if (abilityName.Equals("Punk Rock", StringComparison.OrdinalIgnoreCase))
            ValidateMinMovesFromCategory(pk, pi, moveNames, moveData, learnsets, CompetitiveDatabase.SoundMoves, 1);

        // Reckless: 1+ recoil move
        if (abilityName.Equals("Reckless", StringComparison.OrdinalIgnoreCase))
            ValidateMinMovesFromCategory(pk, pi, moveNames, moveData, learnsets, CompetitiveDatabase.RecoilMoves, 1);

        // Sharpness: 1+ slicing move
        if (abilityName.Equals("Sharpness", StringComparison.OrdinalIgnoreCase))
            ValidateMinMovesFromCategory(pk, pi, moveNames, moveData, learnsets, CompetitiveDatabase.SlicingMoves, 1);

        // Strong Jaw: 2+ biting moves
        if (abilityName.Equals("Strong Jaw", StringComparison.OrdinalIgnoreCase))
            ValidateMinMovesFromCategory(pk, pi, moveNames, moveData, learnsets, CompetitiveDatabase.BitingMoves, 2);

        // Triage: 1+ healing attack
        if (abilityName.Equals("Triage", StringComparison.OrdinalIgnoreCase))
            ValidateMinMovesFromCategory(pk, pi, moveNames, moveData, learnsets, CompetitiveDatabase.HealingAttackMoves, 1);

        // Tough Claws: 2+ contact moves
        if (abilityName.Equals("Tough Claws", StringComparison.OrdinalIgnoreCase))
            ValidateMinMovesFromCategory(pk, pi, moveNames, moveData, learnsets, CompetitiveDatabase.ContactMoves, 2);

        // Moody: must have Substitute + Protect
        if (abilityName.Equals("Moody", StringComparison.OrdinalIgnoreCase))
        {
            EnsureMovePresent(pk, "Substitute", moveNames, learnsets);
            EnsureMovePresent(pk, "Protect", moveNames, learnsets);
        }

        // Super Luck: must have 1+ high crit move
        if (abilityName.Equals("Super Luck", StringComparison.OrdinalIgnoreCase))
            ValidateMinMovesFromCategory(pk, pi, moveNames, moveData, learnsets, CompetitiveDatabase.HighCritMoves, 1);

        if (pk.EVs != null)
        {
            // Clamp individual EVs to 252
            for (int i = 0; i < pk.EVs.Length; i++)
            {
                if (pk.EVs[i] > 252) pk.EVs[i] = 252;
                if (pk.EVs[i] < 0) pk.EVs[i] = 0;
            }

            // Total must not exceed 510
            int totalEVs = pk.EVs.Sum();
            if (totalEVs > 510)
            {
                double scale = 510.0 / totalEVs;
                for (int i = 0; i < pk.EVs.Length; i++)
                    pk.EVs[i] = (int)Math.Floor(pk.EVs[i] * scale);
            }
        }

        // Ensure all 4 moves are non-zero and mutually distinct
        if (pk.Moves != null && learnsets != null && moveData != null)
        {
            var seen = new HashSet<int>();
            int[] moves = pk.Moves;
            bool modified = false;
            for (int i = 0; i < moves.Length; i++)
            {
                int m = moves[i];
                if (m <= 0 || seen.Contains(m))
                {
                    var legal = learnsets.GetLegalMoves(pk.Species);
                    int replacement = legal.FirstOrDefault(cand => cand > 0 && cand < moveData.Length && !seen.Contains(cand) && !moves.Contains(cand));
                    if (replacement > 0)
                    {
                        moves[i] = replacement;
                        seen.Add(replacement);
                        modified = true;
                    }
                }
                else
                {
                    seen.Add(m);
                }
            }

            // Prevent duplicate attacking moves of the same type and damage category (e.g. Sludge Bomb + Sludge Wave, Flamethrower + Fire Blast)
            for (int i = 0; i < moves.Length; i++)
            {
                int m1 = moves[i];
                if (m1 <= 0 || m1 >= moveData.Length || moveData[m1].Category == 0) continue;

                for (int j = i + 1; j < moves.Length; j++)
                {
                    int m2 = moves[j];
                    if (m2 <= 0 || m2 >= moveData.Length || moveData[m2].Category == 0) continue;

                    if (moveData[m1].Type == moveData[m2].Type && moveData[m1].Category == moveData[m2].Category)
                    {
                        string n1 = moveNames != null && m1 < moveNames.Length ? moveNames[m1] : "";
                        string n2 = moveNames != null && m2 < moveNames.Length ? moveNames[m2] : "";
                        bool isUtility1 = CompetitiveDatabase.PriorityMoves.Contains(n1) || CompetitiveDatabase.PivotMoves.Contains(n1);
                        bool isUtility2 = CompetitiveDatabase.PriorityMoves.Contains(n2) || CompetitiveDatabase.PivotMoves.Contains(n2);

                        if (!isUtility1 && !isUtility2)
                        {
                            var legal = learnsets.GetLegalMoves(pk.Species);
                            var coveredTypes = moves.Where(x => x > 0 && x < moveData.Length && moveData[x].Category != 0)
                                                    .Select(x => moveData[x].Type)
                                                    .ToHashSet();

                            int rep = legal.FirstOrDefault(cand =>
                                cand > 0 && cand < moveData.Length &&
                                !moves.Contains(cand) &&
                                moveData[cand].Category != 0 &&
                                !coveredTypes.Contains(moveData[cand].Type));

                            if (rep <= 0)
                            {
                                rep = legal.FirstOrDefault(cand =>
                                    cand > 0 && cand < moveData.Length &&
                                    !moves.Contains(cand) &&
                                    moveData[cand].Category == 0);
                            }

                            if (rep > 0)
                            {
                                moves[j] = rep;
                                modified = true;
                            }
                        }
                    }
                }
            }

            if (modified) pk.Moves = moves;
        }

        // Scrub unviable attacks (Twister, Poison Sting, Last Resort, low BP junk)
        if (pk.Moves != null && moveData != null && learnsets != null)
        {
            var legal = learnsets.GetLegalMoves(pk.Species);
            int[] moves = (int[])pk.Moves.Clone();
            bool modified = false;

            for (int i = 0; i < moves.Length; i++)
            {
                int m = moves[i];
                if (m <= 0 || m >= moveData.Length) continue;
                if (moveData[m].Category == 0) continue; // status move

                if (!IsViableAttack(m, moveData, moveNames))
                {
                    int mType = moveData[m].Type;
                    int mCat = moveData[m].Category;

                    // 1. Try same type & category
                    int rep = legal.FirstOrDefault(cand =>
                        cand > 0 && cand < moveData.Length &&
                        moveData[cand].Type == mType && moveData[cand].Category == mCat &&
                        IsViableAttack(cand, moveData, moveNames) &&
                        !moves.Contains(cand));

                    // 2. Try STAB
                    if (rep <= 0 && pi.Types != null)
                    {
                        rep = legal.FirstOrDefault(cand =>
                            cand > 0 && cand < moveData.Length &&
                            Array.IndexOf(pi.Types, moveData[cand].Type) >= 0 &&
                            IsViableAttack(cand, moveData, moveNames) &&
                            !moves.Contains(cand));
                    }

                    // 3. Try any viable attack
                    if (rep <= 0)
                    {
                        rep = legal.FirstOrDefault(cand =>
                            cand > 0 && cand < moveData.Length &&
                            IsViableAttack(cand, moveData, moveNames) &&
                            !moves.Contains(cand));
                    }

                    // 4. Try any competitive status move
                    if (rep <= 0)
                    {
                        rep = legal.FirstOrDefault(cand =>
                            cand > 0 && cand < moveData.Length &&
                            moveData[cand].Category == 0 &&
                            cand < moveNames.Length &&
                            CompetitiveDatabase.CompetitiveMoves.Contains(moveNames[cand]) &&
                            !moves.Contains(cand));
                    }

                    if (rep > 0)
                    {
                        moves[i] = rep;
                        modified = true;
                    }
                }
            }

            if (modified) pk.Moves = moves;
        }

        // Singles vs Doubles Move Invariants:
        // Wide Guard, Quick Guard, Redirection (Follow Me, Rage Powder), Helping Hand, Ally Switch, etc.
        // fail or are completely unviable in Singles battles.
        if (!isDoubles && pk.Moves != null && moveNames != null)
        {
            var legal = learnsets?.GetLegalMoves(pk.Species);
            int[] moves = (int[])pk.Moves.Clone();
            bool modified = false;

            for (int i = 0; i < moves.Length; i++)
            {
                int m = moves[i];
                if (m <= 0 || m >= moveNames.Length) continue;
                string mName = moveNames[m];

                if (CompetitiveDatabase.DoublesOnlyMoves.Contains(mName))
                {
                    int rep = 0;
                    // 1. If Wide Guard or Quick Guard, try a valid single-battle Protect move
                    if (CompetitiveDatabase.WideQuickGuardMoves.Contains(mName))
                    {
                        if (legal != null)
                        {
                            rep = legal.FirstOrDefault(cand =>
                                cand > 0 && cand < moveNames.Length &&
                                CompetitiveDatabase.SinglesProtectMoves.Contains(moveNames[cand]) &&
                                !moves.Contains(cand));
                        }
                        if (rep <= 0)
                        {
                            rep = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, "Protect"));
                            if (moves.Contains(rep)) rep = 0;
                        }
                    }

                    // 2. Try any viable STAB attack matching attacker bias
                    if (rep <= 0 && pi.Types != null && legal != null && moveData != null)
                    {
                        int wantedCat = pi.Stats[ATK] >= pi.Stats[SPA] ? 1 : 2;
                        rep = legal.FirstOrDefault(cand =>
                            cand > 0 && cand < moveData.Length &&
                            Array.IndexOf(pi.Types, moveData[cand].Type) >= 0 &&
                            moveData[cand].Category == wantedCat &&
                            IsViableAttack(cand, moveData, moveNames) &&
                            !moves.Contains(cand));
                    }

                    // 3. Try any viable attack
                    if (rep <= 0 && legal != null && moveData != null)
                    {
                        rep = legal.FirstOrDefault(cand =>
                            cand > 0 && cand < moveData.Length &&
                            IsViableAttack(cand, moveData, moveNames) &&
                            !moves.Contains(cand));
                    }

                    // 4. Try any competitive singles status move (Recovery, Hazard, Setup, or Utility)
                    if (rep <= 0)
                    {
                        if (legal != null)
                        {
                            rep = legal.FirstOrDefault(cand =>
                                cand > 0 && cand < moveNames.Length &&
                                !CompetitiveDatabase.DoublesOnlyMoves.Contains(moveNames[cand]) &&
                                (CompetitiveDatabase.UtilityStatusMoves.Contains(moveNames[cand]) ||
                                 CompetitiveDatabase.SetupMoves.Contains(moveNames[cand]) ||
                                 CompetitiveDatabase.RecoveryMoves.Contains(moveNames[cand])) &&
                                !moves.Contains(cand));
                        }
                        if (rep <= 0)
                        {
                            string[] fallbacks = ["Protect", "Toxic", "Substitute", "Thunder Wave", "Will-O-Wisp", "Recover"];
                            foreach (var fb in fallbacks)
                            {
                                int candId = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, fb));
                                if (candId > 0 && !moves.Contains(candId))
                                {
                                    rep = candId;
                                    break;
                                }
                            }
                        }
                    }

                    if (rep > 0)
                    {
                        moves[i] = rep;
                        modified = true;
                    }
                }
            }

            if (modified) pk.Moves = moves;
        }

        // Hazard removal has negligible value in Doubles/VGC: sanitize to Protect or viable defensive move
        if (isDoubles && pk.Moves != null && moveNames != null)
        {
            var legal = learnsets?.GetLegalMoves(pk.Species);
            int[] moves = (int[])pk.Moves.Clone();
            bool modified = false;

            for (int i = 0; i < moves.Length; i++)
            {
                int m = moves[i];
                if (m <= 0 || m >= moveNames.Length) continue;
                string mName = moveNames[m];
                if (CompetitiveDatabase.HazardRemovalMoves.Contains(mName))
                {
                    int rep = 0;
                    if (legal != null)
                    {
                        rep = legal.FirstOrDefault(cand =>
                            cand > 0 && cand < moveNames.Length &&
                            (moveNames[cand] == "Protect" || moveNames[cand] == "Detect" || moveNames[cand] == "Spiky Shield") &&
                            !moves.Contains(cand));
                    }
                    if (rep <= 0)
                    {
                        rep = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, "Protect"));
                        if (moves.Contains(rep)) rep = 0;
                    }
                    if (rep > 0)
                    {
                        moves[i] = rep;
                        modified = true;
                    }
                }
            }

            if (modified) pk.Moves = moves;
        }

        // 1. Trick Room: ban on fast Pokemon (Spe > 70)
        if (pk.Moves != null && moveNames != null && pi.Stats[SPE] > 70)
        {
            var legal = learnsets?.GetLegalMoves(pk.Species);
            var pool = (legal != null) ? (IEnumerable<int>)legal : Enumerable.Range(1, Math.Min(moveNames.Length, moveData?.Length ?? moveNames.Length) - 1);
            int[] moves = (int[])pk.Moves.Clone();
            bool modified = false;

            for (int i = 0; i < moves.Length; i++)
            {
                int m = moves[i];
                if (m > 0 && m < moveNames.Length && GameNameComparer.Instance.Equals(moveNames[m], "Trick Room"))
                {
                    int rep = 0;
                    string[] fastAlts = ["Agility", "Tailwind", "Protect", "Substitute", "Swords Dance", "Nasty Plot", "Toxic", "Thunder Wave", "Pound"];
                    foreach (var alt in fastAlts)
                    {
                        int candId = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, alt));
                        if (candId > 0 && pool.Contains(candId) && !moves.Contains(candId))
                        {
                            rep = candId;
                            break;
                        }
                    }

                    if (rep <= 0 && moveData != null)
                    {
                        rep = pool.FirstOrDefault(cand =>
                            cand > 0 && cand < moveData.Length &&
                            pi.Types != null && Array.IndexOf(pi.Types, moveData[cand].Type) >= 0 &&
                            IsViableAttack(cand, moveData, moveNames) &&
                            !moves.Contains(cand));
                    }

                    if (rep <= 0)
                    {
                        foreach (var alt in fastAlts)
                        {
                            int candId = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, alt));
                            if (candId > 0 && !moves.Contains(candId))
                            {
                                rep = candId;
                                break;
                            }
                        }
                    }

                    if (rep > 0)
                    {
                        moves[i] = rep;
                        modified = true;
                    }
                }
            }

            if (modified) pk.Moves = moves;
        }

        // 2. Rest validation: ban Rest without Sleep Talk or Chesto/Lum Berry, and ban Rest alongside instant recovery
        if (pk.Moves != null && moveNames != null)
        {
            bool hasRest = pk.Moves.Any(m => m > 0 && m < moveNames.Length && GameNameComparer.Instance.Equals(moveNames[m], "Rest"));
            if (hasRest)
            {
                bool hasSleepTalk = pk.Moves.Any(m => m > 0 && m < moveNames.Length && GameNameComparer.Instance.Equals(moveNames[m], "Sleep Talk"));
                bool hasWakeBerry = currentItem.Equals("Chesto Berry", StringComparison.OrdinalIgnoreCase) ||
                                    currentItem.Equals("Lum Berry", StringComparison.OrdinalIgnoreCase);
                bool hasInstantRecovery = pk.Moves.Any(m => m > 0 && m < moveNames.Length &&
                    CompetitiveDatabase.RecoveryMoves.Contains(moveNames[m]));

                if (hasInstantRecovery || (!hasSleepTalk && !hasWakeBerry))
                {
                    var legal = learnsets?.GetLegalMoves(pk.Species);
                    var pool = (legal != null) ? (IEnumerable<int>)legal : Enumerable.Range(1, Math.Min(moveNames.Length, moveData?.Length ?? moveNames.Length) - 1);
                    int[] moves = (int[])pk.Moves.Clone();
                    bool modified = false;

                    for (int i = 0; i < moves.Length; i++)
                    {
                        int m = moves[i];
                        if (m > 0 && m < moveNames.Length && GameNameComparer.Instance.Equals(moveNames[m], "Rest"))
                        {
                            int rep = 0;
                            string[] alts = ["Protect", "Substitute", "Toxic", "Will-O-Wisp", "Thunder Wave", "Pound"];
                            foreach (var alt in alts)
                            {
                                int candId = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, alt));
                                if (candId > 0 && pool.Contains(candId) && !moves.Contains(candId))
                                {
                                    rep = candId;
                                    break;
                                }
                            }

                            if (rep <= 0 && moveData != null)
                            {
                                rep = pool.FirstOrDefault(cand =>
                                    cand > 0 && cand < moveData.Length &&
                                    IsViableAttack(cand, moveData, moveNames) &&
                                    !moves.Contains(cand));
                            }

                            if (rep <= 0)
                            {
                                foreach (var alt in alts)
                                {
                                    int candId = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, alt));
                                    if (candId > 0 && !moves.Contains(candId))
                                    {
                                        rep = candId;
                                        break;
                                    }
                                }
                            }

                            if (rep > 0)
                            {
                                moves[i] = rep;
                                modified = true;
                            }
                        }
                    }

                    if (modified) pk.Moves = moves;
                }
            }
        }

        // 3. Duplicate recovery moves: a Pokemon should not run 2+ instant recovery moves
        if (pk.Moves != null && moveNames != null)
        {
            var recoveryIndices = new List<int>();
            for (int i = 0; i < pk.Moves.Length; i++)
            {
                int m = pk.Moves[i];
                if (m > 0 && m < moveNames.Length && CompetitiveDatabase.RecoveryMoves.Contains(moveNames[m]))
                    recoveryIndices.Add(i);
            }

            if (recoveryIndices.Count > 1)
            {
                var legal = learnsets?.GetLegalMoves(pk.Species);
                var pool = (legal != null) ? (IEnumerable<int>)legal : Enumerable.Range(1, Math.Min(moveNames.Length, moveData?.Length ?? moveNames.Length) - 1);
                int[] moves = (int[])pk.Moves.Clone();
                bool modified = false;

                // Keep the first recovery move, replace additional ones
                for (int k = 1; k < recoveryIndices.Count; k++)
                {
                    int slot = recoveryIndices[k];
                    int rep = 0;
                    string[] alts = ["Protect", "Substitute", "Toxic", "Will-O-Wisp", "Thunder Wave", "Pound"];
                    foreach (var alt in alts)
                    {
                        int candId = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, alt));
                        if (candId > 0 && pool.Contains(candId) && !moves.Contains(candId))
                        {
                            rep = candId;
                            break;
                        }
                    }

                    if (rep <= 0 && moveData != null)
                    {
                        rep = pool.FirstOrDefault(cand =>
                            cand > 0 && cand < moveData.Length &&
                            IsViableAttack(cand, moveData, moveNames) &&
                            !moves.Contains(cand));
                    }

                    if (rep <= 0)
                    {
                        foreach (var alt in alts)
                        {
                            int candId = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, alt));
                            if (candId > 0 && !moves.Contains(candId))
                            {
                                rep = candId;
                                break;
                            }
                        }
                    }

                    if (rep > 0)
                    {
                        moves[slot] = rep;
                        modified = true;
                    }
                }

                if (modified) pk.Moves = moves;
            }
        }

        // 4. Fixed damage moves (Seismic Toss, Night Shade, etc.): ban on offensive Pokemon (Atk >= 75 or SpA >= 75)
        if (pk.Moves != null && moveNames != null && moveData != null && (pi.Stats[ATK] >= 75 || pi.Stats[SPA] >= 75))
        {
            var legal = learnsets?.GetLegalMoves(pk.Species);
            var pool = (legal != null) ? (IEnumerable<int>)legal : Enumerable.Range(1, moveData.Length - 1);
            int[] moves = (int[])pk.Moves.Clone();
            bool modified = false;
            bool isPhysical = pi.Stats[ATK] >= pi.Stats[SPA];
            int desiredCat = isPhysical ? 1 : 2;

            for (int i = 0; i < moves.Length; i++)
            {
                int m = moves[i];
                if (m > 0 && m < moveNames.Length && CompetitiveDatabase.FixedDamageMoves.Contains(moveNames[m]))
                {
                    int rep = 0;
                    // 1. Try STAB of desired category
                    rep = pool.FirstOrDefault(cand =>
                        cand > 0 && cand < moveData.Length &&
                        moveData[cand].Category == desiredCat &&
                        pi.Types != null && Array.IndexOf(pi.Types, moveData[cand].Type) >= 0 &&
                        IsViableAttack(cand, moveData, moveNames) &&
                        !moves.Contains(cand));

                    // 2. Try any viable attack of desired category
                    if (rep <= 0)
                    {
                        rep = pool.FirstOrDefault(cand =>
                            cand > 0 && cand < moveData.Length &&
                            moveData[cand].Category == desiredCat &&
                            IsViableAttack(cand, moveData, moveNames) &&
                            !moves.Contains(cand));
                    }

                    // 3. Fallback
                    if (rep <= 0)
                    {
                        string[] alts = ["Protect", "Substitute", "Toxic", "Pound"];
                        foreach (var alt in alts)
                        {
                            int candId = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, alt));
                            if (candId > 0 && !moves.Contains(candId))
                            {
                                rep = candId;
                                break;
                            }
                        }
                    }

                    if (rep > 0)
                    {
                        moves[i] = rep;
                        modified = true;
                    }
                }
            }

            if (modified) pk.Moves = moves;
        }

        // 5. Attack category purity: ensure physical attackers do not carry special attacks, and vice versa
        if (pk.Moves != null && moveData != null && moveNames != null && pi.Stats != null)
        {
            bool isPhysical = pi.Stats[ATK] >= pi.Stats[SPA];
            if (pk.EVs != null && pk.EVs.Length >= 6)
            {
                if (pk.EVs[ATK] >= 100 && pk.EVs[SPA] == 0) isPhysical = true;
                else if (pk.EVs[SPA] >= 100 && pk.EVs[ATK] == 0) isPhysical = false;
            }

            bool isMixed = Math.Abs(pi.Stats[ATK] - pi.Stats[SPA]) <= 10 && pk.EVs != null && pk.EVs.Length >= 6 && pk.EVs[ATK] > 0 && pk.EVs[SPA] > 0;
            if (!isMixed)
            {
                int wrongCat = isPhysical ? 2 : 1;
                int rightCat = isPhysical ? 1 : 2;

                var legal = learnsets?.GetLegalMoves(pk.Species);
                var pool = (legal != null) ? (IEnumerable<int>)legal : Enumerable.Range(1, moveData.Length - 1);
                int[] moves = (int[])pk.Moves.Clone();
                bool modified = false;

                for (int i = 0; i < moves.Length; i++)
                {
                    int m = moves[i];
                    if (m <= 0 || m >= moveData.Length) continue;
                    if (moveData[m].Category == wrongCat)
                    {
                        string mName = m < moveNames.Length ? moveNames[m] : "";
                        // Foul Play uses target's attack; valid on special/defensive Pokemon
                        if (!isPhysical && mName.Equals("Foul Play", StringComparison.OrdinalIgnoreCase)) continue;

                        int rep = 0;
                        // 1. Same type, right category
                        rep = pool.FirstOrDefault(cand =>
                            cand > 0 && cand < moveData.Length &&
                            moveData[cand].Category == rightCat &&
                            moveData[cand].Type == moveData[m].Type &&
                            IsViableAttack(cand, moveData, moveNames) &&
                            !moves.Contains(cand));

                        // 2. STAB, right category
                        if (rep <= 0 && pi.Types != null)
                        {
                            rep = pool.FirstOrDefault(cand =>
                                cand > 0 && cand < moveData.Length &&
                                moveData[cand].Category == rightCat &&
                                Array.IndexOf(pi.Types, moveData[cand].Type) >= 0 &&
                                IsViableAttack(cand, moveData, moveNames) &&
                                !moves.Contains(cand));
                        }

                        // 3. Any viable attack of right category
                        if (rep <= 0)
                        {
                            rep = pool.FirstOrDefault(cand =>
                                cand > 0 && cand < moveData.Length &&
                                moveData[cand].Category == rightCat &&
                                IsViableAttack(cand, moveData, moveNames) &&
                                !moves.Contains(cand));
                        }

                        // 4. Fallback
                        if (rep <= 0)
                        {
                            string[] alts = ["Protect", "Substitute", "Toxic", "Pound"];
                            foreach (var alt in alts)
                            {
                                int candId = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, alt));
                                if (candId > 0 && !moves.Contains(candId))
                                {
                                    rep = candId;
                                    break;
                                }
                            }
                        }

                        if (rep > 0)
                        {
                            moves[i] = rep;
                            modified = true;
                        }
                    }
                }

                if (modified) pk.Moves = moves;
            }
        }

        // 6. Attack setup with Foul Play: boosting user's Attack does not increase Foul Play damage
        if (pk.Moves != null && moveNames != null && moveData != null)
        {
            bool hasAtkSetup = pk.Moves.Any(m => m > 0 && m < moveNames.Length && CompetitiveDatabase.AtkSetupMoves.Contains(moveNames[m]));
            if (hasAtkSetup)
            {
                bool onlyFoulPlay = pk.Moves.Any(m => m > 0 && m < moveNames.Length && moveNames[m].Equals("Foul Play", StringComparison.OrdinalIgnoreCase)) &&
                                    !pk.Moves.Any(m => m > 0 && m < moveData.Length && moveData[m].Category == 1 &&
                                                       (m >= moveNames.Length || !moveNames[m].Equals("Foul Play", StringComparison.OrdinalIgnoreCase)) &&
                                                       (m >= moveNames.Length || !CompetitiveDatabase.FixedDamageMoves.Contains(moveNames[m])));

                if (onlyFoulPlay)
                {
                    var legal = learnsets?.GetLegalMoves(pk.Species);
                    var pool = (legal != null) ? (IEnumerable<int>)legal : Enumerable.Range(1, moveData.Length - 1);
                    int[] moves = (int[])pk.Moves.Clone();
                    bool modified = false;

                    for (int i = 0; i < moves.Length; i++)
                    {
                        int m = moves[i];
                        if (m > 0 && m < moveNames.Length && moveNames[m].Equals("Foul Play", StringComparison.OrdinalIgnoreCase))
                        {
                            int rep = 0;
                            // STAB physical
                            if (pi.Types != null)
                            {
                                rep = pool.FirstOrDefault(cand =>
                                    cand > 0 && cand < moveData.Length &&
                                    moveData[cand].Category == 1 &&
                                    Array.IndexOf(pi.Types, moveData[cand].Type) >= 0 &&
                                    IsViableAttack(cand, moveData, moveNames) &&
                                    !moves.Contains(cand));
                            }

                            if (rep <= 0)
                            {
                                rep = pool.FirstOrDefault(cand =>
                                    cand > 0 && cand < moveData.Length &&
                                    moveData[cand].Category == 1 &&
                                    IsViableAttack(cand, moveData, moveNames) &&
                                    !moves.Contains(cand));
                            }

                            if (rep > 0)
                            {
                                moves[i] = rep;
                                modified = true;
                            }
                        }
                    }

                    if (modified) pk.Moves = moves;
                }
            }
        }
    }

    private static void ValidateMinSTABAttacks(
        TrainerPoke7 pk, PersonalInfo pi, string[] moveNames, Move[] moveData,
        LegalLearnsetAggregator learnsets, int minCount)
    {
        if (moveData == null || pk.Moves == null) return;
        int[] types = pi.Types;

        int stabCount = 0;
        foreach (int m in pk.Moves)
        {
            if (m <= 0 || m >= moveData.Length) continue;
            if (moveData[m].Category != 0 && Array.IndexOf(types, moveData[m].Type) >= 0)
                stabCount++;
        }

        if (stabCount >= minCount || learnsets == null) return;

        // Try to swap non-STAB non-essential moves with STAB moves
        var legal = learnsets.GetLegalMoves(pk.Species);
        var stabPool = legal.Where(m =>
            m > 0 && m < moveData.Length &&
            moveData[m].Category != 0 &&
            IsViableAttack(m, moveData, moveNames) &&
            Array.IndexOf(types, moveData[m].Type) >= 0 &&
            !pk.Moves.Contains(m))
            .OrderByDescending(m => moveData[m].Power)
            .ToList();

        int[] currentMoves = pk.Moves;
        for (int i = 3; i >= 0 && stabCount < minCount && stabPool.Count > 0; i--)
        {
            int existing = currentMoves[i];
            if (existing <= 0 || existing >= moveData.Length) continue;
            // Don't replace STAB moves or setup/utility
            if (Array.IndexOf(types, moveData[existing].Type) >= 0) continue;

            currentMoves[i] = stabPool[0];
            stabPool.RemoveAt(0);
            stabCount++;
        }
        pk.Moves = currentMoves;
    }

    private static void ValidateMinMovesFromCategory(
        TrainerPoke7 pk, PersonalInfo pi, string[] moveNames, Move[] moveData,
        LegalLearnsetAggregator learnsets, HashSet<string> category, int minCount)
    {
        if (moveNames == null || pk.Moves == null) return;

        int catCount = 0;
        foreach (int m in pk.Moves)
        {
            if (m <= 0 || m >= moveNames.Length) continue;
            if (category.Contains(moveNames[m])) catCount++;
        }

        if (catCount >= minCount || learnsets == null) return;

        var legal = learnsets.GetLegalMoves(pk.Species);
        var catPool = legal.Where(m =>
            m > 0 && m < moveNames.Length &&
            category.Contains(moveNames[m]) &&
            !pk.Moves.Contains(m)).ToList();

        if (moveData != null)
            catPool = catPool.OrderByDescending(m => m < moveData.Length ? moveData[m].Power : 0).ToList();

        int[] currentMoves = pk.Moves;
        for (int i = 3; i >= 0 && catCount < minCount && catPool.Count > 0; i--)
        {
            int existing = currentMoves[i];
            if (existing <= 0) continue;
            // Don't replace moves already in the target category
            if (existing < moveNames.Length && category.Contains(moveNames[existing])) continue;

            currentMoves[i] = catPool[0];
            catPool.RemoveAt(0);
            catCount++;
        }
        pk.Moves = currentMoves;
    }

    private static void EnsureMovePresent(
        TrainerPoke7 pk, string moveName, string[] moveNames, LegalLearnsetAggregator learnsets)
    {
        if (moveNames == null || pk.Moves == null) return;

        // Already has it?
        foreach (int m in pk.Moves)
        {
            if (m > 0 && m < moveNames.Length && GameNameComparer.Instance.Equals(moveNames[m], moveName))
                return;
        }

        // Find the move ID
        int moveId = -1;
        for (int i = 0; i < moveNames.Length; i++)
        {
            if (GameNameComparer.Instance.Equals(moveNames[i], moveName))
            {
                moveId = i;
                break;
            }
        }
        if (moveId <= 0) return;

        // Verify it's in the legal pool
        if (learnsets != null && !learnsets.LearnsMove(pk.Species, moveId)) return;

        int[] moves = pk.Moves;
        int replaceIdx = -1;
        for (int i = 3; i >= 0; i--)
        {
            if (moves[i] <= 0) { replaceIdx = i; break; }
        }
        if (replaceIdx < 0)
        {
            // Replace slot that does not hold another critical requirement
            for (int i = 3; i >= 0; i--)
            {
                string mName = (moves[i] > 0 && moves[i] < moveNames.Length) ? moveNames[moves[i]] : "";
                if (!mName.Equals("Substitute", StringComparison.OrdinalIgnoreCase) &&
                    !mName.Equals("Protect", StringComparison.OrdinalIgnoreCase))
                {
                    replaceIdx = i;
                    break;
                }
            }
            if (replaceIdx < 0) replaceIdx = 3;
        }

        moves[replaceIdx] = moveId;
        pk.Moves = moves;
    }

    public static void ValidateTeam(
        TrainerPoke7[] team,
        PersonalInfo[] teamPI,
        string[] teamAbilities,
        CompetitiveRole[] roles,
        string[] itemNames,
        string[] moveNames,
        Move[] moveData,
        TeamArchetype archetype,
        LegalLearnsetAggregator learnsets = null,
        bool isDoubles = false)
    {
        if (team == null || team.Length == 0 || itemNames == null) return;

        string GetItemName(int id) =>
            (id >= 0 && id < itemNames.Length) ? (itemNames[id] ?? "") : "";

        ushort GetItemId(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            int idx = Array.FindIndex(itemNames, z => GameNameComparer.Instance.Equals(z, name));
            return idx > 0 ? (ushort)idx : (ushort)0;
        }

        ushort ReturnItem(string primary, string fallback = "Leftovers")
        {
            ushort id = GetItemId(primary);
            if (id > 0) return id;
            ushort fallbackId = GetItemId(fallback);
            return fallbackId > 0 ? fallbackId : GetItemId("Sitrus Berry");
        }

        string GetDefensiveFallback(PersonalInfo pi)
        {
            if (pi?.Types != null && pi.Types.Contains(PokemonTypes.Poison))
                return "Black Sludge";
            return "Leftovers";
        }

        // --- Max 1 Mega Stone ---
        bool foundMega = false;
        for (int i = 0; i < team.Length; i++)
        {
            string item = GetItemName(team[i].Item);
            if (IsMegaStone(item))
            {
                if (foundMega)
                {
                    // Remove duplicate Mega Stone
                    var pi = teamPI != null && i < teamPI.Length ? teamPI[i] : null;
                    bool isDef = pi != null && (pi.HP + Math.Max(pi.DEF, pi.SPD) > 150);
                    team[i].Item = (int)ReturnItem(isDef ? GetDefensiveFallback(pi) : "Life Orb");
                }
                else
                {
                    foundMega = true;
                }
            }
        }

        // --- Max 1 Z-Crystal ---
        bool foundZCrystal = false;
        for (int i = 0; i < team.Length; i++)
        {
            string item = GetItemName(team[i].Item);
            if (IsZCrystal(item))
            {
                if (foundZCrystal)
                {
                    var pi = teamPI != null && i < teamPI.Length ? teamPI[i] : null;
                    bool isDef = pi != null && (pi.HP + Math.Max(pi.DEF, pi.SPD) > 150);
                    team[i].Item = (int)ReturnItem(isDef ? GetDefensiveFallback(pi) : "Life Orb");
                }
                else
                {
                    foundZCrystal = true;
                }
            }
        }

        // --- Terrain Seed validation: only if teammate has corresponding Surge ---
        bool hasElectricSurge = teamAbilities?.Any(a => GameNameComparer.Instance.Equals(a, "Electric Surge")) ?? false;
        bool hasGrassySurge = teamAbilities?.Any(a => GameNameComparer.Instance.Equals(a, "Grassy Surge")) ?? false;
        bool hasPsychicSurge = teamAbilities?.Any(a => GameNameComparer.Instance.Equals(a, "Psychic Surge")) ?? false;
        bool hasMistySurge = teamAbilities?.Any(a => GameNameComparer.Instance.Equals(a, "Misty Surge")) ?? false;

        for (int i = 0; i < team.Length; i++)
        {
            if (team[i] == null || team[i].Item <= 0) continue;
            string item = GetItemName(team[i].Item);
            bool invalidSeed = (item.Equals("Electric Seed", StringComparison.OrdinalIgnoreCase) && !hasElectricSurge) ||
                               (item.Equals("Grassy Seed", StringComparison.OrdinalIgnoreCase) && !hasGrassySurge) ||
                               (item.Equals("Psychic Seed", StringComparison.OrdinalIgnoreCase) && !hasPsychicSurge) ||
                               (item.Equals("Misty Seed", StringComparison.OrdinalIgnoreCase) && !hasMistySurge);

            if (invalidSeed)
            {
                var pi = teamPI != null && i < teamPI.Length ? teamPI[i] : null;
                bool isDef = pi != null && (pi.HP + Math.Max(pi.DEF, pi.SPD) > 150);
                team[i].Item = (int)ReturnItem(isDef ? GetDefensiveFallback(pi) : "Life Orb");
            }
        }

        // --- Unburden + Surge synergy ---
        for (int i = 0; i < team.Length; i++)
        {
            if (team[i] == null) continue;
            string abil = (teamAbilities != null && i < teamAbilities.Length) ? teamAbilities[i] : "";
            if (GameNameComparer.Instance.Equals(abil, "Unburden"))
            {
                if (hasElectricSurge) team[i].Item = (int)ReturnItem("Electric Seed", "Focus Sash");
                else if (hasGrassySurge) team[i].Item = (int)ReturnItem("Grassy Seed", "Focus Sash");
                else if (hasPsychicSurge) team[i].Item = (int)ReturnItem("Psychic Seed", "Focus Sash");
                else if (hasMistySurge) team[i].Item = (int)ReturnItem("Misty Seed", "Focus Sash");
            }
        }

        // --- No duplicate held items (except berries/common items) ---
        var seenItems = new HashSet<int>();
        var allowDuplicates = new HashSet<string>(GameNameComparer.Instance)
        {
            "Leftovers", "Black Sludge", "Focus Sash", "Life Orb", "Heavy-Duty Boots",
            "Sitrus Berry", "Lum Berry", "Aguav Berry", "Iapapa Berry",
            "Mago Berry", "Wiki Berry", "Figy Berry"
        };
        for (int i = 0; i < team.Length; i++)
        {
            if (team[i].Item <= 0) continue;
            string item = GetItemName(team[i].Item);
            if (allowDuplicates.Contains(item)) continue;

            if (seenItems.Contains(team[i].Item))
            {
                var pi = teamPI != null && i < teamPI.Length ? teamPI[i] : null;
                bool isDef = pi != null && (pi.HP + Math.Max(pi.DEF, pi.SPD) > 150);
                team[i].Item = (int)ReturnItem(isDef ? GetDefensiveFallback(pi) : "Life Orb");
            }
            else
            {
                seenItems.Add(team[i].Item);
            }
        }

        // --- Archetype Core Move Guarantee ---
        if (moveNames != null && team.Length > 0)
        {
            bool HasMoveOnTeam(string moveName)
            {
                foreach (var pk in team)
                {
                    if (pk?.Moves == null) continue;
                    foreach (int m in pk.Moves)
                    {
                        if (m > 0 && m < moveNames.Length && GameNameComparer.Instance.Equals(moveNames[m], moveName))
                            return true;
                    }
                }
                return false;
            }

            void TryEnsureMove(string moveName)
            {
                if (HasMoveOnTeam(moveName)) return;
                int moveId = Array.FindIndex(moveNames, m => GameNameComparer.Instance.Equals(m, moveName));
                if (moveId <= 0) return;
                for (int i = 0; i < team.Length; i++)
                {
                    if (team[i] != null && (learnsets == null || learnsets.LearnsMove(team[i].Species, moveId)))
                    {
                        if (moveName.Equals("Trick Room", StringComparison.OrdinalIgnoreCase))
                        {
                            var pi = teamPI != null && i < teamPI.Length ? teamPI[i] : null;
                            if (pi != null && pi.SPE > 70) continue;
                        }
                        EnsureMovePresent(team[i], moveName, moveNames, learnsets);
                        break;
                    }
                }
            }

            if (archetype == TeamArchetype.TrickRoom)
                TryEnsureMove("Trick Room");
            else if (archetype == TeamArchetype.Rain && !teamAbilities.Any(a => GameNameComparer.Instance.Equals(a, "Drizzle")))
                TryEnsureMove("Rain Dance");
            else if (archetype == TeamArchetype.Sun && !teamAbilities.Any(a => GameNameComparer.Instance.Equals(a, "Drought")))
                TryEnsureMove("Sunny Day");
            else if (archetype == TeamArchetype.Sand && !teamAbilities.Any(a => GameNameComparer.Instance.Equals(a, "Sand Stream")))
                TryEnsureMove("Sandstorm");
            else if (archetype == TeamArchetype.Snow && !teamAbilities.Any(a => GameNameComparer.Instance.Equals(a, "Snow Warning")))
                TryEnsureMove("Hail");
            else if (archetype == TeamArchetype.HyperOffenseScreens && !HasMoveOnTeam("Light Screen") && !HasMoveOnTeam("Reflect") && !HasMoveOnTeam("Aurora Veil"))
                TryEnsureMove("Reflect");
            else if ((archetype is TeamArchetype.HyperOffenseHazards or TeamArchetype.Stall or TeamArchetype.SemiStall) &&
                     !HasMoveOnTeam("Stealth Rock") && !HasMoveOnTeam("Spikes") && !HasMoveOnTeam("Toxic Spikes"))
                TryEnsureMove("Stealth Rock");

            // Ban Sticky Web on Stall, Semi-Stall, and Trick Room
            if (archetype is TeamArchetype.Stall or TeamArchetype.SemiStall or TeamArchetype.TrickRoom)
            {
                for (int i = 0; i < team.Length; i++)
                {
                    if (team[i]?.Moves == null) continue;
                    int[] pMoves = team[i].Moves;
                    bool changed = false;
                    for (int m = 0; m < pMoves.Length; m++)
                    {
                        int mId = pMoves[m];
                        if (mId > 0 && mId < moveNames.Length &&
                            GameNameComparer.Instance.Equals(moveNames[mId], "Sticky Web"))
                        {
                            int replacementId = -1;
                            string[] replacements = { "Stealth Rock", "Spikes", "Toxic Spikes", "Toxic", "Protect", "Substitute" };
                            foreach (var rep in replacements)
                            {
                                int repId = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, rep));
                                if (repId > 0 && (learnsets == null || learnsets.LearnsMove(team[i].Species, repId)) &&
                                    !pMoves.Contains(repId))
                                {
                                    replacementId = repId;
                                    break;
                                }
                            }
                            if (replacementId > 0)
                            {
                                pMoves[m] = replacementId;
                                changed = true;
                            }
                        }
                    }
                    if (changed) team[i].Moves = pMoves;
                }
            }

            // Enforce that Wide Guard, Quick Guard, and Doubles-only moves never appear in Singles battles
            if (!isDoubles)
            {
                for (int i = 0; i < team.Length; i++)
                {
                    if (team[i]?.Moves == null) continue;
                    int[] pMoves = team[i].Moves;
                    bool changed = false;
                    for (int m = 0; m < pMoves.Length; m++)
                    {
                        int mId = pMoves[m];
                        if (mId > 0 && mId < moveNames.Length &&
                            CompetitiveDatabase.DoublesOnlyMoves.Contains(moveNames[mId]))
                        {
                            int repId = -1;
                            if (CompetitiveDatabase.WideQuickGuardMoves.Contains(moveNames[mId]))
                            {
                                int protId = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, "Protect"));
                                if (protId > 0 && (learnsets == null || learnsets.LearnsMove(team[i].Species, protId)) && !pMoves.Contains(protId))
                                    repId = protId;
                            }
                            if (repId <= 0)
                            {
                                string[] replacements = { "Protect", "Toxic", "Substitute", "Thunder Wave", "Will-O-Wisp", "Recover" };
                                foreach (var rep in replacements)
                                {
                                    int candId = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, rep));
                                    if (candId > 0 && (learnsets == null || learnsets.LearnsMove(team[i].Species, candId)) && !pMoves.Contains(candId))
                                    {
                                        repId = candId;
                                        break;
                                    }
                                }
                            }
                            if (repId > 0)
                            {
                                pMoves[m] = repId;
                                changed = true;
                            }
                        }
                    }
                    if (changed) team[i].Moves = pMoves;
                }
            }

            // Validate Eviolite: only pre-evolutions can hold it
            for (int i = 0; i < team.Length; i++)
            {
                if (team[i] == null) continue;
                string held = itemNames != null && team[i].Item >= 0 && team[i].Item < itemNames.Length ? itemNames[team[i].Item] : "";
                if (held.Equals("Eviolite", StringComparison.OrdinalIgnoreCase))
                {
                    if (learnsets?.GameConfig != null && !Legal.CanSpeciesEvolve(team[i].Species, learnsets.GameConfig))
                    {
                        int leftId = Array.FindIndex(itemNames, x => GameNameComparer.Instance.Equals(x, "Leftovers"));
                        if (leftId > 0) team[i].Item = leftId;
                    }
                }
            }

            // Hazard removal is less prioritized: at most 1 hazard remover per team in Singles; 0 in Doubles
            int removalCount = 0;
            for (int i = 0; i < team.Length; i++)
            {
                if (team[i]?.Moves == null) continue;
                int[] pMoves = team[i].Moves;
                bool changed = false;
                for (int m = 0; m < pMoves.Length; m++)
                {
                    int mId = pMoves[m];
                    if (mId > 0 && mId < moveNames.Length &&
                        CompetitiveDatabase.HazardRemovalMoves.Contains(moveNames[mId]))
                    {
                        if (isDoubles || removalCount >= 1)
                        {
                            int repId = -1;
                            string[] replacements = isDoubles
                                ? new[] { "Protect", "Detect" }
                                : new[] { "Toxic", "Recover", "Protect", "Substitute", "Thunder Wave", "Will-O-Wisp" };
                            foreach (var rep in replacements)
                            {
                                int candId = Array.FindIndex(moveNames, x => GameNameComparer.Instance.Equals(x, rep));
                                if (candId > 0 && (learnsets == null || learnsets.LearnsMove(team[i].Species, candId)) && !pMoves.Contains(candId))
                                {
                                    repId = candId;
                                    break;
                                }
                            }
                            if (repId > 0)
                            {
                                pMoves[m] = repId;
                                changed = true;
                            }
                        }
                        else
                        {
                            removalCount++;
                        }
                    }
                }
                if (changed) team[i].Moves = pMoves;
            }

            // Enforce pivot moves for designated Pivot roles
            for (int i = 0; i < team.Length; i++)
            {
                if (team[i]?.Moves == null || roles == null || i >= roles.Length) continue;
                var r = roles[i];
                if (r is CompetitiveRole.OffensivePivot or CompetitiveRole.BulkyPivot or CompetitiveRole.DefensivePivot)
                {
                    bool hasPivot = team[i].Moves.Any(m => m > 0 && m < moveNames.Length && CompetitiveDatabase.PivotMoves.Contains(moveNames[m]));
                    if (!hasPivot)
                    {
                        var legal = learnsets?.GetLegalMoves(team[i].Species);
                        var pool = (legal != null) ? (IEnumerable<int>)legal : Enumerable.Range(1, moveNames.Length - 1);
                        var legalPivots = pool.Where(cand => cand > 0 && cand < moveNames.Length && CompetitiveDatabase.PivotMoves.Contains(moveNames[cand])).ToList();
                        if (legalPivots.Count > 0)
                            {
                                var pi = teamPI != null && i < teamPI.Length ? teamPI[i] : null;
                                bool isPhysical = pi != null && pi.ATK >= pi.SPA;
                                int chosenPivot = legalPivots[0];
                                if (moveData != null && legalPivots.Count > 1)
                                {
                                    int preferredCat = isPhysical ? 1 : 2;
                                    var catMatch = legalPivots.FirstOrDefault(p => p < moveData.Length && moveData[p].Category == preferredCat);
                                    if (catMatch > 0) chosenPivot = catMatch;
                                }

                                int[] pMoves = (int[])team[i].Moves.Clone();
                                int replaceIdx = 3;
                                for (int s = 3; s >= 0; s--)
                                {
                                    int m = pMoves[s];
                                    if (m <= 0) { replaceIdx = s; break; }
                                    string mName = m < moveNames.Length ? moveNames[m] : "";
                                    if (!CompetitiveDatabase.RecoveryMoves.Contains(mName) &&
                                        !CompetitiveDatabase.SetupMoves.Contains(mName))
                                    {
                                        replaceIdx = s;
                                        break;
                                    }
                                }
                                pMoves[replaceIdx] = chosenPivot;
                                team[i].Moves = pMoves;
                            }
                        }
                    }
                }
            }
        }

    public static bool IsViableAttack(int m, Move[] moveData, string[] moveNames)
    {
        if (m <= 0 || moveData == null || m >= moveData.Length) return false;
        var md = moveData[m];
        if (md.Category == 0) return false; // status move

        string name = (moveNames != null && m < moveNames.Length) ? moveNames[m] : "";
        if (string.IsNullOrEmpty(name)) return false;

        // Last Resort is an unusable gimmick on 4-move sets
        if (name.Equals("Last Resort", StringComparison.OrdinalIgnoreCase)) return false;

        // Hidden Power's real type/power is read off the Pokemon's IVs at battle time, but nothing
        // in this pipeline coordinates IVs with the move pool: IVs are rolled independently
        // (0-31 per stat, or forced for Trick Room) with no awareness of what the coverage-fill
        // logic assumed HP's type would be when it scored this move for the set. Picking it here
        // would produce a set that reads as covering some type on paper while the mon's actual
        // in-battle Hidden Power is whatever random type its IVs happen to land on - excluded
        // until IV rolling is made type-aware for this one move.
        if (name.StartsWith("Hidden Power", StringComparison.OrdinalIgnoreCase)) return false;

        // Certified competitive/situational utility attacks (e.g. Seismic Toss, Foul Play, Low Kick, Super Fang)
        if (CompetitiveDatabase.CompetitiveMoves.Contains(name) || CompetitiveDatabase.SituationalMoves.Contains(name))
            return true;

        return md.Power >= 60;
    }

    public static List<string> FindUnresolvedNames(GameConfig config)
    {
        var problems = new List<string>();
        if (config == null) return problems;

        HashSet<string> Vocab(TextName which)
        {
            var text = config.GetText(which);
            return new HashSet<string>(
                text?.Where(s => !string.IsNullOrWhiteSpace(s) && s != "-") ?? Enumerable.Empty<string>(),
                GameNameComparer.Instance);
        }

        var abilities = Vocab(TextName.AbilityNames);
        var moves = Vocab(TextName.MoveNames);
        var items = Vocab(TextName.ItemNames);

        void Check(string label, IEnumerable<string> declared, HashSet<string> real)
        {
            if (real.Count == 0) return; // text not loaded; nothing to say
            var missing = declared.Where(n => !string.IsNullOrWhiteSpace(n) && !real.Contains(n)).ToList();
            if (missing.Count > 0)
                problems.Add($"{label}: {missing.Count} unresolved -> {string.Join(", ", missing)}");
        }

        Check("CompetitiveAbilities", CompetitiveDatabase.CompetitiveAbilities, abilities);
        Check("SituationalAbilities", CompetitiveDatabase.SituationalAbilities, abilities);
        Check("LessCompetitiveAbilities", CompetitiveDatabase.LessCompetitiveAbilities, abilities);

        Check("CompetitiveMoves", CompetitiveDatabase.CompetitiveMoves, moves);
        Check("SituationalMoves", CompetitiveDatabase.SituationalMoves, moves);
        Check("PunchingMoves", CompetitiveDatabase.PunchingMoves, moves);
        Check("SlicingMoves", CompetitiveDatabase.SlicingMoves, moves);
        Check("SoundMoves", CompetitiveDatabase.SoundMoves, moves);
        Check("RecoilMoves", CompetitiveDatabase.RecoilMoves, moves);
        Check("BitingMoves", CompetitiveDatabase.BitingMoves, moves);
        Check("MultiHitMoves", CompetitiveDatabase.MultiHitMoves, moves);
        Check("HazardMoves", CompetitiveDatabase.HazardMoves, moves);
        Check("PivotMoves", CompetitiveDatabase.PivotMoves, moves);
        Check("ScreenMoves", CompetitiveDatabase.ScreenMoves, moves);
        Check("RecoveryMoves", CompetitiveDatabase.RecoveryMoves, moves);
        Check("SetupMoves", CompetitiveDatabase.SetupMoves, moves);
        Check("ContraryMoves", CompetitiveDatabase.ContraryMoves, moves);
        Check("SunAffectedMoves", CompetitiveDatabase.SunAffectedMoves, moves);
        Check("UtilityStatusMoves", CompetitiveDatabase.UtilityStatusMoves, moves);

        Check("MainShopItems", CompetitiveDatabase.MainShopItems, items);
        Check("CompetitiveShopItems", CompetitiveDatabase.CompetitiveShopItems, items);
        Check("BerryShopItems", CompetitiveDatabase.BerryShopItems, items);
        Check("TypeBoostShopItems", CompetitiveDatabase.TypeBoostShopItems, items);

        return problems;
    }

    public static bool IsMegaStone(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return false;
        if (GameNameComparer.Instance.Equals(itemName, "Eviolite")) return false;
        if (CompetitiveDatabase.AllMegaStones.Contains(itemName)) return true;
        string lower = itemName.ToLowerInvariant();
        return lower.EndsWith("ite") || lower.EndsWith("nite") || lower.EndsWith("tite");
    }

    public static bool IsZCrystal(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return false;
        return itemName.EndsWith(" Z", StringComparison.OrdinalIgnoreCase) &&
               !itemName.Equals("Absolite Z", StringComparison.OrdinalIgnoreCase) &&
               !itemName.Equals("Garchompite Z", StringComparison.OrdinalIgnoreCase) &&
               !itemName.Equals("Lucarionite Z", StringComparison.OrdinalIgnoreCase);
    }
}
