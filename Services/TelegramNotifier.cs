using System.Net.Http.Json;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KariyerTakip.Models;

namespace KariyerTakip.Services;

public class TelegramNotifier
{
    private readonly HttpClient _httpClient;
    private readonly TelegramOptions _telegramOptions;
    private readonly ILogger<TelegramNotifier> _logger;

    public TelegramNotifier(HttpClient httpClient, IOptions<AppConfig> config, ILogger<TelegramNotifier> logger)
    {
        _httpClient = httpClient;
        _telegramOptions = config.Value.Telegram;
        _logger = logger;
    }

    public bool IsConfigured => _telegramOptions.Enabled &&
                                !string.IsNullOrWhiteSpace(_telegramOptions.BotToken) &&
                                !string.IsNullOrWhiteSpace(_telegramOptions.ChatId);

    public async Task<bool> SendMessageAsync(string htmlMessage, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogInformation("[Telegram Kapalı/Yapılandırılmamış] Gönderilecek Mesaj:\n{Message}", htmlMessage);
            return true;
        }

        try
        {
            var url = $"https://api.telegram.org/bot{_telegramOptions.BotToken}/sendMessage";
            var payload = new
            {
                chat_id = _telegramOptions.ChatId,
                text = htmlMessage,
                parse_mode = "HTML",
                disable_web_page_preview = false
            };

            var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Telegram bildirimi başarıyla iletildi.");
                return true;
            }

            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Telegram bildirimi gönderilemedi: {StatusCode} - {Error}", response.StatusCode, err);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram mesajı gönderilirken istisna oluştu.");
            return false;
        }
    }

    public string FormatAnnouncementMessage(
        AnnouncementRecord announcement,
        List<PositionEvaluation> matchingPositions,
        string notificationHeader = "📢 <b>YENİ UYGUN KAMU İLANI</b>")
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(notificationHeader);
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"🏛 <b>Kurum:</b> {HtmlEncode(announcement.InstitutionName)}");
        if (!string.IsNullOrWhiteSpace(announcement.UnitName))
        {
            sb.AppendLine($"🏢 <b>Birim:</b> {HtmlEncode(announcement.UnitName)}");
        }
        sb.AppendLine($"📋 <b>İlan:</b> {HtmlEncode(announcement.Title)}");
        sb.AppendLine();

        sb.AppendLine("🎯 <b>Uygun / Değerlendirilen Kadrolar:</b>");
        foreach (var pos in matchingPositions)
        {
            var statusIcon = pos.Status == EligibilityStatus.Eligible ? "✅" : "⚠️";
            var statusText = pos.Status == EligibilityStatus.Eligible ? "ŞARTLARA UYGUN" : "KONTROL GEREKLİ";

            sb.AppendLine($"{statusIcon} <b>{HtmlEncode(pos.PositionTitle)}</b> [{statusText}]");
            if (!string.IsNullOrWhiteSpace(pos.Unvan))
                sb.AppendLine($"   • <b>Unvan:</b> {HtmlEncode(pos.Unvan)}");
            sb.AppendLine($"   • <b>Şehir / Kontenjan:</b> {HtmlEncode(pos.Cities)} (Toplam: {pos.TotalQuota})");
            sb.AppendLine($"   • <b>KPSS Şartı:</b> {HtmlEncode(pos.ExtractedKpssText)}");
            sb.AppendLine($"   • <b>Tecrübe:</b> {HtmlEncode(pos.ExtractedExperienceText)}");
            sb.AppendLine($"   • <b>Sonuç / Gerekçe:</b> <i>{HtmlEncode(pos.SummaryReason)}</i>");
            sb.AppendLine();
        }

        var startStr = announcement.StartDate?.ToString("dd.MM.yyyy HH:mm") ?? "Belirtilmemiş";
        var endStr = announcement.EndDate?.ToString("dd.MM.yyyy HH:mm") ?? "Belirtilmemiş";

        sb.AppendLine("📅 <b>Başvuru Tarihleri:</b>");
        sb.AppendLine($"   • <b>Başlangıç:</b> {startStr}");
        sb.AppendLine($"   • <b>Bitiş:</b> {endStr}");
        sb.AppendLine();

        sb.AppendLine("🔗 <b>Bağlantılar:</b>");
        if (!string.IsNullOrWhiteSpace(announcement.DetailUrl))
        {
            sb.AppendLine($"   • <a href=\"{announcement.DetailUrl}\">Kariyer Kapısı İlan Detayı</a>");
        }
        if (!string.IsNullOrWhiteSpace(announcement.ApplicationUrl))
        {
            sb.AppendLine($"   • <a href=\"{announcement.ApplicationUrl}\">Resmî Başvuru / e-Devlet Ekranı</a>");
        }

        sb.AppendLine();
        sb.AppendLine($"⏱ <i>Kontrol Zamanı: {DateTime.Now:dd.MM.yyyy HH:mm} (KariyerTakip Asistanı)</i>");

        return sb.ToString();
    }

    private string HtmlEncode(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
