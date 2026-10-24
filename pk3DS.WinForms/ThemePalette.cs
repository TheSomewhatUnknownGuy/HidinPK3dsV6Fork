using System.Drawing;

namespace pk3DS.WinForms;

public sealed record ThemePalette
{
    // Windows and containers
    public Color Surface { get; init; }        // sub-form background
    public Color Text { get; init; }           // sub-form text
    public Color Card { get; init; }           // panels, group boxes, tab pages
    public Color CardText { get; init; }
    public Color LabelText { get; init; }
    public Color CheckText { get; init; }
    public Color Glass { get; init; }          // panels named "...Glass..."

    // Buttons
    public Color Button { get; init; }
    public Color ButtonText { get; init; }
    public Color ButtonBorder { get; init; }
    public Color ButtonHover { get; init; }

    // Inputs and lists
    public Color Input { get; init; }          // text boxes and trees
    public Color InputText { get; init; }
    public Color List { get; init; }           // combo boxes and list boxes
    public Color ListText { get; init; }
    public Color TreeLine { get; init; }

    // Grids
    public Color GridBack { get; init; }
    public Color Cell { get; init; }
    public Color CellText { get; init; }
    public Color Header { get; init; }
    public Color HeaderText { get; init; }
    public Color GridLine { get; init; }

    // Menus
    public Color Strip { get; init; }          // the menu bar
    public Color StripText { get; init; }
    public Color MenuItem { get; init; }       // menu bar items and their drop-downs
    public Color MenuItemText { get; init; }

    // Tab control
    public Color TabStrip { get; init; }
    public Color TabSelected { get; init; }
    public Color TabText { get; init; }

    public static ThemePalette For(WinFormsUtil.VisualTheme theme) => theme switch
    {
        WinFormsUtil.VisualTheme.Light => Light,
        WinFormsUtil.VisualTheme.GalaxyPurple => GalaxyPurple,
        WinFormsUtil.VisualTheme.Grey => Grey,
        _ => Dark,
    };

    private static readonly Color Ink = Color.FromArgb(33, 37, 41);   // Light's text
    private static readonly Color Lilac = Color.FromArgb(245, 235, 250);   // Galaxy Purple's text

    public static readonly ThemePalette Dark = new()
    {
        Surface = Color.FromArgb(18, 18, 24), Text = Color.FromArgb(230, 230, 240),
        Card = Color.FromArgb(20, 20, 30), CardText = Color.WhiteSmoke,
        LabelText = Color.FromArgb(220, 220, 220), CheckText = Color.WhiteSmoke,
        Glass = Color.FromArgb(180, 25, 25, 35),
        Button = Color.FromArgb(32, 34, 46), ButtonText = Color.WhiteSmoke,
        ButtonBorder = Color.FromArgb(60, 65, 85), ButtonHover = Color.FromArgb(50, 55, 75),
        Input = Color.FromArgb(12, 12, 18), InputText = Color.FromArgb(220, 220, 230),
        List = Color.FromArgb(12, 12, 18), ListText = Color.FromArgb(220, 220, 230),
        TreeLine = Color.FromArgb(90, 95, 115),
        GridBack = Color.FromArgb(18, 18, 24), Cell = Color.FromArgb(24, 26, 36), CellText = Color.WhiteSmoke,
        Header = Color.FromArgb(40, 44, 60), HeaderText = Color.WhiteSmoke, GridLine = Color.FromArgb(45, 50, 65),
        Strip = Color.FromArgb(45, 50, 65), StripText = Color.WhiteSmoke,
        MenuItem = Color.FromArgb(45, 50, 65), MenuItemText = Color.WhiteSmoke,
        TabStrip = Color.FromArgb(20, 20, 30), TabSelected = Color.FromArgb(45, 45, 60), TabText = Color.WhiteSmoke,
    };

    public static readonly ThemePalette Grey = new()
    {
        Surface = Color.FromArgb(45, 45, 48), Text = Color.FromArgb(241, 241, 241),
        Card = Color.FromArgb(55, 55, 60), CardText = Color.WhiteSmoke,
        LabelText = Color.FromArgb(235, 235, 235), CheckText = Color.WhiteSmoke,
        Glass = Color.FromArgb(180, 50, 50, 60),
        Button = Color.FromArgb(70, 70, 75), ButtonText = Color.WhiteSmoke,
        ButtonBorder = Color.FromArgb(90, 90, 95), ButtonHover = Color.FromArgb(85, 85, 90),
        Input = Color.FromArgb(40, 40, 45), InputText = Color.FromArgb(220, 220, 230),
        List = Color.FromArgb(40, 40, 45), ListText = Color.FromArgb(220, 220, 230),
        TreeLine = Color.FromArgb(90, 95, 115),
        GridBack = Color.FromArgb(45, 45, 50), Cell = Color.FromArgb(50, 50, 55), CellText = Color.WhiteSmoke,
        Header = Color.FromArgb(65, 65, 70), HeaderText = Color.WhiteSmoke, GridLine = Color.FromArgb(70, 70, 75),
        Strip = Color.FromArgb(60, 60, 70), StripText = Color.WhiteSmoke,
        MenuItem = Color.FromArgb(60, 60, 70), MenuItemText = Color.WhiteSmoke,
        TabStrip = Color.FromArgb(20, 20, 30), TabSelected = Color.FromArgb(45, 45, 60), TabText = Color.WhiteSmoke,
    };

    public static readonly ThemePalette Light = new()
    {
        Surface = Color.FromArgb(245, 246, 248), Text = Ink,
        Card = Color.White, CardText = Ink, LabelText = Ink, CheckText = Ink,
        Glass = Color.FromArgb(180, 240, 240, 240),
        Button = Color.FromArgb(235, 238, 242), ButtonText = Ink,
        ButtonBorder = Color.FromArgb(206, 212, 218), ButtonHover = Color.FromArgb(248, 249, 250),
        Input = Color.White, InputText = Ink,
        List = SystemColors.Window, ListText = SystemColors.WindowText,
        TreeLine = SystemColors.ControlDark,
        GridBack = SystemColors.ControlDark, Cell = SystemColors.Window, CellText = SystemColors.WindowText,
        Header = SystemColors.Control, HeaderText = SystemColors.ControlText, GridLine = SystemColors.ControlDark,
        Strip = Color.FromArgb(235, 238, 242), StripText = Ink,
        MenuItem = SystemColors.Control, MenuItemText = SystemColors.ControlText,
        TabStrip = Color.FromArgb(235, 235, 240), TabSelected = Color.White, TabText = Ink,
    };

    public static readonly ThemePalette GalaxyPurple = new()
    {
        Surface = Color.FromArgb(40, 22, 44), Text = Lilac,
        Card = Color.FromArgb(52, 28, 58), CardText = Lilac, LabelText = Lilac, CheckText = Lilac,
        Glass = Color.FromArgb(180, 68, 36, 76),
        Button = Color.FromArgb(68, 36, 76), ButtonText = Lilac,
        ButtonBorder = Color.FromArgb(102, 53, 114), ButtonHover = Color.FromArgb(88, 48, 98),
        Input = Color.FromArgb(32, 16, 36), InputText = Lilac,
        List = Color.FromArgb(32, 16, 36), ListText = Lilac,
        TreeLine = Color.FromArgb(142, 73, 128),
        GridBack = Color.FromArgb(40, 22, 44), Cell = Color.FromArgb(46, 24, 52), CellText = Lilac,
        Header = Color.FromArgb(72, 40, 82), HeaderText = Lilac, GridLine = Color.FromArgb(102, 53, 114),
        Strip = Color.FromArgb(32, 16, 36), StripText = Lilac,
        MenuItem = Color.FromArgb(58, 32, 66), MenuItemText = Lilac,
        TabStrip = Color.FromArgb(38, 20, 44), TabSelected = Color.FromArgb(72, 40, 82), TabText = Lilac,
    };
}
