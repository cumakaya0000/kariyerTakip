using System.Text.Json;
using KariyerTakip.Services;

namespace KariyerTakip.Forms;

public partial class MainForm
{
    private void ShowContentDiff()
    {
        var json = _selectedItem?.Record.LastContentDiffJson;
        if (string.IsNullOrEmpty(json)) { MessageBox.Show("Bu ilan için kaydedilmiş bir içerik değişikliği yok."); return; }
        try
        {
            var differences = JsonSerializer.Deserialize<List<ContentDifference>>(json) ?? new();
            using var dialog = new Form { Text = "İlan şartları — önce / şimdi", Width = 1100, Height = 650, StartPosition = FormStartPosition.CenterParent };
            var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, RowHeadersVisible = false };
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.Columns.Add("Scope", "Şart / kadro"); grid.Columns.Add("Before", "Önce"); grid.Columns.Add("After", "Şimdi");
            grid.Columns[0].FillWeight = 30;
            foreach (var difference in differences) grid.Rows.Add(difference.Scope, difference.OldValue, difference.NewValue);
            dialog.Controls.Add(grid); dialog.ShowDialog(this);
        }
        catch (JsonException ex) { MessageBox.Show("Değişiklik kaydı okunamadı: " + ex.Message); }
    }
}
