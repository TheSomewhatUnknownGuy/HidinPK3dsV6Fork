namespace pk3DS.WinForms;

partial class ModelScaleEditor
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
        this.L_Species = new System.Windows.Forms.Label();
        this.CB_Species = new System.Windows.Forms.ComboBox();
        this.L_Form = new System.Windows.Forms.Label();
        this.CB_Form = new System.Windows.Forms.ComboBox();
        this.L_Height = new System.Windows.Forms.Label();
        this.NUD_Height = new System.Windows.Forms.NumericUpDown();
        this.L_HeightHint = new System.Windows.Forms.Label();
        this.B_SaveOne = new System.Windows.Forms.Button();
        this.B_SaveAll = new System.Windows.Forms.Button();
        this.L_Status = new System.Windows.Forms.Label();
        ((System.ComponentModel.ISupportInitialize)(this.NUD_Height)).BeginInit();
        this.SuspendLayout();
        //
        // L_Species
        //
        this.L_Species.AutoSize = true;
        this.L_Species.Location = new System.Drawing.Point(10, 15);
        this.L_Species.Name = "L_Species";
        this.L_Species.Size = new System.Drawing.Size(45, 13);
        this.L_Species.TabIndex = 0;
        this.L_Species.Text = "Species:";
        //
        // CB_Species
        //
        this.CB_Species.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.CB_Species.Location = new System.Drawing.Point(85, 12);
        this.CB_Species.Name = "CB_Species";
        this.CB_Species.Size = new System.Drawing.Size(260, 21);
        this.CB_Species.TabIndex = 1;
        this.CB_Species.SelectedIndexChanged += new System.EventHandler(this.CB_Species_SelectedIndexChanged);
        //
        // L_Form
        //
        this.L_Form.AutoSize = true;
        this.L_Form.Location = new System.Drawing.Point(10, 45);
        this.L_Form.Name = "L_Form";
        this.L_Form.Size = new System.Drawing.Size(34, 13);
        this.L_Form.TabIndex = 2;
        this.L_Form.Text = "Form:";
        //
        // CB_Form
        //
        this.CB_Form.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.CB_Form.Location = new System.Drawing.Point(85, 42);
        this.CB_Form.Name = "CB_Form";
        this.CB_Form.Size = new System.Drawing.Size(180, 21);
        this.CB_Form.TabIndex = 3;
        this.CB_Form.SelectedIndexChanged += new System.EventHandler(this.CB_Form_SelectedIndexChanged);
        //
        // L_Height
        //
        this.L_Height.AutoSize = true;
        this.L_Height.Location = new System.Drawing.Point(10, 75);
        this.L_Height.Name = "L_Height";
        this.L_Height.Size = new System.Drawing.Size(72, 13);
        this.L_Height.TabIndex = 4;
        this.L_Height.Text = "Model Scale:";
        //
        // NUD_Height
        //
        this.NUD_Height.DecimalPlaces = 1;
        this.NUD_Height.Enabled = false;
        this.NUD_Height.Increment = new decimal(new int[] { 5, 0, 0, 0 });
        this.NUD_Height.Location = new System.Drawing.Point(85, 73);
        this.NUD_Height.Maximum = new decimal(new int[] { 500, 0, 0, 0 });
        this.NUD_Height.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
        this.NUD_Height.Name = "NUD_Height";
        this.NUD_Height.Size = new System.Drawing.Size(70, 20);
        this.NUD_Height.TabIndex = 5;
        this.NUD_Height.Value = new decimal(new int[] { 100, 0, 0, 0 });
        this.NUD_Height.ValueChanged += new System.EventHandler(this.NUD_Height_ValueChanged);
        //
        // L_HeightHint
        //
        this.L_HeightHint.AutoSize = true;
        this.L_HeightHint.ForeColor = System.Drawing.Color.Gray;
        this.L_HeightHint.Location = new System.Drawing.Point(160, 76);
        this.L_HeightHint.Name = "L_HeightHint";
        this.L_HeightHint.Size = new System.Drawing.Size(220, 13);
        this.L_HeightHint.TabIndex = 9;
        this.L_HeightHint.Text = "% - 100 = default size, 150 = 50% bigger";
        //
        // B_SaveOne
        //
        this.B_SaveOne.Enabled = false;
        this.B_SaveOne.Location = new System.Drawing.Point(10, 105);
        this.B_SaveOne.Name = "B_SaveOne";
        this.B_SaveOne.Size = new System.Drawing.Size(120, 26);
        this.B_SaveOne.TabIndex = 6;
        this.B_SaveOne.Text = "Apply Change";
        this.B_SaveOne.UseVisualStyleBackColor = true;
        this.B_SaveOne.Click += new System.EventHandler(this.B_SaveOne_Click);
        //
        // B_SaveAll
        //
        this.B_SaveAll.Enabled = false;
        this.B_SaveAll.Location = new System.Drawing.Point(136, 105);
        this.B_SaveAll.Name = "B_SaveAll";
        this.B_SaveAll.Size = new System.Drawing.Size(120, 26);
        this.B_SaveAll.TabIndex = 7;
        this.B_SaveAll.Text = "Save to ROM";
        this.B_SaveAll.UseVisualStyleBackColor = true;
        this.B_SaveAll.Click += new System.EventHandler(this.B_SaveAll_Click);
        //
        // L_Status
        //
        this.L_Status.Location = new System.Drawing.Point(10, 140);
        this.L_Status.Name = "L_Status";
        this.L_Status.Size = new System.Drawing.Size(400, 45);
        this.L_Status.TabIndex = 8;
        this.L_Status.Text = "";
        //
        // ModelScaleEditor
        //
        this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(420, 195);
        this.Controls.Add(this.L_Status);
        this.Controls.Add(this.B_SaveAll);
        this.Controls.Add(this.B_SaveOne);
        this.Controls.Add(this.L_HeightHint);
        this.Controls.Add(this.NUD_Height);
        this.Controls.Add(this.L_Height);
        this.Controls.Add(this.CB_Form);
        this.Controls.Add(this.L_Form);
        this.Controls.Add(this.CB_Species);
        this.Controls.Add(this.L_Species);
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.Name = "ModelScaleEditor";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        this.Text = "Model Scale Editor";
        ((System.ComponentModel.ISupportInitialize)(this.NUD_Height)).EndInit();
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion

    private System.Windows.Forms.Label L_Species;
    private System.Windows.Forms.ComboBox CB_Species;
    private System.Windows.Forms.Label L_Form;
    private System.Windows.Forms.ComboBox CB_Form;
    private System.Windows.Forms.Label L_Height;
    private System.Windows.Forms.NumericUpDown NUD_Height;
    private System.Windows.Forms.Label L_HeightHint;
    private System.Windows.Forms.Button B_SaveOne;
    private System.Windows.Forms.Button B_SaveAll;
    private System.Windows.Forms.Label L_Status;
}
