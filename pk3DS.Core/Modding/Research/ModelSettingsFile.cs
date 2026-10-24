using System;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

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

    public byte[] Encode() => Raw;

    public bool HasModelHeight => Raw.Length >= MinLength;

    public float ModelHeight
    {
        get => BitConverter.ToSingle(Raw, ModelHeightOffset);
        set => BitConverter.GetBytes(value).CopyTo(Raw, ModelHeightOffset);
    }
}
