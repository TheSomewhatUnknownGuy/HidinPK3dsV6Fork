#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;

namespace pk3DS.Core.Modding.Insertion;

public sealed class InsertionOptions
{
    public byte[]? CodeBin;
    public int ModelRecordShift;
}

public sealed class SpeciesRow
{
    public int ExtraFormGroups;
    public bool GenderDiff;
    public int CloneModelFromSlot = -1;
    public int LowestEvolution = -1;
}

public sealed class InsertionPlan
{
    private static readonly string[] SmallArchives =
    [
        "a/0/1/7", "a/0/1/3", "a/0/1/4", "a/0/1/5", "a/0/1/2", "a/0/1/8"
    ];

    public SpeciesTables After = new();
    public ModelsPlan? Models;
    public List<string> Log = [];
    public List<string> Violations = [];
    public int[] NewSpeciesIds = [];
    public int[] NewSpeciesRecords = [];

    // One source Photo Club row for each model group appended by ModelsPlanner.
    public List<int> PhotoClubSourceGroups = [];

    public void Stage(GarcTransaction tx, string romFsIn, string romFsOut)
        => StageCore(tx, romFsIn, romFsOut, includeModelArchives: true);

    public void StageTables(GarcTransaction tx, string romFsIn, string romFsOut)
        => StageCore(tx, romFsIn, romFsOut, includeModelArchives: false);

    private void StageCore(GarcTransaction tx, string romFsIn, string romFsOut, bool includeModelArchives)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentException.ThrowIfNullOrEmpty(romFsIn);
        ArgumentException.ThrowIfNullOrEmpty(romFsOut);
        string input = RomLayout.ResolveRomFsPath(romFsIn);
        string output = RomLayout.ResolveRomFsPath(romFsOut);

        if (includeModelArchives)
        {
            if (Models == null) throw new InvalidDataException("models plan is missing");
            if (Models.Appended.Count != (long)Models.GroupsAdded * ModelGroupInsertion.FilesPerGroup)
                throw new InvalidDataException("models appended-member list does not match GroupsAdded");
            if (PhotoClubSourceGroups.Count != Models.GroupsAdded)
                throw new InvalidDataException($"Photo Club source list has {PhotoClubSourceGroups.Count} groups, expected {Models.GroupsAdded}");

            string modelsIn = ArchivePath(input, "a/0/9/4");
            string modelsOut = ArchivePath(output, "a/0/9/4");
            // Appending is not idempotent: staging over an archive that already grew (a partial earlier commit) would append twice.
            GARC.GARCFile modelsHeader = ReadHeader(modelsIn);
            if (modelsHeader.fatb.FileCount != Models.BaseMembers)
                throw new InvalidDataException($"a/0/9/4 has {modelsHeader.fatb.FileCount} files; the plan was made for {Models.BaseMembers}");
            StageAppend(tx, modelsIn, modelsOut, new Dictionary<int, byte[]> { [0] = Models.File0 }, Models.Appended);

            string photoIn = ArchivePath(input, "a/3/2/6");
            string photoOut = ArchivePath(output, "a/3/2/6");
            GARC.GARCFile photoHeader = ReadHeader(photoIn);
            if (photoHeader.fatb.FileCount != Models.BaseGroups)
                throw new InvalidDataException($"a/3/2/6 has {photoHeader.fatb.FileCount} files; models has {Models.BaseGroups} base groups");
            var photoAppend = new List<AppendedMember>(PhotoClubSourceGroups.Count);
            foreach (int sourceGroup in PhotoClubSourceGroups)
            {
                if ((uint)sourceGroup >= (uint)photoHeader.fatb.FileCount)
                    throw new InvalidDataException($"Photo Club source group {sourceGroup} is outside 0..{photoHeader.fatb.FileCount - 1}");
                photoAppend.Add(new AppendedMember(sourceGroup, null));
            }
            StageAppend(tx, photoIn, photoOut, new Dictionary<int, byte[]>(), photoAppend);
        }

        byte[][][] tableFiles =
        [
            Join(After.Personal, After.BuildMaster()),
            After.Levelup,
            After.Evolution,
            After.Megaevo,
            After.EggMove,
            Join(After.EvolutionBase ?? [], After.BuildEvolutionBaseBlob()),
        ];
        for (int i = 0; i < SmallArchives.Length; i++)
        {
            string source = ArchivePath(input, SmallArchives[i]);
            string destination = ArchivePath(output, SmallArchives[i]);
            GARC.GARCFile header = ReadHeader(source);
            EnsureParent(destination);
            tx.Stage(destination, new GARC.ArrayGARCFileProvider(tableFiles[i]), header.Version,
                GetPadding(header, source));
        }
    }

    private static void StageAppend(GarcTransaction tx, string source, string destination,
        IReadOnlyDictionary<int, byte[]> replace, IReadOnlyList<AppendedMember> append)
    {
        GARC.GARCFile header = ReadHeader(source);
        EnsureParent(destination);
        using var provider = new AppendGarcProvider(source, replace, append);
        tx.Stage(destination, provider, header.Version, GetPadding(header, source));
    }

    private static GARC.GARCFile ReadHeader(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("required GARC is missing", path);
        return GARC.UnpackGARC(path);
    }

    private static int GetPadding(GARC.GARCFile header, string path)
    {
        if (header.ContentPadToNearest == 0 || header.ContentPadToNearest > int.MaxValue)
            throw new InvalidDataException($"{path} has invalid GARC padding {header.ContentPadToNearest}");
        return checked((int)header.ContentPadToNearest);
    }

    private static string ArchivePath(string root, string relative)
        => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static void EnsureParent(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (parent == null) throw new InvalidDataException($"cannot determine output directory for {path}");
        Directory.CreateDirectory(parent);
    }

    private static byte[][] Join(byte[][] rows, byte[] final)
    {
        var result = new byte[rows.Length + 1][];
        Array.Copy(rows, result, rows.Length);
        result[^1] = final;
        return result;
    }
}

public static class InsertionPipeline
{
    private const int PersonalFormStatsIndexOffset = 0x1C;
    private const int PersonalFormeCountOffset = 0x20;
    private const int HashBufferSize = 4 * 1024 * 1024;
    private static readonly string[] TablePaths =
    [
        "a/0/1/7", "a/0/1/3", "a/0/1/4", "a/0/1/5", "a/0/1/2", "a/0/1/8"
    ];
    public static InsertionPlan? PlanForms(string romFsIn, SpeciesTables? edited,
        IReadOnlyList<(int Species, int Count)> forms, int templateEntry, InsertionOptions o,
        out string? refusal)
    {
        refusal = null;
        try
        {
            if (forms == null || forms.Count == 0) return RefusePlan("at least one form request is required", out refusal);
            o ??= new InsertionOptions();
            if (o.ModelRecordShift != 0) return RefusePlan($"model record shift {o.ModelRecordShift} is only supported for species insertion", out refusal);

            RomKind kind = RomLayout.DetectFromRomFs(romFsIn, out int maxSpecies, out string detectError);
            if (detectError.Length != 0) return RefusePlan(detectError, out refusal);
            if (RomLayout.Refusal(kind, species: false) is string layoutRefusal)
                return RefusePlan(layoutRefusal, out refusal);

            ModelsInput? models = ReadModels(romFsIn, out string modelsError);
            if (models == null) return RefusePlan(modelsError, out refusal);
            if (models.Layout.LastRecord != maxSpecies)
                return RefusePlan($"models last record {models.Layout.LastRecord} does not match max species {maxSpecies}", out refusal);

            SpeciesTables? loaded = edited;
            if (loaded == null)
            {
                loaded = SpeciesTables.Load(romFsIn, out string loadError);
                if (loaded == null) return RefusePlan(loadError, out refusal);
            }
            SpeciesTables before = loaded;
            SpeciesTables current = before;
            ModelsPlan modelPlan = ModelsPlanner.Start(models.Layout, models.File0);
            var photoSources = new List<int>(models.Layout.Groups);
            for (int group = 0; group < models.Layout.Groups; group++) photoSources.Add(group);
            var plannedPhotoSources = new List<int>();
            var formChecks = new List<(int Species, int Count, int Template)>();
            var originalToCurrent = Enumerable.Range(0, before.Personal.Length).ToArray();
            var log = new List<string>();
            int ceiling = RomLayout.PersonalCeiling(o.CodeBin, kind);

            foreach ((int species, int count) in forms)
            {
                int template = species;
                if (templateEntry >= 0)
                {
                    if (templateEntry >= originalToCurrent.Length)
                        return RefusePlan($"template entry {templateEntry} is outside the personal table", out refusal);
                    template = originalToCurrent[templateEntry];
                }
                else if (templateEntry != -1)
                {
                    return RefusePlan($"template entry {templateEntry} must be -1 or a personal entry", out refusal);
                }

                int currentMembers = checked(models.Members + modelPlan.GroupsAdded * ModelGroupInsertion.FilesPerGroup);
                ModelsLayout? currentLayout = ModelsLayout.Parse(modelPlan.File0, currentMembers, out string parseError);
                if (currentLayout == null) return RefusePlan("models layout became invalid: " + parseError, out refusal);
                string? eligibility = FormPlanner.Eligibility(current, species, count, template, maxSpecies, currentLayout);
                if (eligibility != null) return RefusePlan(eligibility, out refusal);
                if (!TryResolveModelGroup(current, template, maxSpecies, currentLayout, out int templateGroup, out string templateError))
                    return RefusePlan(templateError, out refusal);

                FormInsertPlan tablePlan;
                try
                {
                    tablePlan = FormPlanner.Insert(current, species, count, template, maxSpecies, ceiling, currentLayout);
                }
                catch (ArgumentException e)
                {
                    return RefusePlan(e.Message, out refusal);
                }

                int record = species - 1;
                (ushort oldStart, byte oldCount, byte oldFlags) = currentLayout.Record(record);
                var sourceGroups = new List<int>(oldCount + count);
                for (int i = 0; i < oldCount; i++) sourceGroups.Add(oldStart + i);
                for (int i = 0; i < count; i++) sourceGroups.Add(templateGroup);
                byte newFlags = oldFlags < 0x05 ? checked((byte)(oldFlags + 0x04)) : oldFlags;
                ModelsPlan beforeModelPlan = CopyModelsPlan(modelPlan);
                byte[] beforeFile0 = (byte[])modelPlan.File0.Clone();
                string? modelError = ModelsPlanner.AddFormGroups(modelPlan, models.Layout, species, count, templateGroup);
                if (modelError != null) return RefusePlan(modelError, out refusal);
                int afterMembers = checked(models.Members + modelPlan.GroupsAdded * ModelGroupInsertion.FilesPerGroup);
                ModelsLayout? afterLayout = ModelsLayout.Parse(modelPlan.File0, afterMembers, out parseError);
                if (afterLayout == null) return RefusePlan("models layout became invalid: " + parseError, out refusal);
                List<string> modelProblems = InsertionVerifier.CheckModels(currentLayout, beforeFile0,
                    beforeModelPlan, modelPlan, record, checked(oldCount + count), newFlags, sourceGroups);
                if (modelProblems.Count != 0) return RefusePlan(string.Join("; ", modelProblems), out refusal);

                foreach (int sourceGroup in sourceGroups)
                {
                    if ((uint)sourceGroup >= (uint)photoSources.Count)
                        return RefusePlan($"Photo Club source group {sourceGroup} is outside the planned model groups", out refusal);
                    int photoSource = photoSources[sourceGroup];
                    plannedPhotoSources.Add(photoSource);
                    photoSources.Add(photoSource);
                }

                log.AddRange(tablePlan.Log);
                log.Add($"models {currentMembers} -> {afterMembers}, record {record}: ({oldStart},{oldCount},{oldFlags}) -> {afterLayout.Record(record)}");
                formChecks.Add((species, count, template));
                if (templateEntry >= 0)
                {
                    for (int i = 0; i < originalToCurrent.Length; i++)
                        originalToCurrent[i] = tablePlan.OldToNew[originalToCurrent[i]];
                }
                current = tablePlan.After;
            }

            var plan = new InsertionPlan
            {
                After = current,
                Models = modelPlan,
                Log = log,
                PhotoClubSourceGroups = plannedPhotoSources,
            };
            plan.Violations.AddRange(InsertionVerifier.Check(before, current, formChecks, maxSpecies));
            return plan;
        }
        catch (Exception e) when (IsExpectedFailure(e))
        {
            return RefusePlan(e.Message, out refusal);
        }
    }

    public static InsertionPlan? PlanSpecies(string romFsIn, IReadOnlyList<SpeciesRow> rows,
        InsertionOptions o, out string? refusal)
    {
        refusal = null;
        try
        {
            if (rows == null || rows.Count == 0) return RefusePlan("at least one species row is required", out refusal);
            o ??= new InsertionOptions();
            if (o.ModelRecordShift is not (0 or -1))
                return RefusePlan($"model record shift {o.ModelRecordShift} must be 0 or -1", out refusal);

            RomKind kind = RomLayout.DetectFromRomFs(romFsIn, out int maxSpecies, out string detectError);
            if (detectError.Length != 0) return RefusePlan(detectError, out refusal);
            if (RomLayout.Refusal(kind, species: true) is string layoutRefusal)
                return RefusePlan(layoutRefusal, out refusal);

            ModelsInput? models = ReadModels(romFsIn, out string modelsError);
            if (models == null) return RefusePlan(modelsError, out refusal);
            if (models.Layout.LastRecord != maxSpecies)
                return RefusePlan($"models last record {models.Layout.LastRecord} does not match max species {maxSpecies}", out refusal);

            SpeciesTables? before = SpeciesTables.Load(romFsIn, out string loadError);
            if (before == null) return RefusePlan(loadError, out refusal);
            int[] lowestEvolution = rows.Select(row => row?.LowestEvolution ?? -1).ToArray();
            SpeciesInsertPlan tablePlan;
            try
            {
                tablePlan = SpeciesPlanner.InsertBaseSpecies(before, maxSpecies, rows.Count, lowestEvolution);
            }
            catch (Exception e) when (e is ArgumentException or InvalidDataException or OverflowException)
            {
                return RefusePlan(e.Message, out refusal);
            }

            ModelsPlan modelPlan = ModelsPlanner.Start(models.Layout, models.File0);
            var photoSources = Enumerable.Range(0, models.Layout.Groups).ToList();
            var plannedPhotoSources = new List<int>();
            var log = new List<string>(tablePlan.Log);
            var modelProblems = new List<string>();

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                SpeciesRow row = rows[rowIndex] ?? new SpeciesRow();
                if (row.ExtraFormGroups < 0)
                    return RefusePlan($"species row {rowIndex + 1} has negative ExtraFormGroups", out refusal);
                if (row.CloneModelFromSlot < -1)
                    return RefusePlan($"species row {rowIndex + 1} CloneModelFromSlot must be -1 or a species id", out refusal);

                int speciesId = tablePlan.NewSpeciesIds[rowIndex];
                int record = speciesId;
                int groupCount = checked(1 + row.ExtraFormGroups + (row.GenderDiff ? 1 : 0));
                byte flags = (byte)ModelGroupInsertion.FormFlags.Base;
                if (row.GenderDiff) flags |= (byte)ModelGroupInsertion.FormFlags.GenderDifference;
                if (row.ExtraFormGroups > 0) flags |= (byte)ModelGroupInsertion.FormFlags.HasForms;

                int currentMembers = checked(models.Members + modelPlan.GroupsAdded * ModelGroupInsertion.FilesPerGroup);
                ModelsLayout? currentLayout = ModelsLayout.Parse(modelPlan.File0, currentMembers, out string parseError);
                if (currentLayout == null) return RefusePlan("models layout became invalid: " + parseError, out refusal);
                if (record < 0 || record >= currentLayout.Records)
                    return RefusePlan($"new species {speciesId} has no models record at {record}", out refusal);

                int photoTemplateGroup = 0;
                int templateGroup = -1;
                if (row.CloneModelFromSlot >= 0)
                {
                    string? templateError = ModelsPlanner.ResolveSpeciesTemplateGroup(currentLayout,
                        row.CloneModelFromSlot, o.ModelRecordShift, out templateGroup);
                    if (templateError != null) return RefusePlan(templateError, out refusal);
                    if ((uint)templateGroup >= (uint)photoSources.Count)
                        return RefusePlan($"template model group {templateGroup} is outside the planned model groups", out refusal);
                    photoTemplateGroup = photoSources[templateGroup];
                }
                else if (photoSources.Count == 0)
                {
                    return RefusePlan("models has no group available for a blank Photo Club record", out refusal);
                }

                int targetRecord = checked(record + o.ModelRecordShift);
                ModelsPlan beforeModelPlan = CopyModelsPlan(modelPlan);
                byte[] beforeFile0 = (byte[])modelPlan.File0.Clone();
                var sourceGroups = Enumerable.Repeat(templateGroup, groupCount).ToArray();
                if (templateGroup >= 0)
                {
                    string? modelError = ModelsPlanner.AddSpecies(modelPlan, models.Layout, record, groupCount,
                        flags, templateGroup, o.ModelRecordShift);
                    if (modelError != null) return RefusePlan(modelError, out refusal);
                }
                else
                {
                    string? blankError = AddBlankSpeciesGroups(modelPlan, currentLayout, record,
                        groupCount, flags, o.ModelRecordShift);
                    if (blankError != null) return RefusePlan(blankError, out refusal);
                }

                int afterMembers = checked(models.Members + modelPlan.GroupsAdded * ModelGroupInsertion.FilesPerGroup);
                ModelsLayout? afterLayout = ModelsLayout.Parse(modelPlan.File0, afterMembers, out parseError);
                if (afterLayout == null) return RefusePlan("models layout became invalid: " + parseError, out refusal);
                if (templateGroup >= 0)
                {
                    modelProblems.AddRange(InsertionVerifier.CheckModels(currentLayout, beforeFile0,
                        beforeModelPlan, modelPlan, targetRecord, groupCount, flags, sourceGroups,
                        o.ModelRecordShift < 0 ? record : null));
                }
                else
                {
                    VerifyBlankSpeciesGroups(currentLayout, beforeFile0, beforeModelPlan, modelPlan,
                        afterLayout, targetRecord, record, groupCount, flags, modelProblems);
                }

                for (int i = 0; i < groupCount; i++)
                {
                    plannedPhotoSources.Add(photoTemplateGroup);
                    photoSources.Add(photoTemplateGroup);
                }
                log.Add($"species models: id {speciesId}, record {targetRecord}, groups +{groupCount}, "
                    + (templateGroup < 0 ? "blank" : $"template group {templateGroup}"));
            }

            var plan = new InsertionPlan
            {
                After = tablePlan.After,
                Models = modelPlan,
                Log = log,
                NewSpeciesIds = tablePlan.NewSpeciesIds,
                NewSpeciesRecords = tablePlan.NewSpeciesIds.Select(id => checked(id + o.ModelRecordShift)).ToArray(),
                PhotoClubSourceGroups = plannedPhotoSources,
            };
            plan.Violations.AddRange(InsertionVerifier.Check(before, tablePlan.After, [], maxSpecies,
                rows.Count, lowestEvolution));
            plan.Violations.AddRange(modelProblems);
            return plan;
        }
        catch (Exception e) when (IsExpectedFailure(e))
        {
            return RefusePlan(e.Message, out refusal);
        }
    }

    public static List<string> VerifyOutput(string romFsIn, string romFsOut, InsertionPlan plan)
    {
        var problems = new List<string>();
        if (plan == null)
        {
            problems.Add("insertion plan is missing");
            return problems;
        }
        string input = RomLayout.ResolveRomFsPath(romFsIn);
        string output = RomLayout.ResolveRomFsPath(romFsOut);
        byte[][][] tableFiles =
        [
            Join(plan.After.Personal, plan.After.BuildMaster()),
            plan.After.Levelup,
            plan.After.Evolution,
            plan.After.Megaevo,
            plan.After.EggMove,
            Join(plan.After.EvolutionBase ?? [], plan.After.BuildEvolutionBaseBlob()),
        ];
        for (int i = 0; i < TablePaths.Length; i++)
            CheckCount(input, output, TablePaths[i], tableFiles[i].Length, problems);

        if (plan.Models == null)
        {
            problems.Add("models plan is missing");
            return problems;
        }

        string modelsInput = ArchivePath(input, "a/0/9/4");
        string modelsOutput = OutputPath(output, "a/0/9/4");
        try
        {
            GARC.GARCFile beforeHeader = ReadHeader(modelsInput);
            GARC.GARCFile afterHeader = ReadHeader(modelsOutput);
            int expectedCount = checked(plan.Models.BaseMembers + plan.Models.Appended.Count);
            if (beforeHeader.fatb.FileCount != plan.Models.BaseMembers)
                problems.Add($"a/0/9/4 input has {beforeHeader.fatb.FileCount} members, plan has {plan.Models.BaseMembers}");
            if (afterHeader.fatb.FileCount != expectedCount || afterHeader.fato.EntryCount != expectedCount)
                problems.Add($"a/0/9/4 output has {afterHeader.fatb.FileCount} members, expected {expectedCount}");
            if (beforeHeader.fatb.FileCount == plan.Models.BaseMembers && afterHeader.fatb.FileCount == expectedCount)
                CheckModelMemberHashes(modelsInput, beforeHeader, modelsOutput, afterHeader, plan.Models, problems);
        }
        catch (Exception e) when (IsExpectedFailure(e))
        {
            problems.Add($"a/0/9/4: {e.Message}");
        }

        CheckCount(input, output, "a/3/2/6",
            checked((plan.Models.BaseGroups) + plan.Models.GroupsAdded), problems);
        return problems;
    }

    private static void CheckCount(string input, string output, string relativePath, int expected,
        List<string> problems)
    {
        try
        {
            GARC.GARCFile sourceHeader = ReadHeader(ArchivePath(input, relativePath));
            GARC.GARCFile outputHeader = ReadHeader(OutputPath(output, relativePath));
            if (outputHeader.fatb.FileCount != expected || outputHeader.fato.EntryCount != expected)
                problems.Add($"{relativePath} output has {outputHeader.fatb.FileCount} members, expected {expected}");
            if (sourceHeader.Version != outputHeader.Version)
                problems.Add($"{relativePath} output version 0x{outputHeader.Version:X4} differs from input 0x{sourceHeader.Version:X4}");
        }
        catch (Exception e) when (IsExpectedFailure(e))
        {
            problems.Add($"{relativePath}: {e.Message}");
        }
    }

    private static void CheckModelMemberHashes(string inputPath, GARC.GARCFile inputHeader,
        string outputPath, GARC.GARCFile outputHeader, ModelsPlan plan, List<string> problems)
    {
        List<MemberRange> inputMembers = Members(inputHeader, inputPath);
        List<MemberRange> outputMembers = Members(outputHeader, outputPath);
        if (inputMembers.Count != plan.BaseMembers || outputMembers.Count != plan.BaseMembers + plan.Appended.Count)
        {
            problems.Add("a/0/9/4 FATB member ranges do not match the planned member counts");
            return;
        }
        byte[] buffer = new byte[HashBufferSize];
        using var inputStream = File.OpenRead(inputPath);
        using var outputStream = File.OpenRead(outputPath);
        for (int index = 1; index < plan.BaseMembers; index++)
        {
            if (!SameHash(inputStream, inputMembers[index], outputStream, outputMembers[index], buffer))
                problems.Add($"a/0/9/4 base member {index} changed");
        }
        for (int index = 0; index < plan.Appended.Count; index++)
        {
            AppendedMember appended = plan.Appended[index];
            MemberRange outputMember = outputMembers[plan.BaseMembers + index];
            bool equal;
            if (appended.FromBase >= 0)
            {
                if ((uint)appended.FromBase >= (uint)inputMembers.Count)
                {
                    problems.Add($"a/0/9/4 appended member {index} refers to missing input member {appended.FromBase}");
                    continue;
                }
                equal = SameHash(inputStream, inputMembers[appended.FromBase], outputStream, outputMember, buffer);
            }
            else
            {
                byte[] bytes = appended.Bytes ?? Array.Empty<byte>();
                equal = outputMember.Length == bytes.LongLength
                    && SHA256.HashData(bytes).AsSpan().SequenceEqual(HashMember(outputStream, outputPath, outputMember, buffer));
            }
            if (!equal) problems.Add($"a/0/9/4 appended member {index} differs from its source");
        }
    }

    private static bool SameHash(FileStream leftStream, MemberRange left, FileStream rightStream,
        MemberRange right, byte[] buffer)
        => left.Length == right.Length && HashMember(leftStream, "input", left, buffer).AsSpan()
            .SequenceEqual(HashMember(rightStream, "output", right, buffer));

    private static byte[] HashMember(FileStream stream, string label, MemberRange member, byte[] buffer)
    {
        if (member.Offset < 0 || member.Length < 0 || member.Offset > long.MaxValue - member.Length)
            throw new InvalidDataException($"member range {member.Offset}+{member.Length} is invalid");
        if (member.Offset + member.Length > stream.Length)
            throw new InvalidDataException($"{label} member range {member.Offset}+{member.Length} extends past {stream.Length} B");
        stream.Position = member.Offset;
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long remaining = member.Length;
        while (remaining > 0)
        {
            int count = checked((int)Math.Min(buffer.Length, remaining));
            stream.ReadExactly(buffer, 0, count);
            hash.AppendData(buffer, 0, count);
            remaining -= count;
        }
        return hash.GetHashAndReset();
    }

    private static List<MemberRange> Members(GARC.GARCFile header, string path)
    {
        var members = new List<MemberRange>(Math.Max(0, header.fatb.FileCount));
        long fileLength = new FileInfo(path).Length;
        foreach (GARC.FATB_Entry entry in header.fatb.Entries)
        {
            foreach (GARC.FATB_SubEntry subEntry in entry.SubEntries)
            {
                if (!subEntry.Exists) continue;
                long offset = (long)subEntry.Start + (long)header.DataOffset;
                long length = subEntry.Length;
                if (offset < 0 || offset > long.MaxValue - length || offset + length > fileLength)
                    throw new InvalidDataException($"member range {offset}+{length} is outside {path}");
                members.Add(new MemberRange(offset, length));
            }
        }
        if (members.Count != header.fatb.FileCount)
            throw new InvalidDataException($"GARC declares {header.fatb.FileCount} members but has {members.Count} member ranges");
        return members;
    }

    private static string OutputPath(string root, string relative)
    {
        string path = ArchivePath(root, relative);
        if (File.Exists(path + ".part")) return path + ".part";
        return path;
    }

    private static ModelsInput? ReadModels(string romFs, out string error)
    {
        error = "";
        string path = ArchivePath(RomLayout.ResolveRomFsPath(romFs), "a/0/9/4");
        if (!File.Exists(path))
        {
            error = "a/0/9/4 is missing";
            return null;
        }
        GARC.GARCFile header = GARC.UnpackGARC(path);
        if (header.fatb.FileCount <= 0)
        {
            error = $"a/0/9/4 has invalid member count {header.fatb.FileCount}";
            return null;
        }
        List<MemberRange> members = Members(header, path);
        MemberRange file0 = members[0];
        if (file0.Length > int.MaxValue)
        {
            error = $"a/0/9/4 file0 length {file0.Length} exceeds {int.MaxValue}";
            return null;
        }
        var bytes = new byte[(int)file0.Length];
        using (FileStream stream = File.OpenRead(path))
        {
            stream.Position = file0.Offset;
            stream.ReadExactly(bytes);
        }
        ModelsLayout? layout = ModelsLayout.Parse(bytes, header.fatb.FileCount, out error);
        return layout == null ? null : new ModelsInput(bytes, header.fatb.FileCount, layout);
    }

    private static bool TryResolveModelGroup(SpeciesTables tables, int templateEntry, int maxSpecies,
        ModelsLayout models, out int group, out string error)
    {
        group = -1;
        error = "";
        if (templateEntry <= 0 || templateEntry >= tables.Personal.Length)
        {
            error = $"template entry {templateEntry} has no species model record";
            return false;
        }
        int owner = templateEntry;
        int form = 0;
        if (templateEntry > maxSpecies)
        {
            owner = -1;
            for (int species = 1; species <= maxSpecies && species < tables.Personal.Length; species++)
            {
                byte[] entry = tables.Personal[species];
                if (entry.Length <= PersonalFormeCountOffset) continue;
                int pointer = BitConverter.ToUInt16(entry, PersonalFormStatsIndexOffset);
                int forms = entry[PersonalFormeCountOffset];
                if (pointer <= 0 || templateEntry < pointer || (long)templateEntry >= (long)pointer + forms - 1) continue;
                owner = species;
                form = templateEntry - pointer + 1;
                break;
            }
            if (owner < 0)
            {
                error = $"template entry {templateEntry} has no owning species model group";
                return false;
            }
        }
        try
        {
            group = models.GroupOf(owner, form);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            error = $"template entry {templateEntry} has no model group for form {form}";
            return false;
        }
    }

    private static string? AddBlankSpeciesGroups(ModelsPlan plan, ModelsLayout currentLayout,
        int record, int groupCount, byte flags, int recordShift)
    {
        if (record < 0 || record >= currentLayout.Records) return $"models record {record} is outside the table";
        if (recordShift is not (0 or -1)) return $"model record shift {recordShift} must be 0 or -1";
        (ushort start, byte count, byte oldFlags) = currentLayout.Record(record);
        if (start != 0 || count != 0 || oldFlags != 0) return $"models record {record} is already in use";
        int targetRecord = checked(record + recordShift);
        if (targetRecord < 0 || targetRecord >= currentLayout.Records)
            return $"new models record {targetRecord} is outside the table";
        if (recordShift < 0)
        {
            if (targetRecord != currentLayout.LastRecord)
                return $"models record {targetRecord} is not the last used record; refusing to move it as the Egg";
        }
        // currentLayout is parsed from the evolving plan, so it already counts groups an earlier species added.
        long groups = currentLayout.Groups;
        long newFile0Length = plan.File0.LongLength + (long)groupCount * sizeof(ushort);
        if (newFile0Length > int.MaxValue) return "models plan does not match its current layout";
        if (groupCount <= 0 || groupCount > byte.MaxValue) return $"species model group count {groupCount} is outside 1..255";
        var grown = new byte[(int)newFile0Length];
        Array.Copy(plan.File0, grown, plan.File0.Length);
        if (recordShift < 0)
            Array.Copy(plan.File0, targetRecord * ModelGroupInsertion.Table1Stride,
                grown, record * ModelGroupInsertion.Table1Stride, ModelGroupInsertion.Table1Stride);
        int recordOffset = checked(targetRecord * ModelGroupInsertion.Table1Stride);
        BitConverter.TryWriteBytes(grown.AsSpan(recordOffset, sizeof(ushort)), checked((ushort)groups));
        grown[recordOffset + 2] = checked((byte)groupCount);
        grown[recordOffset + 3] = flags;
        plan.File0 = grown;
        for (int i = 0; i < groupCount * sizeof(ushort); i++) plan.File0[checked((int)(newFile0Length - groupCount * sizeof(ushort) + i))] = 0;
        for (int i = 0; i < groupCount * ModelGroupInsertion.FilesPerGroup; i++)
            plan.Appended.Add(new AppendedMember(-1, null));
        plan.GroupsAdded = checked(plan.GroupsAdded + groupCount);
        return null;
    }

    private static void VerifyBlankSpeciesGroups(ModelsLayout beforeLayout, byte[] beforeFile0,
        ModelsPlan beforePlan, ModelsPlan afterPlan, ModelsLayout afterLayout, int targetRecord,
        int movedRecord, int groupCount, byte flags, List<string> violations)
    {
        int addedGroups = afterPlan.GroupsAdded - beforePlan.GroupsAdded;
        if (addedGroups != groupCount) violations.Add($"blank model append added {addedGroups} groups, expected {groupCount}");
        if (afterPlan.Appended.Count != beforePlan.Appended.Count + groupCount * ModelGroupInsertion.FilesPerGroup)
            violations.Add("blank model append has an unexpected member count");
        if (targetRecord < 0 || targetRecord >= afterLayout.Records)
        {
            violations.Add($"blank model target record {targetRecord} is outside the resulting table");
            return;
        }
        (ushort start, byte count, byte actualFlags) = afterLayout.Record(targetRecord);
        if (start != beforeLayout.Groups || count != groupCount || actualFlags != flags)
            violations.Add($"blank model target record {targetRecord} is ({start},{count},{actualFlags})");
        if (movedRecord != targetRecord)
        {
            int sourceOffset = checked(targetRecord * ModelGroupInsertion.Table1Stride);
            int destinationOffset = checked(movedRecord * ModelGroupInsertion.Table1Stride);
            if (!beforeFile0.AsSpan(sourceOffset, ModelGroupInsertion.Table1Stride)
                    .SequenceEqual(afterPlan.File0.AsSpan(destinationOffset, ModelGroupInsertion.Table1Stride)))
                violations.Add($"blank model shift did not move record {targetRecord} to {movedRecord}");
        }
        long oldTable2Length = (long)beforeLayout.Groups * sizeof(ushort);
        if (beforeLayout.Table2Offset + oldTable2Length > beforeFile0.LongLength
            || afterLayout.Table2Offset + oldTable2Length > afterPlan.File0.LongLength
            || !beforeFile0.AsSpan(beforeLayout.Table2Offset, checked((int)oldTable2Length))
                .SequenceEqual(afterPlan.File0.AsSpan(afterLayout.Table2Offset, checked((int)oldTable2Length))))
            violations.Add("blank model append changed existing table-2 entries");
        long descriptorOffset = (long)afterLayout.Table2Offset + (long)beforeLayout.Groups * sizeof(ushort);
        long descriptorLength = (long)groupCount * sizeof(ushort);
        if (descriptorOffset < 0 || descriptorOffset + descriptorLength > afterPlan.File0.LongLength
            || afterPlan.File0.AsSpan(checked((int)descriptorOffset), checked((int)descriptorLength)).IndexOfAnyExcept((byte)0) >= 0)
            violations.Add("blank model groups have nonzero table-2 descriptors");
        for (int i = beforePlan.Appended.Count; i < afterPlan.Appended.Count; i++)
            if (afterPlan.Appended[i].FromBase != -1 || afterPlan.Appended[i].Bytes != null)
                violations.Add($"blank model member {i - beforePlan.Appended.Count} contains copied data");
    }

    private static ModelsPlan CopyModelsPlan(ModelsPlan source) => new()
    {
        File0 = (byte[])source.File0.Clone(),
        Appended = [.. source.Appended],
        GroupsAdded = source.GroupsAdded,
        BaseMembers = source.BaseMembers,
        BaseGroups = source.BaseGroups,
    };

    private static byte[][] Join(byte[][] rows, byte[] final)
    {
        var result = new byte[rows.Length + 1][];
        Array.Copy(rows, result, rows.Length);
        result[^1] = final;
        return result;
    }

    private static string ArchivePath(string root, string relative)
        => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static GARC.GARCFile ReadHeader(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("required GARC is missing", path);
        return GARC.UnpackGARC(path);
    }

    private static InsertionPlan? RefusePlan(string reason, out string? refusal)
    {
        refusal = reason;
        return null;
    }

    private static bool IsExpectedFailure(Exception e)
        => e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException
            or ArgumentException or OverflowException or IndexOutOfRangeException or InvalidOperationException;

    private sealed record ModelsInput(byte[] File0, int Members, ModelsLayout Layout);
    private readonly record struct MemberRange(long Offset, long Length);
}
