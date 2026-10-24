using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>Options for the code, icon, and cry work performed after the Core insertion plan.</summary>
public sealed class SpeciesInsertionRequest
{
    public int ExtraFormGroups;
    public bool HasGenderDifference;
    public int CloneModelFromSlot = -1;
    public bool AddDistinctFormCry;
    public int CloneCryFromMember = -1;
    public int LowestEvolutionSpeciesId = -1;
}

/// <summary>Code-side outputs for one planned species insertion.</summary>
public sealed class SpeciesInsertionResult
{
    public bool Success;
    public string Error = "";
    public List<string> Log { get; } = [];
    public int NewSpeciesSlot;
    public int? NewFormCryMember;
    public byte[][] CryFiles = [];
    public byte[][] PartyIconFiles = [];
    public byte[][] MenuIconFiles = [];
    public byte[] CodeBin;
    public byte[] ExHeader;
    public byte[] NcchHeader0;
    public bool TouchedCodeBin;
    public List<uint> UnconfirmedPointerCandidates = [];
    public List<uint> SkippedStringLikeCandidates = [];

    public string Describe() => string.Join(Environment.NewLine, Log);
}

/// <summary>Applies code.bin, icon, and cry changes for a species already planned by InsertionPipeline.</summary>
public static class SpeciesInsertionOrchestrator
{
    public static SpeciesInsertionResult ApplyCodeSide(
        byte[] codeBin, byte[] exheader, byte[] ncch0, int insertAt,
        SpeciesInsertionRequest request, byte[][] partyIconFiles, byte[][] menuIconFiles, byte[][] cryFiles)
    {
        var r = new SpeciesInsertionResult { NewSpeciesSlot = insertAt };
        if (request == null)
        { r.Error = "species insertion request is missing"; return r; }

        if (codeBin == null)
        {
            r.Error = "code.bin is required to insert a new species - without raising its species-count ceiling to cover the new id, it displays as an Egg in-game no matter how correct the personal-table data is.";
            return r;
        }

        var ceiling = SpeciesCeilingPatcher.Raise(codeBin, insertAt);
        bool ceilingAlreadyCovered = ceiling.Notes.Any(n => n.Contains("nothing to raise"));
        if (ceiling.PatchedCount == 0 && !ceilingAlreadyCovered)
        {
            r.Error = "couldn't raise code.bin's species-count ceiling to cover the new species:\n" + ceiling.Describe();
            return r;
        }
        r.CodeBin = codeBin;
        if (ceiling.PatchedCount > 0)
        {
            r.TouchedCodeBin = true;
            r.Log.Add(ceiling.Describe());
        }
        else
        {
            r.Log.Add($"Species ceiling already covered id {insertAt} - nothing to raise.");
        }

        var eggSentinel = IconEggSentinelPatcher.Raise(codeBin, insertAt);
        if (!eggSentinel.Success)
        {
            r.Error = "couldn't raise code.bin's icon Egg-sentinel to cover the new species:\n" + eggSentinel.Describe();
            return r;
        }
        r.CodeBin = codeBin;
        if (eggSentinel.Patched) r.TouchedCodeBin = true;
        r.Log.Add(eggSentinel.Describe());

        r.PartyIconFiles = partyIconFiles ?? [];
        r.MenuIconFiles = menuIconFiles ?? [];
        if (request.CloneModelFromSlot >= 0 && partyIconFiles != null && menuIconFiles != null)
        {
            var speciesIconTable = SpeciesIconTable.Analyze(codeBin, GameVersion.USUM);
            int? templateIconMember = speciesIconTable.Success
                ? speciesIconTable.ResolveMember(request.CloneModelFromSlot, 0)
                : request.CloneModelFromSlot >= SpeciesIconLayout.StockSpeciesCount
                    ? SpeciesIconLayout.ExpansionRegionMember(request.CloneModelFromSlot)
                    : PartyIconCatalog.Build(partyIconFiles).GroupBySpecies().GetDefaultMember(request.CloneModelFromSlot);
            if (templateIconMember == null)
            { r.Error = $"can't clone icons from species slot {request.CloneModelFromSlot} - it has no real icon in this ROM's partyicon data"; return r; }

            bool? remapped = IconEggSentinelPatcher.IsRemapped(codeBin);
            int newIconMember = speciesIconTable.Success
                ? speciesIconTable.ResolveMember(insertAt, 0) ?? -1
                : SpeciesIconLayout.ExpansionRegionMember(insertAt, remapped ?? false);
            var owner = speciesIconTable.Success ? speciesIconTable.CompactOwnerOf(newIconMember) : null;
            bool unsafeFallback = !speciesIconTable.Success && remapped != true && newIconMember >= SpeciesIconTable.CompactTableFirstMember;
            if (newIconMember < 0 || owner != null || unsafeFallback)
            {
                string who = owner != null ? $"species {owner.Value.Species} form {owner.Value.Form}" : "an existing alt form";
                r.Log.Add($"WARNING: icon NOT cloned. Species {insertAt} resolves to icon member {newIconMember}, which belongs to {who}; writing it would repaint that Pokemon's icon.");
            }
            else
            {
                byte[][] party = partyIconFiles, menu = menuIconFiles;
                if (newIconMember >= party.Length || newIconMember >= menu.Length)
                {
                    party = GrowIconArray(party, newIconMember + 1, templateIconMember.Value);
                    menu = GrowIconArray(menu, newIconMember + 1, templateIconMember.Value);
                    r.Log.Add($"Grew partyicon/menuicon to {party.Length}/{menu.Length} members for icon member {newIconMember}.");
                }

                string preloadError = IconPreloadPatcher.Raise(codeBin, newIconMember, out bool preloadPatched);
                if (preloadError != null) { r.Error = preloadError; return r; }
                if (preloadPatched)
                {
                    r.TouchedCodeBin = true;
                    r.Log.Add($"Icon preload limit raised to member {newIconMember}.");
                }

                var iconCopy = SpeciesIconLayout.CopyIconByMember(party, menu, fromMember: templateIconMember.Value, toMember: newIconMember);
                if (!iconCopy.Success)
                { r.Error = $"can't clone icons from species slot {request.CloneModelFromSlot}: {iconCopy.Error}"; return r; }

                r.PartyIconFiles = iconCopy.PartyIconFiles;
                r.MenuIconFiles = iconCopy.MenuIconFiles;
                r.Log.Add($"Cloned party/menu icon for new species {insertAt} (icon member {iconCopy.ToMember}) from species slot {request.CloneModelFromSlot}'s icon (member {iconCopy.FromMember}).");
            }
        }

        r.CryFiles = cryFiles ?? [];
        r.ExHeader = exheader;
        r.NcchHeader0 = ncch0;
        if (request.AddDistinctFormCry && request.ExtraFormGroups > 0 && cryFiles != null)
        {
            byte[] cryTemplate = request.CloneCryFromMember >= 0 && request.CloneCryFromMember < cryFiles.Length
                ? cryFiles[request.CloneCryFromMember]
                : null;
            var formSplice = SpeciesArraySplicer.Insert(r.CryFiles, insertAt: r.CryFiles.Length, count: 1, template: cryTemplate);
            r.CryFiles = formSplice.Files;
            int newFormMember = r.CryFiles.Length - 1;
            r.NewFormCryMember = newFormMember;

            if (exheader == null)
            { r.Error = "a distinct form cry needs code.bin and ExHeader.bin, but they weren't supplied"; return r; }

            var cryPatch = CryTablePatcher.InsertRecord(codeBin, exheader, ncch0, insertAt, form: 1, cryGarcMember: newFormMember);
            if (!cryPatch.Success) { r.Error = cryPatch.Error; return r; }
            r.CodeBin = cryPatch.CodeBin;
            r.ExHeader = cryPatch.ExHeader;
            r.NcchHeader0 = cryPatch.NcchHeader0;
            r.TouchedCodeBin = true;
            r.UnconfirmedPointerCandidates = cryPatch.UnconfirmedPointerCandidates;
            r.SkippedStringLikeCandidates = cryPatch.SkippedStringLikeCandidates;
            r.Log.Add($"Inserted form cry at cry-GARC member {newFormMember}" +
                      (cryTemplate != null ? $" (cloned from member {request.CloneCryFromMember})" : " (blank placeholder)") +
                      $", code.bin form-table record added (grew .text: {cryPatch.GrewTextSegment}, confirmed pointer relocations: {cryPatch.RelocatedPointers}).");
            if (cryPatch.UnconfirmedPointerCandidates.Count > 0)
                r.Log.Add($"NOTE: {cryPatch.UnconfirmedPointerCandidates.Count} word(s) in .text looked pointer-shaped but had no confirming LDR [PC,#imm] reference, so were left untouched - see UnconfirmedPointerCandidates if any of these need relocating by hand.");
            if (cryPatch.SkippedStringLikeCandidates.Count > 0)
                r.Log.Add($"({cryPatch.SkippedStringLikeCandidates.Count} .rodata/.data word(s) that looked pointer-shaped were left alone because they decode as embedded string text, not addresses - expected, not a concern.)");
        }

        r.Success = true;
        return r;
    }

    private static byte[][] GrowIconArray(byte[][] files, int length, int templateMember)
    {
        if (files.Length >= length) return files;
        var grown = new byte[length][];
        Array.Copy(files, grown, files.Length);
        byte[] filler = templateMember >= 0 && templateMember < files.Length ? files[templateMember] : files[^1];
        for (int i = files.Length; i < length; i++) grown[i] = (byte[])filler.Clone();
        return grown;
    }
}
