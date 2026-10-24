using System;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

public static class TMIconPatcher
{
    public const uint StatusTMIcon = 312;

    public const uint PhysicalTMIcon = 310;

    public const uint SpecialTMIcon = 311;

    public const uint BlankIcon = 0x300;

    private const int FallbackOffset = 0x4B9B54;

    private const uint ExpectedFallback = 0xE3A01C03;

    public sealed class Result
    {
        public bool Applied { get; init; }
        public string Message { get; init; } = "";
        public uint PreviousIcon { get; init; }
        public uint NewIcon { get; init; }
        public override string ToString() => Message;
    }

    public static Result Retarget(byte[] code, uint icon = StatusTMIcon)
    {
        if (code == null || FallbackOffset + 4 > code.Length)
            return new Result { Message = "code binary too small to contain the icon lookup" };

        uint current = BitConverter.ToUInt32(code, FallbackOffset);
        if (current != ExpectedFallback)
        {
            // Already retargeted by a previous run is not a failure; anything else is.
            if ((current & 0xFFFFF000) == (ExpectedFallback & 0xFFFFF000))
            {
                uint existing = ARMCodec.DecodeImm8r4((current >> 8) & 0xF, current & 0xFF);
                return existing == icon
                    ? new Result { Applied = false, PreviousIcon = existing, NewIcon = icon, Message = $"icon fallback already points at {icon}" }
                    : new Result { Applied = false, PreviousIcon = existing, NewIcon = icon, Message = $"icon fallback holds an unexpected value ({existing}); left alone" };
            }
            return new Result { Message = $"no icon fallback at 0x{FallbackOffset:X}; this build's lookup differs" };
        }

        var enc = ARMCodec.EncodeImm8r4(icon);
        if (enc == null || ARMCodec.DecodeImm8r4(enc.Value.rot, enc.Value.imm8) != icon)
            return new Result { Message = $"icon {icon} is not encodable as an ARM immediate" };

        uint rebuilt = (current & 0xFFFFF000) | ((enc.Value.rot & 0xF) << 8) | (enc.Value.imm8 & 0xFF);
        if (ARMCodec.DecodeImm8r4((rebuilt >> 8) & 0xF, rebuilt & 0xFF) != icon)
            return new Result { Message = $"rebuilt instruction does not decode to {icon}; refused" };

        BitConverter.GetBytes(rebuilt).CopyTo(code, FallbackOffset);
        return new Result
        {
            Applied = true,
            PreviousIcon = BlankIcon,
            NewIcon = icon,
            Message = $"TM101-128 icon fallback: {BlankIcon} (blank) -> {icon}",
        };
    }
}
