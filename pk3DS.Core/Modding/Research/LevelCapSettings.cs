#nullable enable

namespace pk3DS.Core.Modding.Research;

public static class LevelCapSettings
{
    private static LevelCapTable? _table;

    public static LevelCapTable Table
    {
        get => _table ??= LevelCapTable.Default();
        set { _table = value; Customised = true; }
    }

    public static bool Customised { get; private set; }

    public static void Reset() { _table = null; Customised = false; }
}
