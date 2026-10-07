using KariyerTakip.Common;
using KariyerTakip.Services;

namespace KariyerTakip.Forms;

public partial class MainForm
{
    private void ShowDataMaintenance()
    {
        if (_scanPresenter.IsRunning) { MessageBox.Show("Veri bakımı için taramanın bitmesini bekleyin."); return; }
        using var dialog = new Form { Text = "Veri bakımı", Width = 440, Height = 240, StartPosition = FormStartPosition.CenterParent };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(15) };
        var export = new Button { Text = "Tüm ilanları JSON'a aktar", AutoSize = true };
        var import = new Button { Text = "JSON arşivini içe aktar", AutoSize = true };
        var backup = new Button { Text = "SQLite veritabanı yedeği al", AutoSize = true };
        layout.Controls.AddRange(new Control[] { export, import, backup,
            new Label { AutoSize = true, MaximumSize = new Size(380, 0), Text = "İçe aktarma mevcut ilanlarla birleştirir. Önce otomatik SQLite yedeği alınır. Değerlendirme geçmişi varsayılan olarak kadro başına son 20 kayıtla sınırlıdır (ayar dosyasından değiştirilebilir)." } });
        async Task Run(Func<Task> action)
        {
            layout.Enabled = false;
            try { await _scanCoordinator.RunMaintenanceAsync(action); }
            catch (Exception ex) { MessageBox.Show(dialog, "Veri işlemi tamamlanamadı: " + ex.Message); }
            finally { layout.Enabled = true; }
        }
        export.Click += async (_, _) =>
        {
            using var save = new SaveFileDialog { Filter = "JSON arşivi (*.json)|*.json", FileName = "KariyerTakip.json" };
            if (save.ShowDialog(dialog) != DialogResult.OK) return;
            await Run(async () => { await AtomicFile.WriteAsync(save.FileName, await AnnouncementArchive.ExportAsync(_repository)); MessageBox.Show(dialog, "Arşiv kaydedildi."); });
        };
        backup.Click += async (_, _) =>
        {
            using var save = new SaveFileDialog { Filter = "SQLite veritabanı (*.db)|*.db", FileName = "KariyerTakip-yedek.db" };
            if (save.ShowDialog(dialog) != DialogResult.OK) return;
            await Run(async () => { await _repository.BackupDatabaseAsync(save.FileName); MessageBox.Show(dialog, "Yedek kaydedildi."); });
        };
        import.Click += async (_, _) =>
        {
            using var open = new OpenFileDialog { Filter = "JSON arşivi (*.json)|*.json" };
            if (open.ShowDialog(dialog) != DialogResult.OK) return;
            await Run(async () =>
            {
                var archive = AnnouncementArchive.Parse(await File.ReadAllTextAsync(open.FileName));
                var backupPath = Path.Combine(AppPaths.BaseDirectory, $"import-oncesi-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
                await _repository.BackupDatabaseAsync(backupPath);
                try { await archive.MergeAsync(_repository); }
                catch (Exception ex) { throw new InvalidOperationException($"İçe aktarma tamamlanamadı. Önceki veritabanı yedeği: {backupPath}", ex); }
                await LoadAnnouncementsFromDbAsync();
                MessageBox.Show(dialog, $"{archive.Announcements.Count} ilan içe aktarıldı.\nÖnceki veritabanı yedeği: {backupPath}");
            });
        };
        dialog.Controls.Add(layout); dialog.ShowDialog(this);
    }
}
