using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

using pk3DS.Core;
using pk3DS.Core.Structures;

namespace pk3DS.WinForms;

public partial class ZoneDataEditor7 : Form
{
    private readonly LazyGARCFile zoneGarc;
    private readonly string[] locationList;
    private readonly ZoneData7[] zones;
    private int entry = -1;

    public ZoneDataEditor7(LazyGARCFile zd, string[] locations)
    {
        zoneGarc = zd;
        locationList = locations;

        byte[] blob = zoneGarc.Files[0];
        zones = ZoneData7.GetArray(blob);
        for (int i = 0; i < zones.Length; i++)
            zones[i].SetZoneName(locationList, i);

        InitializeComponent();
        ApplyGridTheme(WinFormsUtil.CurrentTheme == WinFormsUtil.VisualTheme.Dark);

        CB_Zone.Items.AddRange(zones.Select(z => (object)z.Name).ToArray());
        CB_Zone.SelectedIndex = 0;

        this.FormClosing += Form_Closing;
    }

    private void ApplyGridTheme(bool dark)
    {
        Grid.ViewBackColor = dark ? Color.FromArgb(30, 30, 35) : Color.FromArgb(50, 50, 60);
        Grid.ViewForeColor = Color.White;
        Grid.LineColor = dark ? Color.FromArgb(60, 60, 75) : Color.FromArgb(90, 90, 100);
        Grid.CategoryForeColor = WinFormsUtil.Accent(WinFormsUtil.VisualTheme.Dark);   // the grid stays dark in every theme, so it takes the bright accent
        Grid.HelpBackColor = Grid.ViewBackColor;
        Grid.HelpForeColor = Color.LightCyan;
        Grid.CommandsBackColor = Grid.ViewBackColor;
        Grid.CommandsForeColor = Color.White;
        Grid.SelectedItemWithFocusBackColor = Color.FromArgb(80, 100, 150);
    }

    private void ChangeEntry(object sender, EventArgs e)
    {
        entry = CB_Zone.SelectedIndex;
        L_Index.Text = "Index: " + entry.ToString("000");
        Grid.SelectedObject = zones[entry];
    }

    /// <summary>Writes every zone's (possibly-edited) bytes back into the flat zone-array blob.</summary>
    private void SaveAll()
    {
        byte[] blob = new byte[zones.Length * ZoneData7.SIZE];
        for (int i = 0; i < zones.Length; i++)
            Array.Copy(zones[i].Write(), 0, blob, i * ZoneData7.SIZE, ZoneData7.SIZE);

        var files = zoneGarc.Files;
        files[0] = blob;
        zoneGarc.Files = files;
    }

    private void B_Save_Click(object sender, EventArgs e)
    {
        SaveAll();
        zoneGarc.Save();
        WinFormsUtil.Alert("Zone data saved.");
    }

    private void Form_Closing(object sender, FormClosingEventArgs e)
    {
        SaveAll();
    }
}
