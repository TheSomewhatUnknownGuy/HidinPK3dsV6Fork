using System;
using System.Collections.Generic;

namespace pk3DS.Core.Modding.Research
{
    public enum AmxTemplateType
    {
        SimpleTextDialogue,

        ItemGift,

        PokemonGift,

        StaticEncounter,

        InGameTrade,

        FossilRevival
    }

    public class AmxTemplateMetadata
    {
        public AmxTemplateType Type { get; set; }
        public string Title { get; set; }
        public int SourceScriptIndex { get; set; }
        public int DefaultTextFileIndex { get; set; }
        public int DefaultScriptId { get; set; }
        public string Summary { get; set; }
        public string FailureModes { get; set; }
        public string[] KeyOpcodes { get; set; }
    }

    public class SimpleTextConfig
    {
        public int TextFileIndex { get; set; } = 582;
        public int TextLineIndex { get; set; } = 0;
    }

    public class ItemGiftConfig
    {
        public uint Flag { get; set; } = 2500;
        public int ItemID { get; set; } = 1;
        public int Quantity { get; set; } = 1;
        public int TextFileIndex { get; set; } = 586;
        public int TextPreGift { get; set; } = 0;
        public int TextPostGift { get; set; } = 1;
    }

    public class PokemonGiftConfig
    {
        public uint Flag { get; set; } = 2501;
        public int GiftTableIndex { get; set; } = 0;
        public int Species { get; set; } = 1;
        public int Level { get; set; } = 5;
        public int TextFileIndex { get; set; } = 521;
        public int TextPreGift { get; set; } = 0;
        public int TextPostGift { get; set; } = 1;
        public int TextPartyFull { get; set; } = 2;
    }

    public class StaticBossConfig
    {
        public uint Flag { get; set; } = 2502;
        public int StaticTableIndex { get; set; } = 0;
        public uint OverworldEventId { get; set; } = 200;
        public int Species { get; set; } = 150;
        public int Level { get; set; } = 70;
        public int TextFileIndex { get; set; } = 14;
        public int TextPreBattle { get; set; } = 0;
    }

    public class TradeNpcConfig
    {
        public uint Flag { get; set; } = 2503;
        public int TradeTableIndex { get; set; } = 0;
        public int RequestedSpecies { get; set; } = 1;
        public int OfferedSpecies { get; set; } = 4;
        public int TextFileIndex { get; set; } = 14;
        public int TextOffer { get; set; } = 0;
        public int TextPostTrade { get; set; } = 1;
    }

    public static class AmxEventTemplates
    {
        private static readonly Dictionary<AmxTemplateType, AmxTemplateMetadata> Templates = new()
        {
            [AmxTemplateType.SimpleTextDialogue] = new AmxTemplateMetadata
            {
                Type = AmxTemplateType.SimpleTextDialogue,
                Title = "Simple Dialogue NPC",
                SourceScriptIndex = 560,
                DefaultTextFileIndex = 582,
                DefaultScriptId = 0,
                Summary = "Cleanest baseline for nameless lore/dialogue NPCs with no speaker tag, no items, and no flags.",
                FailureModes = "Adding speaker tags causes visual overflow; skipping message wait results in instant dialogue box closing.",
                KeyOpcodes = ["Begin (0x2E)", "GetArg (0xBE)", "DoCommand? (0x87)", "Return (0x30)"]
            },
            [AmxTemplateType.ItemGift] = new AmxTemplateMetadata
            {
                Type = AmxTemplateType.ItemGift,
                Title = "Item Gift NPC",
                SourceScriptIndex = 569,
                DefaultTextFileIndex = 586,
                DefaultScriptId = 0,
                Summary = "Reliable item gift template with flag verification, fanfare playback, and persistent collection state.",
                FailureModes = "Do not use 625 (Tapu Koko shrine event script) as it causes save corruption; always verify flag uniqueness.",
                KeyOpcodes = ["FlagCheck", "ItemAdd", "SoundPlay", "FlagSet"]
            },
            [AmxTemplateType.PokemonGift] = new AmxTemplateMetadata
            {
                Type = AmxTemplateType.PokemonGift,
                Title = "Pokémon Gift NPC",
                SourceScriptIndex = 630,
                DefaultTextFileIndex = 521,
                DefaultScriptId = 0,
                Summary = "Full Pokémon gift routine: verifies party/PC space, grants gift Pokémon from GARC a/1/5/9 file 0, offers nickname prompt, logs to PokeDiary.",
                FailureModes = "Omitting party space check crashes game when party is full; omitting PokeDiarySet creates incomplete save records.",
                KeyOpcodes = ["PartyCountCheck", "PokemonAdd", "PokeDiarySet", "FlagSet"]
            },
            [AmxTemplateType.StaticEncounter] = new AmxTemplateMetadata
            {
                Type = AmxTemplateType.StaticEncounter,
                Title = "Static Encounter Boss",
                SourceScriptIndex = 14,
                DefaultTextFileIndex = 14,
                DefaultScriptId = 5228,
                Summary = "Overworld boss battle trigger: cry animation, CallWildBattleCore against static table index, post-battle victory branch, ChrDel overworld despawn.",
                FailureModes = "Failing to despawn character (ChrDel) leaves sprite on map after capture; missing appearance flag causes sprite respawn upon re-entry.",
                KeyOpcodes = ["FlagCheck", "SoundCry", "CallWildBattleCore", "ChrDel", "FlagSet"]
            },
            [AmxTemplateType.InGameTrade] = new AmxTemplateMetadata
            {
                Type = AmxTemplateType.InGameTrade,
                Title = "In-Game Trade NPC",
                SourceScriptIndex = 14,
                DefaultTextFileIndex = 14,
                DefaultScriptId = 5227,
                Summary = "NPC Trade routine: queries party for requested species, triggers trade animation, exchanges Pokémon from GARC a/1/5/9 file 4, sets trade flag.",
                FailureModes = "Mismatch between script trade index and GARC a/1/5/9 file 4 trade index causes illegal or crash species trades.",
                KeyOpcodes = ["FlagCheck", "PartyScanSpecies", "CallTradeCore", "FlagSet"]
            },
            [AmxTemplateType.FossilRevival] = new AmxTemplateMetadata
            {
                Type = AmxTemplateType.FossilRevival,
                Title = "Fossil Revival NPC",
                SourceScriptIndex = 20,
                DefaultTextFileIndex = 19,
                DefaultScriptId = 5471,
                Summary = "Multi-choice fossil restoration menu: checks player bag for fossil item, deducts fossil, grants restored Pokémon with party space check.",
                FailureModes = "Duplicate func_215 helper call corrupts AMX stack; ensure clean ItemSub branch before Pokémon delivery.",
                KeyOpcodes = ["MenuCreate", "ItemCheck", "ItemSub", "PokemonAdd"]
            }
        };

        public static AmxTemplateMetadata GetMetadata(AmxTemplateType type)
        {
            return Templates.TryGetValue(type, out var meta) ? meta : null;
        }

        public static IReadOnlyCollection<AmxTemplateMetadata> GetAllTemplates()
        {
            return Templates.Values;
        }
    }
}
