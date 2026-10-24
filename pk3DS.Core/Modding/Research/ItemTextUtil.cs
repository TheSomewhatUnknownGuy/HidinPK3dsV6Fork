#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace pk3DS.Core.Modding.Research;

public static class ItemTextUtil
{
    public static string Pluralize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name ?? "";
        string trimmed = name.Trim();

        // Invariant nouns (already plural or unchanged)
        if (trimmed.EndsWith(" Boots", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Heavy-Duty Boots", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(" Dice", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Loaded Dice", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(" Clothes", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(" Glasses", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(" Specs", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(" Leftovers", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        // Sibilant endings: s, sh, ch, x, z -> +es
        if (trimmed.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("sh", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("z", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed + "es";
        }

        // Consonant + y -> -y + ies (e.g. Policy -> Policies, Energy -> Energies)
        if (trimmed.EndsWith("y", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 1)
        {
            char beforeY = char.ToLowerInvariant(trimmed[^2]);
            if (beforeY is not ('a' or 'e' or 'i' or 'o' or 'u'))
            {
                return trimmed[..^1] + "ies";
            }
        }

        return trimmed + "s";
    }

    public static string PluralAlt(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name ?? "";
        string trimmed = name.Trim();
        if (trimmed.Contains("[VAR NUMBRNCH")) return trimmed;
        return $"{trimmed}[VAR NUMBRNCH(00FE,0100)]s";
    }

    public static string FormatDescription(string? text, int maxLineLength = 40)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\\n", "\n").Trim();

        if (normalized.Contains('\n'))
        {
            var lines = normalized.Split('\n', StringSplitOptions.None);
            return string.Join("\\n", lines.Select(l => l.Trim()));
        }

        // Single continuous line: word wrap
        var words = normalized.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "";

        var wrapped = new List<string>();
        var curLine = new StringBuilder();

        foreach (var word in words)
        {
            if (curLine.Length == 0)
            {
                curLine.Append(word);
            }
            else if (curLine.Length + 1 + word.Length <= maxLineLength)
            {
                curLine.Append(' ').Append(word);
            }
            else
            {
                wrapped.Add(curLine.ToString());
                curLine.Clear();
                curLine.Append(word);
            }
        }
        if (curLine.Length > 0) wrapped.Add(curLine.ToString());

        return string.Join("\\n", wrapped);
    }
}
