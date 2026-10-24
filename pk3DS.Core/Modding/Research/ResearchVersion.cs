#nullable enable

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace pk3DS.Core.Modding.Research;

public static class ResearchVersion
{
    public static readonly string[] Known = ["UM", "US", "SM"];

    public static string? Override { get; set; }

    public static string Resolve(GameConfig? cfg, string? romfsPath = null)
    {
        if (!string.IsNullOrWhiteSpace(Override)) return Override!;

        // Sun/Moon are a different generation of the corpus entirely.
        if (cfg?.SM == true) return "SM";

        // The loader already told the two apart; only the ambiguous USUM case falls to the path.
        if (cfg?.Version == GameVersion.UM) return "UM";
        if (cfg?.Version == GameVersion.US) return "US";

        string hint = (romfsPath ?? "") + " " + (cfg?.RomFS ?? "");
        if (hint.Length > 0)
        {
            // Check Moon first: "Ultra Sun" and "Ultra Moon" both contain "U", and a path holding
            // both words is more likely a parent folder than a ROM, so the more specific token wins.
            if (Contains(hint, "ultramoon") || Contains(hint, "ultra moon") || HasToken(hint, "um"))
                return "UM";

            if (Contains(hint, "ultrasun") || Contains(hint, "ultra sun") || HasToken(hint, "us"))
                return "US";
        }

        // UM is the version the bulk of the corpus was written against, so it is the safer default.
        return "UM";
    }

    public static bool IsInferred => string.IsNullOrWhiteSpace(Override);

    public static string CachePathFor(string version) =>
        ResearchDatabase.CacheFile($"research_cache_{version}.json");

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    internal static bool HasToken(string haystack, string token) =>
        Regex.IsMatch(haystack, $"(?<![a-z0-9]){token}(?![a-z0-9])", RegexOptions.IgnoreCase);
}
