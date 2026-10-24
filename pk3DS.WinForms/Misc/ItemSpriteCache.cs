// A miss is a normal outcome here - most lookups are for items with no art installed - so the
// null-returning paths are load-bearing and worth having the compiler check.
#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace pk3DS.WinForms;

public static class ItemSpriteCache
{
    private static readonly Dictionary<string, Image?> Cache = new(StringComparer.Ordinal);
    private static readonly object Sync = new();
    private static string[]? _searchRoots;

    public static string[] SearchRoots => _searchRoots ??=
    [
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ItemSprites"),
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CustomSprites", "Items"),
    ];

    public static string Normalise(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static Image? Get(string? itemName)
    {
        string key = Normalise(itemName);
        if (key.Length == 0) return null;

        lock (Sync)
        {
            if (!Cache.TryGetValue(key, out var cached))
            {
                cached = Load(key);
                Cache[key] = cached;
            }

            return cached == null ? null : new Bitmap(cached);
        }
    }

    private static Image? Load(string key)
    {
        foreach (string root in SearchRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (string file in Directory.EnumerateFiles(root))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".png" or ".bmp" or ".gif" or ".jpg" or ".jpeg")) continue;
                if (Normalise(Path.GetFileNameWithoutExtension(file)) != key) continue;

                try
                {
                    // Read through memory so the file is not left locked - sprites live in a folder
                    // the user edits while the editor is open.
                    byte[] bytes = File.ReadAllBytes(file);
                    using var ms = new MemoryStream(bytes);
                    return new Bitmap(ms);
                }
                catch { return null; }
            }
        }
        return null;
    }

    public static void Invalidate()
    {
        lock (Sync)
        {
            foreach (var img in Cache.Values) img?.Dispose();
            Cache.Clear();
        }
    }

    public static (int Hits, int Misses) Coverage(IEnumerable<string> itemNames)
    {
        int hit = 0, miss = 0;
        foreach (string n in itemNames.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            if (Get(n) != null) hit++; else miss++;
        }
        return (hit, miss);
    }
}
