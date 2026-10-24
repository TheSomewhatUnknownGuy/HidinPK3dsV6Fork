#nullable enable
using System;
using System.IO;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Insertion;

public sealed class SpeciesTables
{
    public byte[][] Personal = [];
    public byte[][] Evolution = [];
    public byte[][] Levelup = [];
    public byte[][] Megaevo = [];
    public byte[][] EggMove = [];
    public byte[][]? EvolutionBase;
    public byte[] PersonalMasterTail = [];

    public static SpeciesTables? Load(string romFs, out string error)
    {
        error = "";
        try
        {
            string root = RomLayout.ResolveRomFsPath(romFs);
            byte[][] personalWithMaster = ReadArchive(root, "a/0/1/7");
            byte[][] evolution = ReadArchive(root, "a/0/1/4");
            byte[][] levelup = ReadArchive(root, "a/0/1/3");
            byte[][] megaevo = ReadArchive(root, "a/0/1/5");
            byte[][] eggmove = ReadArchive(root, "a/0/1/2");
            byte[][] evolutionBaseWithBlob = ReadArchive(root, "a/0/1/8");

            if (personalWithMaster.Length < 2) throw new InvalidDataException("a/0/1/7 needs personal entries and a master file");
            if (evolutionBaseWithBlob.Length < 2) throw new InvalidDataException("a/0/1/8 needs species entries and a blob file");

            byte[] master = personalWithMaster[^1];
            byte[][] personal = Slice(personalWithMaster, 0, personalWithMaster.Length - 1);
            int entryLength = personal[0].Length;
            if (entryLength <= 0x20) throw new InvalidDataException($"personal entry length {entryLength} is too short");
            ValidateUniformLength(personal, entryLength, "personal");

            long masterEntryBytes = (long)personal.Length * entryLength;
            if (masterEntryBytes > master.LongLength)
                throw new InvalidDataException($"personal master is {master.LongLength} B; expected at least {masterEntryBytes} B for {personal.Length} entries");
            byte[] masterTail = master.AsSpan(checked((int)masterEntryBytes)).ToArray();

            ValidateUniformLength(evolution, FirstLength(evolution, "evolution"), "evolution");
            ValidateUniformLength(megaevo, FirstLength(megaevo, "megaevo"), "megaevo");

            byte[][] evolutionBase = Slice(evolutionBaseWithBlob, 0, evolutionBaseWithBlob.Length - 1);
            ValidateUniformLength(evolutionBase, 2, "evolutionbase");

            return new SpeciesTables
            {
                Personal = personal,
                Evolution = evolution,
                Levelup = levelup,
                Megaevo = megaevo,
                EggMove = eggmove,
                EvolutionBase = evolutionBase,
                PersonalMasterTail = masterTail,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or ArgumentException or OverflowException)
        {
            error = e.Message;
            return null;
        }
    }

    public SpeciesTables Clone() => new()
    {
        Personal = CloneRows(Personal),
        Evolution = CloneRows(Evolution),
        Levelup = CloneRows(Levelup),
        Megaevo = CloneRows(Megaevo),
        EggMove = CloneRows(EggMove),
        EvolutionBase = EvolutionBase == null ? null : CloneRows(EvolutionBase),
        PersonalMasterTail = (byte[])PersonalMasterTail.Clone(),
    };

    public byte[] BuildMaster() => Concat(Personal, PersonalMasterTail);

    public byte[] BuildEvolutionBaseBlob() => Concat(EvolutionBase ?? [], []);

    private static byte[][] ReadArchive(string root, string relativePath)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) throw new FileNotFoundException($"required GARC is missing: {relativePath}", path);
        long length = new FileInfo(path).Length;
        if (length > int.MaxValue) throw new InvalidDataException($"GARC is too large to load in memory: {relativePath} ({length} B)");
        byte[] archiveBytes = File.ReadAllBytes(path);
        return new GARC.MemGARC(archiveBytes).Files;
    }

    private static int FirstLength(byte[][] rows, string name)
    {
        if (rows.Length == 0) throw new InvalidDataException($"{name} GARC is empty");
        return rows[0].Length;
    }

    private static void ValidateUniformLength(byte[][] rows, int expectedLength, string name)
    {
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i].Length != expectedLength)
                throw new InvalidDataException($"{name} entry {i} is {rows[i].Length} B; expected {expectedLength} B");
        }
    }

    private static byte[][] Slice(byte[][] source, int start, int length)
    {
        var result = new byte[length][];
        Array.Copy(source, start, result, 0, length);
        return result;
    }

    private static byte[][] CloneRows(byte[][] rows)
    {
        var clone = new byte[rows.Length][];
        for (int i = 0; i < rows.Length; i++) clone[i] = (byte[])rows[i].Clone();
        return clone;
    }

    private static byte[] Concat(byte[][] rows, byte[] tail)
    {
        long totalLength = tail.LongLength;
        foreach (byte[] row in rows) totalLength = checked(totalLength + row.LongLength);
        if (totalLength > int.MaxValue) throw new InvalidDataException($"combined table is too large: {totalLength} B");

        byte[] result = new byte[(int)totalLength];
        long offset = 0;
        foreach (byte[] row in rows)
        {
            Array.Copy(row, 0L, result, offset, row.LongLength);
            offset += row.LongLength;
        }
        Array.Copy(tail, 0L, result, offset, tail.LongLength);
        return result;
    }
}
