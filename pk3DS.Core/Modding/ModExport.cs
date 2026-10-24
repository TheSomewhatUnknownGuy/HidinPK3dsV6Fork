using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.Modding;

public static class ModExport
{
    public enum Layout { Azahar, Luma }

    public record Result(List<string> Files, long Bytes, List<string> Errors);

    public const long Fat32Limit = 1L << 32;

    public static readonly string[] UhuruFiles =
    [
        "exefs/code.bin",
        "exheader.bin",
        "romfs/Bag.cro",
        "romfs/Battle.cro",
        "romfs/Box.cro",
        "romfs/FieldRo.cro",
        "romfs/Finder.cro",
        "romfs/FinderStudioCapture.cro",
        "romfs/FinderStudioDecoration.cro",
        "romfs/FinderStudioViewer.cro",
        "romfs/MyAlbum.cro",
        "romfs/Resort.cro",
        "romfs/Shop.cro",
        "romfs/Status.cro",
        "romfs/Zukan.cro",
        "romfs/a/0/1/1",
        "romfs/a/0/1/2",
        "romfs/a/0/1/3",
        "romfs/a/0/1/4",
        "romfs/a/0/1/5",
        "romfs/a/0/1/7",
        "romfs/a/0/1/8",
        "romfs/a/0/1/9",
        "romfs/a/0/2/0",
        "romfs/a/0/3/0",
        "romfs/a/0/3/1",
        "romfs/a/0/3/2",
        "romfs/a/0/3/3",
        "romfs/a/0/3/4",
        "romfs/a/0/3/5",
        "romfs/a/0/3/6",
        "romfs/a/0/3/7",
        "romfs/a/0/4/2",
        "romfs/a/0/4/7",
        "romfs/a/0/6/1",
        "romfs/a/0/6/2",
        "romfs/a/0/6/3",
        "romfs/a/0/7/3",
        "romfs/a/0/8/2",
        "romfs/a/0/8/7",
        "romfs/a/0/8/8",
        "romfs/a/0/9/2",
        "romfs/a/0/9/4",
        "romfs/a/0/9/5",
        "romfs/a/0/9/6",
        "romfs/a/1/0/3",
        "romfs/a/1/0/5",
        "romfs/a/1/0/6",
        "romfs/a/1/0/7",
        "romfs/a/1/1/8",
        "romfs/a/1/1/9",
        "romfs/a/1/2/4",
        "romfs/a/1/4/0",
        "romfs/a/1/4/3",
        "romfs/a/1/5/6",
        "romfs/a/1/5/9",
        "romfs/a/1/6/8",
        "romfs/a/1/7/4",
        "romfs/a/2/4/4",
        "romfs/a/2/4/7",
        "romfs/a/2/5/7",
        "romfs/a/2/7/3",
        "romfs/a/2/7/7",
        "romfs/a/2/7/8",
        "romfs/a/3/2/6",
        "romfs/a/3/3/0",
        "romfs/data/sound/bgm_xy_silence.dspadpcm.bcstm",
        "romfs/data/sound/niji_sound.bcsar"
    ];

    public static string ReadTitleId(string exheaderPath)
    {
        using var f = File.OpenRead(exheaderPath);
        var b = new byte[8];
        f.Position = 0x200;
        f.ReadExactly(b);
        return BitConverter.ToUInt64(b, 0).ToString("X16");
    }

#nullable enable
    public static string? CodeLayoutError(long exheaderLength, byte[] exheaderHead, long codeLength)
    {
        if (exheaderLength is not (0x400 or 0x800))
            return $"exheader.bin: length 0x{exheaderLength:X} ({exheaderLength:N0} bytes), expected 0x400 or 0x800";
        if (exheaderHead == null || exheaderHead.Length < 0x38)
            return "exheader.bin: code layout fields are missing from its header";

        long pages = (long)BitConverter.ToUInt32(exheaderHead, 0x14)
            + BitConverter.ToUInt32(exheaderHead, 0x24)
            + BitConverter.ToUInt32(exheaderHead, 0x34);
        long expectedCodeLength = pages * 0x1000L;
        if (codeLength != expectedCodeLength)
            return $"exefs/code.bin: length {codeLength:N0} bytes (0x{codeLength:X}) does not match "
                + $"exheader page sum {pages} pages requiring {expectedCodeLength:N0} bytes (0x{expectedCodeLength:X})";
        return null;
    }
#nullable disable

    public static string DestPath(string outDir, string tid, string rel, Layout layout)
    {
        rel = rel.Replace('\\', '/').TrimStart('/');
        string root = layout == Layout.Azahar ? Path.Combine(outDir, tid) : Path.Combine(outDir, "luma", "titles", tid);
        if (layout == Layout.Luma && rel.Equals("exefs/code.bin", StringComparison.OrdinalIgnoreCase))
            rel = "code.bin";
        return Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string SourcePath(string romfs, string exefs, string exheader, string rel)
    {
        rel = rel.Replace('\\', '/').TrimStart('/');
        if (rel.Split('/').Any(s => s is "" or "." or ".." || s.Contains(':'))) // ':' is a drive or stream on Windows
            return null; // never read outside the ROM folder
        if (rel.Equals("exheader.bin", StringComparison.OrdinalIgnoreCase))
            return exheader;
        if (rel.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            if (exefs == null) return null;
            string sub = rel[6..];
            if (!sub.Equals("code.bin", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(exefs, sub);
            string dot = Path.Combine(exefs, ".code.bin"), plain = Path.Combine(exefs, "code.bin");
            if (File.Exists(dot) && File.Exists(plain)) // both: the newer one, as ExeFS.ResolveCodeBin picks
                return File.GetLastWriteTimeUtc(plain) > File.GetLastWriteTimeUtc(dot) ? plain : dot;
            return File.Exists(dot) ? dot : plain;
        }
        if (rel.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
            return romfs == null ? null : Path.Combine(romfs, rel[6..].Replace('/', Path.DirectorySeparatorChar));
        return null;
    }

    public static Result Build(string romfs, string exefs, string exheader, string outDir, Layout layout,
        IEnumerable<string> extraPaths, bool uhuru, bool dryRun)
    {
        var rels = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "exheader.bin", "exefs/code.bin" };
        if (uhuru)
            rels.UnionWith(UhuruFiles);
        foreach (string p in extraPaths ?? [])
        {
            string r = p.Replace('\\', '/').TrimStart('/');
            rels.Add(r.Equals("exefs/.code.bin", StringComparison.OrdinalIgnoreCase) ? "exefs/code.bin" : r);
        }

        var errors = new List<string>();
        string tid;
        try { tid = ReadTitleId(exheader); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            errors.Add($"exheader.bin: cannot read the title ID ({ex.Message})");
            return new Result([], 0, errors);
        }
        var plan = new List<(string Src, string Dest, long Len)>();
        long exheaderLength = -1, codeLength = -1;
        byte[] exheaderHead = [];
        foreach (string rel in rels)
        {
            string src = SourcePath(romfs, exefs, exheader, rel);
            if (src == null || !File.Exists(src)) { errors.Add($"{rel}: not found in the ROM folder"); continue; }
            long len = new FileInfo(src).Length;
            if (len >= Fat32Limit) errors.Add($"{rel}: {len:N0} bytes, too big for a FAT32 SD card (4 GiB)");
            if (rel.Equals("exefs/code.bin", StringComparison.OrdinalIgnoreCase) && len % 0x200 != 0)
                errors.Add($"{rel}: still compressed (size not a multiple of 0x200); decompress it in pk3DS first");
            if (rel.Equals("exheader.bin", StringComparison.OrdinalIgnoreCase))
            {
                exheaderLength = len;
                if (len >= 0x38)
                {
                    exheaderHead = new byte[0x38];
                    using var f = File.OpenRead(src);
                    f.ReadExactly(exheaderHead);
                }
            }
            if (rel.Equals("exefs/code.bin", StringComparison.OrdinalIgnoreCase))
                codeLength = len;
            plan.Add((src, DestPath(outDir, tid, rel, layout), len));
        }

        if (exheaderLength >= 0 && codeLength >= 0
            && CodeLayoutError(exheaderLength, exheaderHead, codeLength) is { } layoutError)
            errors.Add(layoutError);

        var result = new Result(plan.Select(x => x.Dest).ToList(), plan.Sum(x => x.Len), errors);
        if (errors.Count > 0 || dryRun)
            return result;
        foreach (var (src, dest, _) in plan)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            string tmp = dest + ".tmp"; // a failed copy never leaves a truncated file under its real name
            try { File.Copy(src, tmp, true); File.Move(tmp, dest, true); }
            catch { try { File.Delete(tmp); } catch { } throw; } // keep the copy error, not a cleanup error
        }
        return result;
    }
}
