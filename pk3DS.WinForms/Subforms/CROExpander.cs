using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding;
using pk3DS.Core.Modding.Research;


namespace pk3DS.WinForms
{
    public partial class CROExpander : Form
    {
        private const string CodeBinLabel = "code.bin";

        private byte[] Data;
        private string LoadedPath;

        private bool IsCodeBin;
        private byte[] ExHeaderData;
        private byte[] NcchData;
        private string ExHeaderPath;
        private string NcchPath;

        public CROExpander()
        {
            InitializeComponent();
            PopulateCROList();
            CB_SearchMethod.SelectedIndex = 0;
        }

        private void PopulateCROList()
        {
            CB_CRO.Items.Clear();

            if (!string.IsNullOrEmpty(Main.ExeFSPath) && Directory.Exists(Main.ExeFSPath)
                && File.Exists(ExeFS.ResolveCodeBin(Main.ExeFSPath)))
                CB_CRO.Items.Add(CodeBinLabel);

            if (!string.IsNullOrEmpty(Main.RomFSPath) && Directory.Exists(Main.RomFSPath))
            {
                var croFiles = Directory.GetFiles(Main.RomFSPath, "*.cro", SearchOption.TopDirectoryOnly);
                foreach (var file in croFiles)
                    CB_CRO.Items.Add(Path.GetFileName(file));
            }

            if (CB_CRO.Items.Count > 0)
                CB_CRO.SelectedIndex = 0;
        }

        private void B_Browse_Click(object sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog { Filter = "CRO files|*.cro|All files|*.*" };
            if (ofd.ShowDialog() != DialogResult.OK) return;
            LoadCRO(ofd.FileName);
        }

        private void CB_CRO_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CB_CRO.SelectedIndex < 0) return;
            string selected = CB_CRO.SelectedItem.ToString();
            if (selected == CodeBinLabel) { LoadCodeBin(); return; }
            string path = Path.Combine(Main.RomFSPath, selected);
            LoadCRO(path);
        }

        private void LoadCRO(string path)
        {
            if (!File.Exists(path)) return;
            Data = File.ReadAllBytes(path);
            LoadedPath = path;
            SetCodeBinMode(false);
            bool expanded = ResearchEngine.IsFileExpanded(path, Data);
            string stateStr = expanded ? " [Expanded Mod Active]" : "";
            L_Status.Text = $"Loaded: {Path.GetFileName(path)} ({Data.Length:X} bytes){stateStr}";
        }

        private void LoadCodeBin()
        {
            if (string.IsNullOrEmpty(Main.ExeFSPath) || !Directory.Exists(Main.ExeFSPath))
                return;
            string codePath = ExeFS.ResolveCodeBin(Main.ExeFSPath);
            if (!File.Exists(codePath))
            {
                WinFormsUtil.Error("code.bin not found in ExeFS.");
                return;
            }

            string romRoot = Path.GetDirectoryName(Main.RomFSPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string exhPath = Path.Combine(romRoot ?? "", "ExHeader.bin");
            string ncchPath = Path.Combine(romRoot ?? "", "HeaderNCCH0.bin");

            if (!File.Exists(exhPath))
            {
                WinFormsUtil.Error("ExHeader.bin not found.",
                    $"Expected it next to RomFS/ExeFS at:\n{exhPath}\n\n" +
                    "code.bin expansion needs this file to update the segment size fields.");
                return;
            }

            Data = File.ReadAllBytes(codePath);
            ExHeaderData = File.ReadAllBytes(exhPath);
            NcchData = File.Exists(ncchPath) ? File.ReadAllBytes(ncchPath) : null;
            LoadedPath = codePath;
            ExHeaderPath = exhPath;
            NcchPath = ncchPath;
            SetCodeBinMode(true);

            L_Status.Text = $"Loaded: code.bin ({Data.Length:X} bytes)";
        }

        private void SetCodeBinMode(bool codeBinMode)
        {
            IsCodeBin = codeBinMode;
            RB_Code.Text = codeBinMode ? ".text" : ".code";
            RB_BSS.Text = codeBinMode ? ".rodata" : ".bss";
            RB_Reloc.Enabled = !codeBinMode;
            if (codeBinMode && RB_Reloc.Checked)
                RB_Code.Checked = true;

            Tab_Repoint.Enabled = !codeBinMode;
            if (codeBinMode && TC_Main.SelectedTab == Tab_Repoint)
                TC_Main.SelectedTab = Tab_Expand;
        }


        #region CRO Logic (Using CROUtil)

        private uint ReadU32(int offset) => CROUtil.ReadU32(Data, offset);
        private void WriteU32(uint value, int offset) => CROUtil.WriteU32(Data, value, offset);

        private void ExpandSegment(char section, int bytesToAdd)
        {
            if (Data == null) return;
            Data = CROUtil.ExpandSegment(Data, section, bytesToAdd);
            WinFormsUtil.Alert("Expansion complete!", $"Added {bytesToAdd:X} bytes to section '{section}'.");
            LoadCROFromMemory();
        }

        private void LoadCROFromMemory()
        {
            L_Status.Text = $"Modified: {Path.GetFileName(LoadedPath)} ({Data.Length:X} bytes)";
        }

        private void B_ApplyExpand_Click(object sender, EventArgs e)
        {
            if (Data == null) return;

            if (IsCodeBin)
            {
                var segment = RB_Code.Checked ? CodeBinExpander.Segment.Text
                            : RB_Data.Checked ? CodeBinExpander.Segment.Data
                            : CodeBinExpander.Segment.Rodata;
                int pages = (int)NUD_Pages.Value;

                var result = CodeBinExpander.Expand(Data, ExHeaderData, NcchData, segment, pages, Main.ExeFSPath);
                if (!result.Success)
                {
                    WinFormsUtil.Error("Expansion failed.", result.Error);
                    return;
                }

                Data = result.CodeBin;
                ExHeaderData = result.ExHeader;
                if (NcchData != null) NcchData = result.NcchHeader0;

                string warning = result.UnconfirmedPointerCandidates.Count > 0
                    ? $"\n\n{result.UnconfirmedPointerCandidates.Count} word(s) in .text looked pointer-shaped " +
                      "but weren't backed by an actual LDR [PC,#imm] literal-pool reference anywhere - these " +
                      "were left UNTOUCHED rather than guessed at (this ROM's .text mixes ARM and THUMB code, " +
                      "and a THUMB instruction pair can be indistinguishable from a real pointer with no " +
                      "confirming reference). If any of these were genuinely real pointers (e.g. table/vtable " +
                      "entries with no direct LDR), they'll need relocating by hand. First few file offsets: " +
                      string.Join(", ", result.UnconfirmedPointerCandidates.Take(10).Select(o => $"0x{o:X}"))
                    : "";

                string skipped = result.SkippedStringLikeCandidates.Count > 0
                    ? $"\n\n{result.SkippedStringLikeCandidates.Count} word(s) in .rodata/.data that looked " +
                      "pointer-shaped were left alone because they decode as embedded string text (not real " +
                      "addresses) - expected, not a concern. Everything else pointer-shaped in .rodata/.data " +
                      "WAS relocated (that segment has no ARM/THUMB ambiguity, so the default there is the " +
                      "opposite of .text's: relocate unless it positively looks like text)."
                    : "";

                WinFormsUtil.Alert("Expansion complete!",
                    $"Added {pages * 0x1000:X} bytes to {segment}.\n" +
                    $"Insertion point: 0x{result.InsertionPointFileOffset:X}\n" +
                    $"Pointers relocated (confirmed .text + all non-string .rodata/.data): {result.RelocatedPointers}" + warning + skipped);
                LoadCROFromMemory();
                return;
            }

            char section = ' ';
            if (RB_Code.Checked) section = 'c';
            else if (RB_Data.Checked) section = 'd';
            else if (RB_BSS.Checked) section = 'b';
            else if (RB_Reloc.Checked) section = 'r';

            int amount = (int)NUD_Pages.Value * 0x1000;
            if (section == 'b') amount = (int)NUD_Pages.Value * 4;

            ExpandSegment(section, amount);
        }

        private void RepointExpand(bool isTable)
        {
            if (Data == null) return;
            // 1. Get offsets
            uint segmentTableOffset = ReadU32(0xC8);
            uint[] startTable = new uint[4];
            for (int i = 0; i < 4; i++)
                startTable[i] = ReadU32((int)(segmentTableOffset + i * 0x0C));

            uint patchTableOffset = ReadU32(0x128);
            uint patchTableCount = ReadU32(0x12C);

            // 2. Find the target
            uint findValue = (uint)NUD_FindAddr.Value;
            bool searchByWriteLocation = CB_SearchMethod.SelectedIndex == 0; // 0 = Written To, 1 = Absolute Address

            int targetSegment = -1;
            uint targetAddend = 0;

            for (int i = 0; i < patchTableCount; i++)
            {
                int entryOfs = (int)(patchTableOffset + i * 0x0C);
                uint writeInfo = ReadU32(entryOfs);
                uint addend = ReadU32(entryOfs + 8);
                int seg = Data[entryOfs + 5];

                if (searchByWriteLocation)
                {
                    uint writeAddr = (writeInfo >> 4) + startTable[writeInfo & 0xF];
                    if (writeAddr == findValue)
                    {
                        targetSegment = seg;
                        targetAddend = addend;
                        break;
                    }
                }
                else
                {
                    uint targetAddr = addend + startTable[seg];
                    if (targetAddr == findValue)
                    {
                        targetSegment = seg;
                        targetAddend = addend;
                        break;
                    }
                }
            }

            if (targetSegment == -1)
            {
                WinFormsUtil.Error("No reference found in the relocation table for that address.");
                return;
            }

            uint updateValue = (uint)NUD_NewAddr.Value;

            if (isTable)
            {
                uint tableLen = (uint)NUD_TableLen.Value;
                uint oldTableAbsolute = targetAddend + startTable[targetSegment];

                // Check for unused space (Python optimization)
                while (updateValue + tableLen > Data.Length)
                {
                    if (DialogResult.Yes != WinFormsUtil.Prompt(MessageBoxButtons.YesNo, "Target space is outside current file. Expand .data?"))
                        return;
                    ExpandSegment('d', 0x1000);
                }

                // Copy Data
                for (int i = 0; i < tableLen; i++)
                {
                    Data[updateValue + i] = Data[oldTableAbsolute + i];
                    Data[oldTableAbsolute + i] = CROUtil.FreeSpaceFill; // Blank out old
                }

                int updateSegment = SegmentOf(updateValue, segmentTableOffset, startTable);

                // Update Relocations
                for (int i = 0; i < patchTableCount; i++)
                {
                    int entryOfs = (int)(patchTableOffset + i * 0x0C);
                    uint writeInfo = ReadU32(entryOfs);
                    uint pointedAt = ReadU32(entryOfs + 8);
                    int seg = Data[entryOfs + 5];

                    // check if points AT an address in our table
                    if (seg == targetSegment && pointedAt >= targetAddend && pointedAt < targetAddend + tableLen)
                    {
                        Data[entryOfs + 5] = (byte)updateSegment;
                        CROUtil.WriteU32(Data, updateValue - startTable[updateSegment] + (pointedAt - targetAddend), entryOfs + 8);
                    }

                    // writes TO an address in our table (offsets are per segment, so the segment must match too)
                    uint writeOffset = writeInfo >> 4;
                    if ((int)(writeInfo & 0xF) == targetSegment && writeOffset >= targetAddend && writeOffset < targetAddend + tableLen)
                    {
                        uint newWriteOffset = updateValue + (writeOffset - targetAddend) - startTable[updateSegment];
                        CROUtil.WriteU32(Data, (newWriteOffset << 4) | (uint)updateSegment, entryOfs);
                    }
                }
                WinFormsUtil.Alert("Table moved and references updated!");
            }
            else // Function
            {
                // A relocation's addend is relative to its TARGET segment (byte 5), not to the segment
                // being written. Using the writer's base aimed every pointer stored in .data (vtables,
                // callback tables) at data-base + offset instead of the function.
                int newSeg = SegmentOf(updateValue, segmentTableOffset, startTable);
                int count = 0;
                for (int i = 0; i < patchTableCount; i++)
                {
                    int entryOfs = (int)(patchTableOffset + i * 0x0C);
                    if (ReadU32(entryOfs + 8) == targetAddend && Data[entryOfs + 5] == targetSegment)
                    {
                        Data[entryOfs + 5] = (byte)newSeg;
                        CROUtil.WriteU32(Data, updateValue - startTable[newSeg], entryOfs + 8);
                        count++;
                    }
                }
                WinFormsUtil.Alert("Function repointed!", $"Updated {count} references.");
            }
            LoadCROFromMemory();
        }

        private int SegmentOf(uint fileOffset, uint segmentTableOffset, uint[] startTable)
        {
            uint codeLen = ReadU32((int)segmentTableOffset + 4);
            uint dataStart = ReadU32((int)segmentTableOffset + 0x18);
            if (fileOffset < startTable[0] + codeLen) return 0;
            return fileOffset < dataStart ? 1 : 2;
        }

        private void B_ApplyTable_Click(object sender, EventArgs e) => RepointExpand(true);
        private void B_ApplyFunc_Click(object sender, EventArgs e) => RepointExpand(false);

        private void B_Save_Click(object sender, EventArgs e)
        {
            if (Data == null) return;
            if (pk3DS.Core.Modding.UhuruGuard.RefusalFor(LoadedPath) is { } uhuru) { WinFormsUtil.Alert(uhuru); return; }

            if (IsCodeBin)
            {
                BackupThenWrite(LoadedPath, Data);
                BackupThenWrite(ExHeaderPath, ExHeaderData);
                string savedList = $"{LoadedPath}\n{ExHeaderPath}";
                if (NcchData != null && File.Exists(NcchPath))
                {
                    BackupThenWrite(NcchPath, NcchData);
                    savedList += $"\n{NcchPath}";
                }

                WinFormsUtil.Alert("Saved!",
                    $"{savedList}\n\n" +
                    "IMPORTANT deployment step (confirmed working in Citra/Azahar): this project's " +
                    "normal distribution only ships code.bin itself (see README.txt) - that alone is " +
                    "NOT enough once code.bin has been resized. You must ALSO copy ExHeader.bin into " +
                    "the mods folder's ROOT (next to the exefs/romfs folders, NOT inside exefs) as " +
                    "\"exheader.bin\" - that is what tells Citra/Luma3DS the new code size. Skipping " +
                    "this step is why a resized code.bin can hang at \"Now Loading...\" instead of " +
                    "booting.\n\n" +
                    "Please reload your ROM in pk3DS (File -> Open) so all editors update to the new size.");
                return;
            }

            BackupThenWrite(LoadedPath, Data);
            WinFormsUtil.Alert("Saved!", LoadedPath + "\n\nIMPORTANT: Please reload your ROM in pk3DS (File -> Open) so all editors update to the new expanded file sizes and prevent crashes.");
        }

        private static void BackupThenWrite(string path, byte[] data)
        {
            string bakPath = path + ".bak";
            if (!File.Exists(bakPath)) File.Copy(path, bakPath);
            File.WriteAllBytes(path, data);
        }

        #endregion
    }
}
