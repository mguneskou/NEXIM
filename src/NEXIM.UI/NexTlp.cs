// NEXIM — Shared TableLayoutPanel helper for settings panels.
// NexTlp wraps a two-column TableLayoutPanel (label | control) that docks
// to the top of its parent and auto-sizes vertically.  Controls in the
// right column are anchored Left+Right so they stretch when the form resizes.
//
// Usage:
//   var tbl = new NexTlp(labelColumnWidth: 170);
//   tbl.AddHeader("Section Name");
//   var nud = tbl.AddNud("Label", min, max, value, decimals);
//   var cbo = tbl.AddCombo("Label", items, selectedIndex);
//   tbl.AddGap();
//   parent.Controls.Add(tbl);

namespace NEXIM.UI;

/// <summary>
/// Convenience wrapper around a 2-column <see cref="TableLayoutPanel"/>
/// for NEXIM settings panels.  All row heights are auto-sized.
/// </summary>
internal sealed class NexTlp : TableLayoutPanel
{
    private static readonly Font _headerFont =
        new Font("Segoe UI", 9f, FontStyle.Bold);
    private static readonly Font _normalFont =
        new Font("Segoe UI", 9f, FontStyle.Regular);
    private readonly ToolTip _tip;

    private const int RowHeight = 28;

    public NexTlp(int labelColumnWidth = 170)
    {
        // Override any bold font inherited from a GroupBox parent
        Font          = _normalFont;
        Dock          = DockStyle.Top;
        AutoSize      = true;
        AutoSizeMode  = AutoSizeMode.GrowAndShrink;
        ColumnCount   = 2;
        Padding       = new Padding(8, 8, 8, 4);
        GrowStyle     = TableLayoutPanelGrowStyle.AddRows;

        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelColumnWidth));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _tip = new ToolTip { AutoPopDelay = 8000, InitialDelay = 600 };
    }

    // ── Row factories ─────────────────────────────────────────────────────

    /// <summary>Bold section header spanning both columns.</summary>
    public Label AddHeader(string text)
    {
        var lbl = new Label
        {
            Text      = text,
            Font      = _headerFont,
            AutoSize  = true,
            Margin    = new Padding(2, 8, 2, 2),
            ForeColor = Color.FromArgb(0, 60, 120),
        };
        Controls.Add(lbl);
        SetColumnSpan(lbl, 2);
        return lbl;
    }

    /// <summary>Label + NumericUpDown row.</summary>
    public NumericUpDown AddNud(string labelText,
                                decimal min, decimal max, decimal value,
                                int decimalPlaces = 0)
    {
        Controls.Add(MakeLabel(labelText));
        var nud = new NumericUpDown
        {
            Minimum       = min,
            Maximum       = max,
            Value         = Math.Clamp(value, min, max),
            DecimalPlaces = decimalPlaces,
            Increment     = decimalPlaces > 0 ? (decimal)Math.Pow(10, -decimalPlaces) : 1m,
            Anchor        = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Margin        = new Padding(2, 3, 8, 3),
        };
        Controls.Add(nud);
        return nud;
    }

    /// <summary>Label + ComboBox row.</summary>
    public ComboBox AddCombo(string labelText, string[] items, int selectedIndex = 0)
    {
        Controls.Add(MakeLabel(labelText));
        var cbo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor        = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Margin        = new Padding(2, 3, 8, 3),
        };
        foreach (var item in items) cbo.Items.Add(item);
        if (selectedIndex >= 0 && selectedIndex < cbo.Items.Count)
            cbo.SelectedIndex = selectedIndex;
        Controls.Add(cbo);
        return cbo;
    }

    /// <summary>Label + TextBox row (optionally read-only).</summary>
    public TextBox AddTextBox(string labelText, string text = "", bool readOnly = false)
    {
        Controls.Add(MakeLabel(labelText));
        var tb = new TextBox
        {
            Text      = text,
            ReadOnly  = readOnly,
            Anchor    = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Margin    = new Padding(2, 3, 8, 3),
        };
        Controls.Add(tb);
        return tb;
    }

    /// <summary>Label + CheckBox row.</summary>
    public CheckBox AddCheck(string labelText, string checkText, bool isChecked = false)
    {
        Controls.Add(MakeLabel(labelText));
        var chk = new CheckBox
        {
            Text    = checkText,
            Checked = isChecked,
            Anchor  = AnchorStyles.Left | AnchorStyles.Top,
            Margin  = new Padding(2, 4, 2, 4),
        };
        Controls.Add(chk);
        return chk;
    }

    /// <summary>
    /// Add a control that spans both columns (used for RadioButtons, info labels, etc.).
    /// </summary>
    public void AddWide(Control ctl)
    {
        ctl.Margin = new Padding(4, 3, 8, 3);
        Controls.Add(ctl);
        SetColumnSpan(ctl, 2);
    }

    /// <summary>
    /// Add a label (left col) and an arbitrary control (right col).
    /// The control is anchored Left+Right.
    /// </summary>
    public void AddRow(string labelText, Control ctl)
    {
        Controls.Add(MakeLabel(labelText));
        ctl.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        ctl.Margin = new Padding(2, 3, 8, 3);
        Controls.Add(ctl);
    }

    /// <summary>A small vertical gap row (empty label + empty label).</summary>
    public void AddGap(int height = 4)
    {
        var spacer = new Label { Height = height, AutoSize = false, Margin = Padding.Empty };
        Controls.Add(spacer);
        SetColumnSpan(spacer, 2);
    }

    /// <summary>A horizontal separator line spanning both columns.</summary>
    public void AddSeparator()
    {
        var sep = new Label
        {
            AutoSize    = false,
            Height      = 1,
            BackColor   = Color.FromArgb(180, 180, 180),
            Margin      = new Padding(4, 4, 8, 4),
            Anchor      = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        };
        Controls.Add(sep);
        SetColumnSpan(sep, 2);
    }

    /// <summary>Gray info/tip label spanning both columns.</summary>
    public Label AddInfoLabel(string text)
    {
        var lbl = new Label
        {
            Text      = text,
            ForeColor = SystemColors.GrayText,
            AutoSize  = true,
            Margin    = new Padding(4, 2, 4, 8),
        };
        Controls.Add(lbl);
        SetColumnSpan(lbl, 2);
        return lbl;
    }

    // ── Private ───────────────────────────────────────────────────────────

    private Label MakeLabel(string text)
    {
        var lbl = new Label
        {
            Text         = text,
            Font         = _normalFont,
            AutoSize     = false,
            AutoEllipsis = true,
            TextAlign    = ContentAlignment.MiddleLeft,
            Anchor       = AnchorStyles.Left | AnchorStyles.Top,
            Margin       = new Padding(4, 4, 4, 4),
            Height       = RowHeight,
        };
        _tip.SetToolTip(lbl, text);   // show full text on hover when truncated
        return lbl;
    }
}
