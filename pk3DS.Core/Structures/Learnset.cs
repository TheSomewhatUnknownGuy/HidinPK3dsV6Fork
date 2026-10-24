using System;
using System.IO;

namespace pk3DS.Core.Structures;

public abstract class Learnset
{
    public int Count;
    public int[] Moves;
    public int[] Levels;

    public abstract byte[] Write();

    public int[] GetMoves(int maxLevel, int minLevel = 0)
    {
        if (minLevel <= 1 && maxLevel >= 100)
            return Moves;
        if (minLevel > maxLevel)
            return [];
        int start = Array.FindIndex(Levels, z => z >= minLevel);
        if (start < 0)
            return [];
        int end = Array.FindLastIndex(Levels, z => z <= maxLevel);
        if (end < 0)
            return [];
        int[] result = new int[end - start + 1];
        Array.Copy(Moves, start, result, 0, result.Length);
        return result;
    }

    public int[] GetEncounterMoves(int level)
    {
        const int count = 4;
        int[] moves = new int[count];
        int ctr = 0;
        for (int i = 0; i < Moves.Length; i++)
        {
            if (Levels[i] > level)
                break;
            int move = Moves[i];
            if (moves.AsSpan().Contains(move))
                continue;

            moves[ctr++] = move;
            ctr &= 3;
        }
        return moves;
    }

    public int GetMinMoveLevel(int level)
    {
        if (Levels.Length == 0)
            return 1;

        int end = Array.FindLastIndex(Levels, z => z <= level);
        return Math.Max(end - 4, 1);
    }

    public int GetLevelLearnMove(int move)
    {
        int index = Array.IndexOf(Moves, move);
        return index < 0 ? index : Levels[index];
    }
}

public class Learnset6 : Learnset
{
    public Learnset6(byte[] data)
    {
        if (data.Length < 4 || data.Length % 4 != 0)
        { Count = 0; Levels = []; Moves = []; return; }
        Count = (data.Length / 4) - 1;
        Moves = new int[Count];
        Levels = new int[Count];
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        for (int i = 0; i < Count; i++)
        {
            Moves[i] = br.ReadInt16();
            Levels[i] = br.ReadInt16();
        }
    }

    public override byte[] Write()
    {
        Count = (ushort)Moves.Length;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        for (int i = 0; i < Count; i++)
        {
            bw.Write((short)Moves[i]);
            bw.Write((short)Levels[i]);
        }
        bw.Write(-1);
        return ms.ToArray();
    }

    public static Learnset[] GetArray(byte[][] entries)
    {
        var data = new Learnset[entries.Length];
        for (int i = 0; i < data.Length; i++)
            data[i] = new Learnset6(entries[i]);
        return data;
    }
}