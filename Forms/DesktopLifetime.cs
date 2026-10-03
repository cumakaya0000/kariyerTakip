namespace KariyerTakip.Forms;

internal sealed class DesktopLifetime : IDisposable
{
    private readonly Form _form;
    private readonly NotifyIcon _icon;
    private readonly Func<bool> _minimizeToTray;
    public DesktopLifetime(Form form, Func<bool> minimizeToTray)
    {
        _form = form; _minimizeToTray = minimizeToTray;
        _icon = new NotifyIcon { Icon = KariyerTakip.Common.AppBrand.CreateIcon(), Text = "KariyerTakip" };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Aç", null, (_, _) => Restore());
        menu.Items.Add("Çıkış", null, (_, _) => _form.Close());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => Restore();
        _form.Resize += OnResize;
    }
    private void OnResize(object? sender, EventArgs e)
    {
        if (_minimizeToTray() && _form.WindowState == FormWindowState.Minimized)
        { _icon.Visible = true; _form.Hide(); }
    }
    private void Restore()
    { _form.Show(); _form.WindowState = FormWindowState.Normal; _form.Activate(); _icon.Visible = false; }
    public void Dispose()
    {
        _form.Resize -= OnResize;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}
