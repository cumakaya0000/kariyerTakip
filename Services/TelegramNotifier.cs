using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KariyerTakip.Models;

namespace KariyerTakip.Services;

public enum TelegramSendStatus
{
    Success,
    Disabled,
    TransientFailure,
    PermanentFailure
}

public class TelegramSendResult
{
    public TelegramSendStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public int? StatusCode { get; set; }

    public bool IsSuccess => Status == TelegramSendStatus.Success;

    public static TelegramSendResult Ok() => new() { Status = TelegramSendStatus.Success };
    public static TelegramSendResult Disabled() => new() { Status = TelegramSendStatus.Disabled, ErrorMessage = "Telegram yapılandırılmamış veya devre dışı bırakılmış." };
    public static TelegramSendResult Transient(string error, int? code = null) => new() { Status = TelegramSendStatus.TransientFailure, ErrorMessage = error, StatusCode = code };
    public static TelegramSendResult Permanent(string error, int? code = null) => new() { Status = TelegramSendStatus.PermanentFailure, ErrorMessage = error, StatusCode = code };
}

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

    public async Task<TelegramSendResult> SendMessageAsync(string htmlMessage, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogInformation("[Telegram Kapalı/Yapılandırılmamış] Mesaj gönderilmedi.");
            return TelegramSendResult.Disabled();
        }

        try
        {
            var chunks = SplitMessage(htmlMessage, 4000);
            foreach (var chunk in chunks)
            {
                var url = $"https://api.telegram.org/bot{_telegramOptions.BotToken}/sendMessage";
                var payload = new
                {
                    chat_id = _telegramOptions.ChatId,
                    text = chunk,
                    parse_mode = "HTML",
                    disable_web_page_preview = false
                };

                var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    continue;
                }

                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                var statusCode = (int)response.StatusCode;

                _logger.LogError("Telegram bildirimi gönderilemedi: {StatusCode} - {Error}", statusCode, err);

                if (statusCode == 429 || statusCode >= 500)
                {
                    return TelegramSendResult.Transient(err, statusCode);
                }

                return TelegramSendResult.Permanent(err, statusCode);
            }

            _logger.LogInformation("Telegram bildirimi başarıyla iletildi.");
            return TelegramSendResult.Ok();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Telegram ağına erişirken geçici hata oluştu.");
            return TelegramSendResult.Transient(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram mesajı gönderilirken istisna oluştu.");
            return TelegramSendResult.Permanent(ex.Message);
        }
    }

    public string FormatAnnouncementMessage(
        AnnouncementRecord announcement,
        List<PositionEvaluation> positions,
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

        sb.AppendLine("🎯 <b>Kadro ve Şart Detayları:</b>");
        foreach (var pos in positions)
        {
            var statusIcon = pos.Status switch
            {
                EligibilityStatus.Eligible => "✅",
                EligibilityStatus.NeedsReview => "⚠️",
                _ => "❌"
            };

            var statusText = pos.Status switch
            {
                EligibilityStatus.Eligible => "ŞARTLARA UYGUN",
                EligibilityStatus.NeedsReview => "KONTROL GEREKLİ",
                _ => "UYGUN DEĞİL"
            };

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
        if (IsValidUrl(announcement.DetailUrl))
        {
            sb.AppendLine($"   • <a href=\"{HtmlAttributeEncode(announcement.DetailUrl)}\">Kariyer Kapısı İlan Detayı</a>");
        }
        if (IsValidUrl(announcement.ApplicationUrl))
        {
            sb.AppendLine($"   • <a href=\"{HtmlAttributeEncode(announcement.ApplicationUrl)}\">Resmî Başvuru / e-Devlet Ekranı</a>");
        }

        sb.AppendLine();
        sb.AppendLine($"⏱ <i>Kontrol Zamanı: {DateTime.Now:dd.MM.yyyy HH:mm} (KariyerTakip Asistanı)</i>");

        return sb.ToString();
    }

    private static List<string> SplitMessage(string text, int maxChunkSize)
    {
        var list = new List<string>();
        if (text.Length <= maxChunkSize)
        {
            list.Add(text);
            return list;
        }

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var current = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            if (current.Length + line.Length + 1 > maxChunkSize)
            {
                list.Add(current.ToString());
                current.Clear();
            }
            current.AppendLine(line);
        }

        if (current.Length > 0)
        {
            list.Add(current.ToString());
        }

        return list;
    }

    private static bool IsValidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult) &&
               (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }

    private static string HtmlEncode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private static string HtmlAttributeEncode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return HtmlEncode(text).Replace("\"", "&quot;").Replace("'", "&#39;");
    }
}
