using System.Text;
using KariyerTakip.Models;
using KariyerTakip.Services;

namespace KariyerTakip.Forms;

public partial class MainForm
{
    private Label _lblListSummary = null!;
    private Button _btnExportAnnouncements = null!;

    private void SelectVisibleAnnouncements(bool selected)
    {
        _isUpdatingAnnouncementRows = true;
        try
        {
            foreach (DataGridViewRow row in _gridAnnouncements.Rows)
            {
                if (row.Tag is not AnnouncementDisplayItem item) continue;
                row.Cells["Checked"].Value = selected;
                if (selected) _checkedAnnouncementGuids.Add(item.Record.Guid);
                else _checkedAnnouncementGuids.Remove(item.Record.Guid);
            }
        }
        finally { _isUpdatingAnnouncementRows = false; UpdateCheckedAnnouncements(); }
    }

    private void SetupColumnMenu()
    {
        var menu = new ContextMenuStrip();
        foreach (DataGridViewColumn column in _gridAnnouncements.Columns)
        {
            if (column.Name is "Checked" or "Title") continue;
            var choice = new ToolStripMenuItem(column.HeaderText) { CheckOnClick = true, Checked = true };
            choice.CheckedChanged += (_, _) => column.Visible = choice.Checked;
            menu.Items.Add(choice);
        }
        _gridAnnouncements.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position);
        };
        _gridAnnouncements.Disposed += (_, _) => menu.Dispose();
    }

    private async Task ExportCheckedAnnouncementsAsync()
    {
        var items = _cachedItems.Where(x => x.Record.Source == _selectedSource && _checkedAnnouncementGuids.Contains(x.Record.Guid)).ToList();
        if (items.Count == 0) return;
        using var dialog = new SaveFileDialog
        {
            Title = "Seçili ilanları dışa aktar", Filter = "CSV dosyası (*.csv)|*.csv", DefaultExt = "csv", AddExtension = true,
            FileName = $"{(_selectedSource == AnnouncementSource.KamuIlan ? "KamuIlan" : "KariyerKapisi")}-{DateTime.Today:yyyy-MM-dd}.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, AnnouncementCsvExporter.Build(items), new UTF8Encoding(true));
            _lblStatus.Text = $"{items.Count} ilan CSV dosyasına aktarıldı.";
        }
        catch (Exception ex) { MessageBox.Show("İlanlar dışa aktarılamadı: " + ex.Message, "Dışa Aktarma", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
