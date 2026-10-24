using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace pk3DS.WinForms;

public sealed class ItemGuideDialog : Form
{
    public ItemGuideDialog()
    {
        Text = "Making Custom Items - Quick Guide";
        ClientSize = new Size(620, 560);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.CenterParent;

        var rtb = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9.5f),
            Text = GuideText,
        };

        // The text sits on a card inside a padded panel, so it does not touch the window edge and the
        // form's gradient still shows around it.
        var card = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 8, 14), Name = "Card" };
        card.Controls.Add(rtb);

        var pnl = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var btnClose = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Size = new Size(90, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        btnClose.Location = new Point(pnl.Width - btnClose.Width - 12, 8);
        pnl.Controls.Add(btnClose);
        pnl.Resize += (_, _) => btnClose.Location = new Point(pnl.Width - btnClose.Width - 12, 8);

        Controls.Add(card);
        Controls.Add(pnl);
        AcceptButton = btnClose;
        WinFormsUtil.ApplyTheme(this);

        // After theming, so the card colour is the theme's: solid card, accent-coloured section headings.
        var accent = WinFormsUtil.Accent(WinFormsUtil.CurrentTheme);
        rtb.BackColor = Color.FromArgb(255, card.BackColor);
        card.BackColor = Color.Transparent;
        rtb.SelectAll();
        rtb.SelectionIndent = 4;
        rtb.SelectionRightIndent = 4;
        int pos = 0;
        foreach (var line in rtb.Text.Split('\n'))
        {
            string t = line.Trim();
            if (t.Length > 3 && t == t.ToUpperInvariant() && t.Any(char.IsLetter))
            {
                rtb.Select(pos, line.Length);
                rtb.SelectionColor = accent;
                rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
            }
            pos += line.Length + 1;
        }
        rtb.Select(0, 0);
    }

    private const string GuideText =
@"WHAT THE ITEM EDITOR CAN DO ON ITS OWN

1. Clone an existing item's EFFECT, then change its name/description/icon/pocket.
   This is the safe, reliable path. An item's in-battle/bag-use behavior is driven
   entirely by its ITEM_DATA fields - it does not know or care what it's named.
   Pick a donor item whose real effect is what you want, clone it, then only change
   the cosmetic fields (Name, Description, Icon, Pocket).

2. The single field that decides everything: ItemKind (offset 0xC).
   Only ItemKind = 1 reaches the normal 28-entry ""use from Bag"" healing/status
   effect table (Potion, Full Heal, berries, etc.). Any other value either does
   nothing when used, or - if cloned from an item that drives a totally different
   state machine (Ability Capsule, Rare Candy, a key item) - can misbehave or crash
   depending on what that donor's real code path expects to find.
   -> If you want a ""use it and something happens"" item, clone from a real
      ItemKind = 1 donor (a Vitamin or a Heal item is a safe template) and keep
      the HealParam/HealTarget-style fields, don't touch ItemKind itself.

3. Pocket placement is a SEPARATE table from the item data you're editing here.
   Cloning inherits the donor's pocket by default; if the item lands in the wrong
   Bag pocket, it's this separate table, not ItemKind, that needs adjusting.

4. Icons: a new item id needs a real entry in the icon table or it shows blank/
   ""???"". Reuse an existing item's icon rather than trying to add a brand new
   sprite slot - see the Icon Editor's ""Copy From"" for the same authoritative
   member-resolution machinery.

5. Item ids: capped at 1023 (a 10-bit packed field in the engine). An id past that
   silently aliases onto a real low item - if a custom item ""becomes"" an unrelated
   item on the cartridge, this is almost always why.

WHAT NEEDS THE RESEARCH CENTER INSTEAD

- Anything that opens a UI or triggers a whole separate game feature from an item
  (a portable Move Relearner, a portable PC, anything beyond a single stat/status
  effect) needs a real ARM hook chained off Bag.cro's dispatcher, not just a
  data field - there's no such thing as an ItemKind value for ""open a menu.""
- A brand new effect no existing item's ItemKind already expresses.
- Anything that has to coordinate with Battle.cro (a held-item effect that changes
  damage calculation, checks a battle state, etc.).

The Research Center is where those get built as real code, verified against the
ROM's own bytes rather than assumed from a label - the same way every dispatch
mechanism described above was originally found.";
}
