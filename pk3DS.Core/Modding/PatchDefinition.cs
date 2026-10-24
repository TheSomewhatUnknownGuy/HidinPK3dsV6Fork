using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace pk3DS.Core.Modding
{
    public enum PatchUIType
    {
        Toggle,     // CheckBox (True/False)
        Dropdown,   // ComboBox (Select one from options)
        Number      // NumericUpDown (Value injection)
    }

    public class PatchInstruction
    {
        [JsonProperty("offset")]
        public uint Offset { get; set; }

        [JsonProperty("hexBytes")]
        public string HexBytes { get; set; }

        [JsonProperty("assembly")]
        public string Assembly { get; set; }
    }

    public class PatchOption
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("instructions")]
        public List<PatchInstruction> Instructions { get; set; } = new List<PatchInstruction>();
    }

    public class PatchDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("targetFile")]
        public string TargetFile { get; set; } // e.g., "code.bin" or "battle.cro"

        [JsonProperty("uiType")]
        public PatchUIType UIType { get; set; }

        // For Toggle patches:
        [JsonProperty("instructions")]
        public List<PatchInstruction> Instructions { get; set; } = new List<PatchInstruction>();
        
        [JsonProperty("defaultState")]
        public bool DefaultState { get; set; } // If toggle is on by default

        // For Dropdown patches:
        [JsonProperty("options")]
        public List<PatchOption> Options { get; set; }
        
        [JsonProperty("defaultOptionIndex")]
        public int DefaultOptionIndex { get; set; }

        // For Number patches (Not fully implemented in UI currently, kept for parity):
        [JsonProperty("minValue")]
        public decimal MinValue { get; set; }

        [JsonProperty("maxValue")]
        public decimal MaxValue { get; set; }

        [JsonProperty("defaultValue")]
        public decimal DefaultValue { get; set; }

        public bool IsValid()
        {
            if (string.IsNullOrEmpty(Name)) return false;
            
            switch (UIType)
            {
                case PatchUIType.Toggle:
                    return Instructions != null && Instructions.Count > 0;
                case PatchUIType.Dropdown:
                    return Options != null && Options.Count > 0 && Options.All(o => o.Instructions != null && o.Instructions.Count > 0);
                case PatchUIType.Number:
                    return MinValue < MaxValue;
                default:
                    return false;
            }
        }
    }
}
