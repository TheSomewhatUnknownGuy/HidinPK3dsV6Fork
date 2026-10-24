using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>Writes a TextPackage into its language's game/story text archives through TextFile, all or nothing.</summary>
public static class TextPackageInstaller
{
    /// <summary>Relative to the pk3DS folder; copied there by pk3DS.WinForms.csproj.</summary>
    public const string UhuruEnglishPath = "text-packages/UhuruEnglish.json";
    private const int Korean = 7;

    /// <summary>Lines in one text file of one language, or -1 when the archive or file cannot be read.</summary>
    public static int LineCount(GameConfig config, string garcName, int language, int file)
    {
        int was = config.Language;
        try
        {
            config.Language = language;
            var garc = config.GetGARCData(garcName);
            return garc == null || file < 0 || file >= garc.FileCount ? -1 : new TextFile(config, garc.Files[file]).Lines.Length;
        }
        catch (Exception) { return -1; }
        finally { config.Language = was; }
    }

    public static PackageResult Apply(TextPackage package, GameConfig config)
    {
        var result = new PackageResult { Package = package.Name };
        foreach (string m in TextPackage.CountMismatches(package.ExpectedKoreanLineCounts,
                     (kind, file) => LineCount(config, Archive(kind), Korean, file)))
            result.Fail(m);
        if (!result.Success) { result.Fail("nothing was written"); return result; }

        int was = config.Language;
        try
        {
            config.Language = package.Language;
            // Build every new file first, save after: one unreadable file leaves the ROM untouched.
            var game = Build(config, "gametext", package.GameTextLines, Counts(package, "game"), result);
            var story = Build(config, "storytext", package.StoryTextLines, Counts(package, "story"), result);
            if (!result.Success) { result.Fail("nothing was written"); return result; }
            game?.Save();
            story?.Save();
            result.Say($"{package.Name}: {package.GameTextLines.Values.Sum(f => f.Count)} game and " +
                       $"{package.StoryTextLines.Values.Sum(f => f.Count)} story line(s) written in language {package.Language}");
        }
        catch (Exception ex) { result.Fail($"install failed: {ex.Message}"); }
        finally { config.Language = was; }
        if (was == package.Language) config.InitializeGameText(); // editors read the cached strings
        return result;
    }

    private static string Archive(string kind) => kind == "story" ? "storytext" : "gametext";

    private static Dictionary<string, int> Counts(TextPackage p, string kind)
        => p.ExpectedKoreanLineCounts.TryGetValue(kind, out var c) ? c : [];

    private static GARCFile Build(GameConfig config, string garcName, Dictionary<string, Dictionary<string, string>> files,
        Dictionary<string, int> koreanCounts, PackageResult result)
    {
        if (files.Count == 0) return null;
        var garc = config.GetGARCData(garcName);
        var data = garc.Files;
        foreach (var (fileText, edits) in files)
        {
            if (!int.TryParse(fileText, out int fi) || fi < 0 || fi >= data.Length)
            { result.Fail($"{garcName}: no file {fileText}"); continue; }
            var lines = TextPackage.ApplyLines(new TextFile(config, data[fi]).Lines, edits, koreanCounts.GetValueOrDefault(fileText));
            data[fi] = TextFile.GetBytes(config, lines);
        }
        garc.Files = data;
        return garc;
    }
}
