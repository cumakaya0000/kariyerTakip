using System.Drawing;

namespace KariyerTakip.Forms;

internal sealed class ThemeTabControl : TabControl
{
    private Color _strip = SystemColors.Control;
    private Color _selected = SystemColors.Window;
    private Color _text = SystemColors.ControlText;

    public ThemeTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        DrawItem += (_, e) =>
        {
            using var brush = new SolidBrush(e.Index == SelectedIndex ? _selected : _strip);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, e.Bounds, _text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if ((e.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, _text, _selected);
        };
    }

    public void SetPalette(Color strip, Color selected, Color text)
    {
        _strip = strip; _selected = selected; _text = text;
        BackColor = strip; ForeColor = text;
        Invalidate();
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        // Native tabs otherwise leave a light strip and frame around owner-drawn headers.
        if (message.Msg != 0x000F && message.Msg != 0x0318) return; // WM_PAINT / WM_PRINTCLIENT
        if (!IsHandleCreated || ClientSize.Width == 0 || ClientSize.Height == 0) return;
        using var graphics = message.Msg == 0x0318 && message.WParam != IntPtr.Zero
            ? Graphics.FromHdc(message.WParam) : Graphics.FromHwnd(Handle);
        using var background = new Region(ClientRectangle);
        background.Exclude(DisplayRectangle);
        for (var index = 0; index < TabCount; index++) background.Exclude(GetTabRect(index));
        using var brush = new SolidBrush(_strip);
        graphics.FillRegion(brush, background);
    }
}
