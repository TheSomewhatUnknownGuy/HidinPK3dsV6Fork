#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace pk3DS.Core.Modding;

public sealed class BinaryWriteRequest
{
    public string Path { get; init; } = "";

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Reason { get; init; } = "";

    public string Detail { get; init; } = "";

    public int Length { get; init; }

    public int ChangedBytes { get; init; } = -1;
}

public static class BinaryWriteGuard
{
    public static bool RequireApproval { get; set; } = true;

    public static bool BackupBeforeWrite { get; set; } = true;

    public static Func<BinaryWriteRequest, bool>? ApprovalHandler { get; set; }

    private static readonly List<string> Log = [];
    private static readonly object Sync = new();

    public static IReadOnlyList<string> History
    {
        get { lock (Sync) return [.. Log]; }
    }

    public static bool IsGuarded(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        if (name.TrimStart('.').Equals("code.bin", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("exefs", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) return true;
        return System.IO.Path.GetExtension(path).Equals(".cro", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryWrite(string path, byte[] data, string reason, string detail = "", bool uhuruDiffChecked = false)
    {
        if (string.IsNullOrEmpty(path) || data == null)
            return false;

        // In-place table editors (TM table) may opt in: on Uhuru a same-size code.bin write that misses every Uhuru region is allowed.
        string? uhuru = uhuruDiffChecked ? UhuruGuard.InPlaceRefusal(path, File.Exists(path) ? File.ReadAllBytes(path) : null, data) : UhuruGuard.RefusalFor(path);
        if (uhuru != null)
        {
            Record(new BinaryWriteRequest { Path = path, Reason = reason }, "REFUSED: " + uhuru);
            return false;
        }

        if (!IsGuarded(path))
        {
            File.WriteAllBytes(path, data);
            return true;
        }

        var request = new BinaryWriteRequest
        {
            Path = path,
            Reason = reason,
            Detail = detail,
            Length = data.Length,
            ChangedBytes = CountChangedBytes(path, data),
        };

        if (RequireApproval)
        {
            var handler = ApprovalHandler;
            if (handler == null)
            {
                Record(request, "REFUSED (no approval handler available)");
                return false;
            }
            if (!handler(request))
            {
                Record(request, "DECLINED by user");
                return false;
            }
        }

        if (BackupBeforeWrite)
            TryBackup(path);

        pk3DS.Core.CTR.CROUtil.SaveCro(path, data);
        Record(request, RequireApproval ? "APPROVED" : "written (approval disabled)");
        return true;
    }

    private static int CountChangedBytes(string path, byte[] data)
    {
        try
        {
            if (!File.Exists(path)) return -1;
            byte[] existing = File.ReadAllBytes(path);
            int n = Math.Min(existing.Length, data.Length);
            int changed = Math.Abs(existing.Length - data.Length);
            for (int i = 0; i < n; i++)
            {
                if (existing[i] != data[i]) changed++;
            }
            return changed;
        }
        catch { return -1; }
    }

    private static void TryBackup(string path)
    {
        try
        {
            string backup = path + ".orig";
            if (!File.Exists(backup) && File.Exists(path))
                File.Copy(path, backup);
        }
        catch { /* a missing backup must not stop an approved write */ }
    }

    private static void Record(BinaryWriteRequest r, string outcome)
    {
        lock (Sync)
        {
            Log.Add($"[{DateTime.Now:HH:mm:ss}] {r.FileName}: {outcome} - {r.Reason}"
                    + (r.ChangedBytes >= 0 ? $" ({r.ChangedBytes} bytes differ)" : ""));
        }
    }
}
