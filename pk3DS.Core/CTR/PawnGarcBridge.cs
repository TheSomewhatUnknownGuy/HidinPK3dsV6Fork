using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core.Structures;

namespace pk3DS.Core.CTR
{
    public static class PawnGarcBridge
    {
        private static readonly byte[] PgeifMagic = [(byte)'P', (byte)'G', (byte)'E', (byte)'I'];

        public static string FindPgeVfsDir()
        {
            string[] baseDirs =
            [
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory()
            ];

            foreach (var baseDir in baseDirs)
            {
                if (string.IsNullOrWhiteSpace(baseDir)) continue;

                var dir = new DirectoryInfo(baseDir);
                for (int level = 0; level < 10 && dir != null; level++)
                {
                    string candidate = Path.Combine(dir.FullName, "PawnGarcEditor_release_0.4.0", "PawnGarcEditor", "VFS");
                    if (Directory.Exists(candidate))
                        return candidate;

                    candidate = Path.Combine(dir.FullName, "PawnGarcEditor", "VFS");
                    if (Directory.Exists(candidate))
                        return candidate;

                    try
                    {
                        var matching = dir.GetDirectories("*PawnGarc*", SearchOption.TopDirectoryOnly);
                        foreach (var m in matching)
                        {
                            string nested = Path.Combine(m.FullName, "PawnGarcEditor", "VFS");
                            if (Directory.Exists(nested))
                                return nested;
                            nested = Path.Combine(m.FullName, "VFS");
                            if (Directory.Exists(nested))
                                return nested;
                        }
                    }
                    catch
                    {
                        // Ignore permission issues during directory search
                    }

                    dir = dir.Parent;
                }
            }

            return null;
        }

        public static bool InvalidatePgeCache(int worldID = -1)
        {
            string vfs = FindPgeVfsDir();
            if (string.IsNullOrEmpty(vfs))
                return false;

            string[] subVariants = ["USUM", "SM"];
            bool anyCleared = false;

            foreach (var variant in subVariants)
            {
                string garcCache = Path.Combine(vfs, variant, "romfs", "a", "0", "8", "2", "_garc");
                if (!Directory.Exists(garcCache))
                    continue;

                try
                {
                    if (worldID >= 0)
                    {
                        int baseMember = worldID * 11;
                        int[] members = [baseMember, baseMember + 7, baseMember + 8];
                        foreach (int m in members)
                        {
                            string raw = Path.Combine(garcCache, $"member_{m:D4}_raw.bin");
                            string dec = Path.Combine(garcCache, $"member_{m:D4}_dec.bin");
                            if (File.Exists(raw)) File.Delete(raw);
                            if (File.Exists(dec)) File.Delete(dec);
                        }

                        string meta = Path.Combine(garcCache, "_source_meta.txt");
                        if (File.Exists(meta)) File.Delete(meta);
                    }
                    else
                    {
                        var files = Directory.GetFiles(garcCache);
                        foreach (var f in files)
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                    anyCleared = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PawnGarcBridge] Failed to invalidate cache in {variant}: {ex.Message}");
                }
            }

            return anyCleared;
        }

        public static byte[] ExportPgeif(FieldNpcTable npcTable, OWTrainerFile trFile, int groupIndex)
        {
            var root = new JsonObject
            {
                ["pgeif"] = "placements",
                ["version"] = 1,
                ["usum_field_area_group_index"] = (uint)groupIndex,
                ["group_index"] = groupIndex
            };

            var placementsArr = new JsonArray();
            uint idCounter = 0;

            // 1. Export 01_EM (Field NPCs)
            if (npcTable?.Entries != null)
            {
                for (int i = 0; i < npcTable.Entries.Count; i++)
                {
                    var e = npcTable.Entries[i];
                    var obj = new JsonObject
                    {
                        ["id"] = idCounter++,
                        ["kind"] = "01_EM",
                        ["pack_idx"] = 1,
                        ["zone_idx"] = 0,
                        ["entry_index"] = i,
                        ["x"] = (double)e.X,
                        ["y"] = (double)e.Y,
                        ["z"] = (double)e.Z,
                        ["meta_u32"] = new JsonObject
                        {
                            ["character_id"] = e.CharacterId,
                            ["event_id"] = e.EventId,
                            ["script_id"] = e.ScriptId
                        }
                    };
                    placementsArr.Add(obj);
                }
            }

            // 2. Export 07_ET (Trainers)
            if (trFile?.Areas != null)
            {
                for (int a = 0; a < trFile.Areas.Count; a++)
                {
                    for (int t = 0; t < trFile.Areas[a].Count; t++)
                    {
                        var e = trFile.Areas[a][t];
                        var obj = new JsonObject
                        {
                            ["id"] = idCounter++,
                            ["kind"] = "07_ET",
                            ["pack_idx"] = 7,
                            ["zone_idx"] = a,
                            ["entry_index"] = t,
                            ["x"] = (double)e.X,
                            ["y"] = (double)e.Y,
                            ["z"] = (double)e.Z,
                            ["meta_u32"] = new JsonObject
                            {
                                ["battle_trainer_id"] = e.BattleScriptID >= 1000 ? e.BattleScriptID - 1000 : e.BattleScriptID,
                                ["character_id"] = e.ModelID,
                                ["event_id"] = e.EventID,
                                ["script_id"] = e.BattleScriptID,
                                ["object_type"] = e.ObjectType,
                                ["model_id"] = e.ModelID,
                                ["flagwork"] = e.Flagwork,
                                ["flagwork_num"] = e.FlagworkNum,
                                ["rom_version"] = e.RomVersion
                            }
                        };
                        placementsArr.Add(obj);
                    }
                }
            }

            root["placements"] = placementsArr;

            string jsonStr = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonStr);

            byte[] pgeif = new byte[16 + jsonBytes.Length];
            Array.Copy(PgeifMagic, 0, pgeif, 0, 4);
            BitConverter.GetBytes((uint)1).CopyTo(pgeif, 4);
            BitConverter.GetBytes((uint)jsonBytes.Length).CopyTo(pgeif, 8);
            BitConverter.GetBytes((uint)0).CopyTo(pgeif, 12);
            Array.Copy(jsonBytes, 0, pgeif, 16, jsonBytes.Length);

            return pgeif;
        }

        public static bool ImportPgeif(byte[] pgeifBytes, FieldNpcTable npcTable, OWTrainerFile trFile, out string report)
        {
            report = "";
            if (pgeifBytes == null || pgeifBytes.Length < 16)
            {
                report = "Invalid PGEIF file: size is less than 16 bytes.";
                return false;
            }

            if (pgeifBytes[0] != 'P' || pgeifBytes[1] != 'G' || pgeifBytes[2] != 'E' || pgeifBytes[3] != 'I')
            {
                report = "Invalid PGEIF file: magic header mismatch (expected 'PGEI').";
                return false;
            }

            uint jsonLen = BitConverter.ToUInt32(pgeifBytes, 8);
            if (jsonLen == 0 || 16 + jsonLen > (uint)pgeifBytes.Length)
            {
                report = "Invalid PGEIF header: json_len is out of range.";
                return false;
            }

            string json = Encoding.UTF8.GetString(pgeifBytes, 16, (int)jsonLen).TrimEnd('\0', ' ');
            JsonNode doc;
            try
            {
                doc = JsonNode.Parse(json);
            }
            catch (Exception ex)
            {
                report = $"Failed to parse PGEIF JSON: {ex.Message}";
                return false;
            }

            if (doc?["pgeif"]?.GetValue<string>() != "placements")
            {
                report = "Unsupported PGEIF kind (expected 'placements').";
                return false;
            }

            var placementsArr = doc["placements"]?.AsArray();
            if (placementsArr == null)
            {
                report = "PGEIF does not contain a valid 'placements' array.";
                return false;
            }

            int emUpdated = 0;
            int etUpdated = 0;

            foreach (var item in placementsArr)
            {
                if (item == null) continue;
                string kind = item["kind"]?.GetValue<string>() ?? "";
                int entryIdx = item["entry_index"]?.GetValue<int>() ?? 0;
                int zoneIdx = item["zone_idx"]?.GetValue<int>() ?? 0;
                float x = (float)(item["x"]?.GetValue<double>() ?? 0.0);
                float y = (float)(item["y"]?.GetValue<double>() ?? 0.0);
                float z = (float)(item["z"]?.GetValue<double>() ?? 0.0);

                var meta = item["meta_u32"];

                if (kind == "01_EM" && npcTable?.Entries != null)
                {
                    if (entryIdx >= 0 && entryIdx < npcTable.Entries.Count)
                    {
                        var npc = npcTable.Entries[entryIdx];
                        npc.X = x;
                        npc.Y = y;
                        npc.Z = z;
                        if (meta?["character_id"] != null) npc.CharacterId = meta["character_id"].GetValue<uint>();
                        if (meta?["event_id"] != null) npc.EventId = meta["event_id"].GetValue<uint>();
                        if (meta?["script_id"] != null) npc.ScriptId = meta["script_id"].GetValue<uint>();
                        emUpdated++;
                    }
                }
                else if (kind == "07_ET" && trFile?.Areas != null)
                {
                    if (zoneIdx >= 0 && zoneIdx < trFile.Areas.Count)
                    {
                        var area = trFile.Areas[zoneIdx];
                        if (entryIdx >= 0 && entryIdx < area.Count)
                        {
                            var tr = area[entryIdx];
                            tr.X = x;
                            tr.Y = y;
                            tr.Z = z;
                            if (meta?["character_id"] != null) tr.ModelID = meta["character_id"].GetValue<uint>();
                            if (meta?["model_id"] != null) tr.ModelID = meta["model_id"].GetValue<uint>();
                            if (meta?["script_id"] != null) tr.BattleScriptID = meta["script_id"].GetValue<uint>();
                            else if (meta?["battle_trainer_id"] != null) tr.BattleScriptID = meta["battle_trainer_id"].GetValue<uint>() + 1000;
                            if (meta?["event_id"] != null) tr.EventID = meta["event_id"].GetValue<uint>();
                            if (meta?["object_type"] != null) tr.ObjectType = meta["object_type"].GetValue<uint>();
                            etUpdated++;
                        }
                    }
                }
            }

            report = $"Successfully imported PGEIF: {emUpdated} NPCs and {etUpdated} Trainers updated.";
            return true;
        }
    }
}
