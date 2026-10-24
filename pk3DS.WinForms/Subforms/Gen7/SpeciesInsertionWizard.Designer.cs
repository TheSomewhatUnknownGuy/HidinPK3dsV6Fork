namespace pk3DS.WinForms;

partial class SpeciesInsertionWizard
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
        this.L_Status = new System.Windows.Forms.Label();
        this.L_SpeciesCount = new System.Windows.Forms.Label();
        this.NUD_SpeciesCount = new System.Windows.Forms.NumericUpDown();
        this.DGV_Species = new System.Windows.Forms.DataGridView();
        this.B_Build = new System.Windows.Forms.Button();
        this.B_Save = new System.Windows.Forms.Button();
        this.TB_Log = new System.Windows.Forms.TextBox();
        ((System.ComponentModel.ISupportInitialize)(this.NUD_SpeciesCount)).BeginInit();
        ((System.ComponentModel.ISupportInitialize)(this.DGV_Species)).BeginInit();
        this.SuspendLayout();
        //
        // L_Status
        //
        this.L_Status.Location = new System.Drawing.Point(12, 9);
        this.L_Status.Name = "L_Status";
        this.L_Status.Size = new System.Drawing.Size(816, 70);
        this.L_Status.TabIndex = 0;
        this.L_Status.Text = "Status";
        //
        // L_SpeciesCount
        //
        this.L_SpeciesCount.AutoSize = true;
        this.L_SpeciesCount.Location = new System.Drawing.Point(12, 87);
        this.L_SpeciesCount.Name = "L_SpeciesCount";
        this.L_SpeciesCount.Size = new System.Drawing.Size(130, 13);
        this.L_SpeciesCount.TabIndex = 1;
        this.L_SpeciesCount.Text = "Number of species to add:";
        //
        // NUD_SpeciesCount
        //
        this.NUD_SpeciesCount.Location = new System.Drawing.Point(160, 85);
        this.NUD_SpeciesCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        this.NUD_SpeciesCount.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
        this.NUD_SpeciesCount.Value = new decimal(new int[] { 1, 0, 0, 0 });
        this.NUD_SpeciesCount.Name = "NUD_SpeciesCount";
        this.NUD_SpeciesCount.Size = new System.Drawing.Size(55, 20);
        this.NUD_SpeciesCount.TabIndex = 2;
        this.NUD_SpeciesCount.ValueChanged += new System.EventHandler(this.NUD_SpeciesCount_ValueChanged);
        //
        // DGV_Species
        //
        this.DGV_Species.AllowUserToAddRows = false;
        this.DGV_Species.AllowUserToDeleteRows = false;
        this.DGV_Species.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        this.DGV_Species.Location = new System.Drawing.Point(12, 112);
        this.DGV_Species.Name = "DGV_Species";
        this.DGV_Species.RowHeadersWidth = 25;
        this.DGV_Species.Size = new System.Drawing.Size(816, 140);
        this.DGV_Species.TabIndex = 3;
        //
        // B_Build
        //
        this.B_Build.Location = new System.Drawing.Point(12, 260);
        this.B_Build.Name = "B_Build";
        this.B_Build.Size = new System.Drawing.Size(130, 28);
        this.B_Build.TabIndex = 4;
        this.B_Build.Text = "Build In Memory";
        this.B_Build.UseVisualStyleBackColor = true;
        this.B_Build.Click += new System.EventHandler(this.B_Build_Click);
        //
        // B_Save
        //
        this.B_Save.Enabled = false;
        this.B_Save.Location = new System.Drawing.Point(150, 260);
        this.B_Save.Name = "B_Save";
        this.B_Save.Size = new System.Drawing.Size(130, 28);
        this.B_Save.TabIndex = 5;
        this.B_Save.Text = "Save to ROM";
        this.B_Save.UseVisualStyleBackColor = true;
        this.B_Save.Click += new System.EventHandler(this.B_Save_Click);
        //
        // TB_Log
        //
        this.TB_Log.Location = new System.Drawing.Point(12, 296);
        this.TB_Log.Multiline = true;
        this.TB_Log.Name = "TB_Log";
        this.TB_Log.ReadOnly = true;
        this.TB_Log.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
        this.TB_Log.Size = new System.Drawing.Size(686, 174);
        this.TB_Log.TabIndex = 6;
        //
        // SpeciesInsertionWizard
        //
        this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(710, 484);
        this.Controls.Add(this.TB_Log);
        this.Controls.Add(this.B_Save);
        this.Controls.Add(this.B_Build);
        this.Controls.Add(this.DGV_Species);
        this.Controls.Add(this.NUD_SpeciesCount);
        this.Controls.Add(this.L_SpeciesCount);
        this.Controls.Add(this.L_Status);
        this.MaximizeBox = false;
        this.MinimumSize = new System.Drawing.Size(700, 520);
        this.Name = "SpeciesInsertionWizard";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        this.Text = "Insert Species";
        ((System.ComponentModel.ISupportInitialize)(this.NUD_SpeciesCount)).EndInit();
        ((System.ComponentModel.ISupportInitialize)(this.DGV_Species)).EndInit();
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion

    private System.Windows.Forms.Label L_Status;
    private System.Windows.Forms.Label L_SpeciesCount;
    private System.Windows.Forms.NumericUpDown NUD_SpeciesCount;
    private System.Windows.Forms.DataGridView DGV_Species;
    private System.Windows.Forms.Button B_Build;
    private System.Windows.Forms.Button B_Save;
    private System.Windows.Forms.TextBox TB_Log;
}
