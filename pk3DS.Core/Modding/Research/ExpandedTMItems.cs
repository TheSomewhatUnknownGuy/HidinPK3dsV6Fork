using System;
using System.IO;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// The item IDs behind the Expansion Pack's extra TMs (TM101-TM128), and writing their names and bag entries.
/// <para>
/// <see cref="ExpandCode"/> takes its IDs from <c>TMExpansion.PickItemIds</c>: the lowest IDs below 1024 that no item
/// uses. The bag stores item IDs in 10 bits, so an ID of 1024 or more would show as a different item.
/// </para>
/// <para>
/// The checked US Expansion and Uhuru ROMs have 36 usable "???" ids below 960; the UM Expansion could differ (nine
/// was observed there), so ids are chosen at runtime by name. Handing out ids from 960 up was dropped: on the checked
/// UM Expansion ROM those are real items (Booster Energy, Eternatusite, Tera Crystal, and more), and writing TM names
/// over them would have destroyed them.
/// </para>
/// </summary>
public static class ExpandedTMItems
{
    /// <summary>Display name for a slot: index 100 is "TM101".</summary>
    public static string GetTMName(int tableIndex) => $"TM{tableIndex + 1:D3}";

    /// <summary>
    /// Grows the TM table in <paramref name="code"/> to <paramref name="newCount"/> entries. The new TMs get the
    /// lowest item ids below 1024 that no item uses, so none overwrites a real item. <paramref name="code"/> is not
    /// changed: returns the new code.bin buffer, or null with the reason in <paramref name="error"/>.
    /// </summary>
    /// <param name="newMoves">One move per new TM.</param>
    /// <param name="newItemIds">The item ids of the new TMs, in TM order.</param>
#nullable enable
    public static byte[]? ExpandCode(GameConfig cfg, byte[] code, int newCount, ushort[] newMoves,
                                     out ushort[] newItemIds, out string error) =>
        ExpandCode(cfg, code, newCount, newMoves, false, out newItemIds, out error, out _);

    /// <summary>Like the compatibility overload, and reports any code.bin growth needed for the allocation.</summary>
    public static byte[]? ExpandCode(GameConfig cfg, byte[] code, int newCount, ushort[] newMoves,
                                     out ushort[] newItemIds, out string error, out GrowResult? growth) =>
        ExpandCode(cfg, code, newCount, newMoves, true, out newItemIds, out error, out growth);

    private static byte[]? ExpandCode(GameConfig cfg, byte[] code, int newCount, ushort[] newMoves, bool allowGrowth,
                                      out ushort[] newItemIds, out string error, out GrowResult? growth)
    {
        newItemIds = [];
        growth = null;
        error = "";
        if (newCount > TMExpansion.EffectiveMax && PassiveGuard.Enabled) { error = PassiveGuard.TmCapReason; return null; } // before any growth is attempted
        var t = TMTable.Locate(code, out error);
        if (t == null) return null;

        string[] names = cfg.GetText(TextName.ItemNames);
        // With no item names, every id looks free, so the new TMs would take ids 1..28 over real items.
        if (names.Length == 0) { error = "the item name table is empty, so the new TMs cannot be named"; return null; }
        bool Usable(int id) => (id >= names.Length || RecipeIdAllocator.IsFree(names, id))
            && !UhuruGuard.ProtectedPocketItems.Contains(id) && !UhuruGuard.ProtectedBattleBagItems.Contains(id);
        int[] ids = TMExpansion.PickItemIds(newCount - t.Count, Usable);
        if (ids.Length == 0)
        {
            error = $"not enough free item ids below 1024 for {newCount - t.Count} new TMs (the bag stores item ids in 10 bits; ids from 1024 show as other items)";
            return null;
        }

        long needed = TMExpansion.CodeBytes + 4L * newCount;
        if (needed <= 0 || needed > int.MaxValue)
        { error = $"TM expansion needs {needed} bytes, outside the supported range"; return null; }

        var keepOut = InstallRecord.Load(cfg.RomFS).PlacedIn("code.bin");
        byte[]? TryExpand(byte[] source, TMTable table, int textEnd, out string failure, out bool spaceRefused)
        {
            // One allocator decides where the block goes. When nothing fits, the largest remaining run goes in as
            // the room, so Expand reports its usual executable-padding refusal.
            var allocator = CodeSpaceAllocator.ForCodeBin(source, keepOut, textEnd);
            var grant = allocator.Allocate((int)needed, "TM expansion");
            var room = grant.Success ? ((int)grant.Offset, grant.Size) : allocator.LargestFree();
            byte[]? expanded = TMExpansion.Expand(source, newCount,
                table.ReadItems(source).Concat(ids.Select(i => (ushort)i)).ToArray(),
                table.ReadMoves(source).Concat(newMoves).ToArray(), room, out failure);
            spaceRefused = expanded == null && failure.Contains("not enough executable padding in .text", StringComparison.Ordinal);
            return expanded;
        }

        string? romRootForText = string.IsNullOrWhiteSpace(cfg.ExeFS) ? null : Directory.GetParent(Path.GetFullPath(cfg.ExeFS))?.FullName;
        string? exHeaderForText = romRootForText == null ? null : Path.Combine(romRootForText, "ExHeader.bin");
        byte[]? exHeaderBytes = exHeaderForText != null && File.Exists(exHeaderForText) ? File.ReadAllBytes(exHeaderForText) : null;
        byte[]? result = TryExpand(code, t, CodeGrowth.TextEndFor(exHeaderBytes, code.LongLength), out error, out bool spaceRefused);
        GrowResult? pendingGrowth = null;
        if (result == null && spaceRefused && allowGrowth && GameConfig.AllowGrowth && !UhuruGuard.Active)
        {
            string? exeFsPath = cfg.ExeFS;
            string? romRoot = string.IsNullOrWhiteSpace(exeFsPath)
                ? null : Directory.GetParent(Path.GetFullPath(exeFsPath))?.FullName;
            string? exHeaderPath = romRoot == null ? null : Path.Combine(romRoot, "ExHeader.bin");
            string? ncch0Path = romRoot == null ? null : Path.Combine(romRoot, "HeaderNCCH0.bin");
            if (exHeaderPath == null || !File.Exists(exHeaderPath))
            {
                error += "; ExHeader.bin was not found next to ExeFS, so code.bin could not grow";
                return null;
            }

            byte[] exHeader = File.ReadAllBytes(exHeaderPath);
            byte[]? ncch0 = ncch0Path != null && File.Exists(ncch0Path) ? File.ReadAllBytes(ncch0Path) : null;
            // The editor records the offset journal only after the user approves the code.bin write.
            var grown = CodeGrowth.Grow(code, exHeader, ncch0, (int)needed);
            if (!grown.Ok)
            {
                error += "; code.bin growth refused: " + grown.Error;
                return null;
            }

            var grownTable = TMTable.Locate(grown.Code, out string grownTableError);
            if (grownTable == null)
            {
                error = "TM table was not found after code.bin growth: " + grownTableError;
                return null;
            }
            result = TryExpand(grown.Code, grownTable, CodeGrowth.TextEndFor(grown.ExHeader), out error, out _);
            if (result == null) return null;
            pendingGrowth = grown;
        }
        if (result == null) return null;

        int maxId = ids.Max();
        if (maxId >= (int)ItemCeilingPatcher.RetailCeiling)
        {
            var ceiling = ItemCeilingPatcher.Raise(result, maxId);
            if (!ceiling.Success)
            {
                error = ceiling.Describe();
                return null;
            }
        }

        // Applied, or already pointing at the icon from an earlier run. A refusal leaves NewIcon at 0,
        // or PreviousIcon different from NewIcon.
        var icon = TMIconPatcher.Retarget(result);
        if (!(icon.Applied || (icon.NewIcon != 0 && icon.PreviousIcon == icon.NewIcon)))
        {
            error = icon.Message;
            return null;
        }

        newItemIds = ids.Select(i => (ushort)i).ToArray();
        growth = pendingGrowth;
        return result;
    }
#nullable restore

    /// <summary>
    /// Writes what the new TMs need outside code.bin: their pocket and bag entries in the RomFS, and their names
    /// and move descriptions in the item text. Call it only after code.bin was written.
    /// </summary>
    /// <param name="firstSlot">TM index of <paramref name="newItemIds"/>[0], e.g. 100 for TM101.</param>
    public static void WriteItemData(GameConfig cfg, ushort[] newItemIds, ushort[] newMoves, int firstSlot)
    {
        // Thrown, not returned: the caller reports it. A silent return would leave TM101+ unnamed with no message.
        if (string.IsNullOrWhiteSpace(cfg.RomFS) || !Directory.Exists(cfg.RomFS)) throw new DirectoryNotFoundException("RomFS folder not found");
        if (newItemIds.Length == 0) return;

        int maxItemID = newItemIds.Max();
        ResearchEngine.ApplyExpandedTMItemAttributesPatch(cfg.RomFS, maxItemID, newItemIds);
        ResearchEngine.ApplyExpandedTMBattleBagPatch(cfg.RomFS, maxItemID, newItemIds);
        ExpandedTMText.EnsureCapacity(cfg, maxItemID + 1);

        string[] itemNames = cfg.GetText(TextName.ItemNames);
        string[] itemDescriptions = cfg.GetText(TextName.ItemFlavor);
        string[] moveDescriptions = cfg.GetText(TextName.MoveFlavor);
        for (int i = 0; i < newItemIds.Length; i++)
        {
            int itemId = newItemIds[i];
            if (itemId <= 0 || itemId >= itemNames.Length) continue;
            itemNames[itemId] = GetTMName(firstSlot + i);
            if (itemId < itemDescriptions.Length && i < newMoves.Length && newMoves[i] > 0 && newMoves[i] < moveDescriptions.Length)
                itemDescriptions[itemId] = moveDescriptions[newMoves[i]];
        }

        cfg.SetText(TextName.ItemNames, itemNames);
        cfg.SetText(TextName.ItemFlavor, itemDescriptions);
        cfg.SaveText(TextName.ItemNames);
        cfg.SaveText(TextName.ItemFlavor);
    }
}
