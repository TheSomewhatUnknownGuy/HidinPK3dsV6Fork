using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Lines to write into one language's text archives: a translation. Kept apart from PatchPackage because
/// Recipes.Discover lists every patch-packages JSON as a mechanic. Pure (no ROM access): tests/CompetitiveCheck
/// compiles this file on its own. Written by scripts/uhuru/entext.py; installed by TextPackageInstaller.
/// </summary>
public sealed class TextPackage
{
    public string Name { get; set; } = "";
    /// <summary>The one language written (2 = English).</summary>
    public int Language { get; set; } = 2;
    /// <summary>Game text: file index to line index to text, in pk3DS TextFile syntax.</summary>
    public Dictionary<string, Dictionary<string, string>> GameTextLines { get; set; } = [];
    /// <summary>Story text, same shape.</summary>
    public Dictionary<string, Dictionary<string, string>> StoryTextLines { get; set; } = [];
    /// <summary>"game"/"story" to file to the Korean line count the translation was made from. Any mismatch = other ROM version: nothing is written.</summary>
    public Dictionary<string, Dictionary<string, int>> ExpectedKoreanLineCounts { get; set; } = [];

    /// <summary>Story file holding Uhuru's facility text; empty in English until translated.</summary>
    public const int UhuruFacilityStoryFile = 1078;

    public static TextPackage FromJson(string json) => JsonSerializer.Deserialize<TextPackage>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    public static TextPackage Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>Copy of the lines grown to at least minCount and past the highest edit (new lines empty), edits applied. Non-numeric keys are ignored.</summary>
    public static string[] ApplyLines(string[] lines, IReadOnlyDictionary<string, string> edits, int minCount)
    {
        int top = Math.Max(minCount, lines.Length);
        foreach (var k in edits.Keys)
            if (int.TryParse(k, out int li) && li >= top) top = li + 1;
        var s = new string[top];
        Array.Copy(lines, s, lines.Length);
        for (int i = lines.Length; i < top; i++) s[i] = "";
        foreach (var (k, text) in edits)
            if (int.TryParse(k, out int li) && li >= 0) s[li] = text;
        return s;
    }

    /// <summary>One message per file whose Korean line count differs from what the package expects.</summary>
    public static List<string> CountMismatches(Dictionary<string, Dictionary<string, int>> expected, Func<string, int, int> koreanLineCount)
    {
        var problems = new List<string>();
        foreach (var (garc, files) in expected ?? [])
            foreach (var (fileText, want) in files)
            {
                int got = int.TryParse(fileText, out int fi) ? koreanLineCount(garc, fi) : -1;
                if (got != want)
                    problems.Add($"{garc} text file {fileText}: Korean has {got} lines, the translation expects {want} (a different Uhuru version?)");
            }
        return problems;
    }

    /// <summary>An Uhuru ROM (trainer blocks found) whose facility story text is Korean-only. englishLines -1 (unreadable) also counts as missing, so the prompt never goes silently dead.</summary>
    public static bool NeedsUhuruEnglish(int trainerBaseCount, int englishLines, int koreanLines)
        => trainerBaseCount > 0 && englishLines <= 0 && koreanLines > 0;
}
