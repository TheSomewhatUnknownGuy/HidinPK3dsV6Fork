using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace pk3DS.Core.Modding.Research;

public sealed class TextPackage
{
    public string Name { get; set; } = "";
    public int Language { get; set; } = 2;
    public Dictionary<string, Dictionary<string, string>> GameTextLines { get; set; } = [];
    public Dictionary<string, Dictionary<string, string>> StoryTextLines { get; set; } = [];
    public Dictionary<string, Dictionary<string, int>> ExpectedKoreanLineCounts { get; set; } = [];

    public const int UhuruFacilityStoryFile = 1078;

    public static TextPackage FromJson(string json) => JsonSerializer.Deserialize<TextPackage>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    public static TextPackage Load(string path) => FromJson(File.ReadAllText(path));

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

    public static bool NeedsUhuruEnglish(int trainerBaseCount, int englishLines, int koreanLines)
        => trainerBaseCount > 0 && englishLines <= 0 && koreanLines > 0;
}
