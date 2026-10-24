#nullable enable
using System;
using System.IO;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Insertion;

public enum RomKind { Vanilla, Expansion, Uhuru, Unknown }

public static class RomLayout
{
    private const int VanillaPersonalCeiling = 1153;
    private const int ExpansionPersonalCeiling = 1507;
    private const int IconPreloadSearchWindow = 0x1000;
    private static readonly int[] IconPreloadCountOffsets = [0x1D4C2C, 0x1DF164];

    public static RomKind Detect(int evolutionBaseFiles, int trdataFiles, int trpokeFiles)
    {
        if (trdataFiles % 5 == 0 && pk3DS.Core.TrainerBlocks.DetectBaseCount(trdataFiles / 5, trdataFiles, trpokeFiles) > 0)
            return RomKind.Uhuru;

        int maxSpecies = MaxSpecies(evolutionBaseFiles);
        if (maxSpecies >= 1025) return RomKind.Expansion;
        if (maxSpecies >= 800) return RomKind.Vanilla;
        return RomKind.Unknown;
    }

    public static int MaxSpecies(int evolutionBaseFiles) => evolutionBaseFiles - 2;

    public static RomKind DetectFromRomFs(string romFs, out int maxSpecies, out string error)
    {
        maxSpecies = 0;
        error = "";
        try
        {
            string root = ResolveRomFsPath(romFs);
            int evolutionBaseFiles = ReadGarcFileCount(root, "a/0/1/8");
            int trdataFiles = ReadGarcFileCount(root, "a/1/0/6");
            int trpokeFiles = ReadGarcFileCount(root, "a/1/0/7");
            maxSpecies = MaxSpecies(evolutionBaseFiles);
            return Detect(evolutionBaseFiles, trdataFiles, trpokeFiles);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or ArgumentException or InvalidDataException)
        {
            error = e.Message;
            maxSpecies = 0;
            return RomKind.Unknown;
        }
    }

    internal static string ResolveRomFsPath(string path)
    {
        if (Directory.Exists(Path.Combine(path, "a"))) return path;
        if (!Directory.Exists(path)) return path;

        foreach (string child in Directory.GetDirectories(path))
        {
            if (Path.GetFileName(child).Equals("romfs", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(child, "a")))
                return child;
        }
        return path;
    }

    private static int ReadGarcFileCount(string root, string relativePath)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) throw new FileNotFoundException($"required GARC is missing: {relativePath}", path);
        GARC.GARCFile header = GARC.UnpackGARC(path);
        if (header.fatb.FileCount < 0) throw new InvalidDataException($"invalid file count in {relativePath}");
        return header.fatb.FileCount;
    }

    public static int PersonalCeiling(byte[]? code, RomKind kind)
    {
        if (code == null) return DefaultPersonalCeiling(kind);

        int found = int.MaxValue;
        foreach (int offset in IconPreloadCountOffsets)
        {
            int count = ReadCountAt(code, offset);
            if (count < 0) count = SearchCountNear(code, offset);
            if (count >= 0 && count < found) found = count;
        }

        return found == int.MaxValue ? DefaultPersonalCeiling(kind) : found;
    }

    private static int DefaultPersonalCeiling(RomKind kind) => kind switch
    {
        RomKind.Expansion or RomKind.Uhuru => ExpansionPersonalCeiling,
        _ => VanillaPersonalCeiling,
    };

    private static int ReadCountAt(byte[] code, int offset)
    {
        if (offset < 0 || (long)offset + sizeof(uint) > code.LongLength) return -1;
        uint count = BitConverter.ToUInt32(code, offset);
        return count is >= VanillaPersonalCeiling and <= 8192 ? (int)count : -1;
    }

    private static int SearchCountNear(byte[] code, int offset)
    {
        int start = Math.Max(0, (offset - IconPreloadSearchWindow) & ~3);
        int end = (int)Math.Min(code.LongLength - sizeof(uint), (long)offset + IconPreloadSearchWindow);
        int best = -1;
        for (int current = start; current <= end; current += sizeof(uint))
        {
            int count = ReadCountAt(code, current);
            if (count < 0) continue;
            if (best >= 0 && best != count) return -1;
            best = count;
        }
        return best;
    }

    public static string? Refusal(RomKind kind, bool species)
    {
        if (kind == RomKind.Uhuru)
            return "Refused on Uhuru: a/0/9/4 is 3.83 GiB and its 32-bit offsets cannot address the archive; a sequential repack leaves only 257,685,787 B free; the personal, levelup, and evolution tails are unexplained; code.bin writes are guarded.";
        if (kind == RomKind.Vanilla && species)
            return "Refused on Vanilla: an Expansion Pack ROM is required for species insertion; species patchers byte-verify Expansion Pack bytes, 13 documented code_map.csv rows differ between Vanilla and Expansion at identical offsets, and the icon Egg-sentinel routine has no retail counterpart.";
        if (kind == RomKind.Unknown)
            return "layout not recognised; insertion is refused.";
        return null;
    }
}
