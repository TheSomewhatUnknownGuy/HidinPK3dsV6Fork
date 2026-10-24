using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace pk3DS.Core.Modding.Research;

public sealed class CodeBinExpansionEntry
{
    public string Segment { get; set; } = "";
    public uint InsertionOffset { get; set; }
    public uint InsertedBytes { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public static class CodeBinExpansionJournal
{
    public const string JournalFileName = "code_bin_expansions.json";

    public static string GetJournalPath(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath)) return JournalFileName;
        if (File.Exists(basePath)) basePath = Path.GetDirectoryName(basePath) ?? basePath;
        
        string direct = Path.Combine(basePath, JournalFileName);
        if (File.Exists(direct)) return direct;

        string parent = Path.GetDirectoryName(basePath);
        if (!string.IsNullOrEmpty(parent))
        {
            string parentDirect = Path.Combine(parent, JournalFileName);
            if (File.Exists(parentDirect)) return parentDirect;
        }

        return direct;
    }

    public static List<CodeBinExpansionEntry> Load(string basePath)
    {
        string path = GetJournalPath(basePath);
        if (!File.Exists(path)) return [];

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<CodeBinExpansionEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(string basePath, List<CodeBinExpansionEntry> entries)
    {
        string path = GetJournalPath(basePath);
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Ignore write errors to keep non-blocking
        }
    }

    public static void RecordExpansion(string basePath, CodeBinExpander.Segment segment, uint insertionOffset, uint insertedBytes)
    {
        var entries = Load(basePath);
        entries.Add(new CodeBinExpansionEntry
        {
            Segment = segment.ToString(),
            InsertionOffset = insertionOffset,
            InsertedBytes = insertedBytes,
            Timestamp = DateTime.UtcNow
        });
        Save(basePath, entries);
    }

    public static uint TranslateOffset(uint originalOffset, string basePath)
    {
        var entries = Load(basePath);
        return TranslateOffset(originalOffset, entries);
    }

    public static uint TranslateOffset(uint originalOffset, IEnumerable<CodeBinExpansionEntry> entries)
    {
        uint current = originalOffset;
        foreach (var entry in entries)
        {
            if (current >= entry.InsertionOffset)
            {
                current += entry.InsertedBytes;
            }
        }
        return current;
    }

    public static List<IpsRecord> TranslateIpsRecords(IEnumerable<IpsRecord> records, string basePath)
    {
        var entries = Load(basePath);
        if (entries.Count == 0) return records.ToList();

        var translated = new List<IpsRecord>();
        foreach (var rec in records)
        {
            uint newOffset = TranslateOffset((uint)rec.Offset, entries);
            translated.Add(new IpsRecord((int)newOffset, rec.Bytes, rec.Rle));
        }
        return translated;
    }
}
