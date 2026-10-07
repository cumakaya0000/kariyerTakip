using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KariyerTakip.Models;
using KariyerTakip.Common;
using System.Text.Json;

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
    public DateTime? RetryAfterUtc { get; set; }

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

    public async Task<TelegramSendResult> SendMessageAsync(string htmlMessage, CancellationToken cancellationToken = default,
        int nextChunkIndex = 0, Func<int, Task>? saveProgress = null, TelegramOptions? credentials = null)
    {
        var options = credentials ?? _telegramOptions;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.BotToken) || string.IsNullOrWhiteSpace(options.ChatId))
        {
            _logger.LogInformation("[Telegram Kapalı/Yapılandırılmamış] Mesaj gönderilmedi.");
            return TelegramSendResult.Disabled();
        }

        try
        {
            var chunks = TelegramMessageSplitter.Split(htmlMessage);
            if (nextChunkIndex < 0 || nextChunkIndex > chunks.Count)
                return TelegramSendResult.Permanent("Geçersiz bildirim parça numarası.");
            for (int i = nextChunkIndex; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var url = $"https://api.telegram.org/bot{options.BotToken}/sendMessage";
                var payload = new
                {
                    chat_id = options.ChatId,
                    text = chunk.Text,
                    parse_mode = chunk.ParseMode,
                    disable_web_page_preview = false
                };

                using var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    // A response with ok=false is still a failure even if HTTP was successful.
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var json = JsonDocument.Parse(body);
                    if (!json.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                        return TelegramSendResult.Permanent("Telegram isteği kabul etmedi.");
                    if (saveProgress != null) await saveProgress(i + 1);
                    continue;
                }

                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                var statusCode = (int)response.StatusCode;

                // URLs and tokens never enter error messages or exception logs.
                _logger.LogWarning("Telegram bildirimi gönderilemedi: HTTP {StatusCode}", statusCode);

                if (statusCode == 429 || statusCode >= 500)
                {
                    var result = TelegramSendResult.Transient($"Telegram geçici hatası (HTTP {statusCode}).", statusCode);
                    var delay = RetryPolicy.GetScheduledDelay(1, response.Headers.RetryAfter);
                    try
                    {
                        using var json = JsonDocument.Parse(err);
                        if (json.RootElement.TryGetProperty("parameters", out var parameters) &&
                            parameters.TryGetProperty("retry_after", out var seconds) && seconds.TryGetInt32(out var value))
                            delay = TimeSpan.FromSeconds(Math.Max(0, value));
                    }
                    catch (JsonException) { }
                    result.RetryAfterUtc = DateTime.UtcNow + delay;
                    return result;
                }

                return TelegramSendResult.Permanent($"Telegram isteği reddedildi (HTTP {statusCode}). Token ve sohbet ID'sini kontrol edin.", statusCode);
            }

            _logger.LogInformation("Telegram bildirimi başarıyla iletildi.");
            return TelegramSendResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return TelegramSendResult.Transient("Telegram isteği zaman aşımına uğradı.");
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("Telegram ağına erişirken geçici hata oluştu.");
            return TelegramSendResult.Transient("Telegram ağına erişilemedi.");
        }
        catch (JsonException)
        {
            return TelegramSendResult.Transient("Telegram yanıtı okunamadı.");
        }
    }

    public string FormatAnnouncementMessage(
        AnnouncementRecord announcement,
        List<PositionEvaluation> positions,
        string notificationHeader = "📢 <b>YENİ UYGUN KAMU İLANI</b>")
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(notificationHeader);
        sb.AppendLine($"🌐 <b>Kaynak:</b> {HtmlEncode(announcement.Source.DisplayName())}");
        sb.AppendLine("Otomatik değerlendirmedir; başvurudan önce resmî ilan metnini kontrol edin.");
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
                EligibilityStatus.LikelyIneligible => "◻️",
                _ => "❌"
            };

            var statusText = pos.Status switch
            {
                EligibilityStatus.Eligible => "ŞARTLARA UYGUN",
                EligibilityStatus.NeedsReview => "KONTROL GEREKLİ",
                EligibilityStatus.LikelyIneligible => "BÜYÜK OLASILIKLA UYGUN DEĞİL",
                _ => "UYGUN DEĞİL"
            };

            sb.AppendLine($"{statusIcon} <b>{HtmlEncode(pos.PositionTitle)}</b> [{statusText}]");
            if (!string.IsNullOrWhiteSpace(pos.Unvan))
                sb.AppendLine($"   • <b>Unvan:</b> {HtmlEncode(pos.Unvan)}");
            sb.AppendLine($"   • <b>Şehir / Kontenjan:</b> {HtmlEncode(pos.Cities)} (Toplam: {pos.TotalQuota})");
            sb.AppendLine($"   • <b>KPSS Şartı:</b> {HtmlEncode(pos.ExtractedKpssText)}");
            sb.AppendLine($"   • <b>Tecrübe:</b> {HtmlEncode(pos.ExtractedExperienceText)}");
            sb.AppendLine($"   • <b>Sonuç / Gerekçe:</b> <i>{HtmlEncode(pos.SummaryReason)}</i>");
            if (!pos.PreferencesMatch) sb.AppendLine("   • <b>Tercih uyuşmuyor:</b> Başvuru yeterliliği sağlansa da şehir veya çalışma türü tercih dışı.");
            sb.AppendLine();
        }

        var startStr = AppTime.Format(announcement.StartDate);
        var endStr = AppTime.Format(announcement.EndDate);

        sb.AppendLine("📅 <b>Başvuru Tarihleri:</b>");
        sb.AppendLine($"   • <b>Başlangıç:</b> {startStr}");
        sb.AppendLine($"   • <b>Bitiş:</b> {endStr}");
        sb.AppendLine();

        sb.AppendLine("🔗 <b>Bağlantılar:</b>");
        if (announcement.Source == AnnouncementSource.KamuIlan)
        {
            sb.AppendLine($"   • <a href=\"{KamuIlanClient.BaseUrl}#section3\">Kamu İlan (SBB) listesi / arşivi</a>");
            sb.AppendLine("   • İlanın PDF'sini KariyerTakip uygulamasında açabilirsiniz.");
        }
        else if (IsValidUrl(announcement.DetailUrl))
        {
            sb.AppendLine($"   • <a href=\"{HtmlAttributeEncode(announcement.DetailUrl)}\">{HtmlEncode(announcement.Source.DisplayName())} İlan Detayı</a>");
        }
        if (IsValidUrl(announcement.ApplicationUrl))
        {
            sb.AppendLine($"   • <a href=\"{HtmlAttributeEncode(announcement.ApplicationUrl)}\">Resmî Başvuru / e-Devlet Ekranı</a>");
        }

        sb.AppendLine();
        sb.AppendLine($"⏱ <i>Kontrol Zamanı: {AppTime.Format(DateTime.UtcNow)} (Türkiye saati, KariyerTakip Asistanı)</i>");

        return sb.ToString();
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
