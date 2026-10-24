using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core;

// Big thanks to FireFly for figuring out the 7/6-bit compression routine for scripts.
public static class Scripts
{
    // Decompression - Deprecated: Use FireFly's method.
    internal static byte[] DecompressScript(byte[] data)
    {
        data ??= []; // Bad Input

        using var mn = new MemoryStream();
        using var bw = new BinaryWriter(mn);
        // Read away!
        int pos = 0;
        while (pos < data.Length)
        {
            // Read until the top bit is not set
            byte[] cb = ReadCompressed(data, pos);
            pos += cb.Length;
            // Interpret the bytecode
            byte[] db = DecompressBytes(cb);
            // Write Bytes
            bw.Write(db);
        }
        return mn.ToArray();
    }

    internal static byte[] ReadCompressed(byte[] data, int pos)
    {
        byte[] c1 = data.Skip(pos).TakeWhile(b => b >> 7 > 0).ToArray(); // Take while >= 0x80
        return [.. c1, .. data.Skip(pos + c1.Length).Take(1)]; // Take another
    }

    internal static byte[] DecompressBytes(byte[] cb)
    {
        byte[] db = [];

        if ((cb[0] & 0x40) > 0) // Signed Parameter
        {
            // Check the next bytecode
            if (cb.Length > 1 && cb[1] >> 7 > 0) // Many-bits-required command
            {
                // 2 Byte Signed Parameter
                int cmd = (cb[0] & 0x3 << 14) | (cb[1] & 0x7F << 7) | cb[2]; // 16 Bits total
                db = [.. db, .. BitConverter.GetBytes(cmd).Take(2)]; // 16 Bits

                int dev = ((cb[0] & 0x3F) - 0x40) >> 2; // Lowest 2 bits have already been used for the command
                db = [.. db, .. BitConverter.GetBytes(dev).Take(2)]; // 16 Bits
            }
            else if (cb[0] >> 7 > 0) // Signed Command
            {
                // 3 Byte Signed Parameter
                int cmd = (cb[0] << 7) | cb[1];
                db = [.. db, .. BitConverter.GetBytes(cmd).Take(1)]; // 8 Bits Total

                int dev = ((cb[0] & 0x3F) - 0x40) >> 1; // Lowest bit has already been used for the command
                db = [.. db, .. BitConverter.GetBytes(dev).Take(3)]; // 24 Bits
            }
            else // Signed Value
            {
                // 4 Byte Signed Parameter
                int dev = ((cb[0] & 0x3F) - 0x40) >> 0; // No bits have already been used; no command
                db = [.. db, .. BitConverter.GetBytes(dev).Take(4)]; // 32 Bits
            }
        }
        else if (cb[0] >> 7 > 0) // Manybit
        {
            Array.Reverse(cb);
            int cmd = 0;
            for (int i = 0; i < cb.Length; i++)
                cmd |= (cb[i] & 0x7F) << (7 * i);

            db = [.. db, .. BitConverter.GetBytes((uint)cmd).Take(4)];
        }
        else // Literal
        {
            db = [.. db, .. BitConverter.GetBytes((uint)cb[0]).Take(4)];
        }
        return db;
    }
    // FireFly's (github.com/FireyFly) concise decompression (ported c->c#):
    // https://github.com/FireyFly/poketools/blob/e74538a5b5e5dab1e78c1cd313c55d158f37534d/src/formats/script.c#L61
    internal static uint[] QuickDecompress(byte[] data, int count)
    {
        uint[] code = new uint[count];
        uint i = 0, j = 0, x = 0, f = 0;
        while (i < code.Length)
        {
            int b = data[f++],
                v = b & 0x7F;
            if (++j == 1) // sign extension possible
                x = (uint)((((v >> 6 == 0 ? 1 : 0) - 1) << 6) | v); // only for bit6 being set
            else x = (x << 7) | (byte)v; // shift data into place

            if ((b & 0x80) != 0) continue; // more data to read
            code[i++] = x; j = 0; // write finalized instruction
        }
        return code;
    }

    // Compression
    internal static byte[] CompressScript(byte[] data)
    {
        if (data == null || data.Length % 4 != 0) // Bad Input
            return null;
        using var mn = new MemoryStream();
        using var bw = new BinaryWriter(mn);
        for (int pos = 0; pos < data.Length; pos += 4)
            bw.Write(CompressValue(BitConverter.ToInt32(data, pos)));
        return mn.ToArray();
    }

    internal static byte[] CompressValue(int value)
    {
        int n = 1;
        while (true)
        {
            int shifted = value >> (7 * (n - 1));
            if (shifted is >= -64 and <= 63) break; // fits as this group's sign-extended 7 bits
            n++;
        }
        byte[] result = new byte[n];
        for (int i = 0; i < n; i++)
        {
            int shiftAmt = 7 * (n - 1 - i);
            byte g = (byte)((value >> shiftAmt) & 0x7F);
            result[i] = (byte)(g | (i == n - 1 ? 0x00 : 0x80));
        }
        return result;
    }

    // General Utility
    public static string[] GetHexLines(byte[] data, int count = 4)
    {
        data ??= [];
        // Generates an x-byte wide space separated string array; leftovers included at the end.
        string[] s = new string[(data.Length / count) + (data.Length % count > 0 ? 1 : 0)];
        for (int i = 0; i < s.Length; i++)
            s[i] = BitConverter.ToString(data.Skip(i * count).Take(count).ToArray()).Replace('-', ' ');
        return s;
    }

    public static string[] GetHexLines(uint[] data)
    {
        data ??= [];
        // Generates an 4-byte wide space separated string array.
        string[] s = new string[data.Length];
        for (int i = 0; i < s.Length; i++)
            s[i] = BitConverter.ToString(BitConverter.GetBytes(data[i])).Replace('-', ' ');
        return s;
    }

    internal static byte[] GetBytes(uint[] data)
    {
        return data.Aggregate(Array.Empty<byte>(), (current, t) => [.. current, .. BitConverter.GetBytes(t)]);
    }

    // Interpreting
    public static string[] ParseScript(uint[] cmd, int sanity = -1, uint[] nativeHashes = null)
    {
        // sub_148CBC Moon v1.0
        List<string> parse = [];
        int sanityMode = 0;

        int i = 0; // Current Offset of decompressed instructions
        while (i < cmd.Length) // read away
        {
            // Read a Command
            int line = i;
            uint c = cmd[i++];

            string op;
            switch (c & 0x7FFF)
            {
                default:
                    throw new ArgumentException("Invalid Command ID");
                case 0x01:
                case 0x02:
                case 0x05:
                case 0x06:
                case 0x0F:
                case 0x10:
                case 0x13:
                case 0x14:
                case 0x6D:
                case 0x72:
                {
                    // Peek at next value
                    var next = (int)cmd[i++];
                    // Check Value against negative and zero... ?

                    op = UnkeA(c, next);
                    break;
                }
                case 0x03:
                case 0x04:
                case 0x07:
                case 0x08:
                case 0x11:
                case 0x12:
                case 0x15:
                case 0x16:
                case 0x6E:
                case 0x73:
                {
                    // Peek at next value
                    var next = (int)cmd[i++];
                    // Check Value against negative... ?

                    op = UnkeA(c, next);
                    break;
                }
                case 0x09:
                case 0x17:
                case 0x19:
                case 0x1B:
                case 0x21:
                case 0x22:
                case 0x23:
                case 0x24:
                case 0x25:
                case 0x2A:
                case 0x2B:
                case 0x2E: // Begin
                case 0x2F:
                case 0x30: // Return
                case 0x41:
                case 0x42:
                case 0x43:
                case 0x48:
                case 0x49:
                case 0x4A:
                case 0x4B:
                case 0x4C:
                case 0x4D:
                case 0x4E: // Add?
                case 0x4F:
                case 0x50:
                case 0x51: // Cmp?
                case 0x52:
                case 0x53:
                case 0x54:
                case 0x55:
                case 0x56:
                case 0x59: // ClearAll
                case 0x5A:
                case 0x5D:
                case 0x5E:
                case 0x5F:
                case 0x60:
                case 0x61:
                case 0x62:
                case 0x63:
                case 0x64:
                case 0x65:
                case 0x66:
                case 0x67:
                case 0x68:
                case 0x6B:
                case 0x6C:
                case 0x6F:
                case 0x70:
                case 0x71:
                case 0x74:
                case 0x7A:
                case 0x83:
                case 0x84:
                case 0x86:
                case 0x89: // LineNo?
                case 0xAA:
                case 0xAB: // PushConst2
                case 0xAC: // CmpConst2
                case 0xAD:
                case 0xAE:
                case 0xB7:
                case 0xB8:
                case 0xB9:
                case 0xBA:
                case 0xBB:
                case 0xBC: // PushConst
                case 0xBD:
                case 0xBE:
                case 0xBF: // AdjustStack
                case 0xC0:
                case 0xC1:
                case 0xC2:
                case 0xC3:
                case 0xC4:
                case 0xC5:
                case 0xC6:
                case 0xC7:
                case 0xC8: // CmpLocal
                case 0xC9: // CmpConst
                case 0xCA:
                case 0xCF:
                case 0xD0:
                case 0xD1:
                case 0xD2:
                case 0xD3:
                case 0xD4:
                {
                    // no sanity checks
                    var arg = (short)(c >> 16);

                    op = UnkeA(c & 0xFF, arg);

                    if ((c & 0xFF) == 0x30) // return
                        op += Environment.NewLine;
                    break;
                }
                case 0x0A:
                case 0x0B:
                case 0x0C:
                case 0x0D:
                case 0x0E:
                case 0x18:
                case 0x1A:
                case 0x1C:
                case 0x1D:
                case 0x1E:
                case 0x1F:
                case 0x20:
                case 0x26:
                case 0x27: // PushConst
                case 0x28:
                case 0x29:
                case 0x2C:
                case 0x2D:
                case 0x34:
                case 0x44:
                case 0x45:
                case 0x46:
                case 0x47:
                case 0x57:
                case 0x58:
                case 0x5B:
                case 0x5C:
                case 0x69:
                case 0x6A:
                case 0x75:
                case 0x76:
                case 0x77:
                case 0x78:
                case 0x79:
                case 0x85:
                {
                    var next = (int)cmd[i++];
                    // No sanity check needed

                    op = UnkeA(c, next);
                    break;
                }

                case 0x31: // CallFunc
                case 0x33:
                case 0x35: // Jump!=
                case 0x36: // Jump==
                case 0x37:
                case 0x38:
                case 0x39:
                case 0x3A:
                case 0x3B:
                case 0x3C:
                case 0x3D:
                case 0x3E:
                case 0x3F:
                case 0x40:
                case 0x81: // Jump
                {
                    var delta = (int)cmd[i++];
                    // sanity check range...
                    // negative.. weird

                    int newOfs = (line * 4) + delta;
                    op = $"{Commands[c]} => 0x{newOfs:X4} ({delta})";
                    break;
                }
                case 0x7B:
                {
                    var next = (int)cmd[i++];
                    sanityMode |= 1;                       // flag mode 1

                    op = UnkeA(c, next);
                    break;
                }
                case 0x82: // JumpIfElse
                {
                    //var jOffset = (i * 4) - 4; // todo: this may be the correct jump start point...
                    var count = cmd[i++]; // switch case table
                    // sanity check

                    // Populate If-Case Tree
                    var tree = new List<string>();

                    // Cases
                    for (int j = 0; j < count; j++)
                    {
                        var jmp = (int)cmd[i++];
                        var toOffset = ((i - 2) * 4) + jmp;
                        var ifValue = (int)cmd[i++];
                        tree.Add($"\t{ifValue} => 0x{toOffset:X4} ({jmp})");
                    }
                    // Default
                    {
                        int jmp = (int)cmd[i++];
                        var toOffset = ((i - 2) * 4) + jmp;
                        tree.Add($"\t* => 0x{toOffset:X4} ({jmp})");
                    }

                    op = Commands[c] + Environment.NewLine + string.Join(Environment.NewLine, tree);
                    break;
                }
                case 0x87: // SYSREQ.N - "DoCommand?" - call a native by this script's own table index
                {
                    var next1 = (int)cmd[i++];
                    var next2 = (int)cmd[i++];
                    sanityMode |= 2;                       // flag mode 2

                    string native = AmxNatives.Resolve(nativeHashes, next1);
                    op = native != null
                        ? $"{Commands[c]}({native}, {next2})"
                        : UnkeA(c, next1, next2);
                    break;
                }

                case 0x8A:
                case 0x8B:
                case 0x8C:
                case 0x8D:
                case 0x9C:
                case 0x9D:
                {
                    var next1 = (int)cmd[i++];
                    var next2 = (int)cmd[i++];

                    op = UnkeA(c, next1, next2);
                    break;
                }

                case 0x8E:
                case 0x8F:
                case 0x90:
                case 0x91:
                {
                    var next1 = cmd[i++];
                    var next2 = cmd[i++];
                    var next3 = cmd[i++];

                    op = UnkeF(c, next1, next2, next3);
                    break;
                }

                case 0x92:
                case 0x93:
                case 0x94:
                case 0x95:
                {
                    var next1 = (int)cmd[i++];
                    var next2 = (int)cmd[i++];
                    var next3 = (int)cmd[i++];
                    var next4 = (int)cmd[i++];

                    op = UnkeA(c, next1, next2, next3, next4);
                    break;
                }

                case 0x96: // float
                case 0x97:
                case 0x98:
                case 0x99:
                {
                    var next1 = cmd[i++];
                    var next2 = cmd[i++];
                    var next3 = cmd[i++];
                    var next4 = cmd[i++];
                    var next5 = cmd[i++];

                    op = UnkeF(c, next1, next2, next3, next4, next5);
                    break;
                }

                case 0x9A:
                {
                    var next1 = (int)cmd[i++];
                    var next2 = (int)cmd[i++];
                    // a bunch of sanity checking

                    op = UnkeA(c, next1, next2);
                    break;
                }

                case 0x9B: // Copy
                {
                    var next1 = (int)cmd[i++];
                    var next2 = (int)cmd[i++];
                    // a bunch of sanity checking

                    op = UnkeA(c, next1, next2);
                    break;
                }

                case 0x9E:
                {
                    var next1 = (int)cmd[i++];
                    // perm check a1 + 0x14
                    // can return error code 0x1C

                    op = UnkeA(c, next1);
                    break;
                }

                case 0x9F:
                {
                    // perm check a1 + 0x14
                    // same permission checking as 0x9E
                    // can return error code 0x1C

                    op = UnkeA(c);
                    break;
                }

                case 0xA1: // Goto
                {
                    // minimal sanity checks
                    // can return error code 0x1C
                    int newPos = i + (int)(1 + (2 * (cmd[i] / 4)) + 1);

                    op = UnkeA(c, newPos);
                    break;
                }

                case 0xA2: // GetGlobal2
                case 0xA3: // GetGlobal
                case 0xA6:
                case 0xA7:
                case 0xAF: // SetGlobal
                case 0xB0:
                case 0xB3:
                case 0xB4:
                case 0xCB:
                case 0xCD:
                {
                    // sanity check arg
                    var arg = (short)(c >> 16);

                    op = UnkeA(c & 0xFF, arg);
                    break;
                }
                case 0xA4: // GetGlobal4
                case 0xA5:
                case 0xA8:
                case 0xA9:
                case 0xB1: // SetLocal
                case 0xB2:
                case 0xB5:
                case 0xB6:
                case 0xCC:
                case 0xCE:
                {
                    // sanity check arg, slightly different
                    var arg = (short)(c >> 16);

                    op = UnkeA(c & 0xFF, arg);
                    break;
                }
            }
            parse.Add($"0x{line * 4:X4}: [{c & 0x7FF:X2}] {op}");
        }

        if (sanity >= 0 && sanity != sanityMode)
            throw new ArgumentException();

        return [.. parse];
    }

    internal static string[] ParseMovement(uint[] cmd)
    {
        return GetHexLines(cmd);
    }

    internal static string UnkeA(uint c, params int[] arr)
    {
        string cmd = Commands[c];
        // `Math.Abs` throws on `int.MinValue` (its positive counterpart doesn't fit in an int) -
        // confirmed to actually occur in 2 of this ROM's real scripts, not just a theoretical edge
        // case. `long` sidesteps it without changing the display format for every normal value.
        string parameters = arr.Length == 0 ? "" : string.Join(", ", arr.Select(z => $"{(Math.Abs((long)z) < 100 ? z.ToString() : "0x" + z.ToString("X4"))}"));
        return $"{cmd}({parameters})";
    }

    private static readonly Func<uint, float> getFloat = val => BitConverter.ToSingle(BitConverter.GetBytes(val), 0);

    internal static string UnkeF(uint c, params uint[] arr)
    {
        string cmd = Commands[c];
        string parameters = arr.Length == 1 ? "" : string.Join(", ", arr.Select(z => getFloat(z)));
        return $"{cmd}({parameters})";
    }

    // Opcode names below marked "Pawn: X" were derived by cross-checking this ROM's own real AMX
    // scripts against the public AMX/Pawn virtual machine's documented opcode list (as used by
    // SA-MP/CTRMap tooling - the same lineage the user's own linked references trace back to). The
    // method: canonical Pawn opcode VALUES (LOAD.pri=1, PUSH.C=39, PROC=46, RETN=48, CALL=49, ...)
    // were checked against this dialect's numbering by position, then confirmed independently by
    // decoding every real occurrence of that opcode across all 759 scripts in this ROM (1,392,755
    // instructions total) and checking whether its argument's observed shape matches what that Pawn
    // opcode should have - e.g. opcodes Pawn documents as taking NO operand (LOAD.I, MOVE.pri, ADD,
    // etc.) were confirmed to have an argument value of exactly 0 in EVERY real occurrence, never
    // varying, which a real operand could not do; opcodes documented as taking an arbitrary constant
    // (CONST.pri, PUSH.C) showed wide, arbitrary value spreads across thousands of real occurrences;
    // conditional-jump opcodes showed the expected all-positive-or-symmetric offset ranges; 0x81 and
    // 0x82 were confirmed to occur in IDENTICAL counts (3512 each) everywhere in the corpus, matching
    // Pawn's SWITCH+CASETBL always appearing as an adjacent pair.
    //
    // CORRECTED 2026-09-13: the note this replaces claimed "canonical Pawn only documents opcodes up
    // to 134 (NOP)... anything above 0x86 has no possible Pawn reference to check against". That
    // assumption was wrong - XAMX (github.com/BlastSlimey/XAMX, copyright BlastSlimey, GPL v3, a real
    // Pokemon XY/ORAS/SM/USUM AMX dis/reassembler) documents the full 215-opcode Pawn set this
    // dialect's own numbering was built from, all the way to 0xD6. What IS true, confirmed directly
    // against this codebase's own ParseScript switch below rather than assumed: opcodes 0x00-0x87
    // follow that documented numbering exactly (independently cross-checked three ways already -
    // 0x81/SWITCH, 0x82/CASETBL, 0x87/SYSREQ_N all matched real script behaviour before this comment
    // was even touched), so every "$XX" placeholder in that range below is now filled from XAMX's
    // table. Opcodes 0x88 and up are a DIFFERENT story: this dialect's own switch case for 0xAA-0xD4
    // (see below) packs its argument into the SAME word's upper 16 bits, not a separate following
    // word the way XAMX's "param"-tagged high-range opcodes (PUSH2_C, STACK_P, etc.) are documented -
    // proof this game's compiler repurposed the high opcode space with its own encoding rather than
    // continuing vanilla Pawn's. XAMX's names for 0x88+ do not apply here and must not be copied in;
    // naming those still needs real behavioural analysis, exactly as the original note said - just
    // not because no reference exists, but because the one that does exist is for a different thing.
    internal static readonly Dictionary<uint, string> Commands = new()
    {// { 0x00, "$00" }, // Invalid Code
        { 0x01, "LoadPri" }, // XAMX: OP_LOAD_PRI
        { 0x02, "LoadAlt" }, // XAMX: OP_LOAD_ALT
        { 0x03, "LoadSPri" }, // XAMX: OP_LOAD_S_PRI
        { 0x04, "LoadSAlt" }, // XAMX: OP_LOAD_S_ALT
        { 0x05, "LRefPri" }, // XAMX: OP_LREF_PRI
        { 0x06, "LRefAlt" }, // XAMX: OP_LREF_ALT
        { 0x07, "LRefSPri" }, // XAMX: OP_LREF_S_PRI
        { 0x08, "LRefSAlt" }, // XAMX: OP_LREF_S_ALT
        { 0x09, "LoadIndirect" }, // Pawn: LOAD.I - confirmed always-zero arg across 3083 real occurrences
        { 0x0A, "LoadByteIndirect" }, // XAMX: OP_LODB_I
        { 0x0B, "ConstPri" }, // Pawn: CONST.pri - 328 distinct values across 6698 occurrences, matches an arbitrary constant
        { 0x0C, "ConstAlt" }, // Pawn: CONST.alt
        { 0x0D, "AddrPri" }, // XAMX: OP_ADDR_PRI
        { 0x0E, "AddrAlt" }, // Pawn: ADDR.alt - 3086 occurrences, all negative multiples of 4 (frame-relative local offsets)
        { 0x0F, "StorPri" }, // XAMX: OP_STOR_PRI
        { 0x10, "StorAlt" }, // XAMX: OP_STOR_ALT
        { 0x11, "StorSPri" }, // XAMX: OP_STOR_S_PRI
        { 0x12, "StorSAlt" }, // XAMX: OP_STOR_S_ALT
        { 0x13, "SRefPri" }, // XAMX: OP_SREF_PRI
        { 0x14, "SRefAlt" }, // XAMX: OP_SREF_ALT
        { 0x15, "SRefSPri" }, // XAMX: OP_SREF_S_PRI
        { 0x16, "SRefSAlt" }, // XAMX: OP_SREF_S_ALT
        { 0x17, "StoreIndirect" }, // Pawn: STOR.I - confirmed always-zero arg across 2223 occurrences
        { 0x18, "StoreByteIndirect" }, // XAMX: OP_STRB_I
        { 0x19, "LoadIndexed" }, // XAMX: OP_LIDX
        { 0x1A, "LoadIndexedByte" }, // XAMX: OP_LIDX_B
        { 0x1B, "IndexAddr" }, // XAMX: OP_IDXADDR
        { 0x1C, "IndexAddrByte" }, // XAMX: OP_IDXADDR_B
        { 0x1D, "AlignPri" }, // XAMX: OP_ALIGN_PRI
        { 0x1E, "AlignAlt" }, // XAMX: OP_ALIGN_ALT
        { 0x1F, "LCtrl" }, // Pawn: LCTRL - always arg=5 (FRM) across 3040 occurrences
        { 0x20, "SCtrl" }, // Pawn: SCTRL - always arg=4 (STK) across 3040 occurrences, paired 1:1 with LCtrl above
        { 0x21, "MovePri" }, // Pawn: MOVE.pri - confirmed always-zero arg
        { 0x22, "MoveAlt" }, // Pawn: MOVE.alt
        { 0x23, "Xchg" }, // Pawn: XCHG
        { 0x24, "PushPri" }, // Pawn: PUSH.pri
        { 0x25, "PushAlt" }, // Pawn: PUSH.alt
        { 0x26, "Pick" }, // XAMX: OP_PICK
        { 0x27, "PushConst" },
        { 0x28, "Push" }, // XAMX: OP_PUSH
        { 0x29, "PushS" }, // XAMX: OP_PUSH_S
        { 0x2A, "PopPri" }, // Pawn: POP.pri
        { 0x2B, "PopAlt" }, // Pawn: POP.alt
        { 0x2C, "Stack" }, // XAMX: OP_STACK
        { 0x2D, "Heap" }, // XAMX: OP_HEAP
        { 0x2E, "Begin" },
        { 0x2F, "Ret" }, // XAMX: OP_RET
        { 0x30, "Return" },
        { 0x31, "CallFunc" },
        { 0x32, "CallPri" }, // XAMX: OP_CALL_PRI
        { 0x33, "Jump" }, // Pawn: JUMP (unconditional) - symmetric +/- delta range across 60767 occurrences
        { 0x34, "JumpRel" }, // XAMX: OP_JREL
        { 0x35, "BNE" },
        { 0x36, "BEQ" },
        { 0x37, "JumpEQ" }, // Pawn: JEQ - all-positive delta range, same jump-family shape as BNE/BEQ
        { 0x38, "JumpNEQ" }, // Pawn: JNEQ
        { 0x39, "JLess" }, // XAMX: OP_JLESS - part of the unsigned JLESS/JLEQ/JGRTR/JGEQ family below, which never occurs in this ROM (see 0x3D's note)
        { 0x3A, "JLeq" }, // XAMX: OP_JLEQ
        { 0x3B, "JGrtr" }, // XAMX: OP_JGRTR
        { 0x3C, "JGeq" }, // XAMX: OP_JGEQ
        { 0x3D, "JumpSLess" }, // Pawn: JSLESS - the unsigned JLESS/JLEQ/JGRTR/JGEQ (0x39-0x3C) never occur at all in this ROM, consistent with a signed-only game-script compiler
        { 0x3E, "JumpSLeq" }, // Pawn: JSLEQ
        { 0x3F, "JumpSGrtr" }, // Pawn: JSGRTR
        { 0x40, "JumpSGeq" }, // Pawn: JSGEQ
        { 0x41, "Shl" }, // Pawn: SHL - confirmed always-zero arg
        { 0x42, "Shr" }, // XAMX: OP_SHR
        { 0x43, "SShr" }, // Pawn: SSHR
        { 0x44, "ShlConstPri" }, // XAMX: OP_SHL_C_PRI
        { 0x45, "ShlConstAlt" }, // XAMX: OP_SHL_C_ALT
        { 0x46, "ShrConstPri" }, // XAMX: OP_SHR_C_PRI
        { 0x47, "ShrConstAlt" }, // XAMX: OP_SHR_C_ALT
        { 0x48, "SMul" }, // Pawn: SMUL - confirmed always-zero arg
        { 0x49, "SDiv" }, // XAMX: OP_SDIV
        { 0x4A, "SDivAlt" }, // Pawn: SDIV.alt
        { 0x4B, "UMul" }, // XAMX: OP_UMUL
        { 0x4C, "UDiv" }, // XAMX: OP_UDIV
        { 0x4D, "UDivAlt" }, // XAMX: OP_UDIV_ALT
        { 0x4E, "Add" }, // confirmed - Pawn: ADD, always-zero arg (the "?" is resolved)
        { 0x4F, "Sub" }, // Pawn: SUB
        { 0x50, "SubAlt" }, // Pawn: SUB.alt
        { 0x51, "And" }, // Pawn: AND, not a compare - "Cmp?" was the wrong guess despite the right shape (always-zero arg)
        { 0x52, "Or" }, // Pawn: OR
        { 0x53, "Xor" }, // XAMX: OP_XOR
        { 0x54, "Not" }, // Pawn: NOT
        { 0x55, "Neg" }, // Pawn: NEG
        { 0x56, "Invert" }, // Pawn: INVERT
        { 0x57, "AddConst" }, // Pawn: ADD.C - real, always-negative small-magnitude constant operand across 387 occurrences
        { 0x58, "SMulConst" }, // XAMX: OP_SMUL_C
        { 0x59, "ClearAll" },
        { 0x5A, "ZeroAlt" }, // Pawn: ZERO.alt (pairs with ClearAll/ZERO.pri above)
        { 0x5B, "Zero" }, // XAMX: OP_ZERO
        { 0x5C, "ZeroS" }, // XAMX: OP_ZERO_S
        { 0x5D, "SignPri" }, // XAMX: OP_SIGN_PRI
        { 0x5E, "SignAlt" }, // XAMX: OP_SIGN_ALT
        { 0x5F, "Eq" }, // Pawn: EQ - confirmed always-zero arg
        { 0x60, "Neq" }, // Pawn: NEQ
        { 0x61, "Less" }, // XAMX: OP_LESS
        { 0x62, "Leq" }, // XAMX: OP_LEQ
        { 0x63, "Grtr" }, // XAMX: OP_GRTR
        { 0x64, "Geq" }, // XAMX: OP_GEQ
        { 0x65, "SLess" }, // Pawn: SLESS
        { 0x66, "SLeq" }, // Pawn: SLEQ
        { 0x67, "SGrtr" }, // Pawn: SGRTR
        { 0x68, "SGeq" }, // Pawn: SGEQ
        { 0x69, "EqConst" }, // Pawn: EQ.C.pri - real constant operand (72 occurrences, 6 distinct values)
        { 0x6A, "EqConstAlt" }, // XAMX: OP_EQ_C_ALT
        { 0x6B, "IncPri" }, // XAMX: OP_INC_PRI
        { 0x6C, "IncAlt" }, // XAMX: OP_INC_ALT
        { 0x6D, "Inc" }, // XAMX: OP_INC
        { 0x6E, "IncS" }, // XAMX: OP_INC_S
        { 0x6F, "IncIndirect" }, // XAMX: OP_INC_I
        { 0x70, "DecPri" }, // XAMX: OP_DEC_PRI
        { 0x71, "DecAlt" }, // XAMX: OP_DEC_ALT
        { 0x72, "Dec" }, // XAMX: OP_DEC
        { 0x73, "DecS" }, // XAMX: OP_DEC_S
        { 0x74, "DecIndirect" }, // XAMX: OP_DEC_I
        { 0x75, "MemCopy" }, // Pawn: MOVS - small positive byte-count operand (20 distinct values, 8-512) across 2917 occurrences
        { 0x76, "MemCmp" }, // XAMX: OP_CMPS
        { 0x77, "MemFill" }, // Pawn: FILL - same small positive byte-count shape as MemCopy
        { 0x78, "Halt" }, // Pawn: HALT - always arg=12 (a fixed exit code) across 615 occurrences
        { 0x79, "Bounds" }, // XAMX: OP_BOUNDS
        { 0x7A, "SysReqPri" }, // XAMX: OP_SYSREQ_PRI - a native call via register, distinct from SYSREQ.N/DoCommand?'s table-index form
        { 0x7B, "SysReqConst" }, // XAMX: OP_SYSREQ_C
        // { 0x7C, "$7C" }, // Invalid Code
        // { 0x7D, "$7D" }, // Invalid Code
        // { 0x7E, "$7E" }, // Invalid Code
        // { 0x7F, "$7F" }, // Invalid Code
        // { 0x80, "$80" }, // Invalid Code
        { 0x81, "Switch" }, // Pawn: SWITCH - occurs in an IDENTICAL count (3512) to 0x82/CASETBL everywhere in the corpus, confirming the pair; the stale "JMP" label here was only ever a dead case-block comment, never the real name
        { 0x82, "switch" },
        { 0x83, "SwapPri" }, // XAMX: OP_SWAP_PRI
        { 0x84, "SwapAlt" }, // XAMX: OP_SWAP_ALT
        { 0x85, "PushAddr" }, // XAMX: OP_PUSH_ADR
        { 0x86, "Nop" }, // XAMX: OP_NOP
        { 0x87, "DoCommand?" },
        // { 0x88, "$88" }, // Invalid Code
        { 0x89, "LineNo?" },
        { 0x8A, "$8A" },
        { 0x8B, "$8B" },
        { 0x8C, "$8C" },
        { 0x8D, "$8D" },
        { 0x8E, "$8E" },
        { 0x8F, "$8F" },
        { 0x90, "$90" },
        { 0x91, "$91" },
        { 0x92, "$92" },
        { 0x93, "$93" },
        { 0x94, "$94" },
        { 0x95, "$95" },
        { 0x96, "$96" },
        { 0x97, "$97" },
        { 0x98, "$98" },
        { 0x99, "$99" },
        { 0x9A, "$9A" },
        { 0x9B, "Copy" },
        { 0x9C, "$9C" },
        { 0x9D, "$9D" },
        { 0x9E, "$9E" },
        { 0x9F, "$9F" },
        { 0xA0, "$A0" },
        { 0xA1, "$A1" },
        { 0xA2, "GetGlobal2" },
        { 0xA3, "GetGlobal" },
        { 0xA4, "GetGlobal4" },
        { 0xA5, "$A5" },
        { 0xA6, "$A6" },
        { 0xA7, "$A7" },
        { 0xA8, "$A8" },
        { 0xA9, "$A9" },
        { 0xAA, "$AA" },
        { 0xAB, "PushConst2" },
        { 0xAC, "CmpConst2" },
        { 0xAD, "$AD" },
        { 0xAE, "$AE" },
        { 0xAF, "SetGlobal" },
        { 0xB0, "$B0" },
        { 0xB1, "SetLocal" },
        { 0xB2, "$B2" },
        { 0xB3, "$B3" },
        { 0xB4, "$B4" },
        { 0xB5, "$B5" },
        { 0xB6, "$B6" },
        { 0xB7, "$B7" },
        { 0xB8, "$B8" },
        { 0xB9, "$B9" },
        { 0xBA, "$BA" },
        { 0xBB, "$BB" },
        { 0xBC, "PushConst" },
        { 0xBD, "GetGlobal3" },
        { 0xBE, "GetArg" },
        { 0xBF, "AdjustStack" },
        { 0xC0, "$C0" },
        { 0xC1, "$C1" },
        { 0xC2, "$C2" },
        { 0xC3, "$C3" },
        { 0xC4, "$C4" },
        { 0xC5, "$C5" },
        { 0xC6, "$C6" },
        { 0xC7, "$C7" },
        { 0xC8, "CmpLocal" },
        { 0xC9, "CmpConst" },
        { 0xCA, "$CA" },
        { 0xCB, "$CB" },
        { 0xCC, "$CC" },
        { 0xCD, "$CD" },
        { 0xCE, "$CE" },
        { 0xCF, "$CF" },
        { 0xD0, "$D0" },

        { 0xD1, "$D1" },
        { 0xD2, "$D2" },
        { 0xD3, "$D3" },
        { 0xD4, "$D4" },
        { 0xD5, "Mode1" },
        { 0xD6, "Mode2" },
    };
}