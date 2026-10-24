using System;
using System.IO;
using pk3DS.Core.CTR;

namespace pk3DS.Core;

public class GARCFile(GARC.MemGARC g, GARCReference r, string p)
{
    // Shorthand Alias
    public byte[] GetFile(int file, int subfile = 0) { return g.GetFile(file, subfile); }

    public byte[][] Files
    {
        get => g.Files;
        // Only caches the array - does NOT pack/write anything. A lot of callers throughout this
        // codebase mutate one element of the array the getter already returned (`garc.Files[i] =
        // ...`) rather than reassigning the whole property, which never reaches a property setter
        // at all; packing eagerly here would both miss those callers and, for a multi-gigabyte
        // archive, do real, expensive work that's about to be redone (or never even used) before
        // Save() is actually called. Save() below does the one real pack, from whatever Files holds
        // at that moment - reflecting a whole-array reassignment OR any number of in-place element
        // mutations equally.
        set => g.SetFilesCached(value);
    }

    public int FileCount => g.FileCount;

    public void Save()
    {
        if (string.IsNullOrEmpty(p)) return;

        // Streams the pack straight to disk instead of MemGARC's own approach (pack to a temp file,
        // then File.ReadAllBytes it whole back into RAM) - that hard-fails once the packed archive
        // crosses ~2GB, a real limit hit live on a heavily-expanded "models" archive. Re-opening
        // from that same path afterward (MemGARC's lazy, per-file-on-demand constructor) keeps
        // GetFile/Files reads working exactly as before, just without ever holding gigabytes of
        // packed data in RAM at once.
        GARC.PackGARC(g.Files, p, g.garc.Version, (int)g.garc.ContentPadToNearest);
        g = new GARC.MemGARC(p);
        Console.WriteLine($"Wrote {r.Name} to {r.Reference}");
    }
}

public class LazyGARCFile(GARC.LazyGARC g, GARCReference r, string p)
{
    public int FileCount => g.FileCount;

    private byte[][] _cachedFiles;
    public byte[][] Files
    {
        get
        {
            if (_cachedFiles != null) return _cachedFiles;
            _cachedFiles = g.Files;
            return _cachedFiles;
        }
        set
        {
            for (int i = 0; i < value.Length; i++)
                g[i] = value[i];
            _cachedFiles = value;
        }
    }

    public byte[] this[int file]
    {
        get => g[file];
        set => g[file] = value;
    }

    public void Save()
    {
        g.Save(p);
        Console.WriteLine($"Wrote {r.Name} to {r.Reference}");
    }
}
