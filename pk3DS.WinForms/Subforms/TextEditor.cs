using pk3DS.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace pk3DS.WinForms;

public partial class TextEditor : Form
{
    public TextEditor(string[][] infiles, string mode)
    {
        InitializeComponent();
        int currentRowIndex = -1;
        bool isSyncing = false;
        RTB_Visualizer.ReadOnly = false;
        
        // 1. Precise Shift+Enter handling (Ensuring it only runs ONCE)
        RTB_Visualizer.KeyDown += (s, e) => 
        {
            if (e.KeyCode == Keys.Enter && e.Shift)
            {
                // This completely kills the native Windows newline injection
                e.SuppressKeyPress = true; 
                e.Handled = true;
                
                // Manually insert exactly ONE physical newline
                RTB_Visualizer.SelectedText = "\n"; 
            }
        };

        // 2. Synchronize BACK to the grid (With Hardcoded Cursor Lock)
        RTB_Visualizer.TextChanged += (s, e) => 
        {
            if (isSyncing || dgv.CurrentRow == null || !RTB_Visualizer.Focused) return;
            
            isSyncing = true;
            
            // HARDCODE: Save the exact cursor position before touching the grid
            int savedCursorPos = RTB_Visualizer.SelectionStart;
            
            // Normalize any Windows (\r\n) or Mac (\r) returns into a standard \n before converting to literal tags
            string rawText = RTB_Visualizer.Text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n");
            
            if (dgv.CurrentRow.Cells[1].Value?.ToString() != rawText)
            {
                dgv.CurrentRow.Cells[1].Value = rawText;
            }
            
            // HARDCODE: Force the cursor back to the correct spot so typing moves forward
            RTB_Visualizer.SelectionStart = savedCursorPos;
            RTB_Visualizer.ScrollToCaret();

            isSyncing = false;
            UpdatePreview();
        };

        // 3. Load text ONLY on a verified row change
        dgv.SelectionChanged += (s, e) =>
        {
            if (isSyncing || dgv.CurrentRow == null) return;
            if (dgv.CurrentRow.Index == currentRowIndex) return;

            currentRowIndex = dgv.CurrentRow.Index;

            isSyncing = true;
            string text = dgv.CurrentRow.Cells[1].Value?.ToString() ?? "";
            RTB_Visualizer.Text = text.Replace("\\n", "\n");
            isSyncing = false;
            UpdatePreview();
        };

        // 4. Handle direct grid edits
        dgv.CellValueChanged += (s, e) =>
        {
            if (isSyncing || dgv.CurrentRow == null) return;
            if (RTB_Visualizer.Focused) return;

            isSyncing = true;
            string text = dgv.CurrentRow.Cells[1].Value?.ToString() ?? "";
            RTB_Visualizer.Text = text.Replace("\\n", "\n");
            isSyncing = false;
            UpdatePreview();
        };

        files = infiles;
        Mode = mode;

        for (int i = 0; i < files.Length; i++)
            CB_Entry.Items.Add(DescribeTextFile(i));
        CB_Entry.SelectedIndex = 0;
        dgv.EditMode = DataGridViewEditMode.EditOnEnter;

        PopulateVariablePicker();
        PopulateColorPicker();
        UpdatePreview();
    }

    private readonly string[][] files;
    private readonly string Mode;
    private int entry = -1;

    /// <summary>
    /// Label for one text file: its real name when known, otherwise a preview of what is inside it.
    /// <para>
    /// The names come from <see cref="GameConfig.GameText"/>, which is already selected per game
    /// version, rather than from indices written out by hand. The previous list hard-coded USUM
    /// indices under a "Gen 6" comment and then repeated them in version-guarded branches that
    /// could never run - the unguarded branch above always matched first - so the guards were dead
    /// and one of them (104) named the Battle Tree table as Trainer Names.
    /// </para>
    /// <para>
    /// Most of the archive has no assigned name at all. Rather than leave those as a bare number,
    /// the first line of actual text is shown, which is usually enough to recognise the file.
    /// </para>
    /// </summary>
    private string DescribeTextFile(int index)
    {
        string label = index.ToString("000");
        if (Mode != "gametext")
            return label;

        var reference = Main.Config?.GameText;
        if (reference != null)
        {
            foreach (var r in reference)
            {
                if (r.Index == index)
                    return $"{label} - {Prettify(r.Name.ToString())}";
            }
        }

        string preview = FirstLineOf(index);
        return preview.Length == 0 ? label : $"{label} - “{preview}”";
    }

    /// <summary>"SpeciesClassifications" -> "Species Classifications".</summary>
    private static string Prettify(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");

    private string FirstLineOf(int index)
    {
        if (files == null || index < 0 || index >= files.Length) return "";
        var lines = files[index];
        if (lines == null) return "";

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            // Strip control codes AND [VAR ...]/bracket noise so the combo-box preview reads as
            // plain text, collapsing onto one line for display here.
            string clean = StripControlCodes(line).Replace(Environment.NewLine, " ").Trim();
            if (clean.Length == 0) continue;
            return clean.Length > 40 ? clean[..40].TrimEnd() + "..." : clean;
        }
        return "";
    }

    private void B_Export_Click(object sender, EventArgs e)
    {
        if (files.Length == 0) return;
        var Dump = new SaveFileDialog { Filter = "Text File|*.txt" };
        if (Dump.ShowDialog() != DialogResult.OK) return;
        bool newline = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, "Remove newline formatting codes? (\\n,\\r,\\c)", "Removing newline formatting will make it more readable but will prevent any importing of that dump.") == DialogResult.Yes;
        ExportTextFile(Dump.FileName, newline, files);
    }

    private void B_Import_Click(object sender, EventArgs e)
    {
        if (files.Length == 0) return;
        var Dump = new OpenFileDialog { Filter = "Text File|*.txt" };
        if (Dump.ShowDialog() != DialogResult.OK) return;
        
        if (!ImportTextFile(Dump.FileName)) return;

        ChangeEntry(null, null);
        WinFormsUtil.Alert("Imported Text from Input Path:", Dump.FileName);
    }

    public static void ExportTextFile(string fileName, bool newline, string[][] fileData)
    {
        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xFE], 0, 2); // Write Unicode BOM
        using (var tw = new StreamWriter(ms, new UnicodeEncoding()))
        {
            for (int i = 0; i < fileData.Length; i++)
            {
                string[] data = fileData[i];
                tw.WriteLine("~~~~~~~~~~~~~~~");
                tw.WriteLine("Text File : " + i);
                tw.WriteLine("~~~~~~~~~~~~~~~");
                if (data == null) continue;
                foreach (string line in data)
                {
                    tw.WriteLine(newline
                        ? line.Replace("\\n\\n", " ").Replace("\\n", " ").Replace("\\c", "").Replace("\\r", "").Replace("\\\\", "\\").Replace("\\[", "[")
                        : line);
                }
            }
        }
        File.WriteAllBytes(fileName, ms.ToArray());
    }

    private bool ImportTextFile(string fileName)
    {
        string[] fileText = File.ReadAllLines(fileName, Encoding.Unicode);
        string[][] textLines = new string[files.Length][];
        int ctr = 0;
        bool newlineFormatting = false;
        
        for (int i = 0; i < fileText.Length; i++)
        {
            string line = fileText[i];
            if (line != "~~~~~~~~~~~~~~~") continue;
            string[] brokenLine = fileText[i++ + 1].Split([" : "], StringSplitOptions.None);
            if (brokenLine.Length != 2 || Util.ToInt32(brokenLine[1]) != ctr)
            { WinFormsUtil.Error($"Invalid Line @ {i}, expected Text File : {ctr}"); return false; }
            i += 2; 
            List<string> Lines = [];
            while (i < fileText.Length && fileText[i] != "~~~~~~~~~~~~~~~")
            {
                Lines.Add(fileText[i]);
                newlineFormatting |= fileText[i].Contains("\\n"); 
                i++;
            }
            i--;
            textLines[ctr++] = [.. Lines];
        }

        if (ctr != files.Length)
        {
            WinFormsUtil.Error("The amount of Text Files in the input file does not match.", $"Received: {ctr}, Expected: {files.Length}"); return false;
        }
        if (!newlineFormatting)
        {
            WinFormsUtil.Error("The input Text Files do not have the ingame newline formatting codes (\\n,\\r,\\c).", "When exporting text, do not remove newline formatting."); return false;
        }

        for (int i = 0; i < files.Length; i++)
        {
            try { files[i] = textLines[i]; }
            catch (Exception e) { WinFormsUtil.Error($"The input Text File (# {i}) failed to convert:", e.ToString()); return false; }
        }

        return true;
    }

    private void ChangeEntry(object sender, EventArgs e)
    {
        if (entry > -1 && sender != null)
        {
            try { files[entry] = GetCurrentDGLines(); }
            catch (Exception ex) { WinFormsUtil.Error(ex.ToString()); }
        }

        entry = CB_Entry.SelectedIndex;
        SetStringsDataGridView(files[entry]);
    }

    private void SetStringsDataGridView(string[] textArray)
    {
        dgv.Rows.Clear();
        dgv.Columns.Clear();
        if (textArray == null || textArray.Length == 0) return;
        
        dgv.AllowUserToResizeColumns = false;
        DataGridViewColumn dgvLine = new DataGridViewTextBoxColumn
        {
            HeaderText = "Line", DisplayIndex = 0, Width = 32, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        dgvLine.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        var dgvText = new DataGridViewTextBoxColumn
        {
            HeaderText = "Text", DisplayIndex = 1, SortMode = DataGridViewColumnSortMode.NotSortable, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        };

        dgv.Columns.Add(dgvLine);
        dgv.Columns.Add(dgvText);
        dgv.Rows.Add(textArray.Length);

        for (int i = 0; i < textArray.Length; i++)
        {
            dgv.Rows[i].Cells[0].Value = i;
            dgv.Rows[i].Cells[1].Value = textArray[i];
        }
    }

    private string[] GetCurrentDGLines()
    {
        string[] lines = new string[dgv.RowCount];
        for (int i = 0; i < dgv.RowCount; i++)
            lines[i] = (string)dgv.Rows[i].Cells[1].Value;
        return lines;
    }
    


    private int GetNumberPrompt(string title, string promptText, int defaultValue)
    {
        using Form promptForm = new Form()
        {
            Width = 320, Height = 170, FormBorderStyle = FormBorderStyle.FixedDialog, 
            Text = title, StartPosition = FormStartPosition.CenterParent, 
            MaximizeBox = false, MinimizeBox = false
        };
        Label textLabel = new Label() { Left = 20, Top = 20, Text = promptText, AutoSize = true };
        NumericUpDown nud = new NumericUpDown() { Left = 20, Top = 50, Width = 260, Minimum = 1, Maximum = 1000, Value = defaultValue };
        Button confirmation = new Button() { Text = "OK", Left = 110, Width = 100, Top = 90, DialogResult = DialogResult.OK };
        
        confirmation.Click += (s, e) => { promptForm.DialogResult = DialogResult.OK; promptForm.Close(); };
        
        promptForm.Controls.Add(nud);
        promptForm.Controls.Add(confirmation);
        promptForm.Controls.Add(textLabel);
        promptForm.AcceptButton = confirmation;

        return promptForm.ShowDialog() == DialogResult.OK ? (int)nud.Value : 0;
    }

    private void B_AddLine_Click(object sender, EventArgs e)
    {
        int count = GetNumberPrompt("Add Lines", "How many lines do you want to add after the selection?", 1);
        if (count <= 0) return;

        int currentRow = 0;
        bool appendToEnd = false;
        try { currentRow = dgv.CurrentRow.Index; } catch { appendToEnd = true; } 

        if (appendToEnd)
        {
            dgv.Rows.Add(count);
        }
        else
        {
            if (dgv.Rows.Count != 1 && (currentRow < dgv.Rows.Count - 1 || currentRow == 0))
                if (ModifierKeys != Keys.Control && currentRow != 0 && WinFormsUtil.Prompt(MessageBoxButtons.YesNo, $"Inserting {count} row(s) in between lines will shift all subsequent lines.", "Continue?") != DialogResult.Yes) return;
            
            for (int i = 0; i < count; i++) dgv.Rows.Insert(currentRow + 1 + i);
        }

        for (int i = 0; i < dgv.Rows.Count; i++) dgv.Rows[i].Cells[0].Value = i.ToString();
    }

    private void B_AddLineBefore_Click(object sender, EventArgs e)
    {
        int count = GetNumberPrompt("Add Lines", "How many lines do you want to add before the selection?", 1);
        if (count <= 0) return;

        int currentRow = 0;
        bool appendToEnd = false;
        try { currentRow = dgv.CurrentRow.Index; } catch { appendToEnd = true; }

        if (appendToEnd)
        {
            dgv.Rows.Add(count);
        }
        else
        {
            if (dgv.Rows.Count != 1)
                if (ModifierKeys != Keys.Control && WinFormsUtil.Prompt(MessageBoxButtons.YesNo, $"Inserting {count} row(s) before lines will shift all subsequent lines.", "Continue?") != DialogResult.Yes) return;
            
            for (int i = 0; i < count; i++) dgv.Rows.Insert(currentRow);
        }

        for (int i = 0; i < dgv.Rows.Count; i++) dgv.Rows[i].Cells[0].Value = i.ToString();
    }

    private void B_RemoveLine_Click(object sender, EventArgs e)
    {
        if (dgv.CurrentRow == null) return;
        int currentRow = dgv.CurrentRow.Index;
        int count = GetNumberPrompt("Remove Lines", "How many lines do you want to remove?", 1);
        if (count <= 0) return;

        if (currentRow + count > dgv.Rows.Count) count = dgv.Rows.Count - currentRow; 
        if (currentRow < dgv.Rows.Count - count)
            if (ModifierKeys != Keys.Control && DialogResult.Yes != WinFormsUtil.Prompt(MessageBoxButtons.YesNo, $"Deleting {count} row(s) above other lines will shift all subsequent lines.", "Continue?")) return;

        for (int i = 0; i < count; i++) dgv.Rows.RemoveAt(currentRow);
        for (int i = 0; i < dgv.Rows.Count; i++) dgv.Rows[i].Cells[0].Value = i.ToString();
    }

    private void B_Search_Click(object sender, EventArgs e)
    {
        string query = TB_Search.Text;
        if (string.IsNullOrEmpty(query)) return;

        int startRow = dgv.CurrentCell?.RowIndex ?? 0;
        int startCol = dgv.CurrentCell?.ColumnIndex ?? 0;

        for (int i = startRow; i < dgv.RowCount; i++)
        {
            for (int j = (i == startRow ? startCol + 1 : 0); j < dgv.ColumnCount; j++)
            {
                if (dgv[j, i].Value?.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                {
                    dgv.CurrentCell = dgv[j, i];
                    return;
                }
            }
        }
        WinFormsUtil.Alert("No more occurrences found.");
    }

    private void B_SearchPrev_Click(object sender, EventArgs e)
    {
        string query = TB_Search.Text;
        if (string.IsNullOrEmpty(query)) return;

        int startRow = dgv.CurrentCell?.RowIndex ?? dgv.RowCount - 1;
        int startCol = dgv.CurrentCell?.ColumnIndex ?? dgv.ColumnCount - 1;

        for (int i = startRow; i >= 0; i--)
        {
            for (int j = (i == startRow ? startCol - 1 : dgv.ColumnCount - 1); j >= 0; j--)
            {
                if (dgv[j, i].Value?.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                {
                    dgv.CurrentCell = dgv[j, i];
                    return;
                }
            }
        }
        WinFormsUtil.Alert("No previous occurrences found.");
    }

    private void B_BatchReplace_Click(object sender, EventArgs e)
    {
        string query = TB_Search.Text;
        if (string.IsNullOrEmpty(query)) return;
        string replace = WinFormsUtil.PromptInput("Enter replacement text:", "Batch Replace");
        if (replace == null) return;

        int count = 0;
        for (int i = 0; i < dgv.RowCount; i++)
        {
            var cell = dgv[1, i]; // col 1 is text
            string text = cell.Value?.ToString() ?? "";
            if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                // Note: string.Replace(..., StringComparison) returns a new string
                cell.Value = System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(query), replace, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                count++;
            }
        }
        WinFormsUtil.Alert($"Replaced {count} occurrences.");
    }

    private void TextEditor_FormClosing(object sender, FormClosingEventArgs e)
    {
        dgv.EndEdit();
        if (entry > -1) files[entry] = GetCurrentDGLines();
    }

    private void B_Randomize_Click(object sender, EventArgs e)
    {
        if (Mode == "gametext" && DialogResult.Yes != WinFormsUtil.Prompt(MessageBoxButtons.YesNo, "Randomizing Game Text is dangerous!", "Continue?")) return;

        var dr = WinFormsUtil.Prompt(MessageBoxButtons.YesNoCancel, $"Yes: Randomize ALL{Environment.NewLine}No: Randomize current textfile{Environment.NewLine}Cancel: Abort");
        if (dr == DialogResult.Cancel) return;

        var drs = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, $"Smart shuffle:{Environment.NewLine}Yes: Shuffle if no Variable present{Environment.NewLine}No: Pure random!");
        if (drs == DialogResult.Cancel) return;

        bool all = dr == DialogResult.Yes;
        bool smart = drs == DialogResult.Yes;

        if (entry > -1) files[entry] = GetCurrentDGLines();

        int start = all ? 0 : entry;
        int end = all ? files.Length - 1 : entry;

        List<string> strings = [];
        for (int i = start; i <= end; i++)
        {
            string[] data = files[i];
            strings.AddRange(smart ? data.Where(line => !line.Contains('[')) : data);
        }

        string[] pool = [.. strings];
        Util.Shuffle(pool);

        int ctr = 0;
        for (int i = start; i <= end; i++)
        {
            string[] data = files[i];
            for (int j = 0; j < data.Length; j++) 
            {
                if (!smart || !data[j].Contains('[')) data[j] = pool[ctr++];
            }
            files[i] = data;
        }

        SetStringsDataGridView(files[entry]);
        WinFormsUtil.Alert("Strings randomized!");
    }

    private string StripControlCodes(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        // 1. Remove standard control codes, EXCEPT for \n which we process into linebreaks
        // Replace \r with space if needed, strip others.
        string result = input.Replace("\\n", Environment.NewLine)
                             .Replace("\\r", "")
                             .Replace("\\c", "")
                             .Replace("\\f", "");

        // 2. Remove [VAR] type codes using a simple loop
        StringBuilder sb = new();
        bool inBrackets = false;
        for (int i = 0; i < result.Length; i++)
        {
            char c = result[i];
            if (c == '[') { inBrackets = true; continue; }
            if (c == ']') { inBrackets = false; continue; }
            if (!inBrackets) sb.Append(c);
        }

        result = sb.ToString();
        // Final cleanup of formatting codes (reverse of what's expected for visualizer)
        result = result.Replace("\\n", Environment.NewLine)
                       .Replace("\\r", "")
                       .Replace("\\c", "")
                       .Replace("\\f", "")
                       .Replace("\\\\", "\\");

        return result.Trim();
    }

    #region [VAR ...] variable codes - insert picker and live preview

    /// <summary>
    /// Friendly, human-readable glosses for the most commonly used <c>[VAR ...]</c> codes (from
    /// <see cref="TextVariableCode"/>), used to build a live-legible preview instead of showing raw
    /// bracket syntax. Not exhaustive - every version's code table has entries beyond this list;
    /// anything missing here still shows as its own code name in parentheses, never blank.
    /// </summary>
    private static readonly Dictionary<string, string> VariableFriendlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TRNAME"] = "Trainer's Name",
        ["PKNAME"] = "Pokémon's Species Name",
        ["PKNICK"] = "Pokémon's Nickname",
        ["TYPE"] = "Type",
        ["LOCATION"] = "Location",
        ["ABILITY"] = "Ability",
        ["MOVE"] = "Move",
        ["ITEM1"] = "Item",
        ["ITEM2"] = "Item",
        ["sTRBAG"] = "Bag Item",
        ["BOX"] = "Box Name",
        ["EVSTAT"] = "EV Stat",
        ["OPOWER"] = "O-Power",
        ["RIBBON"] = "Ribbon",
        ["MIINAME"] = "Mii Name",
        ["WEATHER"] = "Weather",
        ["TRNICK"] = "Trainer's Nickname",
        ["1stchrTR"] = "Trainer's Initial",
        ["SHOUTOUT"] = "Shout-Out Phrase",
        ["BERRY"] = "Berry",
        ["REMFEEL"] = "Remnant Feeling",
        ["REMQUAL"] = "Remnant Quality",
        ["WEBSITE"] = "Website",
        ["CHOICECOS"] = "Chosen Costume",
        ["GSYNCID"] = "Global Link ID",
        ["PRVIDSAY"] = "Player's ID",
        ["BTLTEST"] = "Battle Test Name",
        ["GENLOC"] = "General Location",
        ["CHOICEFOOD"] = "Chosen Food",
        ["HOTELITEM"] = "Hotel Item",
        ["TAXISTOP"] = "Taxi Stop",
        ["MAISTITLE"] = "Maison Title",
        ["ITEMPLUR0"] = "Item (Plural)",
        ["ITEMPLUR1"] = "Item (Plural)",
        ["NUMBRNCH"] = "text varies by number",
        ["GENDBR"] = "text varies by gender",
        ["NUM1"] = "Number", ["NUM2"] = "Number", ["NUM3"] = "Number", ["NUM4"] = "Number",
        ["NUM5"] = "Number", ["NUM6"] = "Number", ["NUM7"] = "Number", ["NUM8"] = "Number", ["NUM9"] = "Number", ["NUM10"] = "Number",
        ["NATURE"] = "Nature",
        // Found via a real US+UM gametext scan (61 distinct VAR codes in use on each, identical sets)
        // that turned up exactly these 3 as still uncovered. Glosses inferred from real usage context,
        // not guessed blind: BE05 appears twice back-to-back right after a level-up number with no
        // visible text of its own (a cue/effect trigger, not a gloss-able value); BD06 opens a Z-Move
        // announcement line (an emphasis/screen-effect marker, same family as BD00's color codes but
        // a different effect); BD02 prefixes every line of a multi-line caution/warning message (a
        // per-line warning-icon marker).
        ["BE05"] = "(Effect Cue)",
        ["BD06"] = "(Emphasis Effect)",
        ["BD02"] = "(Warning Icon)",
        ["BTLFACILITY"] = "Battle Facility",
        ["PLAZAFACILITY"] = "Plaza Facility",
        ["ZCRYSTAL"] = "Z-Crystal",
        ["ROTOMLINE"] = "Rotom Dialogue",
        ["BTLSTREAK"] = "Battle Streak",
        ["PHOTOSTICKER"] = "Photo Sticker/Frame",
        ["BTLTEAM"] = "Battle Team",
        ["WARPRIDE"] = "Ultra Warp Ride",
        ["TOTEMSTICKER"] = "Totem Sticker",
        ["AGENCYRANK"] = "Battle Agency Rank",
        ["ROTOLOTO"] = "Roto Loto Power",
        ["PELAGO"] = "Poké Pelago Isle",
        ["REGION"] = "Region/Island",
        ["TRIAL"] = "Trial Name",
        ["STAMP"] = "Passport Stamp",
        ["FESTIVALCOIN"] = "Festival Coins",
        ["PHOTOPOSE"] = "Photo Pose",
        ["BTLSTYLE"] = "Battle Style",
        ["CARDTITLE"] = "Trainer Card Title",
        ["PLAZATHEME"] = "Plaza Theme",
        ["PLAZAGREET"] = "Plaza Greeting",
        ["PLAZARESP"] = "Plaza Response",
        ["PLAZABYE"] = "Plaza Farewell",
        ["PLAZAACT"] = "Plaza Activity",
        ["PLAZAORDER"] = "Plaza Order",
        ["PLAZAITEM"] = "Plaza Item",
        ["PLAZAFACILITYRANK"] = "Facility Rank",
        ["PLAZAGAME"] = "Plaza Game",
        ["PLAZAPHRASE"] = "Plaza Phrase",
        ["PLAZAREQ"] = "Plaza Request",
        ["PLAZAMISSION"] = "Plaza Mission",
        ["PLAZARECORD"] = "Plaza Record",
        ["PLAZARESULT"] = "Plaza Result",
        ["PLAZAFORTUNE"] = "Plaza Fortune",
        ["PLAZATENT"] = "Plaza Tent",
        ["PHOTOFILTER"] = "Photo Filter",
        ["PHOTOPROP"] = "Photo Prop",
        ["PHOTOBG"] = "Photo Background",
        ["PHOTOMUSIC"] = "Photo Music",
    };

    /// <summary>
    /// Codes handled as an actual colour change in the preview (see <see cref="ColorPalette"/>)
    /// rather than shown as a text placeholder - matched case-insensitively against the code name.
    /// </summary>
    private static readonly HashSet<string> VariableColorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "COLOR", "iCOLOR2", "iCOLOR3",
    };

    /// <summary>
    /// The preview's colour, keyed by the small index every real occurrence of <c>[VAR COLOR(n)]</c>
    /// in this ROM's own game text actually uses (0, 1, 2 - never anything else, across all 127 real
    /// text files scanned). Not guessed from outside documentation: derived directly from what each
    /// index consistently wraps in real text - `0001` only ever wraps negative/warning/failure
    /// wording ("disabled", "Unable!", "Unavailable", "There are no Pokémon", "cannot change"), and
    /// `0002` only ever wraps positive/highlighted info (Nature names, met/hatched Location names,
    /// "Learned", numbered move-slot labels, "Accepting ... requests"). `0000` is the reset/default,
    /// confirmed by always appearing as the closing tag right after a `0001`/`0002` span.
    /// `iCOLOR2`/`iCOLOR3` share the same 0/1/2 numbering in the (much smaller) real sample found for
    /// them - applying the same palette is a reasonable extrapolation from the confirmed scheme,
    /// not an independent guess, though their real sample size (2-3 lines each) is far smaller than
    /// `COLOR`'s.
    /// </summary>
    private static readonly Dictionary<int, Color> ColorPalette = new()
    {
        [0] = Color.Gainsboro, // reset / default - close to the preview's own base ForeColor
        [1] = Color.OrangeRed, // negative/warning (confirmed: "disabled", "Unable!", "cannot change")
        [2] = Color.DeepSkyBlue, // positive/highlighted info (confirmed: Nature/Location names, "Learned")
    };

    /// <summary>
    /// The 10-colour palette for the separate <c>[VAR BD00(n,0000,0000)]</c> / <c>[VAR BD01]</c>
    /// code pair - not derived from scanning gametext (unlike <see cref="ColorPalette"/>), but
    /// given directly by the user, who tested all 10 indices live in USUM (both NPC and battle
    /// text) and reported exactly what each one renders as. Only the first of BD00's 3 args does
    /// anything observable; BD01 takes no args and always resets, unlike COLOR's 0000-as-reset
    /// convention - so it's handled as its own case in <see cref="RenderPreview"/>, not folded into
    /// <see cref="VariableColorCodes"/>.
    /// </summary>
    private static readonly Dictionary<int, Color> ExtendedColorPalette = new()
    {
        [0] = Color.Red,
        [1] = Color.LimeGreen,
        [2] = Color.DeepSkyBlue,
        [3] = Color.Orange,
        [4] = Color.HotPink,
        [5] = Color.MediumPurple,
        [6] = Color.Aquamarine,
        [7] = Color.Gold,
        [8] = Color.LightSkyBlue,
        [9] = Color.Yellow,
    };

    private static readonly Regex VarCodePattern = new(@"\[VAR ([^\]\(]+)(?:\(([^\)]*)\))?\]", RegexOptions.Compiled);
    private static readonly Regex WaitPattern = new(@"\[WAIT (\d+)\]", RegexOptions.Compiled);
    private static readonly Regex NullLinePattern = new(@"\[~ (\d+)\]", RegexOptions.Compiled);

    private void PopulateVariablePicker()
    {
        CB_InsertVariable.Items.Clear();
        var variables = Main.Config?.Variables;
        if (variables == null) return;

        foreach (var v in variables)
            CB_InsertVariable.Items.Add(v.Name);
        if (CB_InsertVariable.Items.Count > 0)
            CB_InsertVariable.SelectedIndex = 0;
    }

    private void B_InsertVariable_Click(object sender, EventArgs e)
    {
        if (CB_InsertVariable.SelectedItem == null) return;

        string code = $"[VAR {CB_InsertVariable.SelectedItem}]";
        int pos = RTB_Visualizer.SelectionStart;
        RTB_Visualizer.SelectedText = code;
        RTB_Visualizer.SelectionStart = pos + code.Length;
        RTB_Visualizer.Focus();
    }

    /// <summary>Friendly name -> exact bracket text for every confirmed colour code, so a colour can
    /// be inserted by name (matching <see cref="ColorPalette"/>/<see cref="ExtendedColorPalette"/>)
    /// instead of requiring the hex argument to be typed by hand.</summary>
    private static readonly (string Label, string Code)[] ColorInsertOptions =
    [
        ("Reset to default", "[VAR COLOR(0000)]"),
        ("Negative / warning (red)", "[VAR COLOR(0001)]"),
        ("Positive / highlight (blue)", "[VAR COLOR(0002)]"),
        ("BD00: Red", "[VAR BD00(0000,0000,0000)]"),
        ("BD00: Green", "[VAR BD00(0001,0000,0000)]"),
        ("BD00: Blue", "[VAR BD00(0002,0000,0000)]"),
        ("BD00: Orange", "[VAR BD00(0003,0000,0000)]"),
        ("BD00: Pink", "[VAR BD00(0004,0000,0000)]"),
        ("BD00: Purple", "[VAR BD00(0005,0000,0000)]"),
        ("BD00: Aquamarine", "[VAR BD00(0006,0000,0000)]"),
        ("BD00: Gold", "[VAR BD00(0007,0000,0000)]"),
        ("BD00: Light Blue", "[VAR BD00(0008,0000,0000)]"),
        ("BD00: Yellow", "[VAR BD00(0009,0000,0000)]"),
        ("BD00 reset (BD01)", "[VAR BD01]"),
    ];

    private void PopulateColorPicker()
    {
        CB_ColorChoice.Items.Clear();
        foreach (var (label, _) in ColorInsertOptions)
            CB_ColorChoice.Items.Add(label);
        if (CB_ColorChoice.Items.Count > 0)
            CB_ColorChoice.SelectedIndex = 0;
    }

    private void B_InsertColor_Click(object sender, EventArgs e)
    {
        int idx = CB_ColorChoice.SelectedIndex;
        if (idx < 0 || idx >= ColorInsertOptions.Length) return;

        string code = ColorInsertOptions[idx].Code;
        int pos = RTB_Visualizer.SelectionStart;
        RTB_Visualizer.SelectedText = code;
        RTB_Visualizer.SelectionStart = pos + code.Length;
        RTB_Visualizer.Focus();
    }

    private void B_InsertPause_Click(object sender, EventArgs e)
    {
        int frames = GetNumberPrompt("Insert Pause", "How many frames should the text pause for?", 60);
        if (frames <= 0) return;

        string code = $"[WAIT {frames}]";
        int pos = RTB_Visualizer.SelectionStart;
        RTB_Visualizer.SelectedText = code;
        RTB_Visualizer.SelectionStart = pos + code.Length;
        RTB_Visualizer.Focus();
    }

    /// <summary>
    /// Renders <c>[VAR ...]</c> codes (and the related bracket forms <c>TextFile.cs</c> produces -
    /// <c>[WAIT n]</c>, <c>[~ n]</c>) into <see cref="RTB_Preview"/> as a readable gloss with real
    /// colour changes for the confirmed colour codes, so the preview shows roughly what a player
    /// would see instead of raw bracket syntax. This is a human-readable approximation, not a
    /// byte-perfect emulation of the game's own resolution logic (which needs real save/battle
    /// state a static text editor doesn't have) - most named variables still show as a bracketed
    /// gloss like <c>(Trainer's Name)</c>, since substituting a real value isn't possible here.
    /// </summary>
    private void RenderPreview(string rawInput)
    {
        RTB_Preview.Clear();
        if (string.IsNullOrEmpty(rawInput)) return;

        // Protect an escaped literal '[' (written as \[ in the raw format) from the [VAR ...] scan
        // below with a sentinel, restored to '[' only in the plain-text chunks as they're appended.
        const string EscapedBracket = "";
        string text = rawInput.Replace("\\[", EscapedBracket)
                               .Replace("\\n", "\n")
                               .Replace("\\r", " ⏎ ")
                               .Replace("\\c", " ⏎ ")
                               .Replace("\\f", "");
        text = WaitPattern.Replace(text, "(pause)");
        text = NullLinePattern.Replace(text, "");

        Color currentColor = ColorPalette[0];
        int pos = 0;
        foreach (Match m in VarCodePattern.Matches(text))
        {
            if (m.Index > pos)
                AppendPreview(text[pos..m.Index].Replace(EscapedBracket, "["), currentColor);

            string name = m.Groups[1].Value;
            string args = m.Groups[2].Value;

            if (string.Equals(name, "BD01", StringComparison.OrdinalIgnoreCase))
            {
                currentColor = ColorPalette[0];
            }
            else if (string.Equals(name, "BD00", StringComparison.OrdinalIgnoreCase))
            {
                int extIndex = 0;
                string firstExtArg = args.Split(',')[0];
                int.TryParse(firstExtArg, System.Globalization.NumberStyles.HexNumber, null, out extIndex);
                currentColor = ExtendedColorPalette.TryGetValue(extIndex, out var ec) ? ec : ColorPalette[0];
            }
            else if (VariableColorCodes.Contains(name))
            {
                int colorIndex = 0;
                string firstArg = args.Split(',')[0];
                int.TryParse(firstArg, System.Globalization.NumberStyles.HexNumber, null, out colorIndex);
                currentColor = ColorPalette.TryGetValue(colorIndex, out var c) ? c : ColorPalette[0];
            }
            else
            {
                string gloss = VariableFriendlyNames.TryGetValue(name, out string friendly) ? friendly : name;
                AppendPreview($"({gloss})", currentColor);
            }

            pos = m.Index + m.Length;
        }
        if (pos < text.Length)
            AppendPreview(text[pos..].Replace(EscapedBracket, "["), currentColor);

        RTB_Preview.Select(0, 0);
    }

    private void AppendPreview(string text, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;
        RTB_Preview.SelectionStart = RTB_Preview.TextLength;
        RTB_Preview.SelectionLength = 0;
        RTB_Preview.SelectionColor = color;
        RTB_Preview.AppendText(text.Replace("\\\\", "\\"));
    }

    private void UpdatePreview()
    {
        string rawText = RTB_Visualizer.Text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n");
        RenderPreview(rawText);
    }

    #endregion
}