namespace pk3DS.Core;

/// <summary>
/// Uhuru layout: file index = block * baseCount + trainer id (trdata has 5 blocks, trpoke has 7).
/// See docs/uhuru/trainer-format.md.
/// </summary>
public static class TrainerBlocks
{
    /// <summary>Trainers per block, or 0 when the archive counts do not match the Uhuru layout.</summary>
    public static int DetectBaseCount(int names, int trdataCount, int trpokeCount)
        => names > 0 && trdataCount == 5 * names && trpokeCount == 7 * names ? names : 0;

    /// <summary>Name suffix for a trainer in a difficulty block after the first, e.g. " [B1]"; "" otherwise.</summary>
    public static string Label(int index, int baseCount)
        => baseCount <= 0 || index < baseCount ? "" : $" [B{index / baseCount}]";

    public const int TrdataBlocks = 5, TrpokeBlocks = 7;

    /// <summary>Difficulty block of a file index (0 when not Uhuru).</summary>
    public static int Block(int index, int baseCount) => baseCount > 0 ? index / baseCount : 0;

    /// <summary>Trainer id within its block (the index itself when not Uhuru).</summary>
    public static int BaseId(int index, int baseCount) => baseCount > 0 ? index % baseCount : index;

    /// <summary>trdata block holding the record a trpoke block uses: 5 (Inferno) uses 3, 6 (Balanced Inferno) uses 4.</summary>
    public static int TrdataBlockFor(int trpokeBlock) => trpokeBlock switch { 5 => 3, 6 => 4, _ => trpokeBlock };

    /// <summary>Picker names per trpoke block, with the Difficulty Spray modes that load it (trainer-format.md).</summary>
    public static readonly string[] BlockNames =
    [
        "B0 Basic (mode 0)", "B1 Mega/Z Unlocked (mode 1)", "B2 Difficulty Up (mode 2)", "B3 Spicy (modes 3, 5)",
        "B4 Balanced Spicy (mode 4)", "B5 Inferno (mode 6)", "B6 Balanced Inferno (mode 7)",
    ];

    /// <summary>Pokémon in a trpoke file (0x20 bytes each).</summary>
    public static int TeamCount(int fileLength) => fileLength / 0x20;

    /// <summary>Slot 0 byte 0x0C for a block 5/6 team: its length when that differs from the trdata count, else 0
    /// (Uhuru's count hook reads 0x0C when non-zero, else trdata byte 3).</summary>
    public static int TeamSizeOverride(int teamLength, int trdataCount) => teamLength != trdataCount ? teamLength : 0;

    /// <summary>Pokémon World Tournament trainer ids (same in every block), from docs/uhuru/pwt-trainers.txt; a CompetitiveCheck assert keeps them equal.</summary>
    public static readonly int[] UhuruPwtTrainers =
    [
        3, 4, 5, 20, 27, 44, 45, 46, 51, 53, 57, 66, 67, 69, 85, 86,
        87, 91, 103, 107, 123, 127, 128, 129, 130, 131, 140, 142, 143, 144, 145, 146,
        147, 148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162,
        163, 164, 165, 166, 176, 179, 183, 203, 204, 225, 226, 234, 244, 247, 249, 250,
        267, 268, 281, 294, 295, 304, 328, 329, 336, 345, 350, 351, 352, 354, 355, 356,
        357, 358, 359, 360, 370, 377, 378, 385, 387, 389, 396, 397, 398, 399, 400, 401,
        402, 403, 404, 405, 406, 407, 408, 410, 413, 414, 417, 418, 419, 423, 435, 467,
        470, 477, 478, 479, 483, 484, 489, 490, 494, 495, 496, 502, 503, 504, 505, 507,
        508, 523, 541, 542, 543, 546, 558, 559, 560, 562, 572, 573, 580, 623, 644, 645,
        647, 650,
    ];
}
