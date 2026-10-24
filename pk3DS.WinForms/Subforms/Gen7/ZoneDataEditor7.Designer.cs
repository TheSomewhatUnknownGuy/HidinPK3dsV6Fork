namespace pk3DS.WinForms;

partial class ZoneDataEditor7
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
        this.CB_Zone = new System.Windows.Forms.ComboBox();
        this.L_Zone = new System.Windows.Forms.Label();
        this.L_Index = new System.Windows.Forms.Label();
        this.Grid = new System.Windows.Forms.PropertyGrid();
        this.B_Save = new System.Windows.Forms.Button();
        this.SuspendLayout();
        //
        // CB_Zone
        //
        this.CB_Zone.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
        this.CB_Zone.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
        this.CB_Zone.DropDownWidth = 260;
        this.CB_Zone.FormattingEnabled = true;
        this.CB_Zone.Location = new System.Drawing.Point(50, 10);
        this.CB_Zone.Name = "CB_Zone";
        this.CB_Zone.Size = new System.Drawing.Size(300, 21);
        this.CB_Zone.TabIndex = 1;
        this.CB_Zone.SelectedIndexChanged += new System.EventHandler(this.ChangeEntry);
        //
        // L_Zone
        //
        this.L_Zone.Location = new System.Drawing.Point(12, 13);
        this.L_Zone.Name = "L_Zone";
        this.L_Zone.Size = new System.Drawing.Size(36, 13);
        this.L_Zone.TabIndex = 2;
        this.L_Zone.Text = "Zone:";
        this.L_Zone.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
        //
        // L_Index
        //
        this.L_Index.AutoSize = true;
        this.L_Index.Location = new System.Drawing.Point(12, 38);
        this.L_Index.Name = "L_Index";
        this.L_Index.Size = new System.Drawing.Size(39, 13);
        this.L_Index.TabIndex = 3;
        this.L_Index.Text = "Index: ";
        //
        // B_Save
        //
        this.B_Save.Location = new System.Drawing.Point(360, 9);
        this.B_Save.Name = "B_Save";
        this.B_Save.Size = new System.Drawing.Size(75, 23);
        this.B_Save.TabIndex = 4;
        this.B_Save.Text = "Save";
        this.B_Save.UseVisualStyleBackColor = true;
        this.B_Save.Click += new System.EventHandler(this.B_Save_Click);
        //
        // Grid
        //
        this.Grid.Location = new System.Drawing.Point(11, 62);
        this.Grid.Name = "Grid";
        this.Grid.Size = new System.Drawing.Size(424, 400);
        this.Grid.TabIndex = 5;
        //
        // ZoneDataEditor7
        //
        this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(448, 474);
        this.Controls.Add(this.Grid);
        this.Controls.Add(this.B_Save);
        this.Controls.Add(this.L_Index);
        this.Controls.Add(this.L_Zone);
        this.Controls.Add(this.CB_Zone);
        this.MaximizeBox = false;
        this.MinimumSize = new System.Drawing.Size(464, 500);
        this.Name = "ZoneDataEditor7";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        this.Text = "Zone Data Editor";
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion

    private System.Windows.Forms.ComboBox CB_Zone;
    private System.Windows.Forms.Label L_Zone;
    private System.Windows.Forms.Label L_Index;
    private System.Windows.Forms.PropertyGrid Grid;
    private System.Windows.Forms.Button B_Save;
}
