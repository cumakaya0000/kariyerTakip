using KariyerTakip.Models;
using KariyerTakip.Storage;
using KariyerTakip.Services;
using KariyerTakip.Common;

namespace KariyerTakip.Forms;

public sealed class ApplicationTrackingControl : UserControl
{
    private readonly ComboBox _status = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly TextBox _notes = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Button _save = new() { Text = "Başvuru durumunu ve notları kaydet", Dock = DockStyle.Bottom, Height = 34 };
    private AnnouncementRecord? _record;
    private readonly Label _countdown = new() { Dock = DockStyle.Top, Height = 24 };
    private readonly System.Windows.Forms.Timer _countdownTimer = new() { Interval = 60000 };
    public event Action? TrackingSaved;
    public ApplicationTrackingControl(IAnnouncementRepository repository)
    {
        Dock = DockStyle.Fill; Padding = new Padding(10);
        _status.Items.AddRange(new object[] { "Takip edilmiyor", "Başvuracağım", "Başvurdum", "Geçtim" });
        var header = new Panel { Dock = DockStyle.Top, Height = 110 };
        header.Controls.Add(new Label { Text = "Başvuru durumu", Dock = DockStyle.Top, Height = 24 });
        _status.Location = new Point(0, 26); _status.Dock = DockStyle.None; _status.Width = 280;
        header.Controls.Add(_status);
        header.Controls.Add(new Label { Text = "Kişisel notlar (bu bilgisayarda saklanır)", Location = new Point(0, 56), AutoSize = true });
        _countdown.Dock = DockStyle.None; _countdown.Location = new Point(0, 80); _countdown.Width = 400; header.Controls.Add(_countdown);
        var calendar = new Button { Text = "Başvuracağım ilanları takvime aktar (.ics)", Dock = DockStyle.Bottom, Height = 34 };
        Controls.Add(calendar);
        calendar.Click += async (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "Takvim (*.ics)|*.ics", FileName = "Basvurular.ics" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { await File.WriteAllTextAsync(dialog.FileName, ApplicationCalendarExporter.Build(await repository.GetAllAnnouncementsAsync(true))); }
            catch (Exception ex) { MessageBox.Show("Takvim oluşturulamadı: " + ex.Message); }
        };
        Controls.Add(_notes); Controls.Add(header); Controls.Add(_save);
        Bind(null);
        _countdownTimer.Tick += (_, _) => UpdateCountdown();
        _countdownTimer.Start();
        _save.Click += async (s, e) =>
        {
            var record = _record; if (record == null) return;
            _save.Enabled = false;
            try
            {
                var status = (ApplicationStatus)_status.SelectedIndex;
                var notes = _notes.Text;
                await repository.SaveApplicationTrackingAsync(record.Guid, status, notes);
                record.ApplicationStatus = status; record.ApplicationNotes = notes;
                TrackingSaved?.Invoke();
            }
            catch (Exception ex) { MessageBox.Show("Başvuru bilgisi kaydedilemedi: " + ex.Message); }
            finally { _save.Enabled = _record != null; }
        };
    }
    public void Bind(AnnouncementRecord? record)
    {
        _record = record; Enabled = record != null;
        _status.SelectedIndex = (int)(record?.ApplicationStatus ?? ApplicationStatus.None);
        _notes.Text = record?.ApplicationNotes ?? "";
        UpdateCountdown();
    }
    private void UpdateCountdown()
    {
        _countdown.Text = _record?.EndDate is DateTime end ?
            $"Son başvuru: {AppTime.Format(end)} — {(end > DateTime.UtcNow ? $"{Math.Ceiling((end - DateTime.UtcNow).TotalDays)} gün kaldı" : "Süre doldu")}" : "Son başvuru tarihi belirtilmemiş";
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _countdownTimer.Dispose();
        base.Dispose(disposing);
    }
    public static string DisplayStatus(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Planning => "Başvuracağım", ApplicationStatus.Applied => "Başvurdum",
        ApplicationStatus.Skipped => "Geçtim", _ => "—"
    };
}
