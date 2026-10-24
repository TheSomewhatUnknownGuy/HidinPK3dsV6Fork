#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Finds free item / move / ability slots for a recipe that needs them.
/// </summary>
public static class RecipeIdAllocator
{
    /// <summary>Placeholder names the games use for a slot that is not really occupied.</summary>
    private static readonly string[] Placeholders = ["???", "-----", "—", "———"];

    /// <summary>Whether an id is unnamed or holds a placeholder.</summary>
    public static bool IsFree(string[]? names, int id)
    {
        if (names == null || id < 1 || id >= names.Length) return false;
        string n = names[id]?.Trim() ?? "";
        return n.Length == 0
            || Array.IndexOf(Placeholders, n) >= 0
            || n.All(c => c is '?' or '？' or '(' or ')');
    }

    /// <summary>Lowest free id not in <paramref name="taken"/>, or -1.</summary>
    public static int NextFree(string[]? names, ISet<int>? taken = null)
    {
        if (names == null) return -1;
        for (int i = 1; i < names.Length; i++)
            if (IsFree(names, i) && (taken == null || !taken.Contains(i))) return i;
        return -1;
    }

    /// <summary>
    /// Start of the lowest run of <paramref name="count"/> consecutive free ids, or -1.
    /// </summary>
    public static int NextFreeRun(string[]? names, int count, ISet<int>? taken = null)
    {
        if (names == null || count <= 0) return -1;
        if (count == 1) return NextFree(names, taken);

        bool Free(int i) => IsFree(names, i) && (taken == null || !taken.Contains(i));
        for (int i = 1; i + count <= names.Length; i++)
        {
            bool all = true;
            for (int k = 0; k < count && all; k++)
                if (!Free(i + k)) all = false;
            if (all) return i;
        }
        return -1;
    }

    /// <summary>The name table a mechanic kind's ids live in.</summary>
    public static string[]? TableFor(GameConfig config, CustomMechanicKind kind) => kind switch
    {
        CustomMechanicKind.Move => config?.GetText(TextName.MoveNames),
        CustomMechanicKind.Ability => config?.GetText(TextName.AbilityNames),
        _ => config?.GetText(TextName.ItemNames),
    };

    /// <summary>The name table a package parameter's ids live in.</summary>
    public static string[]? TableForParameter(GameConfig config, string? type) => (type ?? "").ToLowerInvariant() switch
    {
        "move" => config?.GetText(TextName.MoveNames),
        "ability" => config?.GetText(TextName.AbilityNames),
        _ => config?.GetText(TextName.ItemNames),
    };

    /// <summary>
    /// Assigns every id a recipe needs, and reports what could not be placed.
    /// </summary>
    /// <param name="reserved">
    /// Ids already spoken for elsewhere - typically by other recipes in the same batch, whose own
    /// writes have not reached the id-name table on disk yet (a dry run never does; a commit does,
    /// but only after this call returns). Every id this call claims is added to it, so passing the
    /// same set into consecutive calls keeps a whole batch from handing out the same "free" id twice.
    /// </param>
    public static Dictionary<string, string>? AssignIds(
        Recipe recipe, GameConfig config, List<string> problems, ISet<int>? reserved = null)
    {
        if (recipe == null || config == null) return null;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ps = recipe.Package?.Parameters;

        if (ps is { Count: > 0 })
        {
            var taken = reserved ?? new HashSet<int>();
            // IsFree treats a blank or "???" name as free, but Uhuru keeps data in some of those item ids.
            if (UhuruGuard.Active)
            {
                taken.UnionWith(UhuruGuard.ProtectedPocketItems);
                taken.UnionWith(UhuruGuard.ProtectedBattleBagItems);
            }

            // List parameters first: each position's MEANING is fixed (the recipe's own code/data
            // indexes slot 0, slot 1, ... positionally - TeraShard's slot 0 is baked in as the
            // Normal-type handler, slot 1 Fighting, and so on), but the actual id VALUE at each
            // position does not have to be the literal authored default - only genuinely free and
            // distinct from everything else this install claims. Trusting the authored default
            // outright went stale for real: HiddenPowerItems and TeraShard both hardcoded 885+ as
            // "a free run," but this ROM's own Expansion Pack content already occupies part of that
            // range (885 = Fighting Pulse Shard) - the package failed outright instead of simply
            // using different, actually-free slots. Keep the authored id per-position when it is
            // still genuinely free and unclaimed (preserves existing saves/behavior when nothing
            // conflicts); fall back to the next real free id, slot by slot, otherwise.
            foreach (var (key, p) in ps)
            {
                if (!string.Equals(p?.Type, "list", StringComparison.OrdinalIgnoreCase)) continue;

                // Same rule as single ids below: AllowUnnamed = false means the values are fixed
                // references, not slots to fill (CenterMoveTutor's script ids are not item ids, and
                // swapping them for free item numbers points every Center NPC at a garbage script).
                if (p?.AllowUnnamed == false)
                {
                    values[key] = p.Default ?? "";
                    continue;
                }

                var table = TableForParameter(config, p?.Type);
                var parts = (p?.Default ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var resolved = new List<string>(parts.Length);
                foreach (string part in parts)
                {
                    int.TryParse(part, out int authored);
                    int id = (authored > 0 && IsFree(table, authored) && !taken.Contains(authored))
                        ? authored : NextFree(table, taken);
                    if (id <= 0) { problems.Add($"no free slot for '{key}'"); return null; }
                    taken.Add(id);
                    resolved.Add(id.ToString());
                }
                values[key] = string.Join(",", resolved);
            }

            foreach (var (key, p) in ps)
            {
                if (string.Equals(p?.Type, "list", StringComparison.OrdinalIgnoreCase)) continue;
                var table = TableForParameter(config, p?.Type);

                // Keep the authored default when it is genuinely free on THIS ROM and unclaimed.
                if (int.TryParse(p?.Default, out int def) && IsFree(table, def) && !taken.Contains(def))
                {
                    values[key] = def.ToString();
                    taken.Add(def);
                    continue;
                }

                // A parameter pointing at something that must already exist (AllowUnnamed = false)
                // keeps its default: substituting a blank slot would break a working reference.
                if (p?.AllowUnnamed == false)
                {
                    values[key] = p?.Default ?? "";
                    continue;
                }

                int free = NextFree(table, taken);
                if (free <= 0) { problems.Add($"no free slot for '{key}'"); return null; }
                values[key] = free.ToString();
                taken.Add(free);
            }
            return values;
        }

        // Not package-driven: the recipe's own entries need a consecutive run.
        if (recipe.SlotCount > 0)
        {
            if (UhuruGuard.Active && recipe.Kind == CustomMechanicKind.Item)
            {
                reserved ??= new HashSet<int>();
                reserved.UnionWith(UhuruGuard.ProtectedPocketItems);
                reserved.UnionWith(UhuruGuard.ProtectedBattleBagItems);
            }
            int start = NextFreeRun(TableFor(config, recipe.Kind), recipe.SlotCount, reserved);
            if (start <= 0)
            {
                problems.Add($"no run of {recipe.SlotCount} consecutive free id(s)");
                return null;
            }
            for (int i = 0; i < recipe.Entries.Count; i++)
            {
                recipe.Entries[i].Id = start + i;
                reserved?.Add(start + i);
            }
        }

        return values;
    }
}
