using System.Drawing;
using Microsoft.Extensions.Logging;
using KariyerTakip.Models;

namespace KariyerTakip.Forms;

public partial class MainForm
{
    private readonly HashSet<Button> _themeButtons = new();

    private void RegisterButtonTheme(Button button)
    {
        if (!_themeButtons.Add(button)) return;
        button.Paint += (_, e) =>
        {
            if (!_isDarkMode || button.Enabled) return;
            using var background = new SolidBrush(button.BackColor);
            e.Graphics.FillRectangle(background, button.ClientRectangle);
            using var border = new Pen(Color.FromArgb(85, 95, 110));
            e.Graphics.DrawRectangle(border, 0, 0, button.Width - 1, button.Height - 1);
            TextRenderer.DrawText(e.Graphics, button.Text, button.Font, button.ClientRectangle,
                Color.FromArgb(180, 190, 205), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
    }
    private void ToggleTheme()
    {
        _isDarkMode = !_isDarkMode;
        _btnThemeToggle.Text = _isDarkMode ? "Açık" : "Koyu";
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        // ── Color Palettes ──
        Color formBg, panelBg, cardBg, textPrimary, textSecondary, gridBg, gridAltRow, gridHeaderBg, gridHeaderFg, gridLineBorder;
        Color headerBg, headerSubFg;
        Color detailBg;
        Color logBg, logFg;
        Color tabBg;

        if (_isDarkMode)
        {
            formBg         = Color.FromArgb(30, 30, 30);
            panelBg        = Color.FromArgb(40, 40, 40);
            cardBg         = Color.FromArgb(45, 45, 48);
            textPrimary    = Color.FromArgb(230, 230, 230);
            textSecondary  = Color.FromArgb(170, 170, 170);
            gridBg         = Color.FromArgb(38, 38, 42);
            gridAltRow     = Color.FromArgb(48, 48, 55);
            gridHeaderBg   = Color.FromArgb(55, 55, 60);
            gridHeaderFg   = Color.FromArgb(220, 220, 220);
            gridLineBorder = Color.FromArgb(65, 65, 70);
            headerBg       = Color.FromArgb(18, 18, 22);
            headerSubFg    = Color.FromArgb(140, 160, 180);
            detailBg       = Color.FromArgb(38, 40, 45);
            logBg          = Color.FromArgb(18, 20, 25);
            logFg          = Color.FromArgb(200, 210, 220);
            tabBg          = Color.FromArgb(40, 40, 40);
        }
        else
        {
            formBg         = Color.FromArgb(244, 246, 249);
            panelBg        = Color.White;
            cardBg         = Color.White;
            textPrimary    = Color.FromArgb(30, 30, 30);
            textSecondary  = Color.FromArgb(100, 110, 120);
            gridBg         = Color.White;
            gridAltRow     = Color.FromArgb(248, 250, 252);
            gridHeaderBg   = Color.FromArgb(240, 242, 245);
            gridHeaderFg   = Color.FromArgb(30, 30, 30);
            gridLineBorder = Color.FromArgb(220, 225, 230);
            headerBg       = Color.FromArgb(15, 37, 65);
            headerSubFg    = Color.FromArgb(180, 200, 220);
            detailBg       = Color.FromArgb(250, 252, 255);
            logBg          = Color.FromArgb(20, 24, 30);
            logFg          = Color.FromArgb(220, 230, 240);
            tabBg          = Color.White;
        }

        // ── Form ──
        BackColor = formBg;

        // ── Header ──
        var topPanel = Controls.OfType<Panel>().FirstOrDefault(p => p.Dock == DockStyle.Top);
        if (topPanel != null)
        {
            topPanel.BackColor = headerBg;
            void ThemeHeader(Control parent)
            {
                foreach (Control c in parent.Controls)
                {
                    if (c is Label lbl && lbl != _lblStatus)
                        lbl.ForeColor = lbl == _lblLastScanTime ? headerSubFg : Color.White;
                    else if (c is Panel)
                    {
                        c.BackColor = headerBg;
                        ThemeHeader(c);
                    }
                }
            }
            ThemeHeader(topPanel);
            foreach (var button in new[] { _btnThemeToggle, _btnCancelScan, _btnScanNow }) RegisterButtonTheme(button);
            _btnThemeToggle.BackColor = _isDarkMode ? Color.FromArgb(50, 55, 60) : Color.FromArgb(35, 60, 90);
        }

        // ── Grid ──
        _gridAnnouncements.BackgroundColor = gridBg;
        _gridAnnouncements.DefaultCellStyle.BackColor = gridBg;
        _gridAnnouncements.DefaultCellStyle.ForeColor = textPrimary;
        _gridAnnouncements.DefaultCellStyle.SelectionBackColor = _isDarkMode ? Color.FromArgb(60, 80, 120) : Color.FromArgb(200, 220, 250);
        _gridAnnouncements.DefaultCellStyle.SelectionForeColor = textPrimary;
        _gridAnnouncements.AlternatingRowsDefaultCellStyle.BackColor = gridAltRow;
        _gridAnnouncements.ColumnHeadersDefaultCellStyle.BackColor = gridHeaderBg;
        _gridAnnouncements.ColumnHeadersDefaultCellStyle.ForeColor = gridHeaderFg;
        _gridAnnouncements.GridColor = gridLineBorder;
        _gridAnnouncements.EnableHeadersVisualStyles = false;

        foreach (DataGridViewRow row in _gridAnnouncements.Rows)
        {
            var it = row.Tag as AnnouncementDisplayItem;
            if (it == null) continue;
            if (it.Status == EligibilityStatus.Eligible)
                row.DefaultCellStyle.BackColor = _isDarkMode ? Color.FromArgb(30, 60, 30) : Color.FromArgb(235, 255, 235);
            else if (it.Status == EligibilityStatus.NeedsReview)
                row.DefaultCellStyle.BackColor = _isDarkMode ? Color.FromArgb(60, 55, 25) : Color.FromArgb(255, 250, 230);
            else
                row.DefaultCellStyle.BackColor = gridBg;
        }

        // ── Detail Panel ──
        _lblDetailInstitution.ForeColor = textPrimary;
        _lblDetailTitle.ForeColor = textSecondary;
        _lblDetailDates.ForeColor = textSecondary;
        _rtbDetailContent.BackColor = cardBg;
        _rtbDetailContent.ForeColor = textPrimary;

        // ── Tabs & Tab Pages ──
        if (_tabControl is ThemeTabControl mainTabs) mainTabs.SetPalette(formBg, cardBg, textPrimary);
        foreach (TabPage tp in _tabControl.TabPages)
        {
            tp.BackColor = tabBg;
            ApplyThemeToChildren(tp, panelBg, cardBg, textPrimary, textSecondary);
        }
        _lblDetailTitle.ForeColor = textSecondary;
        _lblDetailDates.ForeColor = textSecondary;
        _columnMenu.BackColor = cardBg;
        _columnMenu.ForeColor = textPrimary;
        _columnMenu.Renderer = new ToolStripProfessionalRenderer(new MenuPalette(cardBg, gridLineBorder,
            _gridAnnouncements.DefaultCellStyle.SelectionBackColor));
        foreach (ToolStripItem item in _columnMenu.Items) item.ForeColor = textPrimary;
        foreach (DataGridViewRow row in _gridAnnouncements.Rows)
            if (row.Tag is AnnouncementDisplayItem item && item.Record.EndDate is { } deadline &&
                deadline >= DateTime.UtcNow && deadline <= DateTime.UtcNow.AddDays(3))
            {
                row.Cells["RemainingTime"].Style.ForeColor = _isDarkMode ? Color.Orange : Color.DarkOrange;
                row.Cells["RemainingTime"].Style.SelectionForeColor = _isDarkMode ? Color.Orange : Color.FromArgb(135, 70, 0);
            }

        // ── Log ──
        _rtbLog.BackColor = logBg;
        _rtbLog.ForeColor = logFg;

        // ── Detail area parent panels ──
        var detailParent = _rtbDetailContent.Parent;
        while (detailParent != null && detailParent != _tabControl)
        {
            if (detailParent is Panel dp)
                dp.BackColor = detailBg;
            detailParent = detailParent.Parent;
        }

        if (_selectedItem != null) PopulateDetailPanel(_selectedItem);
        Invalidate(true);
    }

    private void ApplyThemeToChildren(Control parent, Color panelBg, Color cardBg, Color textPrimary, Color textSecondary)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case TabPage page:
                    page.BackColor = panelBg;
                    ApplyThemeToChildren(page, panelBg, cardBg, textPrimary, textSecondary);
                    break;
                case ThemeTabControl tabs:
                    tabs.SetPalette(panelBg, cardBg, textPrimary);
                    ApplyThemeToChildren(tabs, panelBg, cardBg, textPrimary, textSecondary);
                    break;
                case DataGridView grid:
                    grid.BackgroundColor = cardBg;
                    grid.DefaultCellStyle.BackColor = cardBg;
                    grid.DefaultCellStyle.ForeColor = textPrimary;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = panelBg;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = textPrimary;
                    grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = panelBg;
                    grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = textPrimary;
                    grid.EnableHeadersVisualStyles = false;
                    grid.AlternatingRowsDefaultCellStyle.BackColor = _isDarkMode ? Color.FromArgb(48, 48, 55) : Color.FromArgb(248, 250, 252);
                    grid.DefaultCellStyle.SelectionBackColor = _isDarkMode ? Color.FromArgb(60, 80, 120) : Color.FromArgb(200, 220, 250);
                    grid.DefaultCellStyle.SelectionForeColor = textPrimary;
                    grid.RowHeadersDefaultCellStyle.BackColor = panelBg;
                    grid.RowHeadersDefaultCellStyle.ForeColor = textPrimary;
                    grid.RowHeadersDefaultCellStyle.SelectionBackColor = grid.DefaultCellStyle.SelectionBackColor;
                    grid.RowHeadersDefaultCellStyle.SelectionForeColor = textPrimary;
                    grid.GridColor = _isDarkMode ? Color.FromArgb(65, 65, 70) : Color.FromArgb(220, 225, 230);
                    foreach (DataGridViewColumn column in grid.Columns)
                        if (column is DataGridViewComboBoxColumn choice) choice.FlatStyle = FlatStyle.Flat;
                    break;
                case ListBox list:
                    list.BackColor = cardBg; list.ForeColor = textPrimary;
                    break;
                case Panel p:
                    p.BackColor = panelBg;
                    ApplyThemeToChildren(p, panelBg, cardBg, textPrimary, textSecondary);
                    break;
                case UserControl control:
                    control.BackColor = panelBg;
                    control.ForeColor = textPrimary;
                    ApplyThemeToChildren(control, panelBg, cardBg, textPrimary, textSecondary);
                    break;
                case SplitContainer sc:
                    sc.BackColor = _isDarkMode ? Color.FromArgb(50, 50, 55) : Color.FromArgb(230, 235, 240);
                    sc.Panel1.BackColor = panelBg;
                    sc.Panel2.BackColor = panelBg;
                    ApplyThemeToChildren(sc.Panel1, panelBg, cardBg, textPrimary, textSecondary);
                    ApplyThemeToChildren(sc.Panel2, panelBg, cardBg, textPrimary, textSecondary);
                    break;
                case Label lbl when lbl != _lblDetailStatusBadge && lbl != _lblStatus:
                    lbl.ForeColor = textPrimary;
                    break;
                case TextBox txt:
                    txt.BackColor = cardBg;
                    txt.ForeColor = textPrimary;
                    break;
                case ComboBox cmb:
                    cmb.BackColor = cardBg;
                    cmb.ForeColor = textPrimary;
                    break;
                case NumericUpDown nud:
                    nud.BackColor = cardBg;
                    nud.ForeColor = textPrimary;
                    break;
                case CheckBox chk:
                    chk.ForeColor = textPrimary;
                    chk.BackColor = panelBg;
                    break;
                case Button button:
                    RegisterButtonTheme(button);
                    // Keep the colored portal actions; style ordinary editor buttons for both themes.
                    if (button != _btnOpenKariyerKapisi && button != _btnOpenKamuIlanSite && button != _btnOpenEDevlet && button != _btnSendTelegramNow && button != _btnOpenSelectedAnnouncements)
                    {
                        button.UseVisualStyleBackColor = false;
                        button.FlatStyle = FlatStyle.Flat;
                        button.BackColor = _isDarkMode ? Color.FromArgb(55, 65, 80) : Color.FromArgb(235, 241, 248);
                        button.ForeColor = textPrimary;
                        button.FlatAppearance.BorderColor = _isDarkMode ? Color.FromArgb(85, 95, 110) : Color.FromArgb(185, 200, 215);
                    }
                    break;
                case DateTimePicker dtp:
                    if (dtp is ThemeDateTimePicker themedDate) themedDate.SetPalette(cardBg, textPrimary,
                        _isDarkMode ? Color.FromArgb(85, 95, 110) : Color.FromArgb(185, 200, 215));
                    dtp.CalendarMonthBackground = cardBg;
                    dtp.CalendarForeColor = textPrimary;
                    dtp.CalendarTitleBackColor = panelBg;
                    dtp.CalendarTitleForeColor = textPrimary;
                    break;
                case RichTextBox rtb when rtb != _rtbLog:
                    rtb.BackColor = cardBg;
                    rtb.ForeColor = textPrimary;
                    break;
                default:
                    if (c.HasChildren)
                        ApplyThemeToChildren(c, panelBg, cardBg, textPrimary, textSecondary);
                    break;
            }
        }
    }

}

internal sealed class MenuPalette(Color background, Color border, Color selection) : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => background;
    public override Color ImageMarginGradientBegin => background;
    public override Color ImageMarginGradientMiddle => background;
    public override Color ImageMarginGradientEnd => background;
    public override Color MenuBorder => border;
    public override Color MenuItemBorder => border;
    public override Color MenuItemSelected => selection;
    public override Color CheckBackground => selection;
    public override Color CheckSelectedBackground => selection;
    public override Color CheckPressedBackground => selection;
    public override Color SeparatorDark => border;
    public override Color SeparatorLight => background;
}
