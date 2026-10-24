using System;
using System.Linq;

namespace pk3DS.Core;

public class GameInfo
{
    public int MaxSpeciesID { get; set; }
    public int MaxItemID { get; set; }
    public int MaxMoveID { get; set; }
    public ushort[] HeldItems { get; set; }
    public int MaxAbilityID { get; set; }
    // 0 = normal ROM; > 0 = Uhuru difficulty blocks (trainers per block).
    public int TrainerBaseCount { get; set; }

    public GameInfo(GameConfig gameConfig)
    {
        switch (gameConfig.Version)
        {
            case GameVersion.XY: LoadXY(); break;
            case GameVersion.ORASDEMO:
            case GameVersion.ORAS: LoadAO(); break;
            case GameVersion.SMDEMO:
            case GameVersion.SM: LoadSM(); break;
            case GameVersion.US:
            case GameVersion.UM:
            case GameVersion.USUM: LoadUSUM(); break;
        }
        RecalculateLimits(gameConfig);
    }

    public void RecalculateLimits(GameConfig config)
    {
        if (config == null) return;

        // Dynamic species recalculation from Personal table or text list.
        // This used to hardcode MaxSpeciesID to exactly 1025 whenever the table looked expanded -
        // fine for the one-time 807->1026 Expansion Pack raise this was written for, but it silently
        // stopped growing again after that: inserting species past slot 1025 (SpeciesInsertionWizard)
        // left MaxSpeciesID capped at 1025 forever, which fed straight into GetText's species-name
        // auto-pad (`Info.MaxSpeciesID > 807` -> pad to `MaxSpeciesID + 1`) - so names past 1025
        // would never get padding room and any direct `SpeciesNames[newIndex]` lookup for a
        // genuinely new species could throw, the same IndexOutOfRangeException class of bug already
        // hit and fixed for trainer names this session. Track the table's own real last index instead.
        if (pk3DS.Core.Modding.ProjectState.Instance.AppliedPatches.Contains("Pokemon+ Gen 9 Expansion Patch") || (config.Personal != null && config.Personal.Table != null && config.Personal.Table.Length >= 1025))
        {
            // Base species in Gen 9 expansion is 1025 (or more if custom base species were inserted).
            // Note: learnsets, evolutions, and personal tables in Gen 7 expansion are expanded in
            // parallel to include all ~1330 entries (base species + alt forms), so their array
            // lengths must NOT be used as MaxSpeciesID.
            int baseSpeciesCount = 1025;
            var textRef = config.GameText?.FirstOrDefault(f => f.Name == TextName.SpeciesNames);
            if (textRef != null && config.GameTextStrings != null && textRef.Index < config.GameTextStrings.Length)
            {
                int rawLen = config.GameTextStrings[textRef.Index]?.Length ?? 0;
                if (rawLen > 1026)
                    baseSpeciesCount = rawLen - 1;
            }
            MaxSpeciesID = baseSpeciesCount;
        }
        else if (config.Version == GameVersion.US || config.Version == GameVersion.UM || config.Version == GameVersion.USUM)
        {
            MaxSpeciesID = Legal.MaxSpeciesID_7_USUM; // 807
        }
        else if (config.Version == GameVersion.SM || config.Version == GameVersion.SMDEMO)
        {
            MaxSpeciesID = Legal.MaxSpeciesID_7_SM; // 802
        }
        else if (config.Version == GameVersion.ORAS || config.Version == GameVersion.ORASDEMO || config.Version == GameVersion.XY)
        {
            MaxSpeciesID = Legal.MaxSpeciesID_6; // 721
        }

        // Dynamic move count recalculation from moves list
        if (config.Moves != null && config.Moves.Length > MaxMoveID)
        {
            MaxMoveID = config.Moves.Length - 1;
        }

        // Dynamic ability count recalculation from ability text list
        var abils = config.GetText(TextName.AbilityNames);
        if (abils != null && abils.Length > 0)
        {
            MaxAbilityID = abils.Length - 1;
        }

        // Dynamic item count recalculation from item text list
        var items = config.GetText(TextName.ItemNames);
        if (items != null && items.Length > 0)
        {
            MaxItemID = items.Length - 1;
        }

        // Check ExpansionConfig for custom overrides
        try
        {
            var cfg = pk3DS.Core.Modding.ExpansionConfig.Load(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "ExpansionConfig.json"));
            if (cfg != null)
            {
                // Same guard MaxItems uses: never promote an id the ROM has no name/data row for.
                // MaxAbilities defaults to 320 while this build's table ends at 319, so the
                // randomizer handed 21 species a nameless, effectless ability 320.
                if (cfg.MaxAbilities > MaxAbilityID && (abils == null || cfg.MaxAbilities < abils.Length))
                    MaxAbilityID = cfg.MaxAbilities;
                // Only override MaxSpecies if the personal table is actually expanded (> 807 species)
                if (cfg.MaxSpecies > MaxSpeciesID && (config.Personal == null || config.Personal.Table == null || config.Personal.Table.Length >= 1025))
                    MaxSpeciesID = cfg.MaxSpecies;
                var moveNames = config.GetText(TextName.MoveNames);
                if (cfg.MaxMoves > MaxMoveID && (moveNames == null || cfg.MaxMoves < moveNames.Length))
                    MaxMoveID = cfg.MaxMoves;
                if (cfg.MaxItems > MaxItemID && items != null && cfg.MaxItems < items.Length)
                    MaxItemID = cfg.MaxItems;
            }
        }
        catch { }
    }


    private void LoadXY()
    {
        MaxSpeciesID = Legal.MaxSpeciesID_6;
        MaxMoveID = Legal.MaxMoveID_6_XY;
        MaxItemID = Legal.MaxItemID_6_XY;
        HeldItems = Legal.HeldItem_XY;
        MaxAbilityID = Legal.MaxAbilityID_6_XY;
    }

    private void LoadAO()
    {
        MaxSpeciesID = Legal.MaxSpeciesID_6;
        MaxMoveID = Legal.MaxMoveID_6_AO;
        MaxItemID = Legal.MaxItemID_6_AO;
        HeldItems = Legal.HeldItem_AO;
        MaxAbilityID = Legal.MaxAbilityID_6_AO;
    }

    private void LoadSM()
    {
        MaxSpeciesID = Legal.MaxSpeciesID_7_SM;
        MaxMoveID = Legal.MaxMoveID_7_SM;
        MaxItemID = Legal.MaxItemID_7_SM;
        HeldItems = Legal.HeldItems_SM;
        MaxAbilityID = Legal.MaxAbilityID_7_SM;
    }

    private void LoadUSUM()
    {
        MaxSpeciesID = Legal.MaxSpeciesID_7_USUM;
        MaxMoveID = Legal.MaxMoveID_7_USUM;
        MaxItemID = Legal.MaxItemID_7_USUM;
        HeldItems = Legal.HeldItems_USUM;
        MaxAbilityID = Legal.MaxAbilityID_7_USUM;
    }
}
