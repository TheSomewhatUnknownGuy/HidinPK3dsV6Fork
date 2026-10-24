namespace pk3DS.WinForms;

partial class AMXScriptEditor
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
        this.L_Script = new System.Windows.Forms.Label();
        this.CB_Script = new System.Windows.Forms.ComboBox();
        this.L_Info = new System.Windows.Forms.Label();
        this.DGV_Instructions = new System.Windows.Forms.DataGridView();
        this.B_Save = new System.Windows.Forms.Button();
        this.GB_Tutorials = new System.Windows.Forms.GroupBox();
        this.CHK_RepairCatchTutorial = new System.Windows.Forms.CheckBox();
        this.CHK_SkipCatchTutorial = new System.Windows.Forms.CheckBox();
        this.CHK_UndoCatchTutorial = new System.Windows.Forms.CheckBox();
        this.CHK_SkipZMoveTutorial = new System.Windows.Forms.CheckBox();
        this.CHK_UndoZMoveTutorial = new System.Windows.Forms.CheckBox();
        this.B_ApplyTutorialSkips = new System.Windows.Forms.Button();
        this.L_TutorialSkipStatus = new System.Windows.Forms.Label();
        ((System.ComponentModel.ISupportInitialize)(this.DGV_Instructions)).BeginInit();
        this.GB_Tutorials.SuspendLayout();
        this.SuspendLayout();
        //
        // L_Script
        //
        this.L_Script.AutoSize = true;
        this.L_Script.Location = new System.Drawing.Point(12, 15);
        this.L_Script.Name = "L_Script";
        this.L_Script.Size = new System.Drawing.Size(40, 13);
        this.L_Script.TabIndex = 0;
        this.L_Script.Text = "Script:";
        //
        // CB_Script
        //
        this.CB_Script.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        // AutoCompleteSource must be set to ListItems BEFORE AutoCompleteMode leaves None - with
        // DropDownStyle already DropDownList, setting AutoCompleteMode first (while
        // AutoCompleteSource is still its default, None) throws immediately; confirmed the first
        // time this form was actually opened.
        this.CB_Script.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
        this.CB_Script.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
        this.CB_Script.Location = new System.Drawing.Point(58, 12);
        this.CB_Script.Name = "CB_Script";
        this.CB_Script.Size = new System.Drawing.Size(120, 21);
        this.CB_Script.TabIndex = 1;
        this.CB_Script.SelectedIndexChanged += new System.EventHandler(this.CB_Script_SelectedIndexChanged);
        //
        // L_Info
        //
        this.L_Info.Location = new System.Drawing.Point(190, 12);
        this.L_Info.Name = "L_Info";
        this.L_Info.Size = new System.Drawing.Size(420, 80);
        this.L_Info.TabIndex = 2;
        //
        // B_Save
        //
        this.B_Save.Enabled = false;
        this.B_Save.Location = new System.Drawing.Point(12, 40);
        this.B_Save.Name = "B_Save";
        this.B_Save.Size = new System.Drawing.Size(120, 28);
        this.B_Save.TabIndex = 3;
        this.B_Save.Text = "Save Script";
        this.B_Save.UseVisualStyleBackColor = true;
        this.B_Save.Click += new System.EventHandler(this.B_Save_Click);
        //
        // DGV_Instructions
        //
        this.DGV_Instructions.AllowUserToAddRows = false;
        this.DGV_Instructions.AllowUserToDeleteRows = false;
        this.DGV_Instructions.Location = new System.Drawing.Point(12, 280);
        this.DGV_Instructions.Name = "DGV_Instructions";
        this.DGV_Instructions.RowHeadersWidth = 25;
        this.DGV_Instructions.Size = new System.Drawing.Size(598, 280);
        this.DGV_Instructions.TabIndex = 5;
        this.DGV_Instructions.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.DGV_Instructions_CellEndEdit);
        //
        // GB_Tutorials
        //
        this.GB_Tutorials.Controls.Add(this.CHK_RepairCatchTutorial);
        this.GB_Tutorials.Controls.Add(this.CHK_SkipCatchTutorial);
        this.GB_Tutorials.Controls.Add(this.CHK_UndoCatchTutorial);
        this.GB_Tutorials.Controls.Add(this.CHK_SkipZMoveTutorial);
        this.GB_Tutorials.Controls.Add(this.CHK_UndoZMoveTutorial);
        this.GB_Tutorials.Controls.Add(this.B_ApplyTutorialSkips);
        this.GB_Tutorials.Controls.Add(this.L_TutorialSkipStatus);
        this.GB_Tutorials.Location = new System.Drawing.Point(12, 72);
        this.GB_Tutorials.Name = "GB_Tutorials";
        this.GB_Tutorials.Size = new System.Drawing.Size(598, 202);
        this.GB_Tutorials.TabIndex = 4;
        this.GB_Tutorials.TabStop = false;
        this.GB_Tutorials.Text = "Cutscene skips (known one-way edits, applied directly to the amxscript archive)";
        //
        // CHK_RepairCatchTutorial
        //
        this.CHK_RepairCatchTutorial.AutoSize = true;
        this.CHK_RepairCatchTutorial.ForeColor = System.Drawing.Color.Firebrick;
        this.CHK_RepairCatchTutorial.Location = new System.Drawing.Point(10, 20);
        this.CHK_RepairCatchTutorial.Name = "CHK_RepairCatchTutorial";
        this.CHK_RepairCatchTutorial.Size = new System.Drawing.Size(420, 17);
        this.CHK_RepairCatchTutorial.TabIndex = 0;
        this.CHK_RepairCatchTutorial.Text = "Repair the earlier bad catching-tutorial edit (restores the removed calls)";
        this.CHK_RepairCatchTutorial.UseVisualStyleBackColor = true;
        this.CHK_RepairCatchTutorial.Visible = false;
        //
        // CHK_SkipCatchTutorial
        //
        this.CHK_SkipCatchTutorial.AutoSize = true;
        this.CHK_SkipCatchTutorial.ForeColor = System.Drawing.Color.Firebrick;
        this.CHK_SkipCatchTutorial.Location = new System.Drawing.Point(10, 40);
        this.CHK_SkipCatchTutorial.Name = "CHK_SkipCatchTutorial";
        this.CHK_SkipCatchTutorial.Size = new System.Drawing.Size(560, 17);
        this.CHK_SkipCatchTutorial.TabIndex = 1;
        this.CHK_SkipCatchTutorial.Text = "Skip the catching tutorial (script 117) - WARNING: known to loop, see the Undo box below";
        this.CHK_SkipCatchTutorial.UseVisualStyleBackColor = true;
        //
        // CHK_UndoCatchTutorial
        //
        this.CHK_UndoCatchTutorial.AutoSize = true;
        this.CHK_UndoCatchTutorial.Location = new System.Drawing.Point(10, 60);
        this.CHK_UndoCatchTutorial.Name = "CHK_UndoCatchTutorial";
        this.CHK_UndoCatchTutorial.Size = new System.Drawing.Size(200, 17);
        this.CHK_UndoCatchTutorial.TabIndex = 2;
        this.CHK_UndoCatchTutorial.Text = "Undo the catching-tutorial skip";
        this.CHK_UndoCatchTutorial.UseVisualStyleBackColor = true;
        //
        // CHK_SkipZMoveTutorial
        //
        this.CHK_SkipZMoveTutorial.AutoSize = true;
        this.CHK_SkipZMoveTutorial.Location = new System.Drawing.Point(10, 84);
        this.CHK_SkipZMoveTutorial.Name = "CHK_SkipZMoveTutorial";
        this.CHK_SkipZMoveTutorial.Size = new System.Drawing.Size(400, 17);
        this.CHK_SkipZMoveTutorial.TabIndex = 3;
        this.CHK_SkipZMoveTutorial.Text = "Skip the Z-Move tutorial (script 130) - unconfirmed if it loops too";
        this.CHK_SkipZMoveTutorial.UseVisualStyleBackColor = true;
        //
        // CHK_UndoZMoveTutorial
        //
        this.CHK_UndoZMoveTutorial.AutoSize = true;
        this.CHK_UndoZMoveTutorial.Location = new System.Drawing.Point(10, 104);
        this.CHK_UndoZMoveTutorial.Name = "CHK_UndoZMoveTutorial";
        this.CHK_UndoZMoveTutorial.Size = new System.Drawing.Size(200, 17);
        this.CHK_UndoZMoveTutorial.TabIndex = 4;
        this.CHK_UndoZMoveTutorial.Text = "Undo the Z-Move-tutorial skip";
        this.CHK_UndoZMoveTutorial.UseVisualStyleBackColor = true;
        //
        // B_ApplyTutorialSkips
        //
        this.B_ApplyTutorialSkips.Location = new System.Drawing.Point(10, 130);
        this.B_ApplyTutorialSkips.Name = "B_ApplyTutorialSkips";
        this.B_ApplyTutorialSkips.Size = new System.Drawing.Size(160, 24);
        this.B_ApplyTutorialSkips.TabIndex = 5;
        this.B_ApplyTutorialSkips.Text = "Apply checked boxes";
        this.B_ApplyTutorialSkips.UseVisualStyleBackColor = true;
        this.B_ApplyTutorialSkips.Click += new System.EventHandler(this.B_ApplyTutorialSkips_Click);
        //
        // L_TutorialSkipStatus
        //
        this.L_TutorialSkipStatus.Location = new System.Drawing.Point(10, 158);
        this.L_TutorialSkipStatus.Name = "L_TutorialSkipStatus";
        this.L_TutorialSkipStatus.Size = new System.Drawing.Size(578, 40);
        this.L_TutorialSkipStatus.TabIndex = 6;
        //
        // AMXScriptEditor
        //
        this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(622, 572);
        this.Controls.Add(this.DGV_Instructions);
        this.Controls.Add(this.GB_Tutorials);
        this.Controls.Add(this.B_Save);
        this.Controls.Add(this.L_Info);
        this.Controls.Add(this.CB_Script);
        this.Controls.Add(this.L_Script);
        this.MinimumSize = new System.Drawing.Size(638, 610);
        this.Name = "AMXScriptEditor";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        this.Text = "AMX Script Editor";
        ((System.ComponentModel.ISupportInitialize)(this.DGV_Instructions)).EndInit();
        this.GB_Tutorials.ResumeLayout(false);
        this.GB_Tutorials.PerformLayout();
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion

    private System.Windows.Forms.Label L_Script;
    private System.Windows.Forms.ComboBox CB_Script;
    private System.Windows.Forms.Label L_Info;
    private System.Windows.Forms.Button B_Save;
    private System.Windows.Forms.DataGridView DGV_Instructions;
    private System.Windows.Forms.GroupBox GB_Tutorials;
    private System.Windows.Forms.CheckBox CHK_RepairCatchTutorial;
    private System.Windows.Forms.CheckBox CHK_SkipCatchTutorial;
    private System.Windows.Forms.CheckBox CHK_UndoCatchTutorial;
    private System.Windows.Forms.CheckBox CHK_SkipZMoveTutorial;
    private System.Windows.Forms.CheckBox CHK_UndoZMoveTutorial;
    private System.Windows.Forms.Button B_ApplyTutorialSkips;
    private System.Windows.Forms.Label L_TutorialSkipStatus;
}
