using System;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// The 9th (last) file in each species' model group within the `models` GARC (a094) - confirmed
/// live against real ROM data (Bulbasaur/Venusaur/Charizard/Snorlax/Magikarp/Mewtwo) to be a small,
/// 120-byte container: a 4-byte magic ("PC" + `0x0002`), then three `u32` sub-section offsets
/// (always 0x10/0x70/0x78 in every real sample checked - the file's own total length always equals
/// the third), a "section 1" block of mostly small integer counts and several floats that read as
/// plausible model bounding-box extents, and an always-zero trailing 8 bytes.
/// <para>
/// A historical note in this project's own planning assumed <c>Height</c>/<c>HeightModifier</c>/
/// <c>FieldHeightModifier</c> floats sat at 0x10/0x14/0x18 - checked directly against real data this
/// session and confirmed WRONG: those three words are always exactly 16/112/120 (the section-offset
/// header just described), never floats, identically across every species checked. The one field in
/// this file that IS confirmed real - a clean, small, positive float, distinct from the surrounding
/// noise - sits at 0x38 instead, and its value tracks each species' actual relative size sensibly
/// (Snorlax 1.79, Magikarp 1.17, Bulbasaur 0.9, Mewtwo 0.6, Venusaur 0.58, Charizard 0.55) - this is
/// exposed here as <see cref="ModelHeight"/>. The rest of the file (bounding-box-shaped floats
/// around 0x20-0x34, a couple of small trailing fields around 0x3C-0x48) is left unexposed rather
/// than guessed at without the same kind of real-data confirmation.
/// </para>
/// </summary>
public sealed class ModelSettingsFile
{
    public const int ModelHeightOffset = 0x38;
    public const int MinLength = ModelHeightOffset + 4;

    public byte[] Raw;

    public static ModelSettingsFile Decode(byte[] rawMember)
    {
        byte[] data = rawMember.Length > 0 && rawMember[0] == 0x11 ? LZSS.Decompress(rawMember) : rawMember;
        return new ModelSettingsFile { Raw = (byte[])data.Clone() };
    }

    /// <summary>Re-encodes back to a raw GARC member - always uncompressed (every real sample checked already was), which every decoder in this codebase already handles transparently.</summary>
    public byte[] Encode() => Raw;

    public bool HasModelHeight => Raw.Length >= MinLength;

    public float ModelHeight
    {
        get => BitConverter.ToSingle(Raw, ModelHeightOffset);
        set => BitConverter.GetBytes(value).CopyTo(Raw, ModelHeightOffset);
    }
}
