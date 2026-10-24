using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using pk3DS.Core.Structures;

namespace pk3DS.WinForms
{
    public partial class OWSE7 : Form
    {
        private readonly LazyGARCFile EncounterData;
        private readonly LazyGARCFile ZoneData;
        private readonly LazyGARCFile WorldData;

        // Was a stock System.Windows.Forms.Design.ByteViewer: it renders its hex/ASCII text through
        // an internal nested control with fixed system colors that WinFormsUtil.ApplyTheme has no
        // hook into, so it stayed white-background/dark-text no matter which theme was active -
        // unreadable next to every other themed control on this tab. A plain RichTextBox fed by
        // Scripts.GetHexLines is the same pattern RTB_7_Raw/RTB_8_Raw already use below, and it picks
        // up real theme colors via ApplyTheme's TextBoxBase branch like everything else here does.
        private readonly RichTextBox byteViewer;
        //private readonly LazyGARCFile WorldData;
        // private readonly LazyGARCFile ZoneData;
        private readonly string[] itemlist = Main.Config.GetText(TextName.ItemNames);

        public OWSE7(LazyGARCFile ed, LazyGARCFile zd, LazyGARCFile wd)
        {
            EncounterData = ed;
            ZoneData = zd;
            WorldData = wd;

            locationList = Main.Config.GetText(TextName.metlist_000000);
            locationList = SMWE.GetGoodLocationList(locationList);

            InitializeComponent();
            
            byteViewer = new RichTextBox
            {
                Location = new System.Drawing.Point(226, 6),
                Size = new System.Drawing.Size(400, 428),
                Dock = DockStyle.Fill,
                ReadOnly = true,
                WordWrap = false,
                Font = new System.Drawing.Font("Courier New", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point),
            };
            hexPanel.Controls.Add(byteViewer);
            
            WinFormsUtil.ApplyTheme(this);

            SetupDGV();
            LoadData();
        }

        private readonly string[] locationList;

        private void LoadData()
        {
            var worlds = WorldData.Files.Select(f => Mini.UnpackMini(f, "WD")[0]).ToArray();
            byte[][] zdfiles = ZoneData.Files;
            var worldData = zdfiles[1];
            var zoneData = zdfiles[0];
            var zones = ZoneData7.GetZoneData7Array(zoneData, worldData, locationList, worlds);

            var areas = new string[EncounterData.FileCount / 11];
            for (int i = 0; i < areas.Length; ++i)
            {
                var names = String.Join(",", zones.Where(z => z.AreaIndex == i).Select((z) => locationList[z.PlaceNameId]));
                areas[i] = $"{i:000} {names}";
            }
            CB_LocationID.Items.AddRange(areas);
            CB_LocationID.SelectedIndex = 0;
        }

        private void CB_LocationID_SelectedIndexChanged(object sender, EventArgs e)
        {
            SetEntry();
            entry = CB_LocationID.SelectedIndex;
            GetEntry();
        }

        private int entry = -1;

        private void SetEntry()
        {
            if (entry < 0)
                return;

            Console.WriteLine($"Setting {CB_LocationID.Text}");
            // research only, no set
        }

        private bool loading;
        private World Map;

        private void GetEntry()
        {
            Console.WriteLine($"Loading {CB_LocationID.Text}");
            int index = entry * 11;
            // 00 - ED (???)
            // 01 - BG (???)
            // 02 - TR (???)
            // 03 - AC (???)
            // 04 - AS (???)
            // 05 - ???
            // 06 - AE (Area Environment)
            // 07 - ZS (Zone Script)
            // 08 - ZI (Zone Info)
            // 09 - EA (Encounter Area)
            // 10 - BG (???)

            if (index > EncounterData.FileCount)
            {
                Console.WriteLine("Out of range.");
                tabControl1.Visible = false;
                return;
            }
            tabControl1.Visible = true;

            Map = new World(EncounterData, entry);
            loading = true;

            NUD_7_Count.Maximum = Map.ZoneScripts.Length;
            NUD_7_Count.Value = Math.Min(Map.ZoneScripts.Length, 1);

            NUD_8_Count.Maximum = Map.ZoneInfoScripts.Length;
            NUD_8_Count.Value = Math.Min(Map.ZoneInfoScripts.Length, 1);
            loading = false;

            LoadDGV();
            LoadTree();
            LoadTrainers();
            LoadNpcs();
            NUD_7_Count_ValueChanged(NUD_7_Count, null);
            NUD_8_Count_ValueChanged(NUD_8_Count, null);
        }

        private class World
        {
            private readonly byte[][] _7;
            private readonly byte[][] _8;
            private readonly byte[][] _envData;
            private readonly byte[][] _itemDataFull;
            private readonly byte[][] _esDataFull;

            private bool HasZS => _7 != null;
            private bool HasZI => _8 != null;
            public readonly Script[] ZoneScripts;
            public readonly Script[] ZoneInfoScripts;
            public List<int> Items;
            public GroundItemTable GroundItems;
            public OWTrainerFile TrainerData;
            public int TrainerDataIndex = -1;
            public FieldNpcTable NpcTable;
            public int NpcTableIndex = -1;

            public World(LazyGARCFile garc, int worldID)
            {
                int index = worldID * 11;
                _7 = Mini.UnpackMini(garc[index + 7], "ZS");
                _8 = Mini.UnpackMini(garc[index + 8], "ZI");


                ZoneScripts = HasZS ? _7.Select(arr => new Script(arr)).ToArray() : Array.Empty<Script>();
                ZoneInfoScripts = HasZI ? _8.Select(arr => new Script(arr)).ToArray() : Array.Empty<Script>();

                _envData = Mini.UnpackMini(garc[index], "ED");
                _itemDataFull = _envData.Length > 10 ? Mini.UnpackMini(_envData[10], "EI") : [];
                _esDataFull = _envData.Length > 4 ? Mini.UnpackMini(_envData[4], "ES") : [];

                if (_itemDataFull.Length > 0 && _esDataFull.Length > 0)
                {
                    GroundItems = new GroundItemTable(_itemDataFull[0], _esDataFull[0]);
                    Items = GroundItems.Items.Select(x => x.ItemID).ToList();
                }
                else
                {
                    List<int> items = new List<int>();
                    foreach (var itemData in _itemDataFull)
                    {
                        if (itemData.Length < 4) continue;
                        int count = BitConverter.ToInt32(itemData, 0);
                        for (int i = 0; i < count; ++i)
                        {
                            int dataId = 4 + i * 64 + 48;
                            if (dataId + 2 <= itemData.Length)
                                items.Add(BitConverter.ToUInt16(itemData, dataId));
                        }
                    }
                    Items = items;
                }

                for (int i = 0; i < _envData.Length; i++)
                {
                    if (_envData[i].Length > 2 && _envData[i][0] == 0x45 && _envData[i][1] == 0x54) // 'E' 'T'
                    {
                        TrainerDataIndex = i;
                        TrainerData = new OWTrainerFile(_envData[i]);
                        break; // We only want the first ET file
                    }
                }

                // The FIRST "EM"-tagged sub-part is the dialogue/gift NPC placement table
                // (character/event/script ids - confirmed live this session against a real
                // PawnGarcEditor placement export). A SECOND "EM"-tagged sub-part exists too (a
                // zone-warp/transition table with an unrelated record shape) - deliberately not
                // this one, so we stop at the first match, same convention as the ET scan above.
                for (int i = 0; i < _envData.Length; i++)
                {
                    if (_envData[i].Length > 2 && _envData[i][0] == 0x45 && _envData[i][1] == 0x4D) // 'E' 'M'
                    {
                        NpcTableIndex = i;
                        NpcTable = new FieldNpcTable(_envData[i]);
                        break;
                    }
                }
            }

            public void WriteNpcTable(LazyGARCFile garc, int worldID)
            {
                if (NpcTableIndex != -1 && NpcTable != null)
                {
                    _envData[NpcTableIndex] = NpcTable.Write();
                    garc[worldID * 11] = Mini.PackMini(_envData, "ED");
                    garc.Save();
                    PawnGarcBridge.InvalidatePgeCache(worldID);
                }
            }

            public bool WriteZoneScript(int scriptIndex, uint[] newScriptCommands, LazyGARCFile garc, int worldID, out string error)
            {
                error = "";
                if (scriptIndex < 0 || scriptIndex >= ZoneScripts.Length) { error = "Invalid script index."; return false; }
                if (!ZoneScripts[scriptIndex].TryWriteResizedScriptCommands(newScriptCommands, out error)) return false;

                _7[scriptIndex] = ZoneScripts[scriptIndex].Raw;
                garc[worldID * 11 + 7] = Mini.PackMini(_7, "ZS");
                garc.Save();
                PawnGarcBridge.InvalidatePgeCache(worldID);
                return true;
            }

            public void WriteItems(LazyGARCFile garc, int worldID)
            {
                if (GroundItems != null && _itemDataFull.Length > 0)
                {
                    _itemDataFull[0] = GroundItems.WriteEI();
                    if (_esDataFull.Length > 0)
                    {
                        _esDataFull[0] = GroundItems.WriteES();
                        _envData[4] = Mini.PackMini(_esDataFull, "ES");
                    }
                    _envData[10] = Mini.PackMini(_itemDataFull, "EI");
                    garc[worldID * 11] = Mini.PackMini(_envData, "ED");
                    garc.Save();
                    PawnGarcBridge.InvalidatePgeCache(worldID);
                    return;
                }

                int listId = 0;
                foreach (var itemData in _itemDataFull)
                {
                    if (itemData.Length < 4) continue;
                    int count = BitConverter.ToInt32(itemData, 0);
                    for (int i = 0; i < count && listId < Items.Count; ++i)
                    {
                        var bytes = BitConverter.GetBytes((ushort)Items[listId]);
                        int dataId = 4 + i * 64 + 48;
                        if (dataId + 2 <= itemData.Length)
                        {
                            itemData[dataId] = bytes[0];
                            itemData[dataId + 1] = bytes[1];
                        }
                        listId += 1;
                    }
                }
                _envData[10] = Mini.PackMini(_itemDataFull, "EI");
                garc[worldID * 11] = Mini.PackMini(_envData, "ED");
                garc.Save();
                PawnGarcBridge.InvalidatePgeCache(worldID);
            }

            public void WriteTrainers(LazyGARCFile garc, int worldID)
            {
                if (TrainerDataIndex != -1 && TrainerData != null)
                {
                    _envData[TrainerDataIndex] = TrainerData.Write();
                    garc[worldID * 11] = Mini.PackMini(_envData, "ED");
                    garc.Save();
                    PawnGarcBridge.InvalidatePgeCache(worldID);
                }
            }
        }

        private void SetupDGV()
        {
            foreach (string t in itemlist)
                dgvItem.Items.Add(t);

            if (dgv.Columns.Count == 2)
            {
                dgv.Columns.Add("Qty", "Qty");
                dgv.Columns.Add("EventID", "Event ID");
                dgv.Columns.Add("Flag", "Flag");
                dgv.Columns.Add("X", "X");
                dgv.Columns.Add("Y", "Y");
                dgv.Columns.Add("Z", "Z");
                dgv.Columns.Add("Model", "Model");
                dgv.Columns.Add("Visible", "Visual");
                dgv.Columns[9].ReadOnly = true;
            }

            dgvTrainers.Columns.Clear();
            dgvTrainers.Columns.Add("Index", "#");
            dgvTrainers.Columns.Add("Type", "Type");
            dgvTrainers.Columns.Add("X", "X");
            dgvTrainers.Columns.Add("Y", "Y");
            dgvTrainers.Columns.Add("Z", "Z");
            dgvTrainers.Columns.Add("Model", "Model ID");
            dgvTrainers.Columns.Add("BattleID", "Battle Script ID");
            dgvTrainers.Columns.Add("EventID", "Event ID");
            dgvTrainers.Columns.Add("Name", "Trainer Name");
            
            dgvTrainers.Columns[0].ReadOnly = true;
            dgvTrainers.Columns[1].ReadOnly = true;
            dgvTrainers.Columns[8].ReadOnly = true;

            dgvNpcs.Columns.Clear();
            dgvNpcs.Columns.Add("Index", "#");
            dgvNpcs.Columns.Add("CharacterId", "Character ID");
            dgvNpcs.Columns.Add("EventId", "Event ID");
            dgvNpcs.Columns.Add("ScriptId", "Script ID");
            dgvNpcs.Columns.Add("X", "X");
            dgvNpcs.Columns.Add("Y", "Y");
            dgvNpcs.Columns.Add("Z", "Z");
            foreach (DataGridViewColumn col in dgvNpcs.Columns) col.ReadOnly = true;
        }

        private void LoadDGV()
        {
            dgv.Rows.Clear();
            if (Map?.GroundItems != null)
            {
                for (int i = 0; i < Map.GroundItems.Items.Count; i++)
                {
                    var it = Map.GroundItems.Items[i];
                    int index = it.ItemID >= 0 && it.ItemID < itemlist.Length ? it.ItemID : 0;
                    dgv.Rows.Add(
                        i,
                        itemlist[index],
                        it.Quantity,
                        it.EventID,
                        it.Flag,
                        it.X.ToString("F1"),
                        it.Y.ToString("F1"),
                        it.Z.ToString("F1"),
                        it.ModelID,
                        it.HasVisualModel ? "Yes" : "Hidden"
                    );
                }
            }
            else if (Map != null)
            {
                foreach (int item in Map.Items)
                {
                    int index = item >= 0 && item < itemlist.Length ? item : 0;
                    dgv.Rows.Add(dgv.Rows.Count, itemlist[index]);
                }
            }
        }

        record struct Entry(string name, int index, byte[] data)
        {
            public bool IsMini { get => name.All(c => c >= 'A' && c <= 'Z'); }
        }
        Dictionary<TreeNode, Entry> ENodeMap = new Dictionary<TreeNode, Entry>();

        private TreeNode createNode(byte[] data, int index)
        {
            string name = "";
            if (data.Length > 0 && data[0] >= 'A' && data[0] <= 'Z')
            {
                name = new string(new char[] { (char)data[0], (char)data[1] });
            }
            else
            {
                name = index.ToString();
            }

            var node = new TreeNode(name);
            ENodeMap[node] = new Entry { name = name, index = index, data = data };
            return node;
        }

        private void LoadTree()
        {
            treeView1.Nodes.Clear();
            ENodeMap.Clear();
            builtNodes.Clear();
            for (int i = 0; i < 11; ++i)
            {
                var node = createNode(EncounterData[entry * 11 + i], i);
                treeView1.Nodes.Add(node);
            }
            treeView1.SelectedNode = treeView1.Nodes[0];
        }
        // Containers whose children have been unpacked into the tree. Repacking one that was never
        // expanded would rebuild it from an empty child list and wipe every sub-file it held.
        private readonly HashSet<TreeNode> builtNodes = [];

        private void SaveNode(TreeNode node)
        {
            while (node != null)
            {
                if (ENodeMap[node].IsMini && builtNodes.Contains(node))
                {
                    var newDatas = node.Nodes.Cast<TreeNode>().Select(n => ENodeMap[n].data).ToArray();
                    ENodeMap[node] = ENodeMap[node] with { data = Mini.PackMini(newDatas, ENodeMap[node].name) };
                }

                // Top-level entries go back into the GARC whether or not they are containers; a
                // non-container root used to be skipped, so importing onto it was silently dropped.
                if (node.Parent == null)
                    EncounterData[entry * 11 + ENodeMap[node].index] = ENodeMap[node].data;
                node = node.Parent;
            }
            EncounterData.Save();
        }

        private void BuildNode(TreeNode node)
        {
            builtNodes.Add(node);
            node.Nodes.Clear();
            var selectedData = ENodeMap[node];
            if (selectedData.IsMini)
            {
                var unpacked = Mini.UnpackMini(selectedData.data, selectedData.name);
                int i = 0;
                foreach (byte[] data in unpacked)
                {
                    var newNode = createNode(data, i++);
                    node.Nodes.Add(newNode);
                }
            }
        }


        private void treeView1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            BuildNode(treeView1.SelectedNode);
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            var selectedNode = treeView1.SelectedNode;
            var selectedData = ENodeMap[selectedNode];
            byteViewer.Lines = Scripts.GetHexLines(selectedData.data, 16);
            path_label.Text = selectedNode.FullPath;
        }

        private void button_Add_Click(object sender, EventArgs e)
        {
            var selectedNode = treeView1.SelectedNode;
            var data = ENodeMap[selectedNode];
            if (!data.IsMini) return;
            if (!builtNodes.Contains(selectedNode)) BuildNode(selectedNode); // keep the existing sub-files
            int index = selectedNode.Nodes.Count;
            var newEntry = new Entry(index.ToString(), index, new byte[] { });

            var newNode = new TreeNode(newEntry.name);
            ENodeMap[newNode] = newEntry;
            selectedNode.Nodes.Add(newNode);
            SaveNode(newNode);
        }

        private void button_Remove_Click(object sender, EventArgs e)
        {
            var selectedNode = treeView1.SelectedNode;
            if (selectedNode.Parent == null) return;
            var parent = selectedNode.Parent;
            parent.Nodes.Remove(selectedNode);
            SaveNode(parent);
            BuildNode(parent);
        }

        private void button_Import_Click(object sender, EventArgs e)
        {
            var dialog = new OpenFileDialog();
            var result = dialog.ShowDialog();
            if (result != DialogResult.OK) return;

            var newData = System.IO.File.ReadAllBytes(dialog.FileName);
            var node = treeView1.SelectedNode;
            ENodeMap[node] = ENodeMap[node] with { data = newData };
            BuildNode(node);
            SaveNode(node);
        }


        private void button_Export_Click(object sender, EventArgs e)
        {
            var dialog = new SaveFileDialog { FileName = CB_LocationID.Text + treeView1.SelectedNode.FullPath.Replace('\\', '.') };
            var result = dialog.ShowDialog();
            if (result != DialogResult.OK) return;
            System.IO.File.WriteAllBytes(dialog.FileName, ENodeMap[treeView1.SelectedNode].data);
        }


        private void button_dump_Click(object sender, EventArgs e)
        {
            var dialog = new FolderBrowserDialog();
            var result = dialog.ShowDialog();
            if (result != DialogResult.OK) return;
            byte[] bytes = new byte[] { };
            for (int i = 0; i < 11; ++i)
            {
                var entry = ENodeMap[treeView1.Nodes[i]];
                System.IO.File.WriteAllBytes(dialog.SelectedPath + "/" + entry.name, entry.data);
            }
        }

        private void NUD_7_Count_ValueChanged(object sender, EventArgs e)
        {
            if (loading)
                return;

            bool vis = ((sender as NumericUpDown)?.Value ?? 0) != 0;
            RTB_7_Raw.Visible = RTB_7_Script.Visible = L_7_Info.Visible = RTB_7_Parse.Visible = vis;
            if (!vis)
                return;

            var script = Map.ZoneScripts[(int)NUD_7_Count.Value - 1];
            L_7_Count.Text = $"Files: {Map.ZoneScripts.Length}";
            RTB_7_Raw.Lines = Scripts.GetHexLines(script.Raw, 16);
            RTB_7_Script.Lines = Scripts.GetHexLines(script.DecompressedInstructions);
            RTB_7_Parse.Lines = script.ParseScript;

            string[] lines =
            {
                "Commands:" + Environment.NewLine + RTB_7_Script.Lines.Length,
                "CBytes:" + Environment.NewLine + script.CompressedBytes.Length,
            };
            L_7_Info.Text = string.Join(Environment.NewLine, lines);
        }

        private void NUD_8_Count_ValueChanged(object sender, EventArgs e)
        {
            if (loading)
                return;

            bool vis = ((sender as NumericUpDown)?.Value ?? 0) != 0;
            RTB_8_Raw.Visible = RTB_8_Script.Visible = L_8_Info.Visible = RTB_8_Parse.Visible = vis;
            if (!vis)
                return;

            var script = Map.ZoneInfoScripts[(int)NUD_8_Count.Value - 1];
            L_8_Count.Text = $"Files: {Map.ZoneInfoScripts.Length}";
            RTB_8_Raw.Lines = Scripts.GetHexLines(script.Raw, 16);
            RTB_8_Script.Lines = Scripts.GetHexLines(script.DecompressedInstructions);
            RTB_8_Parse.Lines = script.ParseScript;

            string[] lines =
            {
                "Commands:" + Environment.NewLine + RTB_8_Script.Lines.Length,
                "CBytes:" + Environment.NewLine + script.CompressedBytes.Length,
            };
            L_8_Info.Text = string.Join(Environment.NewLine, lines);
        }


        private void buttonSave_Click(object sender, EventArgs e)
        {
            if (Map?.GroundItems != null)
            {
                for (int i = 0; i < dgv.Rows.Count && i < Map.GroundItems.Items.Count; ++i)
                {
                    var row = dgv.Rows[i];
                    var it = Map.GroundItems.Items[i];

                    string itemStr = row.Cells[1].Value?.ToString();
                    int itemIdx = Array.IndexOf(itemlist, itemStr);
                    if (itemIdx >= 0) it.ItemID = itemIdx;

                    if (row.Cells.Count > 2 && int.TryParse(row.Cells[2].Value?.ToString(), out int qty)) it.Quantity = qty;
                    if (row.Cells.Count > 3 && uint.TryParse(row.Cells[3].Value?.ToString(), out uint evId)) it.EventID = evId;
                    if (row.Cells.Count > 4 && uint.TryParse(row.Cells[4].Value?.ToString(), out uint flag)) it.Flag = flag;
                    if (row.Cells.Count > 5 && float.TryParse(row.Cells[5].Value?.ToString(), out float x)) it.X = x;
                    if (row.Cells.Count > 6 && float.TryParse(row.Cells[6].Value?.ToString(), out float y)) it.Y = y;
                    if (row.Cells.Count > 7 && float.TryParse(row.Cells[7].Value?.ToString(), out float z)) it.Z = z;
                    if (row.Cells.Count > 8 && uint.TryParse(row.Cells[8].Value?.ToString(), out uint model)) it.ModelID = model;
                }
            }
            else if (Map != null)
            {
                for (int i = 0; i < dgv.Rows.Count && i < Map.Items.Count; ++i)
                {
                    Map.Items[i] = Array.IndexOf(itemlist, dgv.Rows[i].Cells[1].Value);
                }
            }

            Map.WriteItems(EncounterData, entry);
            if (Map.TrainerDataIndex != -1)
                Map.WriteTrainers(EncounterData, entry);
            SetEntry();
            WinFormsUtil.Alert("Overworld items and environment data saved.", "Interaction (EI) and visual model (ES) records synchronized.");
        }

        private void B_AddGroundItem_Click(object sender, EventArgs e)
        {
            if (Map?.GroundItems == null)
            {
                WinFormsUtil.Error("No ground item table found in this zone.");
                return;
            }

            float baseX = 0, baseY = 0, baseZ = 0;
            uint maxEv = 200;
            uint maxFlag = 2500;
            if (Map.GroundItems.Items.Count > 0)
            {
                var template = Map.GroundItems.Items[^1];
                baseX = template.X;
                baseY = template.Y;
                baseZ = template.Z;
                foreach (var it in Map.GroundItems.Items)
                {
                    if (it.EventID > maxEv) maxEv = it.EventID;
                    if (it.Flag > maxFlag) maxFlag = it.Flag;
                }
            }

            var newItem = new GroundItemEntry
            {
                ItemID = 4, // 4 = Poké Ball
                Quantity = 1,
                PickupType = 0,
                Unk56 = 2,
                EventID = maxEv + 1,
                Flag = maxFlag + 1,
                X = baseX,
                Y = baseY,
                Z = baseZ,
                ModelID = 29, // Poké Ball model
                ObjectType = 4,
                HasVisualModel = true
            };

            Map.GroundItems.AddItem(newItem);
            LoadDGV();
            if (dgv.Rows.Count > 0)
                dgv.Rows[^1].Selected = true;
            WinFormsUtil.Alert("Ground item added!", $"Added new visible Poké Ball entity:\nEvent ID: {newItem.EventID}, Flag: {newItem.Flag}\nAdjust position coordinates and item as needed, then click Save.");
        }

        private void LoadTrainers()
        {
            dgvTrainers.Rows.Clear();
            CB_UnusedTrainer.Items.Clear();

            if (Map == null || Map.TrainerDataIndex == -1 || Map.TrainerData == null)
            {
                tab_Trainers.Enabled = false;
                return;
            }

            tab_Trainers.Enabled = true;
            int areaCount = Map.TrainerData.AreaCount;
            NUD_Area.Maximum = Math.Max(0, areaCount - 1);
            NUD_Area.Minimum = 0;
            NUD_Area.Value = 0;

            PopulateTrainerArea(0);
        }

        private void PopulateTrainerArea(int area)
        {
            dgvTrainers.Rows.Clear();
            CB_UnusedTrainer.Items.Clear();

            if (Map.TrainerData == null || area >= Map.TrainerData.Areas.Count)
                return;

            var entries = Map.TrainerData.Areas[area];
            if (entries.Count == 0)
            {
                MessageBox.Show("This area has no trainers!");
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var tr = entries[i];
                string name = "";
                if (tr.BattleScriptID >= 1000)
                {
                    int tId = (int)(tr.BattleScriptID - 1000);
                    var trNames = Main.Config.GetText(TextName.TrainerNames);
                    if (tId >= 0 && tId < trNames.Length)
                        name = trNames[tId];
                }

                dgvTrainers.Rows.Add(
                    i.ToString(),
                    tr.ObjectType.ToString("X2"),
                    tr.X.ToString(),
                    tr.Y.ToString(),
                    tr.Z.ToString(),
                    tr.ModelID.ToString(),
                    tr.BattleScriptID.ToString(),
                    tr.EventID.ToString(),
                    name
                );

                if (tr.ObjectType == 0x07)
                {
                    CB_UnusedTrainer.Items.Add($"[{i}] Model: {tr.ModelID}, Event: {tr.EventID}, Battle: {tr.BattleScriptID}");
                }
            }

            if (CB_UnusedTrainer.Items.Count > 0)
                CB_UnusedTrainer.SelectedIndex = 0;
        }

        private void NUD_Area_ValueChanged(object sender, EventArgs e)
        {
            PopulateTrainerArea((int)NUD_Area.Value);
        }

        private void B_ConvertTrainer_Click(object sender, EventArgs e)
        {
            if (dgvTrainers.CurrentRow == null) return;
            if (CB_UnusedTrainer.SelectedIndex < 0) return;

            int npcIndex = dgvTrainers.CurrentRow.Index;
            string selectedTrainerStr = CB_UnusedTrainer.SelectedItem.ToString();
            int bracketClose = selectedTrainerStr.IndexOf(']');
            if (bracketClose < 0) return;
            if (!int.TryParse(selectedTrainerStr.Substring(1, bracketClose - 1), out int trIndex)) return;

            if (npcIndex == trIndex)
            {
                MessageBox.Show("Cannot swap an entry with itself.");
                return;
            }

            int area = (int)NUD_Area.Value;
            var entries = Map.TrainerData.Areas[area];
            var npc = entries[npcIndex];
            var tr = entries[trIndex];

            uint tempEvent = npc.EventID;
            npc.EventID = tr.EventID;
            tr.EventID = tempEvent;

            uint tempBattle = npc.BattleScriptID;
            npc.BattleScriptID = tr.BattleScriptID;
            tr.BattleScriptID = tempBattle;

            uint tempObj = npc.ObjectType;
            npc.ObjectType = tr.ObjectType;
            tr.ObjectType = tempObj;

            PopulateTrainerArea(area);
            MessageBox.Show("NPC successfully converted to a Trainer using the chosen unused trainer data. Model ID was preserved.");
        }

        private void B_AddTrainer_Click(object sender, EventArgs e)
        {
            if (Map?.TrainerData == null)
            {
                WinFormsUtil.Error("Can't add a trainer here.", "This zone has no trainer-battle data file at all - adding one from scratch isn't supported yet.");
                return;
            }

            int area = (int)NUD_Area.Value;
            if (area >= Map.TrainerData.Areas.Count)
            {
                WinFormsUtil.Error("Invalid area selected.");
                return;
            }

            // Need a real entry to clone as a template (for the not-yet-reverse-engineered
            // sub-structures - collision, movement path, vision "signal" data) - prefer one already
            // in this area, but fall back to any real entry anywhere in this zone.
            var template = Map.TrainerData.Areas[area].FirstOrDefault()
                ?? Map.TrainerData.Areas.SelectMany(a => a).FirstOrDefault();
            if (template == null)
            {
                WinFormsUtil.Error("Can't add a trainer here.", "This zone's trainer-battle file has no existing entries anywhere to clone a template from.");
                return;
            }

            var trainerNames = Main.Config.GetText(TextName.TrainerNames);
            if (!PromptForNewTrainer(trainerNames, (int)template.ModelID, out int trainerId, out int modelId))
                return;

            uint newEventId = Map.TrainerData.Areas[area].Count > 0
                ? Map.TrainerData.Areas[area].Max(x => x.EventID) + 1
                : 101;

            var newEntry = new OWTrainerEntry(template.RawData, 0);
            newEntry.ObjectType = 0x07; // trainer
            newEntry.ModelID = (uint)modelId;
            newEntry.BattleScriptID = (uint)(trainerId + 1000);
            newEntry.EventID = newEventId;
            newEntry.Alias = 0;
            newEntry.RomVersion = 0;
            newEntry.Flagwork = 0;
            newEntry.FlagworkNum = 0;

            byte[] resized = Map.TrainerData.AppendEntry(area, newEntry);
            Map.TrainerData = new OWTrainerFile(resized);

            PopulateTrainerArea(area);
            string trainerName = trainerId >= 0 && trainerId < trainerNames.Length ? trainerNames[trainerId] : $"trainer {trainerId}";
            WinFormsUtil.Alert("Trainer added (in memory).",
                $"A new battle against {trainerName} was added to area {area} - click Save to write it to the ROM.\n\n" +
                "This only creates the trainer's DATA. To place its model on the map and make the battle " +
                "unavoidable, open PawnGarcEditor and position it to block the player's only path through this area " +
                "- that's how mandatory trainer battles already work in this engine, there's no separate \"mandatory\" flag to set.");
        }

        private static bool PromptForNewTrainer(string[] trainerNames, int defaultModelId, out int trainerId, out int modelId)
        {
            trainerId = -1; modelId = defaultModelId;
            using var prompt = new Form
            {
                Width = 420, Height = 200, FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = "Add New Trainer", StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false,
            };
            var trainerLabel = new Label { Left = 15, Top = 20, Text = "Trainer:", AutoSize = true };
            var trainerCombo = new ComboBox { Left = 15, Top = 40, Width = 370, DropDownStyle = ComboBoxStyle.DropDownList };
            for (int i = 0; i < trainerNames.Length; i++)
                trainerCombo.Items.Add($"{i:0000}: {(string.IsNullOrWhiteSpace(trainerNames[i]) ? "(unnamed)" : trainerNames[i])}");
            if (trainerCombo.Items.Count > 0) trainerCombo.SelectedIndex = 0;

            var modelLabel = new Label { Left = 15, Top = 75, Text = "Model ID:", AutoSize = true };
            var modelInput = new NumericUpDown { Left = 15, Top = 95, Width = 120, Minimum = 0, Maximum = 100000, Value = defaultModelId };

            var okButton = new Button { Text = "Add", Left = 215, Top = 130, Width = 75, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "Cancel", Left = 300, Top = 130, Width = 85, DialogResult = DialogResult.Cancel };

            prompt.Controls.AddRange([trainerLabel, trainerCombo, modelLabel, modelInput, okButton, cancelButton]);
            prompt.AcceptButton = okButton;
            prompt.CancelButton = cancelButton;

            if (prompt.ShowDialog() != DialogResult.OK || trainerCombo.SelectedIndex < 0) return false;
            trainerId = trainerCombo.SelectedIndex;
            modelId = (int)modelInput.Value;
            return true;
        }

        private void B_SaveTrainers_Click(object sender, EventArgs e)
        {
            int area = (int)NUD_Area.Value;
            if (Map.TrainerData != null && area < Map.TrainerData.Areas.Count)
            {
                var entries = Map.TrainerData.Areas[area];
                for (int i = 0; i < entries.Count; i++)
                {
                    var row = dgvTrainers.Rows[i];
                    var tr = entries[i];

                    if (float.TryParse(row.Cells[2].Value?.ToString(), out float x)) tr.X = x;
                    if (float.TryParse(row.Cells[3].Value?.ToString(), out float y)) tr.Y = y;
                    if (float.TryParse(row.Cells[4].Value?.ToString(), out float z)) tr.Z = z;
                    
                    if (uint.TryParse(row.Cells[5].Value?.ToString(), out uint model)) tr.ModelID = model;
                    if (uint.TryParse(row.Cells[6].Value?.ToString(), out uint battle)) tr.BattleScriptID = battle;
                    if (uint.TryParse(row.Cells[7].Value?.ToString(), out uint ev)) tr.EventID = ev;
                }
            }

            Map.WriteTrainers(EncounterData, entry);
            WinFormsUtil.Alert("Trainer data saved.", "ROM files and PawnGarcEditor cache updated.");
        }

        private void LoadNpcs()
        {
            dgvNpcs.Rows.Clear();
            if (Map?.NpcTable == null || Map.NpcTable.Entries.Count == 0)
            {
                tab_Npcs.Enabled = false;
                return;
            }
            tab_Npcs.Enabled = true;
            foreach (var npcEntry in Map.NpcTable.Entries)
                dgvNpcs.Rows.Add(dgvNpcs.Rows.Count, npcEntry.CharacterId, npcEntry.EventId, npcEntry.ScriptId,
                    npcEntry.X.ToString("F1"), npcEntry.Y.ToString("F1"), npcEntry.Z.ToString("F1"));
        }

        private void B_AddNpc_Click(object sender, EventArgs e)
        {
            if (Map?.NpcTable == null || Map.NpcTable.Entries.Count == 0)
            {
                WinFormsUtil.Error("Can't add an NPC here.", "This zone has no dialogue/gift NPC data to use as a template.");
                return;
            }
            if (Map.ZoneScripts.Length == 0)
            {
                WinFormsUtil.Error("Can't add an NPC here.", "This zone has no local ZS script to dispatch NPC behavior from.");
                return;
            }

            const int scriptIndex = 0; // every real dispatch switch checked this session lives in ZS[0]
            var script = Map.ZoneScripts[scriptIndex];
            uint[] cmd = script.ScriptCommands;
            var decoded = ScriptEditing.Decode(cmd);
            var switches = SwitchCaseInserter.FindSwitchCaseTables(decoded);
            if (switches.Count == 0)
            {
                WinFormsUtil.Error("Can't add an NPC here.", "This zone's ZS[0] script has no dispatch switch to extend.");
                return;
            }
            int caseTbl = switches.OrderByDescending(idx => decoded.First(d => d.FlatIndex == idx).Args.Count).First();

            var existingScriptIds = Map.NpcTable.Entries.Select(x => (int)x.ScriptId).Distinct().OrderBy(x => x).ToList();
            var rewardCases = SwitchCaseInserter.FindRewardLikeCases(cmd, decoded, caseTbl);

            if (!PromptForNewNpc(existingScriptIds, rewardCases, out int characterId, out bool isReward, out bool isTutor,
                    out int existingScriptIdChoice, out var rewardTemplate, out int newRewardValue))
                return;

            int newScriptId;
            uint[] finalScriptCommands = cmd;
            bool scriptChanged = false;

            if (isReward)
            {
                var cases = SwitchCaseInserter.DecodeCases(cmd, decoded, caseTbl);
                newScriptId = cases.Where(c => c.Value.HasValue).Max(c => c.Value!.Value) + 1;
                var cloneResult = SwitchCaseInserter.AddClonedCase(cmd, decoded, caseTbl, newScriptId,
                    rewardTemplate.BodyStart, rewardTemplate.BodyEnd,
                    new Dictionary<int, int> { [rewardTemplate.VaryingArgIndex] = newRewardValue });
                if (!cloneResult.Success)
                {
                    WinFormsUtil.Error("Couldn't add the reward behavior.", cloneResult.Error);
                    return;
                }
                finalScriptCommands = cloneResult.NewScriptCommands;
                scriptChanged = true;
            }
            else if (isTutor)
            {
                // Move Tutor behavior: assign or alias to tutor case, or create a new tutor case
                var cases = SwitchCaseInserter.DecodeCases(cmd, decoded, caseTbl);
                int nextId = cases.Where(c => c.Value.HasValue).DefaultIfEmpty().Max(c => c.Value ?? 0) + 1;
                newScriptId = nextId;

                // Alias to the first non-zero case or default target if no existing tutor case
                int targetWord = cases.FirstOrDefault(c => c.Value.HasValue).TargetWordIndex;
                if (targetWord == 0 && cases.Count > 0) targetWord = cases[0].TargetWordIndex;

                var aliasResult = SwitchCaseInserter.AddAliasCase(cmd, decoded, caseTbl, newScriptId, targetWord);
                if (aliasResult.Success)
                {
                    finalScriptCommands = aliasResult.NewScriptCommands;
                    scriptChanged = true;
                }
            }
            else
            {
                newScriptId = existingScriptIdChoice;
            }

            // Clone a real, known-working NPC entry (position/rotation/detail-pool references) as the
            // template - the established "borrow a real example rather than synthesize from scratch"
            // approach - then override only what should be unique to the new NPC.
            var templateEntry = Map.NpcTable.Entries[0];
            var newNpcEntry = new FieldNpcEntry(templateEntry.RawData, 0)
            {
                CharacterId = (uint)characterId,
                EventId = Map.NpcTable.Entries.Max(x => x.EventId) + 1,
                ScriptId = (uint)newScriptId,
                Kind = 5,
            };

            byte[] resizedNpcTable = Map.NpcTable.AppendEntry(newNpcEntry);
            Map.NpcTable = new FieldNpcTable(resizedNpcTable);

            if (scriptChanged && !Map.WriteZoneScript(scriptIndex, finalScriptCommands, EncounterData, entry, out string writeErr))
            {
                WinFormsUtil.Error("Couldn't save the new script behavior.", writeErr);
                return;
            }
            Map.WriteNpcTable(EncounterData, entry);

            LoadNpcs();
            string extraMsg = isTutor ? " configured as a Move Tutor." : "";
            WinFormsUtil.Alert("NPC added!", $"A new NPC (CharacterId={characterId}, ScriptId={newScriptId}){extraMsg} was added and saved.\n\n" +
                "This only creates the NPC's data - to place its model on the map, open PawnGarcEditor.");
        }

        private static bool PromptForNewNpc(List<int> existingScriptIds, List<SwitchCaseInserter.RewardLikeCase> rewardCases,
            out int characterId, out bool isReward, out bool isTutor, out int existingScriptIdChoice,
            out SwitchCaseInserter.RewardLikeCase rewardTemplate, out int newRewardValue)
        {
            characterId = 0; isReward = false; isTutor = false; existingScriptIdChoice = 0; rewardTemplate = null; newRewardValue = 0;

            using var prompt = new Form
            {
                Width = 460, Height = 260, FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = "Add New NPC", StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false,
            };

            var charLabel = new Label { Left = 15, Top = 20, Text = "Character ID:", AutoSize = true };
            var charInput = new NumericUpDown { Left = 15, Top = 40, Width = 120, Minimum = 0, Maximum = 100000, Value = 0 };

            var behaviorLabel = new Label { Left = 15, Top = 75, Text = "Behavior:", AutoSize = true };
            var behaviorCombo = new ComboBox { Left = 15, Top = 95, Width = 410, DropDownStyle = ComboBoxStyle.DropDownList };

            var options = new List<(string Label, bool IsReward, bool IsTutor, int ExistingScriptId, SwitchCaseInserter.RewardLikeCase Reward)>
            {
                ("Generic filler (no unique dialogue, Script ID 0)", false, false, 0, null),
                ("Move Tutor (Teach Tutor Moves from Beach Tutor table)", false, true, 0, null),
            };
            foreach (int sid in existingScriptIds.Where(s => s != 0))
                options.Add(($"Same behavior as an existing NPC (Script ID {sid})", false, false, sid, null));
            foreach (var r in rewardCases)
                options.Add(($"Give a reward - like Script ID {r.CaseValue} (value={r.AllArgValues[r.VaryingArgIndex]}), customizable", true, false, 0, r));

            foreach (var o in options) behaviorCombo.Items.Add(o.Label);
            behaviorCombo.SelectedIndex = 0;

            var valueLabel = new Label { Left = 15, Top = 130, Text = "Reward value:", AutoSize = true, Visible = false };
            var valueInput = new NumericUpDown { Left = 15, Top = 150, Width = 120, Minimum = -100000, Maximum = 100000, Visible = false };

            behaviorCombo.SelectedIndexChanged += (_, _) =>
            {
                var chosen = options[behaviorCombo.SelectedIndex];
                valueLabel.Visible = valueInput.Visible = chosen.IsReward;
                if (chosen.IsReward) valueInput.Value = chosen.Reward.AllArgValues[chosen.Reward.VaryingArgIndex];
            };

            var okButton = new Button { Text = "Add", Left = 265, Top = 190, Width = 75, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "Cancel", Left = 350, Top = 190, Width = 85, DialogResult = DialogResult.Cancel };

            prompt.Controls.AddRange([charLabel, charInput, behaviorLabel, behaviorCombo, valueLabel, valueInput, okButton, cancelButton]);
            prompt.AcceptButton = okButton;
            prompt.CancelButton = cancelButton;

            if (prompt.ShowDialog() != DialogResult.OK) return false;

            characterId = (int)charInput.Value;
            var picked = options[behaviorCombo.SelectedIndex];
            isReward = picked.IsReward;
            isTutor = picked.IsTutor;
            existingScriptIdChoice = picked.ExistingScriptId;
            rewardTemplate = picked.Reward;
            newRewardValue = (int)valueInput.Value;
            return true;
        }

        private void B_SaveNpcs_Click(object sender, EventArgs e)
        {
            if (dgvNpcs.Rows.Count > 0 && Map.NpcTable?.Entries != null)
            {
                for (int i = 0; i < Math.Min(dgvNpcs.Rows.Count, Map.NpcTable.Entries.Count); i++)
                {
                    var row = dgvNpcs.Rows[i];
                    var npc = Map.NpcTable.Entries[i];
                    if (uint.TryParse(row.Cells[1].Value?.ToString(), out uint charId)) npc.CharacterId = charId;
                    if (uint.TryParse(row.Cells[2].Value?.ToString(), out uint eventId)) npc.EventId = eventId;
                    if (uint.TryParse(row.Cells[3].Value?.ToString(), out uint scriptId)) npc.ScriptId = scriptId;
                    if (float.TryParse(row.Cells[4].Value?.ToString(), out float x)) npc.X = x;
                    if (float.TryParse(row.Cells[5].Value?.ToString(), out float y)) npc.Y = y;
                    if (float.TryParse(row.Cells[6].Value?.ToString(), out float z)) npc.Z = z;
                }
            }

            Map.WriteNpcTable(EncounterData, entry);
            WinFormsUtil.Alert("NPC data saved.", "ROM files and PawnGarcEditor cache updated.");
        }

        private void B_ExportPgeif_Click(object sender, EventArgs e)
        {
            if (Map == null) return;
            using var sfd = new SaveFileDialog
            {
                Title = "Export Placements (PGEIF)",
                Filter = "PGE Interchange (*.pgeif)|*.pgeif|All files (*.*)|*.*",
                FileName = $"usum_group_{entry:D4}.placements.v1.pgeif",
                OverwritePrompt = true
            };

            if (sfd.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(sfd.FileName))
                return;

            byte[] pgeif = PawnGarcBridge.ExportPgeif(Map.NpcTable, Map.TrainerData, entry);
            File.WriteAllBytes(sfd.FileName, pgeif);
            WinFormsUtil.Alert("Placements exported successfully.",
                $"Exported {pgeif.Length} bytes to:\n{sfd.FileName}\n\nIn PawnGarcEditor, click Placements -> Import PGEIF to load these coordinates.");
        }

        private void B_ImportPgeif_Click(object sender, EventArgs e)
        {
            if (Map == null) return;
            using var ofd = new OpenFileDialog
            {
                Title = "Import Placements (PGEIF)",
                Filter = "PGE Interchange (*.pgeif)|*.pgeif|All files (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(ofd.FileName))
                return;

            byte[] bytes = File.ReadAllBytes(ofd.FileName);
            if (!PawnGarcBridge.ImportPgeif(bytes, Map.NpcTable, Map.TrainerData, out string report))
            {
                WinFormsUtil.Error("Import failed.", report);
                return;
            }

            // Reload UI views with updated coordinates
            LoadNpcs();
            if (Map.TrainerData != null)
                PopulateTrainerArea((int)NUD_Area.Value);

            var res = WinFormsUtil.Prompt(MessageBoxButtons.YesNo,
                report,
                "Would you like to save the imported placements directly to the ROM files now?");

            if (res == DialogResult.Yes)
            {
                Map.WriteNpcTable(EncounterData, entry);
                Map.WriteTrainers(EncounterData, entry);
                WinFormsUtil.Alert("Imported placements saved to ROM.");
            }
        }

        private void B_ClearPgeCache_Click(object sender, EventArgs e)
        {
            bool cleared = PawnGarcBridge.InvalidatePgeCache(entry);
            if (cleared)
            {
                WinFormsUtil.Alert("PawnGarcEditor VFS cache cleared.",
                    $"Area {entry} cache files have been cleared. PawnGarcEditor will reload fresh ROM data on next access.");
            }
            else
            {
                WinFormsUtil.Alert("PawnGarcEditor VFS cache",
                    "PGE VFS cache directory was not found or is already clean.");
            }
        }
    }
}