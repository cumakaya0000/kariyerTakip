using KariyerTakip.Models;
using KariyerTakip.Services;

namespace KariyerTakip.Forms;

public sealed class SettingsEditorControl : UserControl
{
    private readonly CheckBox _enabled = new() { Text = "Telegram bildirimlerini etkinleştir", AutoSize = true };
    private readonly TextBox _token = new() { UseSystemPasswordChar = true };
    private readonly TextBox _chatId = new();
    private readonly CheckBox _tray = new() { Text = "Küçültünce sistem tepsisinde çalış", AutoSize = true };
    private readonly CheckBox _startup = new() { Text = "Windows ile başlat", AutoSize = true };
    private readonly NumericUpDown _parallel = new() { Minimum = 1, Maximum = 8 };
    private readonly NumericUpDown _delay = new() { Minimum = 0, Maximum = 60000, Increment = 100 };
    private readonly NumericUpDown _reminder = new() { Minimum = 0, Maximum = 30 };
    private readonly AppConfig _config;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action? SettingsSaved { get; set; }

    public SettingsEditorControl(AppConfig config, ConfigurationStore store, TelegramNotifier notifier)
    {
        _config = config;
        Dock = DockStyle.Fill; AutoScroll = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ProfileEditorControl.AddRow(layout, "Bildirim", _enabled);
        ProfileEditorControl.AddRow(layout, "Bot token (Windows ile şifrelenir)", _token);
        ProfileEditorControl.AddRow(layout, "Telegram sohbet ID", _chatId);
        var test = new Button { Text = "Test bildirimi gönder", AutoSize = true };
        ProfileEditorControl.AddRow(layout, "", test);
        ProfileEditorControl.AddRow(layout, "Masaüstü", _tray);
        ProfileEditorControl.AddRow(layout, "", _startup);
        ProfileEditorControl.AddRow(layout, "Eşzamanlı ilan (1–8)", _parallel);
        ProfileEditorControl.AddRow(layout, "İstekler arası bekleme (ms)", _delay);
        ProfileEditorControl.AddRow(layout, "Hatırlatma: kalan gün (0: kapalı)", _reminder);
        var save = new Button { Text = "Ayarları kaydet", AutoSize = true };
        ProfileEditorControl.AddRow(layout, "", save);
        Controls.Add(layout);
        _enabled.Checked = config.Telegram.Enabled; _token.Text = config.Telegram.BotToken; _chatId.Text = config.Telegram.ChatId;
        _tray.Checked = config.Desktop.MinimizeToTray; _startup.Checked = config.Desktop.StartWithWindows;
        _parallel.Value = Math.Clamp(config.Scan.MaxConcurrency, 1, 8);
        _delay.Value = Math.Clamp(config.Scan.RequestDelayMs, 0, 60000);
        _reminder.Value = Math.Clamp(config.Scan.ReminderDays, 0, 30);
        test.Click += async (s, e) =>
        {
            test.Enabled = false;
            try
            {
                var result = await notifier.SendMessageAsync("🔔 <b>KariyerTakip Test Bildirimi</b>\nBağlantı başarılı.",
                    credentials: new TelegramOptions { Enabled = true, BotToken = _token.Text.Trim(), ChatId = _chatId.Text.Trim() });
                MessageBox.Show(result.IsSuccess ? "Test bildirimi iletildi." : result.ErrorMessage ?? "Bildirim gönderilemedi.", "Telegram testi");
            }
            catch (Exception) { MessageBox.Show("Telegram bağlantısı kurulamadı. Ağ bağlantınızı kontrol edin."); }
            finally { test.Enabled = true; }
        };
        save.Click += async (s, e) =>
        {
            save.Enabled = false;
            try
            {
                if (_enabled.Checked && (string.IsNullOrWhiteSpace(_token.Text) || string.IsNullOrWhiteSpace(_chatId.Text)))
                    throw new InvalidOperationException("Telegram'ı etkinleştirmek için token ve sohbet ID girin.");
                var oldStartup = config.Desktop.StartWithWindows;
                var next = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(System.Text.Json.JsonSerializer.Serialize(config))!;
                next.Telegram = new TelegramOptions { Enabled = _enabled.Checked, BotToken = _token.Text.Trim(), ChatId = _chatId.Text.Trim() };
                next.Desktop = new DesktopOptions { MinimizeToTray = _tray.Checked, StartWithWindows = _startup.Checked };
                next.Scan.MaxConcurrency = (int)_parallel.Value; next.Scan.RequestDelayMs = (int)_delay.Value;
                next.Scan.ReminderDays = (int)_reminder.Value;
                if (oldStartup != next.Desktop.StartWithWindows) WindowsStartup.Apply(next.Desktop.StartWithWindows);
                try { await store.SaveAsync(next); }
                catch { if (oldStartup != next.Desktop.StartWithWindows) WindowsStartup.Apply(oldStartup); throw; }
                // Keep DI's option instance and its nested instances shared with existing services.
                config.Telegram.Enabled = next.Telegram.Enabled; config.Telegram.BotToken = next.Telegram.BotToken; config.Telegram.ChatId = next.Telegram.ChatId;
                config.Desktop = next.Desktop;
                config.Scan.MaxConcurrency = next.Scan.MaxConcurrency; config.Scan.RequestDelayMs = next.Scan.RequestDelayMs; config.Scan.ReminderDays = next.Scan.ReminderDays;
                SettingsSaved?.Invoke();
                MessageBox.Show("Ayarlar kaydedildi. Telegram anahtarı bu Windows hesabı için şifrelenmiştir.", "Tamamlandı");
            }
            catch (Exception ex) { MessageBox.Show("Ayarlar kaydedilemedi: " + FileLoggerProvider.Redact(ex.Message), "Ayar hatası", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { save.Enabled = true; }
        };
    }
}
