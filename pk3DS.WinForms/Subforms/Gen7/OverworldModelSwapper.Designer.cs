namespace pk3DS.WinForms;

partial class OverworldModelSwapper
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        this.L_Target = new System.Windows.Forms.Label();
        this.L_TargetZone = new System.Windows.Forms.Label();
        this.CB_TargetZone = new System.Windows.Forms.ComboBox();
        this.L_TargetArea = new System.Windows.Forms.Label();
        this.NUD_TargetArea = new System.Windows.Forms.NumericUpDown();
        this.B_LoadTarget = new System.Windows.Forms.Button();
        this.L_TargetChar = new System.Windows.Forms.Label();
        this.CB_TargetCharacter = new System.Windows.Forms.ComboBox();
        this.L_TargetSlot = new System.Windows.Forms.Label();
        this.NUD_TargetSlot = new System.Windows.Forms.NumericUpDown();
        this.L_TargetSlotNote = new System.Windows.Forms.Label();
        this.TB_TargetSlotNote = new System.Windows.Forms.TextBox();
        this.L_Source = new System.Windows.Forms.Label();
        this.L_SourceZone = new System.Windows.Forms.Label();
        this.CB_SourceZone = new System.Windows.Forms.ComboBox();
        this.L_SourceArea = new System.Windows.Forms.Label();
        this.NUD_SourceArea = new System.Windows.Forms.NumericUpDown();
        this.B_LoadSource = new System.Windows.Forms.Button();
        this.L_SourceChar = new System.Windows.Forms.Label();
        this.CB_SourceCharacter = new System.Windows.Forms.ComboBox();
        this.L_SourceSlot = new System.Windows.Forms.Label();
        this.NUD_SourceSlot = new System.Windows.Forms.NumericUpDown();
        this.L_SourceSlotNote = new System.Windows.Forms.Label();
        this.TB_SourceSlotNote = new System.Windows.Forms.TextBox();
        this.B_Swap = new System.Windows.Forms.Button();
        this.B_SaveAll = new System.Windows.Forms.Button();
        this.L_Status = new System.Windows.Forms.Label();
        ((System.ComponentModel.ISupportInitialize)(this.NUD_TargetArea)).BeginInit();
        ((System.ComponentModel.ISupportInitialize)(this.NUD_TargetSlot)).BeginInit();
        ((System.ComponentModel.ISupportInitialize)(this.NUD_SourceArea)).BeginInit();
        ((System.ComponentModel.ISupportInitialize)(this.NUD_SourceSlot)).BeginInit();
        this.SuspendLayout();
        //
        // L_Target
        //
        this.L_Target.AutoSize = true;
        this.L_Target.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
        this.L_Target.Location = new System.Drawing.Point(10, 12);
        this.L_Target.Name = "L_Target";
        this.L_Target.Size = new System.Drawing.Size(120, 15);
        this.L_Target.TabIndex = 0;
        this.L_Target.Text = "Target (to be replaced)";
        //
        // L_TargetZone
        //
        this.L_TargetZone.AutoSize = true;
        this.L_TargetZone.Location = new System.Drawing.Point(10, 40);
        this.L_TargetZone.Name = "L_TargetZone";
        this.L_TargetZone.Size = new System.Drawing.Size(90, 13);
        this.L_TargetZone.TabIndex = 1;
        this.L_TargetZone.Text = "Known zone:";
        //
        // CB_TargetZone
        //
        this.CB_TargetZone.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.CB_TargetZone.FormattingEnabled = true;
        this.CB_TargetZone.Location = new System.Drawing.Point(140, 37);
        this.CB_TargetZone.Name = "CB_TargetZone";
        this.CB_TargetZone.Size = new System.Drawing.Size(210, 21);
        this.CB_TargetZone.TabIndex = 2;
        this.CB_TargetZone.SelectedIndexChanged += new System.EventHandler(this.CB_TargetZone_SelectedIndexChanged);
        //
        // L_TargetArea
        //
        this.L_TargetArea.AutoSize = true;
        this.L_TargetArea.Location = new System.Drawing.Point(10, 68);
        this.L_TargetArea.Name = "L_TargetArea";
        this.L_TargetArea.Size = new System.Drawing.Size(120, 13);
        this.L_TargetArea.TabIndex = 3;
        this.L_TargetArea.Text = "Area file index (AC):";
        //
        // NUD_TargetArea
        //
        this.NUD_TargetArea.Location = new System.Drawing.Point(140, 66);
        this.NUD_TargetArea.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
        this.NUD_TargetArea.Name = "NUD_TargetArea";
        this.NUD_TargetArea.Size = new System.Drawing.Size(80, 20);
        this.NUD_TargetArea.TabIndex = 4;
        //
        // B_LoadTarget
        //
        this.B_LoadTarget.Location = new System.Drawing.Point(350, 64);
        this.B_LoadTarget.Name = "B_LoadTarget";
        this.B_LoadTarget.Size = new System.Drawing.Size(90, 24);
        this.B_LoadTarget.TabIndex = 5;
        this.B_LoadTarget.Text = "Load";
        this.B_LoadTarget.UseVisualStyleBackColor = true;
        this.B_LoadTarget.Click += new System.EventHandler(this.B_LoadTarget_Click);
        //
        // L_TargetChar
        //
        this.L_TargetChar.AutoSize = true;
        this.L_TargetChar.Location = new System.Drawing.Point(10, 96);
        this.L_TargetChar.Name = "L_TargetChar";
        this.L_TargetChar.Size = new System.Drawing.Size(120, 13);
        this.L_TargetChar.TabIndex = 6;
        this.L_TargetChar.Text = "Known character:";
        //
        // CB_TargetCharacter
        //
        this.CB_TargetCharacter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.CB_TargetCharacter.Enabled = false;
        this.CB_TargetCharacter.FormattingEnabled = true;
        this.CB_TargetCharacter.Location = new System.Drawing.Point(140, 93);
        this.CB_TargetCharacter.Name = "CB_TargetCharacter";
        this.CB_TargetCharacter.Size = new System.Drawing.Size(300, 21);
        this.CB_TargetCharacter.TabIndex = 7;
        this.CB_TargetCharacter.SelectedIndexChanged += new System.EventHandler(this.CB_TargetCharacter_SelectedIndexChanged);
        //
        // L_TargetSlot
        //
        this.L_TargetSlot.AutoSize = true;
        this.L_TargetSlot.Location = new System.Drawing.Point(10, 122);
        this.L_TargetSlot.Name = "L_TargetSlot";
        this.L_TargetSlot.Size = new System.Drawing.Size(90, 13);
        this.L_TargetSlot.TabIndex = 8;
        this.L_TargetSlot.Text = "Placement slot:";
        //
        // NUD_TargetSlot
        //
        this.NUD_TargetSlot.Location = new System.Drawing.Point(140, 120);
        this.NUD_TargetSlot.Name = "NUD_TargetSlot";
        this.NUD_TargetSlot.Size = new System.Drawing.Size(80, 20);
        this.NUD_TargetSlot.TabIndex = 9;
        this.NUD_TargetSlot.ValueChanged += new System.EventHandler(this.NUD_TargetSlot_ValueChanged);
        //
        // L_TargetSlotNote
        //
        this.L_TargetSlotNote.AutoSize = true;
        this.L_TargetSlotNote.Location = new System.Drawing.Point(10, 148);
        this.L_TargetSlotNote.Name = "L_TargetSlotNote";
        this.L_TargetSlotNote.Size = new System.Drawing.Size(120, 13);
        this.L_TargetSlotNote.TabIndex = 10;
        this.L_TargetSlotNote.Text = "Character name:";
        //
        // TB_TargetSlotNote
        //
        this.TB_TargetSlotNote.Location = new System.Drawing.Point(140, 145);
        this.TB_TargetSlotNote.Name = "TB_TargetSlotNote";
        this.TB_TargetSlotNote.Size = new System.Drawing.Size(300, 20);
        this.TB_TargetSlotNote.TabIndex = 11;
        //
        // L_Source
        //
        this.L_Source.AutoSize = true;
        this.L_Source.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
        this.L_Source.Location = new System.Drawing.Point(10, 182);
        this.L_Source.Name = "L_Source";
        this.L_Source.Size = new System.Drawing.Size(150, 15);
        this.L_Source.TabIndex = 12;
        this.L_Source.Text = "Source (model to copy)";
        //
        // L_SourceZone
        //
        this.L_SourceZone.AutoSize = true;
        this.L_SourceZone.Location = new System.Drawing.Point(10, 210);
        this.L_SourceZone.Name = "L_SourceZone";
        this.L_SourceZone.Size = new System.Drawing.Size(90, 13);
        this.L_SourceZone.TabIndex = 13;
        this.L_SourceZone.Text = "Known zone:";
        //
        // CB_SourceZone
        //
        this.CB_SourceZone.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.CB_SourceZone.FormattingEnabled = true;
        this.CB_SourceZone.Location = new System.Drawing.Point(140, 207);
        this.CB_SourceZone.Name = "CB_SourceZone";
        this.CB_SourceZone.Size = new System.Drawing.Size(210, 21);
        this.CB_SourceZone.TabIndex = 14;
        this.CB_SourceZone.SelectedIndexChanged += new System.EventHandler(this.CB_SourceZone_SelectedIndexChanged);
        //
        // L_SourceArea
        //
        this.L_SourceArea.AutoSize = true;
        this.L_SourceArea.Location = new System.Drawing.Point(10, 238);
        this.L_SourceArea.Name = "L_SourceArea";
        this.L_SourceArea.Size = new System.Drawing.Size(120, 13);
        this.L_SourceArea.TabIndex = 15;
        this.L_SourceArea.Text = "Area file index (AC):";
        //
        // NUD_SourceArea
        //
        this.NUD_SourceArea.Location = new System.Drawing.Point(140, 236);
        this.NUD_SourceArea.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
        this.NUD_SourceArea.Name = "NUD_SourceArea";
        this.NUD_SourceArea.Size = new System.Drawing.Size(80, 20);
        this.NUD_SourceArea.TabIndex = 16;
        //
        // B_LoadSource
        //
        this.B_LoadSource.Location = new System.Drawing.Point(350, 234);
        this.B_LoadSource.Name = "B_LoadSource";
        this.B_LoadSource.Size = new System.Drawing.Size(90, 24);
        this.B_LoadSource.TabIndex = 17;
        this.B_LoadSource.Text = "Load";
        this.B_LoadSource.UseVisualStyleBackColor = true;
        this.B_LoadSource.Click += new System.EventHandler(this.B_LoadSource_Click);
        //
        // L_SourceChar
        //
        this.L_SourceChar.AutoSize = true;
        this.L_SourceChar.Location = new System.Drawing.Point(10, 266);
        this.L_SourceChar.Name = "L_SourceChar";
        this.L_SourceChar.Size = new System.Drawing.Size(120, 13);
        this.L_SourceChar.TabIndex = 18;
        this.L_SourceChar.Text = "Known character:";
        //
        // CB_SourceCharacter
        //
        this.CB_SourceCharacter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.CB_SourceCharacter.Enabled = false;
        this.CB_SourceCharacter.FormattingEnabled = true;
        this.CB_SourceCharacter.Location = new System.Drawing.Point(140, 263);
        this.CB_SourceCharacter.Name = "CB_SourceCharacter";
        this.CB_SourceCharacter.Size = new System.Drawing.Size(300, 21);
        this.CB_SourceCharacter.TabIndex = 19;
        this.CB_SourceCharacter.SelectedIndexChanged += new System.EventHandler(this.CB_SourceCharacter_SelectedIndexChanged);
        //
        // L_SourceSlot
        //
        this.L_SourceSlot.AutoSize = true;
        this.L_SourceSlot.Location = new System.Drawing.Point(10, 292);
        this.L_SourceSlot.Name = "L_SourceSlot";
        this.L_SourceSlot.Size = new System.Drawing.Size(90, 13);
        this.L_SourceSlot.TabIndex = 20;
        this.L_SourceSlot.Text = "Placement slot:";
        //
        // NUD_SourceSlot
        //
        this.NUD_SourceSlot.Location = new System.Drawing.Point(140, 290);
        this.NUD_SourceSlot.Name = "NUD_SourceSlot";
        this.NUD_SourceSlot.Size = new System.Drawing.Size(80, 20);
        this.NUD_SourceSlot.TabIndex = 21;
        this.NUD_SourceSlot.ValueChanged += new System.EventHandler(this.NUD_SourceSlot_ValueChanged);
        //
        // L_SourceSlotNote
        //
        this.L_SourceSlotNote.AutoSize = true;
        this.L_SourceSlotNote.Location = new System.Drawing.Point(10, 318);
        this.L_SourceSlotNote.Name = "L_SourceSlotNote";
        this.L_SourceSlotNote.Size = new System.Drawing.Size(120, 13);
        this.L_SourceSlotNote.TabIndex = 22;
        this.L_SourceSlotNote.Text = "Character name:";
        //
        // TB_SourceSlotNote
        //
        this.TB_SourceSlotNote.Location = new System.Drawing.Point(140, 315);
        this.TB_SourceSlotNote.Name = "TB_SourceSlotNote";
        this.TB_SourceSlotNote.Size = new System.Drawing.Size(300, 20);
        this.TB_SourceSlotNote.TabIndex = 23;
        //
        // B_Swap
        //
        this.B_Swap.Location = new System.Drawing.Point(10, 352);
        this.B_Swap.Name = "B_Swap";
        this.B_Swap.Size = new System.Drawing.Size(150, 28);
        this.B_Swap.TabIndex = 24;
        this.B_Swap.Text = "Swap (In Memory)";
        this.B_Swap.UseVisualStyleBackColor = true;
        this.B_Swap.Click += new System.EventHandler(this.B_Swap_Click);
        //
        // B_SaveAll
        //
        this.B_SaveAll.Enabled = false;
        this.B_SaveAll.Location = new System.Drawing.Point(166, 352);
        this.B_SaveAll.Name = "B_SaveAll";
        this.B_SaveAll.Size = new System.Drawing.Size(150, 28);
        this.B_SaveAll.TabIndex = 25;
        this.B_SaveAll.Text = "Save to ROM";
        this.B_SaveAll.UseVisualStyleBackColor = true;
        this.B_SaveAll.Click += new System.EventHandler(this.B_SaveAll_Click);
        //
        // L_Status
        //
        this.L_Status.Location = new System.Drawing.Point(10, 388);
        this.L_Status.Name = "L_Status";
        this.L_Status.Size = new System.Drawing.Size(430, 90);
        this.L_Status.TabIndex = 26;
        this.L_Status.Text = "";
        //
        // OverworldModelSwapper
        //
        this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(460, 488);
        this.Controls.Add(this.L_Status);
        this.Controls.Add(this.B_SaveAll);
        this.Controls.Add(this.B_Swap);
        this.Controls.Add(this.TB_SourceSlotNote);
        this.Controls.Add(this.L_SourceSlotNote);
        this.Controls.Add(this.NUD_SourceSlot);
        this.Controls.Add(this.L_SourceSlot);
        this.Controls.Add(this.CB_SourceCharacter);
        this.Controls.Add(this.L_SourceChar);
        this.Controls.Add(this.B_LoadSource);
        this.Controls.Add(this.NUD_SourceArea);
        this.Controls.Add(this.L_SourceArea);
        this.Controls.Add(this.CB_SourceZone);
        this.Controls.Add(this.L_SourceZone);
        this.Controls.Add(this.L_Source);
        this.Controls.Add(this.TB_TargetSlotNote);
        this.Controls.Add(this.L_TargetSlotNote);
        this.Controls.Add(this.NUD_TargetSlot);
        this.Controls.Add(this.L_TargetSlot);
        this.Controls.Add(this.CB_TargetCharacter);
        this.Controls.Add(this.L_TargetChar);
        this.Controls.Add(this.B_LoadTarget);
        this.Controls.Add(this.NUD_TargetArea);
        this.Controls.Add(this.L_TargetArea);
        this.Controls.Add(this.CB_TargetZone);
        this.Controls.Add(this.L_TargetZone);
        this.Controls.Add(this.L_Target);
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.Name = "OverworldModelSwapper";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        this.Text = "Overworld Model Swapper";
        ((System.ComponentModel.ISupportInitialize)(this.NUD_TargetArea)).EndInit();
        ((System.ComponentModel.ISupportInitialize)(this.NUD_TargetSlot)).EndInit();
        ((System.ComponentModel.ISupportInitialize)(this.NUD_SourceArea)).EndInit();
        ((System.ComponentModel.ISupportInitialize)(this.NUD_SourceSlot)).EndInit();
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion

    private System.Windows.Forms.Label L_Target;
    private System.Windows.Forms.Label L_TargetZone;
    private System.Windows.Forms.ComboBox CB_TargetZone;
    private System.Windows.Forms.Label L_TargetArea;
    private System.Windows.Forms.NumericUpDown NUD_TargetArea;
    private System.Windows.Forms.Button B_LoadTarget;
    private System.Windows.Forms.Label L_TargetChar;
    private System.Windows.Forms.ComboBox CB_TargetCharacter;
    private System.Windows.Forms.Label L_TargetSlot;
    private System.Windows.Forms.NumericUpDown NUD_TargetSlot;
    private System.Windows.Forms.Label L_TargetSlotNote;
    private System.Windows.Forms.TextBox TB_TargetSlotNote;
    private System.Windows.Forms.Label L_Source;
    private System.Windows.Forms.Label L_SourceZone;
    private System.Windows.Forms.ComboBox CB_SourceZone;
    private System.Windows.Forms.Label L_SourceArea;
    private System.Windows.Forms.NumericUpDown NUD_SourceArea;
    private System.Windows.Forms.Button B_LoadSource;
    private System.Windows.Forms.Label L_SourceChar;
    private System.Windows.Forms.ComboBox CB_SourceCharacter;
    private System.Windows.Forms.Label L_SourceSlot;
    private System.Windows.Forms.NumericUpDown NUD_SourceSlot;
    private System.Windows.Forms.Label L_SourceSlotNote;
    private System.Windows.Forms.TextBox TB_SourceSlotNote;
    private System.Windows.Forms.Button B_Swap;
    private System.Windows.Forms.Button B_SaveAll;
    private System.Windows.Forms.Label L_Status;
}
