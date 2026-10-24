namespace pk3DS.WinForms;

partial class GiftItemEditor
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
        this.B_Save = new System.Windows.Forms.Button();
        this.CB_HideLow = new System.Windows.Forms.CheckBox();
        this.DGV_Gifts = new System.Windows.Forms.DataGridView();
        this.ColFile = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.ColNotes = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.ColCurrent = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.ColConfidence = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.ColContext = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.ColNewItem = new System.Windows.Forms.DataGridViewComboBoxColumn();
        this.ColNewQty = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.ColAddAnother = new System.Windows.Forms.DataGridViewButtonColumn();
        ((System.ComponentModel.ISupportInitialize)(this.DGV_Gifts)).BeginInit();
        this.SuspendLayout();
        //
        // L_Status
        //
        this.L_Status.AutoSize = true;
        this.L_Status.Location = new System.Drawing.Point(8, 8);
        this.L_Status.Name = "L_Status";
        this.L_Status.Size = new System.Drawing.Size(400, 13);
        this.L_Status.TabIndex = 0;
        this.L_Status.Text = "Loading...";
        //
        // B_Save
        //
        this.B_Save.Enabled = false;
        this.B_Save.Location = new System.Drawing.Point(8, 24);
        this.B_Save.Name = "B_Save";
        this.B_Save.Size = new System.Drawing.Size(110, 23);
        this.B_Save.TabIndex = 1;
        this.B_Save.Text = "Save Changes";
        this.B_Save.UseVisualStyleBackColor = true;
        this.B_Save.Click += new System.EventHandler(this.B_Save_Click);
        //
        // CB_HideLow
        //
        this.CB_HideLow.AutoSize = true;
        this.CB_HideLow.Location = new System.Drawing.Point(126, 28);
        this.CB_HideLow.Name = "CB_HideLow";
        this.CB_HideLow.Size = new System.Drawing.Size(200, 17);
        this.CB_HideLow.TabIndex = 2;
        this.CB_HideLow.Text = "Hide low-confidence (remembered)";
        this.CB_HideLow.UseVisualStyleBackColor = true;
        this.CB_HideLow.CheckedChanged += new System.EventHandler(this.CB_HideLow_CheckedChanged);
        //
        // DGV_Gifts
        //
        this.DGV_Gifts.AllowUserToAddRows = false;
        this.DGV_Gifts.AllowUserToDeleteRows = false;
        this.DGV_Gifts.AutoGenerateColumns = false;
        this.DGV_Gifts.ShowCellToolTips = true;
        this.DGV_Gifts.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColFile,
            this.ColNotes,
            this.ColCurrent,
            this.ColConfidence,
            this.ColContext,
            this.ColNewItem,
            this.ColNewQty,
            this.ColAddAnother});
        this.DGV_Gifts.Location = new System.Drawing.Point(8, 50);
        this.DGV_Gifts.Name = "DGV_Gifts";
        this.DGV_Gifts.RowHeadersWidth = 20;
        this.DGV_Gifts.RowTemplate.Height = 20;
        this.DGV_Gifts.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                                                                 | System.Windows.Forms.AnchorStyles.Left)
                                                                | System.Windows.Forms.AnchorStyles.Right)));
        this.DGV_Gifts.Size = new System.Drawing.Size(754, 462);
        this.DGV_Gifts.TabIndex = 3;
        this.DGV_Gifts.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.DGV_Gifts_CellEndEdit);
        this.DGV_Gifts.CellValueChanged += new System.Windows.Forms.DataGridViewCellEventHandler(this.DGV_Gifts_CellValueChanged);
        this.DGV_Gifts.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DGV_Gifts_CellClick);
        this.DGV_Gifts.EditingControlShowing += new System.Windows.Forms.DataGridViewEditingControlShowingEventHandler(this.DGV_Gifts_EditingControlShowing);
        //
        // ColFile
        //
        this.ColFile.HeaderText = "File";
        this.ColFile.Name = "ColFile";
        this.ColFile.ReadOnly = true;
        this.ColFile.Width = 50;
        //
        // ColNotes
        //
        this.ColNotes.HeaderText = "Location / Notes";
        this.ColNotes.Name = "ColNotes";
        this.ColNotes.ReadOnly = true;
        this.ColNotes.Width = 130;
        //
        // ColCurrent
        //
        this.ColCurrent.HeaderText = "Current Gift";
        this.ColCurrent.Name = "ColCurrent";
        this.ColCurrent.ReadOnly = true;
        this.ColCurrent.Width = 95;
        //
        // ColConfidence
        //
        this.ColConfidence.HeaderText = "Match";
        this.ColConfidence.Name = "ColConfidence";
        this.ColConfidence.ReadOnly = true;
        this.ColConfidence.Width = 90;
        //
        // ColContext
        //
        this.ColContext.HeaderText = "Context (hover for full)";
        this.ColContext.Name = "ColContext";
        this.ColContext.ReadOnly = true;
        this.ColContext.Width = 150;
        //
        // ColNewItem
        //
        this.ColNewItem.HeaderText = "New Item";
        this.ColNewItem.Name = "ColNewItem";
        this.ColNewItem.Width = 110;
        this.ColNewItem.DisplayStyleForCurrentCellOnly = true;
        //
        // ColNewQty
        //
        this.ColNewQty.HeaderText = "Qty";
        this.ColNewQty.Name = "ColNewQty";
        this.ColNewQty.Width = 40;
        //
        // ColAddAnother
        //
        this.ColAddAnother.HeaderText = "";
        this.ColAddAnother.Name = "ColAddAnother";
        this.ColAddAnother.Width = 60;
        this.ColAddAnother.Text = "+ Add";
        this.ColAddAnother.UseColumnTextForButtonValue = false;
        //
        // GiftItemEditor
        //
        this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(770, 520);
        this.Controls.Add(this.DGV_Gifts);
        this.Controls.Add(this.CB_HideLow);
        this.Controls.Add(this.B_Save);
        this.Controls.Add(this.L_Status);
        this.MinimumSize = new System.Drawing.Size(620, 360);
        this.Name = "GiftItemEditor";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        this.Text = "NPC Gift Editor";
        ((System.ComponentModel.ISupportInitialize)(this.DGV_Gifts)).EndInit();
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion

    private System.Windows.Forms.Label L_Status;
    private System.Windows.Forms.Button B_Save;
    private System.Windows.Forms.CheckBox CB_HideLow;
    private System.Windows.Forms.DataGridView DGV_Gifts;
    private System.Windows.Forms.DataGridViewTextBoxColumn ColFile;
    private System.Windows.Forms.DataGridViewTextBoxColumn ColNotes;
    private System.Windows.Forms.DataGridViewTextBoxColumn ColCurrent;
    private System.Windows.Forms.DataGridViewTextBoxColumn ColConfidence;
    private System.Windows.Forms.DataGridViewTextBoxColumn ColContext;
    private System.Windows.Forms.DataGridViewComboBoxColumn ColNewItem;
    private System.Windows.Forms.DataGridViewTextBoxColumn ColNewQty;
    private System.Windows.Forms.DataGridViewButtonColumn ColAddAnother;
}
