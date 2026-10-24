using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core;

/// <summary>
/// Resolves an AMX script's SYSREQ.N calls (pk3DS's own opcode 0x87, displayed as "DoCommand?") to
/// real native-function names.
/// <para>
/// Each script carries its own native-function table in its header (offset 0x24 through 0x28 -
/// <see cref="Script.NativeFunctionHashes"/>), one 32-bit slot per native the script references. The
/// slot does not hold the name itself, or an offset into a string table: it holds a hash of the
/// name, computed the same way <see cref="HashName"/> does here. <see cref="Scripts.ParseScript"/>
/// was never able to show real names because nothing in this codebase read that table or knew the
/// hash - confirmed 2026-09-13 after a native-index guess (structurally plausible, wrong in fact)
/// crashed the game; the real fix is resolving the hash, not guessing better.
/// </para>
/// <para>
/// The name list itself (<c>Resources/amx_commands.txt</c>) is sourced from XAMX
/// (https://github.com/BlastSlimey/XAMX, copyright BlastSlimey, GPL v3, same license as this
/// project) - a real Pokemon XY/ORAS/SM/USUM AMX dis/reassembler that documents this exact format.
/// </para>
/// </summary>
public static class AmxNatives
{
    private static readonly Lazy<Dictionary<uint, string>> HashToName = new(LoadFromResource);

    /// <summary>
    /// The exact hash XAMX's own tooling uses to key its native-name list, so a script's raw table
    /// entry can be looked up directly: rolling 32-bit hash, each byte folded in via
    /// <c>hash = (131*hash mod 2^32) xor byte</c>.
    /// </summary>
    public static uint HashName(string name)
    {
        uint hash = 0;
        foreach (char c in name)
            hash = unchecked(131u * hash) ^ (byte)c;
        return hash;
    }

    /// <summary>Resolves a native's name hash to its real name, if it is a known one.</summary>
    public static bool TryResolve(uint hash, out string name) => HashToName.Value.TryGetValue(hash, out name);

    /// <summary>
    /// The real name for native table slot <paramref name="nativeIndex"/> in a script whose table is
    /// <paramref name="hashes"/> (<see cref="Script.NativeFunctionHashes"/>), or null if the index is
    /// out of range or the hash isn't a known name - callers fall back to showing the raw index.
    /// </summary>
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
