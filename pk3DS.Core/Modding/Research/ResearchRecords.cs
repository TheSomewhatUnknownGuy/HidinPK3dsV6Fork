using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public enum ResearchTarget { Unknown = 0, BattleCro, BagCro, ShopCro, BoxCro, StatusCro, FieldRoCro, CodeBin, EvolutionCro }

public enum ResearchSheetKind
{
    Unknown = 0,
    PatchList,          // offset + hex/assembly -> raw byte writes
    RelocationTracker,  // Custom Relocation Patches: patch address / write-to / pointer / segments
    FunctionIndex,      // Table of stock functions: offset -> name/description
    TableRegistry,      // Table locations and sizes: original vs edited table addresses + counts
    FreeSpaceRegistry,  // Table of my custom functions: offset / length / room
    TimingFlags,        // Flags sheet: timing byte -> meaning
    MechanicIndex,      // Move/Ability/Item Index: the master table, entry by entry
    Reference,          // type charts, text dumps, notes - kept for lookup, never applied
}

public readonly record struct ResearchOrigin(string File, string Sheet, int Row)
{
    public override string ToString() =>
        $"{System.IO.Path.GetFileName(File)}[{Sheet}]:{Row + 1}";
}

public sealed class ResearchPatch
{
    public uint Offset { get; set; }
    public byte[] Bytes { get; set; } = [];
    public string Assembly { get; set; }
    public string Note { get; set; }
    public ResearchOrigin Origin { get; set; }
    public string HexBytes => Bytes == null ? "" : Convert.ToHexString(Bytes);
}

public sealed class ResearchRelocation
{
    public string Category { get; set; }        // "Table Pointer", "Function Table Pointer", ...
    public string Specific { get; set; }        // "Light Ball", "Ignorable Abilities", ...
    public string TargetNote { get; set; }
    public uint PatchAddress { get; set; }      // where the 0xC-byte patch entry itself lives
    public uint WriteTo { get; set; }           // address the pointer gets written to
    public uint Pointer { get; set; }           // address being pointed at
    public int WriteSegment { get; set; } = -1;
    public int PointerSegment { get; set; } = -1;
    public bool IsBss { get; set; }
    public ResearchTarget Target { get; set; }
    public ResearchOrigin Origin { get; set; }
}

public sealed class ResearchFunction
{
    public uint Offset { get; set; }            // in-file offset
    public uint LoadedAddress { get; set; }     // runtime address, when documented
    public string Name { get; set; }
    public string Details { get; set; }
    public ResearchTarget Target { get; set; }
    public ResearchOrigin Origin { get; set; }
}

public sealed class ResearchTableLocation
{
    public string Name { get; set; }                 // "Ability Index", "Move Index", ...

    public uint CodeLocation { get; set; }
    public uint EntryParamAddress { get; set; }
    public string BoundsCondition { get; set; }

    public int OriginalEntryCount { get; set; } = -1;
    public int EditedEntryCount { get; set; } = -1;

    public uint OriginalTableData { get; set; }
    public uint EditedTableData { get; set; }

    public ResearchTarget Target { get; set; }
    public ResearchOrigin Origin { get; set; }

    public uint EffectiveTableData => EditedTableData != 0 ? EditedTableData : OriginalTableData;
    public int EffectiveEntryCount => EditedEntryCount >= 0 ? EditedEntryCount : OriginalEntryCount;
}

public sealed class ResearchFreeSpace
{
    public uint Offset { get; set; }
    public int Length { get; set; }
    public int Room { get; set; }               // bytes still unused at this offset
    public string Function { get; set; }        // what already lives there, when anything does
    public string Details { get; set; }
    public ResearchTarget Target { get; set; }
    public ResearchOrigin Origin { get; set; }
}

public sealed class ResearchFunctionBody
{
    public string Name { get; set; }
    public byte[] Code { get; set; } = [];
    public List<string> Assembly { get; } = [];
    public ResearchTarget Target { get; set; }
    public ResearchOrigin Origin { get; set; }
    public int Size => Code?.Length ?? 0;
}

public sealed class ResearchMechanicEntry
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public uint HandlerFileOffset { get; set; }
    public uint EntryFileOffset { get; set; }
}

public sealed class ResearchMechanicIndex
{
    public string Name { get; set; }                    // "Move Index", "Ability Index", ...
    public CustomMechanicKind? Kind { get; set; }
    public uint DocumentedTableOffset { get; set; }     // where it sat in the reference ROM
    public List<ResearchMechanicEntry> Entries { get; } = [];
    public ResearchTarget Target { get; set; }
    public ResearchOrigin Origin { get; set; }

    public uint[] Fingerprint => Entries.Select(e => e.Id).ToArray();
}

public sealed class ResearchTiming
{
    public byte Value { get; set; }
    public string Meaning { get; set; }
    public string Examples { get; set; }
    public ResearchOrigin Origin { get; set; }
}

public sealed class ResearchSheet
{
    public string SourceFile { get; set; }
    public string SheetName { get; set; }
    public string DisplayName { get; set; }
    public string Category { get; set; }        // folder-derived: Move/Ability/Item/Generic/...
    public ResearchSheetKind Kind { get; set; }
    public ResearchTarget Target { get; set; }
    public double Confidence { get; set; }      // 0..1 for the kind classification

    public List<ResearchPatch> Patches { get; } = [];
    public List<ResearchRelocation> Relocations { get; } = [];
    public List<ResearchFunction> Functions { get; } = [];
    public List<ResearchFunctionBody> Bodies { get; } = [];
    public List<ResearchTableLocation> Tables { get; } = [];
    public List<ResearchFreeSpace> FreeSpace { get; } = [];
    public List<ResearchTiming> Timings { get; } = [];
    public List<ResearchMechanicIndex> MechanicIndexes { get; } = [];
    public List<string> Diagnostics { get; } = [];

    public int RecordCount =>
        Patches.Count + Relocations.Count + Functions.Count + Bodies.Count +
        Tables.Count + FreeSpace.Count + Timings.Count +
        MechanicIndexes.Sum(m => m.Entries.Count);
}
