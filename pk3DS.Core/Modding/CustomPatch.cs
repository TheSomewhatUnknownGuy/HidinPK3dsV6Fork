using System.Collections.Generic;

namespace pk3DS.Core.Modding
{
    public class UniversalPatch
    {
        public int FormatVersion { get; set; } = 1;
        public string PatchName { get; set; } = "Unknown Patch";
        public string Author { get; set; } = "Community";
        public string Description { get; set; } = "";
        public List<string> TargetVersions { get; set; } = new List<string>(); // "US", "UM"
        public List<PatchEntry> Patches { get; set; } = new List<PatchEntry>();
    }

    public class PatchEntry
    {
        public string TargetFile { get; set; } = "code.bin";

        public string Mode { get; set; } = "hex";

        public string Code { get; set; } = "";

        public string AsmFilePath { get; set; } = "";

        public string BaseAddress { get; set; } = "0x0";

        public Dictionary<string, VersionOffsets> Offsets { get; set; } = new Dictionary<string, VersionOffsets>();
    }

    public class VersionOffsets
    {
        public string InjectAt { get; set; } = "auto";

        public List<string> Hooks { get; set; } = new List<string>();
    }

    // --- Legacy aliases for backward compatibility ---
    // These map to the old JSON format so existing patches still work.

    public class CustomPatch
    {
        public string PatchName { get; set; } = "Unknown Patch";
        public string Author { get; set; } = "Community";
        public List<string> TargetVersions { get; set; } = new List<string>();
        public string Description { get; set; } = "";
        public List<PatchPayload> Payloads { get; set; } = new List<PatchPayload>();

        public UniversalPatch ToUniversal()
        {
            var up = new UniversalPatch
            {
                FormatVersion = 0,
                PatchName = PatchName,
                Author = Author,
                Description = Description,
                TargetVersions = TargetVersions
            };

            foreach (var p in Payloads)
            {
                var entry = new PatchEntry
                {
                    TargetFile = p.TargetFile,
                    Mode = "hex",
                    Code = p.Code
                };

                // Legacy format has no version-specific offsets — apply to both
                var vOfs = new VersionOffsets { InjectAt = "auto" };
                foreach (var hook in p.BLRepoints)
                    vOfs.Hooks.Add("bl:" + hook);

                entry.Offsets["US"] = vOfs;
                entry.Offsets["UM"] = vOfs;
                up.Patches.Add(entry);
            }

            return up;
        }
    }

    public class PatchPayload
    {
        public string TargetFile { get; set; } = "code.bin";
        public int AllocationSize { get; set; }
        public string Code { get; set; } = "";
        public List<string> BLRepoints { get; set; } = new List<string>();
    }
}
