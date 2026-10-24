using System;
using System.Linq;

namespace pk3DS.Core;

public class Script
{
    public int Length => BitConverter.ToInt32(Raw, 0x00);
    public uint Magic => BitConverter.ToUInt32(Raw, 0x04);
    // case 0x0A0AF1E0: code = read_code_block(f); break;
    // case 0x0A0AF1EF: debug = read_debug_block(f); break;
    public bool Debug => Magic == 0x0A0AF1EF;

    // Renamed from this codebase's original (wrong) guess "PtrOffset"/"PtrCount" - cross-checking
    // XAMX's AMX.py (github.com/BlastSlimey/XAMX, a real dis/reassembler for this exact format)
    // against these same bytes confirms they are the AMX header's Flags and DefSize (definition-
    // record size, 8 for a 32-bit-cell script - what every table below is stepped by). Safe to
    // rename outright: nothing else in this codebase referenced the old names.
    public ushort Flags => BitConverter.ToUInt16(Raw, 0x08);
    public ushort DefSize => BitConverter.ToUInt16(Raw, 0x0A);

    public int ScriptInstructionStart => BitConverter.ToInt32(Raw, 0x0C);
    public int ScriptMovementStart => BitConverter.ToInt32(Raw, 0x10);
    public int FinalOffset => BitConverter.ToInt32(Raw, 0x14);
    public int AllocatedMemory => BitConverter.ToInt32(Raw, 0x18);

    // The symbol-table region XAMX documents but this codebase never read before 2026-09-13: it sits
    // BEFORE ScriptInstructionStart (i.e. it is plain, uncompressed header bytes, not part of the
    // compressed instruction blob), and a wrong native-index guess in this region's absence is what
    // crashed a real edit once - see AmxNatives and NativeFunctionHashes below.
    public int MainAddress => BitConverter.ToInt32(Raw, 0x1C);
    public uint PublicFunctionsStart => BitConverter.ToUInt32(Raw, 0x20);
    public uint NativeFunctionsStart => BitConverter.ToUInt32(Raw, 0x24);
    public uint LibrariesStart => BitConverter.ToUInt32(Raw, 0x28);
    public uint PublicVariablesStart => BitConverter.ToUInt32(Raw, 0x2C);
    public uint PublicTagsStart => BitConverter.ToUInt32(Raw, 0x30);
    public uint OverlaysStart => BitConverter.ToUInt32(Raw, 0x34);
    public uint SymbolNamesStart => BitConverter.ToUInt32(Raw, 0x38);

    public uint[] NativeFunctionHashes
    {
        get
        {
            uint nativeStart = NativeFunctionsStart;
            uint librariesStart = LibrariesStart;
            if (Raw.Length < 0x2C || librariesStart <= nativeStart ||
                (librariesStart - nativeStart) % 8 != 0 || librariesStart > Raw.Length)
                return [];

            int count = (int)((librariesStart - nativeStart) / 8);
            var hashes = new uint[count];
            for (int i = 0; i < count; i++)
                hashes[i] = BitConverter.ToUInt32(Raw, (int)nativeStart + i * 8 + 4);
            return hashes;
        }
    }

    // Generated Attributes
    public int CompressedLength => Length - ScriptInstructionStart;
    public byte[] CompressedBytes => Raw.Skip(ScriptInstructionStart).ToArray();
    public int DecompressedLength => FinalOffset - ScriptInstructionStart;
    public uint[] DecompressedInstructions => Scripts.QuickDecompress(CompressedBytes, DecompressedLength / 4);

    public uint[] ScriptCommands => DecompressedInstructions.Take((ScriptMovementStart - ScriptInstructionStart) / 4).ToArray();
    public uint[] MoveCommands => DecompressedInstructions.Skip((ScriptMovementStart - ScriptInstructionStart) / 4).ToArray();
    public string[] ParseScript => Scripts.ParseScript(ScriptCommands, nativeHashes: NativeFunctionHashes);
    public string[] ParseMoves => Scripts.ParseMovement(MoveCommands);

    public string Info => "Data Start: 0x" + ScriptInstructionStart.ToString("X4")
                                           + Environment.NewLine + "Movement Offset: 0x" + ScriptMovementStart.ToString("X4")
                                           + Environment.NewLine + "Total Used Size: 0x" + FinalOffset.ToString("X4")
                                           + Environment.NewLine + "Reserved Size: 0x" + AllocatedMemory.ToString("X4")
                                           + Environment.NewLine + "Compressed Len: 0x" + CompressedLength.ToString("X4")
                                           + Environment.NewLine + "Decompressed Len: 0x" + DecompressedLength.ToString("X4")
                                           + Environment.NewLine + "Compression Ratio: " +
                                           ((DecompressedLength - CompressedLength) / (decimal)DecompressedLength).ToString("p1");

    public byte[] Raw;

    public Script(byte[] data = null)
    {
        Raw = data ?? [];

        // sub_51AAFC
        if ((Raw[8] & 1) != 0)
            throw new ArgumentException("Multi-environment script!?");
    }

    public byte[] Write(uint[] editedInstructions = null)
    {
        uint[] instructions = editedInstructions ?? DecompressedInstructions;
        byte[] compressed = Scripts.CompressScript(Scripts.GetBytes(instructions));

        byte[] newRaw = new byte[ScriptInstructionStart + compressed.Length];
        Array.Copy(Raw, 0, newRaw, 0, ScriptInstructionStart);
        Array.Copy(compressed, 0, newRaw, ScriptInstructionStart, compressed.Length);
        BitConverter.GetBytes(newRaw.Length).CopyTo(newRaw, 0x00); // Length field

        Raw = newRaw;
        return Raw;
    }

    public bool TryWriteResizedScriptCommands(uint[] newScriptCommands, out string error)
    {
        error = "";
        uint[] moveCommands = MoveCommands;
        int newScriptMovementStart = ScriptInstructionStart + newScriptCommands.Length * 4;
        int newFinalOffset = newScriptMovementStart + moveCommands.Length * 4;
        if (newFinalOffset > AllocatedMemory)
        {
            error = $"This script only has {AllocatedMemory - FinalOffset} bytes of reserved headroom left, but this edit needs {newFinalOffset - FinalOffset} - it won't fit.";
            return false;
        }

        uint[] fullInstructions = new uint[newScriptCommands.Length + moveCommands.Length];
        newScriptCommands.CopyTo(fullInstructions, 0);
        moveCommands.CopyTo(fullInstructions, newScriptCommands.Length);
        byte[] compressed = Scripts.CompressScript(Scripts.GetBytes(fullInstructions));

        byte[] newRaw = new byte[ScriptInstructionStart + compressed.Length];
        Array.Copy(Raw, 0, newRaw, 0, ScriptInstructionStart);
        Array.Copy(compressed, 0, newRaw, ScriptInstructionStart, compressed.Length);
        BitConverter.GetBytes(newRaw.Length).CopyTo(newRaw, 0x00);        // Length
        BitConverter.GetBytes(newScriptMovementStart).CopyTo(newRaw, 0x10); // ScriptMovementStart
        BitConverter.GetBytes(newFinalOffset).CopyTo(newRaw, 0x14);         // FinalOffset
        // AllocatedMemory (0x18) is a fixed reservation, not derived - left untouched.

        Raw = newRaw;
        return true;
    }
}