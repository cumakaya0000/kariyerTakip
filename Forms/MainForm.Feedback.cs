using KariyerTakip.Services;

namespace KariyerTakip.Forms;

public partial class MainForm
{
    private async Task SaveEvaluationFeedbackAsync()
    {
        var item = _selectedItem;
        if (item == null) { MessageBox.Show("Önce bir ilan seçin."); return; }
        using var dialog = new Form { Text = "Değerlendirme geri bildirimi", Size = new Size(540, 340), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var expected = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        expected.Items.AddRange(new object[] { "Uygun", "Kontrol gerekli", "Uygun değil" }); expected.SelectedIndex = 1;
        var note = new TextBox { Multiline = true, Dock = DockStyle.Fill, PlaceholderText = "Hangi koşul yanlış? Kişisel bilgi yazmayın." };
        var save = new Button { Text = "Yerel örneği kaydet", AutoSize = true, DialogResult = DialogResult.OK };
        layout.Controls.Add(new Label { Text = "Beklediğiniz sonucu seçin ve yanlış koşulu açıklayın.", AutoSize = true }, 0, 0);
        layout.Controls.Add(expected, 0, 1); layout.Controls.Add(note, 0, 2); layout.Controls.Add(save, 0, 3);
        dialog.Controls.Add(layout);
        ApplyThemeToChildren(dialog, BackColor, _rtbDetailContent.BackColor, _rtbDetailContent.ForeColor, _rtbDetailContent.ForeColor);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var positions = await _repository.GetPositionsByAnnouncementGuidAsync(item.Record.Guid);
            var expectedStatus = new[] { "Eligible", "NeedsReview", "Ineligible" }[expected.SelectedIndex];
            var path = await new FeedbackStore().SaveAsync(item.Record, positions, item.Status.ToString(), expectedStatus, note.Text);
            MessageBox.Show("İlan metni ve beklenen sonuç yerel dosyaya kaydedildi:\n" + path, "Geri bildirim kaydedildi");
        }
        catch (Exception ex) { MessageBox.Show("Örnek kaydedilemedi: " + ex.Message); }
    }
}
