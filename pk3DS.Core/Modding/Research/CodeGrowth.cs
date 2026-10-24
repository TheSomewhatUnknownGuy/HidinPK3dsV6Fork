#nullable enable

using System;
using pk3DS.Core;
using pk3DS.Core.Modding;

namespace pk3DS.Core.Modding.Research;

public sealed record GrowResult(bool Ok, string Error, byte[] Code, byte[] ExHeader, byte[] Ncch0, int Pages);

public static class CodeGrowth
{
    private const int PageSize = 0x1000;

    public static GrowResult Grow(byte[] code, byte[] exheader, byte[]? ncch0, int bytesNeeded, string? journalBasePath = null)
    {
        if (!GameConfig.AllowGrowth) return Refuse("growth is off");
        if (UhuruGuard.Active) return Refuse("Uhuru: growth would shift Uhuru's own code");
        if (code == null || exheader == null) return Refuse("code.bin and exheader.bin are required");
        if (bytesNeeded <= 0) return Refuse("bytesNeeded must be positive");

        string? inputLayout = ModExport.CodeLayoutError(exheader.LongLength, exheader, code.LongLength);
        if (inputLayout != null) return Refuse("input code layout is inconsistent: " + inputLayout);

        int pages = (int)(((long)bytesNeeded + PageSize - 1) / PageSize);
        long grownLength = code.LongLength + (long)pages * PageSize;
        if (grownLength > int.MaxValue) return Refuse("grown code.bin would exceed the byte-array size limit");

        var expanded = CodeBinExpander.Expand(code, exheader, ncch0, CodeBinExpander.Segment.Text, pages, journalBasePath);
        if (!expanded.Success) return Refuse(expanded.Error);

        string? outputLayout = ModExport.CodeLayoutError(
            expanded.ExHeader.LongLength, expanded.ExHeader, expanded.CodeBin.LongLength);
        if (outputLayout != null) return Refuse("grown code layout is inconsistent: " + outputLayout);

        return new GrowResult(true, "", expanded.CodeBin, expanded.ExHeader, expanded.NcchHeader0, pages);

        GrowResult Refuse(string error) => new(false, error, code ?? [], exheader ?? [], ncch0 ?? [], 0);
    }

    public static int TextEndFor(byte[]? exheader)
    {
        if (exheader == null || exheader.Length < 0x18) return pk3DS.Core.Modding.ResearchEngine.CodeTextRegionEnd;
        long end = BitConverter.ToUInt32(exheader, 0x10)
            + (long)BitConverter.ToUInt32(exheader, 0x14) * PageSize - 0x100000;
        return end is < 0 or > int.MaxValue ? pk3DS.Core.Modding.ResearchEngine.CodeTextRegionEnd : (int)end;
    }

    public static int TextEndFor(byte[]? exheader, long codeLength)
        => exheader != null && exheader.Length >= 0x38
            && pk3DS.Core.Modding.ModExport.CodeLayoutError(exheader.LongLength, exheader[..0x38], codeLength) == null
            ? TextEndFor(exheader) : pk3DS.Core.Modding.ResearchEngine.CodeTextRegionEnd;
}
