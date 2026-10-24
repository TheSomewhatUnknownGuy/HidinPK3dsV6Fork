using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public sealed class ResearchGrid
{
    public string SourceFile { get; init; } = "";
    public string SheetName { get; init; } = "";

    private readonly List<string[]> _rows = new();

    public int RowCount => _rows.Count;
    public int ColumnCount { get; private set; }

    internal void AddRow(string[] cells)
    {
        _rows.Add(cells);
        if (cells.Length > ColumnCount) ColumnCount = cells.Length;
    }

    public string this[int row, int col]
    {
        get
        {
            if (row < 0 || row >= _rows.Count) return "";
            var r = _rows[row];
            if (col < 0 || col >= r.Length) return "";
            return r[col] ?? "";
        }
    }

    public IEnumerable<string> Row(int row)
    {
        if (row < 0 || row >= _rows.Count) yield break;
        foreach (var c in _rows[row]) yield return c ?? "";
    }

    public bool IsRowEmpty(int row)
    {
        for (int c = 0; c < ColumnCount; c++)
            if (!string.IsNullOrWhiteSpace(this[row, c])) return false;
        return true;
    }

    public int FindHeaderRow(IReadOnlyList<string> anyOfThese, int minimumMatches = 2, int searchDepth = 12)
    {
        int bestRow = -1, bestScore = 0;
        int depth = Math.Min(searchDepth, RowCount);
        for (int r = 0; r < depth; r++)
        {
            int score = 0;
            for (int c = 0; c < ColumnCount; c++)
            {
                string cell = this[r, c];
                if (string.IsNullOrWhiteSpace(cell)) continue;
                if (anyOfThese.Any(h => cell.Contains(h, StringComparison.OrdinalIgnoreCase)))
                    score++;
            }
            if (score > bestScore) { bestScore = score; bestRow = r; }
        }
        return bestScore >= minimumMatches ? bestRow : -1;
    }

    public Dictionary<string, int> HeaderMap(int headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int c = 0; c < ColumnCount; c++)
        {
            string raw = this[headerRow, c].Trim();
            if (raw.Length == 0) continue;
            string key = raw;
            int dup = 2;
            while (map.ContainsKey(key)) key = $"{raw}#{dup++}";
            map[key] = c;
        }
        return map;
    }

    public static int ColumnOf(Dictionary<string, int> headers, params string[] fragments)
    {
        foreach (var frag in fragments)
        {
            foreach (var kv in headers)
                if (kv.Key.Contains(frag, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
        }
        return -1;
    }
}
