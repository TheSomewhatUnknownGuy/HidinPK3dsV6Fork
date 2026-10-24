using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// A single recorded code.bin segment expansion event.
/// </summary>
public sealed class CodeBinExpansionEntry
{
    public string Segment { get; set; } = "";
    public uint InsertionOffset { get; set; }
    public uint InsertedBytes { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Persists and translates file offsets across code.bin expansions.
/// Prevents legacy IPS patches and fixed-offset patches from corrupting code.bin
/// when segments (.text, .rodata) have been grown and shifted.
/// </summary>
public static class CodeBinExpansionJournal
{
    public const string JournalFileName = "code_bin_expansions.json";

    /// <summary>
    /// Finds the journal file in the project directory or ExeFS directory.
    /// </summary>
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

    /// <summary>
    /// Records a new expansion event into the journal.
    /// </summary>
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

    /// <summary>
    /// Translates an original unshifted code.bin offset forward through all recorded expansions.
    /// </summary>
    public static uint TranslateOffset(uint originalOffset, string basePath)
    {
        var entries = Load(basePath);
        return TranslateOffset(originalOffset, entries);
    }

    /// <summary>
    /// Translates an original offset forward given a loaded list of journal entries.
    /// </summary>
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

    /// <summary>
    /// Translates all IPS records in-place or into new instances with translated offsets.
    /// </summary>
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
