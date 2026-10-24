namespace pk3DS.WinForms;

partial class IconEditor
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
        this.L_PartyIcon = new System.Windows.Forms.Label();
        this.L_MenuIcon = new System.Windows.Forms.Label();
        this.PB_Party = new System.Windows.Forms.PictureBox();
        this.PB_Menu = new System.Windows.Forms.PictureBox();
        this.GB_ReplaceFrom = new System.Windows.Forms.GroupBox();
        this.L_SourceSpecies = new System.Windows.Forms.Label();
        this.CB_SourceSpecies = new System.Windows.Forms.ComboBox();
        this.L_SourceForm = new System.Windows.Forms.Label();
        this.CB_SourceForm = new System.Windows.Forms.ComboBox();
        this.L_SourcePartyLabel = new System.Windows.Forms.Label();
        this.L_SourceMenuLabel = new System.Windows.Forms.Label();
        this.PB_SourceParty = new System.Windows.Forms.PictureBox();
        this.PB_SourceMenu = new System.Windows.Forms.PictureBox();
        this.B_CopyFrom = new System.Windows.Forms.Button();
        this.B_ImportImage = new System.Windows.Forms.Button();
        this.B_Save = new System.Windows.Forms.Button();
        this.L_Status = new System.Windows.Forms.Label();
        ((System.ComponentModel.ISupportInitialize)(this.PB_Party)).BeginInit();
        ((System.ComponentModel.ISupportInitialize)(this.PB_Menu)).BeginInit();
        this.GB_ReplaceFrom.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.PB_SourceParty)).BeginInit();
        ((System.ComponentModel.ISupportInitialize)(this.PB_SourceMenu)).BeginInit();
        this.SuspendLayout();
        //
        // L_Species
        //
        this.L_Species.AutoSize = true;
        this.L_Species.Location = new System.Drawing.Point(12, 16);
        this.L_Species.Name = "L_Species";
        this.L_Species.Size = new System.Drawing.Size(45, 13);
        this.L_Species.TabIndex = 0;
        this.L_Species.Text = "Species:";
        //
        // CB_Species
        //
        this.CB_Species.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.CB_Species.FormattingEnabled = true;
        this.CB_Species.Location = new System.Drawing.Point(100, 12);
        this.CB_Species.Name = "CB_Species";
        this.CB_Species.Size = new System.Drawing.Size(220, 21);
        this.CB_Species.TabIndex = 1;
        this.CB_Species.SelectedIndexChanged += new System.EventHandler(this.CB_Species_SelectedIndexChanged);
        //
        // L_Form
        //
        this.L_Form.AutoSize = true;
        this.L_Form.Location = new System.Drawing.Point(340, 16);
        this.L_Form.Name = "L_Form";
        this.L_Form.Size = new System.Drawing.Size(33, 13);
        this.L_Form.TabIndex = 2;
        this.L_Form.Text = "Form:";
        //
        // CB_Form
        //
        this.CB_Form.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.CB_Form.FormattingEnabled = true;
        this.CB_Form.Location = new System.Drawing.Point(380, 12);
        this.CB_Form.Name = "CB_Form";
        this.CB_Form.Size = new System.Drawing.Size(160, 21);
        this.CB_Form.TabIndex = 3;
        this.CB_Form.SelectedIndexChanged += new System.EventHandler(this.CB_Form_SelectedIndexChanged);
        //
        // L_PartyIcon
        //
        this.L_PartyIcon.AutoSize = true;
        this.L_PartyIcon.Location = new System.Drawing.Point(12, 46);
        this.L_PartyIcon.Name = "L_PartyIcon";
        this.L_PartyIcon.Size = new System.Drawing.Size(93, 13);
        this.L_PartyIcon.TabIndex = 4;
        this.L_PartyIcon.Text = "Party / Box Icon";
        //
        // L_MenuIcon
        //
        this.L_MenuIcon.AutoSize = true;
        this.L_MenuIcon.Location = new System.Drawing.Point(420, 46);
        this.L_MenuIcon.Name = "L_MenuIcon";
        this.L_MenuIcon.Size = new System.Drawing.Size(112, 13);
        this.L_MenuIcon.TabIndex = 5;
        this.L_MenuIcon.Text = "Menu / Summary Icon";
        //
        // PB_Party
        //
        this.PB_Party.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        this.PB_Party.Location = new System.Drawing.Point(12, 65);
        this.PB_Party.Name = "PB_Party";
        this.PB_Party.Size = new System.Drawing.Size(384, 192);
        this.PB_Party.SizeMode = System.Windows.Forms.PictureBoxSizeMode.CenterImage;
        this.PB_Party.TabIndex = 6;
        this.PB_Party.TabStop = false;
        //
        // PB_Menu
        //
        this.PB_Menu.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        this.PB_Menu.Location = new System.Drawing.Point(420, 65);
        this.PB_Menu.Name = "PB_Menu";
        this.PB_Menu.Size = new System.Drawing.Size(384, 192);
        this.PB_Menu.SizeMode = System.Windows.Forms.PictureBoxSizeMode.CenterImage;
        this.PB_Menu.TabIndex = 7;
        this.PB_Menu.TabStop = false;
        //
        // GB_ReplaceFrom
        //
        this.GB_ReplaceFrom.Controls.Add(this.L_SourceSpecies);
        this.GB_ReplaceFrom.Controls.Add(this.CB_SourceSpecies);
        this.GB_ReplaceFrom.Controls.Add(this.L_SourceForm);
        this.GB_ReplaceFrom.Controls.Add(this.CB_SourceForm);
        this.GB_ReplaceFrom.Controls.Add(this.L_SourcePartyLabel);
        this.GB_ReplaceFrom.Controls.Add(this.L_SourceMenuLabel);
        this.GB_ReplaceFrom.Controls.Add(this.PB_SourceParty);
        this.GB_ReplaceFrom.Controls.Add(this.PB_SourceMenu);
        this.GB_ReplaceFrom.Controls.Add(this.B_CopyFrom);
        this.GB_ReplaceFrom.Location = new System.Drawing.Point(12, 270);
        this.GB_ReplaceFrom.Name = "GB_ReplaceFrom";
        this.GB_ReplaceFrom.Size = new System.Drawing.Size(792, 175);
        this.GB_ReplaceFrom.TabIndex = 8;
        this.GB_ReplaceFrom.TabStop = false;
        this.GB_ReplaceFrom.Text = "Replace with an existing icon already in the ROM";
        //
        // L_SourceSpecies
        //
        this.L_SourceSpecies.AutoSize = true;
        this.L_SourceSpecies.Location = new System.Drawing.Point(10, 26);
        this.L_SourceSpecies.Name = "L_SourceSpecies";
        this.L_SourceSpecies.Size = new System.Drawing.Size(45, 13);
        this.L_SourceSpecies.TabIndex = 0;
        this.L_SourceSpecies.Text = "Species:";
        //
        // CB_SourceSpecies
        //
        this.CB_SourceSpecies.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.CB_SourceSpecies.FormattingEnabled = true;
        this.CB_SourceSpecies.Location = new System.Drawing.Point(98, 23);
        this.CB_SourceSpecies.Name = "CB_SourceSpecies";
        this.CB_SourceSpecies.Size = new System.Drawing.Size(220, 21);
        this.CB_SourceSpecies.TabIndex = 1;
        this.CB_SourceSpecies.SelectedIndexChanged += new System.EventHandler(this.CB_SourceSpecies_SelectedIndexChanged);
        //
        // L_SourceForm
        //
        this.L_SourceForm.AutoSize = true;
        this.L_SourceForm.Location = new System.Drawing.Point(330, 26);
        this.L_SourceForm.Name = "L_SourceForm";
        this.L_SourceForm.Size = new System.Drawing.Size(33, 13);
        this.L_SourceForm.TabIndex = 2;
        this.L_SourceForm.Text = "Form:";
        //
        // CB_SourceForm
        //
        this.CB_SourceForm.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.CB_SourceForm.FormattingEnabled = true;
        this.CB_SourceForm.Location = new System.Drawing.Point(370, 23);
        this.CB_SourceForm.Name = "CB_SourceForm";
        this.CB_SourceForm.Size = new System.Drawing.Size(160, 21);
        this.CB_SourceForm.TabIndex = 3;
        this.CB_SourceForm.SelectedIndexChanged += new System.EventHandler(this.CB_SourceForm_SelectedIndexChanged);
        //
        // L_SourcePartyLabel
        //
        this.L_SourcePartyLabel.AutoSize = true;
        this.L_SourcePartyLabel.Location = new System.Drawing.Point(10, 52);
        this.L_SourcePartyLabel.Name = "L_SourcePartyLabel";
        this.L_SourcePartyLabel.Size = new System.Drawing.Size(66, 13);
        this.L_SourcePartyLabel.TabIndex = 7;
        this.L_SourcePartyLabel.Text = "Party / Box";
        //
        // L_SourceMenuLabel
        //
        this.L_SourceMenuLabel.AutoSize = true;
        this.L_SourceMenuLabel.Location = new System.Drawing.Point(210, 52);
        this.L_SourceMenuLabel.Name = "L_SourceMenuLabel";
        this.L_SourceMenuLabel.Size = new System.Drawing.Size(83, 13);
        this.L_SourceMenuLabel.TabIndex = 8;
        this.L_SourceMenuLabel.Text = "Menu / Summary";
        //
        // PB_SourceParty
        //
        this.PB_SourceParty.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        this.PB_SourceParty.Location = new System.Drawing.Point(10, 68);
        this.PB_SourceParty.Name = "PB_SourceParty";
        this.PB_SourceParty.Size = new System.Drawing.Size(192, 96);
        this.PB_SourceParty.SizeMode = System.Windows.Forms.PictureBoxSizeMode.CenterImage;
        this.PB_SourceParty.TabIndex = 4;
        this.PB_SourceParty.TabStop = false;
        //
        // PB_SourceMenu
        //
        this.PB_SourceMenu.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        this.PB_SourceMenu.Location = new System.Drawing.Point(210, 68);
        this.PB_SourceMenu.Name = "PB_SourceMenu";
        this.PB_SourceMenu.Size = new System.Drawing.Size(192, 96);
        this.PB_SourceMenu.SizeMode = System.Windows.Forms.PictureBoxSizeMode.CenterImage;
        this.PB_SourceMenu.TabIndex = 5;
        this.PB_SourceMenu.TabStop = false;
        //
        // B_CopyFrom
        //
        this.B_CopyFrom.Location = new System.Drawing.Point(420, 100);
        this.B_CopyFrom.Name = "B_CopyFrom";
        this.B_CopyFrom.Size = new System.Drawing.Size(180, 32);
        this.B_CopyFrom.TabIndex = 6;
        this.B_CopyFrom.Text = "Copy This Icon Here";
        this.B_CopyFrom.UseVisualStyleBackColor = true;
        this.B_CopyFrom.Click += new System.EventHandler(this.B_CopyFrom_Click);
        //
        // B_ImportImage
        //
        this.B_ImportImage.Location = new System.Drawing.Point(12, 455);
        this.B_ImportImage.Name = "B_ImportImage";
        this.B_ImportImage.Size = new System.Drawing.Size(180, 32);
        this.B_ImportImage.TabIndex = 9;
        this.B_ImportImage.Text = "Import Image...";
        this.B_ImportImage.UseVisualStyleBackColor = true;
        this.B_ImportImage.Click += new System.EventHandler(this.B_ImportImage_Click);
        //
        // B_Save
        //
        this.B_Save.Location = new System.Drawing.Point(200, 455);
        this.B_Save.Name = "B_Save";
        this.B_Save.Size = new System.Drawing.Size(150, 32);
        this.B_Save.TabIndex = 10;
        this.B_Save.Text = "Save to ROM";
        this.B_Save.UseVisualStyleBackColor = true;
        this.B_Save.Click += new System.EventHandler(this.B_Save_Click);
        //
        // L_Status
        //
        this.L_Status.Location = new System.Drawing.Point(12, 495);
        this.L_Status.Name = "L_Status";
        this.L_Status.Size = new System.Drawing.Size(792, 60);
        this.L_Status.TabIndex = 11;
        this.L_Status.Text = "Drag-and-drop an image onto either preview, or use Import Image, to replace the selected icon. Nothing is written to the ROM until Save to ROM is clicked.\r\n" +
            "Expected size: 32x32 (one pose, auto-duplicated into both frames) or 64x32 (two explicit 32x32 frames side by side). Any other size is automatically resized/compressed to fit.";
        //
        // IconEditor
        //
        this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(816, 565);
        this.Controls.Add(this.L_Status);
        this.Controls.Add(this.B_Save);
        this.Controls.Add(this.B_ImportImage);
        this.Controls.Add(this.GB_ReplaceFrom);
        this.Controls.Add(this.PB_Menu);
        this.Controls.Add(this.PB_Party);
        this.Controls.Add(this.L_MenuIcon);
        this.Controls.Add(this.L_PartyIcon);
        this.Controls.Add(this.CB_Form);
        this.Controls.Add(this.L_Form);
        this.Controls.Add(this.CB_Species);
        this.Controls.Add(this.L_Species);
        this.MaximizeBox = false;
        this.MinimumSize = new System.Drawing.Size(832, 604);
        this.Name = "IconEditor";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        this.Text = "Icon Editor";
        this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.IconEditor_FormClosing);
        ((System.ComponentModel.ISupportInitialize)(this.PB_Party)).EndInit();
        ((System.ComponentModel.ISupportInitialize)(this.PB_Menu)).EndInit();
        this.GB_ReplaceFrom.ResumeLayout(false);
        this.GB_ReplaceFrom.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)(this.PB_SourceParty)).EndInit();
        ((System.ComponentModel.ISupportInitialize)(this.PB_SourceMenu)).EndInit();
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion

    private System.Windows.Forms.Label L_Species;
    private System.Windows.Forms.ComboBox CB_Species;
    private System.Windows.Forms.Label L_Form;
    private System.Windows.Forms.ComboBox CB_Form;
    private System.Windows.Forms.Label L_PartyIcon;
    private System.Windows.Forms.Label L_MenuIcon;
    private System.Windows.Forms.PictureBox PB_Party;
    private System.Windows.Forms.PictureBox PB_Menu;
    private System.Windows.Forms.GroupBox GB_ReplaceFrom;
    private System.Windows.Forms.Label L_SourceSpecies;
    private System.Windows.Forms.ComboBox CB_SourceSpecies;
    private System.Windows.Forms.Label L_SourceForm;
    private System.Windows.Forms.ComboBox CB_SourceForm;
    private System.Windows.Forms.Label L_SourcePartyLabel;
    private System.Windows.Forms.Label L_SourceMenuLabel;
    private System.Windows.Forms.PictureBox PB_SourceParty;
    private System.Windows.Forms.PictureBox PB_SourceMenu;
    private System.Windows.Forms.Button B_CopyFrom;
    private System.Windows.Forms.Button B_ImportImage;
    private System.Windows.Forms.Button B_Save;
    private System.Windows.Forms.Label L_Status;
}
