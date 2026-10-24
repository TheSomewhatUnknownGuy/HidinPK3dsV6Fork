using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core;

public static class AmxNatives
{
    private static readonly Lazy<Dictionary<uint, string>> HashToName = new(LoadFromResource);

    public static uint HashName(string name)
    {
        uint hash = 0;
        foreach (char c in name)
            hash = unchecked(131u * hash) ^ (byte)c;
        return hash;
    }

    public static bool TryResolve(uint hash, out string name) => HashToName.Value.TryGetValue(hash, out name);

    public static string Resolve(uint[] hashes, int nativeIndex)
    {
        if (hashes == null || nativeIndex < 0 || nativeIndex >= hashes.Length) return null;
        return TryResolve(hashes[nativeIndex], out string name) ? name : null;
    }

    private static Dictionary<uint, string> LoadFromResource()
    {
        var result = new Dictionary<uint, string>();
        try
        {
            var assembly = typeof(AmxNatives).Assembly;
            string resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("amx_commands.txt", StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) return result;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return result;
            using var reader = new StreamReader(stream);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("//")) continue;

                string name = trimmed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)[0];
                uint hash = HashName(name);
                result.TryAdd(hash, name); // first entry wins on a collision, matching XAMX's own data.py
            }
        }
        catch { /* no embedded list is not fatal - callers just see raw indices, same as before */ }
        return result;
    }
}
