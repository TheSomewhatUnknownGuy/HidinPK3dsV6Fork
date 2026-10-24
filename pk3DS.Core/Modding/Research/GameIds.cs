#nullable enable

using System.Collections.Generic;

namespace pk3DS.Core.Modding.Research;

public sealed record IdChoice(int Value, string Name, bool Confirmed = true)
{
    public override string ToString() => Confirmed ? $"{Value} - {Name}" : $"{Value} - {Name} (unconfirmed)";
}

public static class GameIds
{
    public static readonly IReadOnlyList<IdChoice> Types =
    [
        new(0, "Normal"), new(1, "Fighting"), new(2, "Flying"), new(3, "Poison"),
        new(4, "Ground"), new(5, "Rock"), new(6, "Bug"), new(7, "Ghost"),
        new(8, "Steel"), new(9, "Fire"), new(10, "Water"), new(11, "Grass"),
        new(12, "Electric"), new(13, "Psychic"), new(14, "Ice"), new(15, "Dragon"),
        new(16, "Dark"), new(17, "Fairy"),
    ];

    public static readonly IReadOnlyList<IdChoice> Weather =
    [
        new(0, "None / clear"),
        new(1, "Harsh sunlight"),
        new(2, "Rain"),
        new(3, "Hail / snow"),
        new(4, "Sandstorm"),
        new(5, "Heavy rain (Primordial Sea)"),
        new(6, "Extremely harsh sun (Desolate Land)"),
        new(7, "Strong winds (Delta Stream)", false),
    ];

    public static readonly IReadOnlyList<IdChoice> Terrain =
    [
        new(0, "None"),
        new(1, "Grassy Terrain"),
        new(2, "Misty Terrain"),
        new(3, "Electric Terrain"),
        new(4, "Psychic Terrain"),
    ];

    public static readonly IReadOnlyList<IdChoice> Status =
    [
        new(0, "None"),
        new(1, "Paralysis", false),
        new(2, "Sleep", false),
        new(3, "Freeze / frostbite"),
        new(4, "Burn"),
        new(5, "Poison", false),
        new(6, "Badly poisoned", false),
    ];

    public static readonly IReadOnlyList<IdChoice> Category =
    [
        new(0, "Status"), new(1, "Physical"), new(2, "Special"),
    ];

    public static readonly IReadOnlyList<IdChoice> Stats =
    [
        new(0, "HP"), new(1, "Attack"), new(2, "Defense"),
        new(3, "Sp. Attack"), new(4, "Sp. Defense"), new(5, "Speed"),
        new(6, "Accuracy", false), new(7, "Evasion", false),
    ];

    public static IReadOnlyList<IdChoice>? For(string? key) => (key ?? "").ToUpperInvariant() switch
    {
        "WEATHER" => Weather,
        "TERRAIN" or "FIELD" or "FIELDEFFECT" => Terrain,
        "TYPE" or "MOVETYPE" or "NEWTYPE" or "ATTACKTYPE" => Types,
        "STATUS" or "AILMENT" or "INFLICT" => Status,
        "CATEGORY" => Category,
        "STAT" or "STATINDEX" => Stats,
        _ => null,
    };
}
