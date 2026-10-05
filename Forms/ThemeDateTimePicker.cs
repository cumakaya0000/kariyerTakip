namespace KariyerTakip.Forms;

// Native date pickers ignore BackColor. Keep their calendar/input behavior and paint the field.
internal sealed class ThemeDateTimePicker : DateTimePicker
{
    private Color _surface = SystemColors.Window;
    private Color _text = SystemColors.WindowText;
    private Color _border = SystemColors.ControlDark;

    public ThemeDateTimePicker() => SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

    public void SetPalette(Color surface, Color text, Color border)
    {
        _surface = surface; _text = text; _border = border;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(_surface);
        e.Graphics.FillRectangle(background, ClientRectangle);
        using var border = new Pen(_border);
        e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        var checkWidth = ShowCheckBox ? 22 : 4;
        if (ShowCheckBox)
            ControlPaint.DrawCheckBox(e.Graphics, new Rectangle(4, (Height - 13) / 2, 13, 13),
                (Checked ? ButtonState.Checked : ButtonState.Normal) | (Enabled ? ButtonState.Normal : ButtonState.Inactive));
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(checkWidth, 1, Math.Max(0, Width - checkWidth - 22), Height - 2),
            Enabled ? _text : SystemColors.GrayText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var arrow = new SolidBrush(Enabled ? _text : SystemColors.GrayText);
        var center = new Point(Width - 12, Height / 2);
        e.Graphics.FillPolygon(arrow, new[] { new Point(center.X - 4, center.Y - 2), new Point(center.X + 4, center.Y - 2), new Point(center.X, center.Y + 3) });
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(checkWidth, 3, Math.Max(0, Width - checkWidth - 24), Math.Max(0, Height - 6)), _text, _surface);
    }

    protected override void OnValueChanged(EventArgs e) { base.OnValueChanged(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Invalidate(); }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); Invalidate(); }
}
