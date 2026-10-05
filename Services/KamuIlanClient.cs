using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using KariyerTakip.Common;
using KariyerTakip.Models;

namespace KariyerTakip.Services;

public class KamuIlanClient
{
    public const string BaseUrl = "https://kamuilan.sbb.gov.tr/";
    private readonly HttpClient _http;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly PdfAnnouncementReader _pdfReader;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly int _delayMs;
    private DateTime _nextRequestUtc;
    private readonly AnnouncementPdfCache? _pdfCache;

    public KamuIlanClient(HttpClient http, PdfAnnouncementReader? pdfReader = null, IOptions<AppConfig>? config = null, AnnouncementPdfCache? pdfCache = null)
    {
        _http = http;
        _pdfCache = pdfCache;
        _pdfReader = pdfReader ?? new PdfAnnouncementReader();
        _delayMs = Math.Max(0, config?.Value.Scan.RequestDelayMs ?? 300);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "KariyerTakip/1.0");
        _http.DefaultRequestHeaders.Referrer = new Uri(BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(40);
    }

    public async Task<ApiResult<PdfAnnouncementDetails>> GetAnnouncementDocumentAsync(SearchIlanItem item, CancellationToken token = default)
    {
        if (!Uri.TryCreate(item.DetailUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "kamuilan.sbb.gov.tr")
            return ApiResult<PdfAnnouncementDetails>.Fail(ApiCallStatus.ParseError, "Kamu İlan PDF bağlantısı geçersiz.");
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await _requestGate.WaitAsync(token);
                try
                {
                    var wait = _nextRequestUtc - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
                    _nextRequestUtc = DateTime.UtcNow.AddMilliseconds(_delayMs);
                }
                finally { _requestGate.Release(); }
                using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    if (attempt < 3) { await Task.Delay(RetryPolicy.GetDelay(attempt, response.Headers.RetryAfter), token); continue; }
                }
                if (!response.IsSuccessStatusCode)
                    return ApiResult<PdfAnnouncementDetails>.Fail(ApiCallStatus.HttpError, $"PDF alınamadı: HTTP {(int)response.StatusCode}", (int)response.StatusCode);
                const int maxBytes = 20 * 1024 * 1024;
                if (response.Content.Headers.ContentLength > maxBytes) throw new FormatException("PDF 20 MB boyut sınırını aşıyor.");
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int count;
                while ((count = await stream.ReadAsync(chunk, token)) > 0)
                {
                    if (buffer.Length + count > maxBytes) throw new FormatException("PDF 20 MB boyut sınırını aşıyor.");
                    buffer.Write(chunk, 0, count);
                }
                token.ThrowIfCancellationRequested();
                var bytes = buffer.ToArray();
                if (_pdfCache != null) await _pdfCache.StoreAsync(item.Guid, bytes, token);
                var details = _pdfReader.Read(bytes, item.IlanBaslik, token);
                details.PdfUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? item.DetailUrl;
                return ApiResult<PdfAnnouncementDetails>.Ok(details);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or RetryDeferredException)
        { return ApiResult<PdfAnnouncementDetails>.Fail(ApiCallStatus.NetworkError, "PDF indirilirken ağ hatası veya zaman aşımı oluştu."); }
        catch (Exception ex)
        { return ApiResult<PdfAnnouncementDetails>.Fail(ApiCallStatus.ParseError, ex is FormatException ? ex.Message : "PDF yapısı çözümlenemedi; resmî belgeyi kontrol edin."); }
        return ApiResult<PdfAnnouncementDetails>.Fail(ApiCallStatus.NetworkError, "PDF indirme denemeleri başarısız.");
    }

    public async Task<ApiResult<List<SearchIlanItem>>> GetActiveAnnouncementsAsync(string keyword = "", CancellationToken token = default)
    {
        try
        {
            using var response = await _http.GetAsync(BaseUrl, token);
            if (!response.IsSuccessStatusCode)
                return ApiResult<List<SearchIlanItem>>.Fail(ApiCallStatus.HttpError, $"Kamu İlan: HTTP {(int)response.StatusCode}", (int)response.StatusCode);
            return ApiResult<List<SearchIlanItem>>.Ok(ParseAnnouncements(await response.Content.ReadAsStringAsync(token), DateTime.UtcNow, keyword));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (FormatException ex) { return ApiResult<List<SearchIlanItem>>.Fail(ApiCallStatus.ParseError, ex.Message); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        { return ApiResult<List<SearchIlanItem>>.Fail(ApiCallStatus.NetworkError, "Kamu İlan sitesine erişilemedi."); }
    }

    public static List<SearchIlanItem> ParseAnnouncements(string html, DateTime nowUtc, string keyword = "")
    {
        var document = new HtmlAgilityPack.HtmlDocument();
        document.LoadHtml(html);
        // The timeline contains the full list; the headline carousel repeats some entries.
        var links = document.DocumentNode.SelectNodes("//ul[@id='nav2']//a[contains(@href,'ilanDetay.aspx?kod=')]");
        if (links == null) throw new FormatException("Kamu İlan sayfa yapısı değişmiş olabilir: ilan listesi bulunamadı.");
        var now = AppTime.ToDisplay(nowUtc);
        var results = new Dictionary<string, SearchIlanItem>();
        foreach (var link in links)
        {
            var institution = Text(link.SelectSingleNode(".//p[@class='alt_p1']"));
            var titleNode = link.SelectSingleNode(".//p[@class='alt_p2']");
            if (institution.Length == 0 || titleNode == null) throw new FormatException("Kamu İlan kurum veya başlık alanı eksik.");
            var dates = Text(titleNode.SelectSingleNode(".//em"));
            var titleCopy = titleNode.CloneNode(true);
            foreach (var em in titleCopy.SelectNodes(".//em") ?? Enumerable.Empty<HtmlNode>()) em.Remove();
            var title = Text(titleCopy);
            if (title.Length == 0) throw new FormatException("Kamu İlan başlığı boş.");
            var (start, end) = ParseDates(dates, now);
            if (end.HasValue && end < nowUtc) continue;
            var href = HtmlEntity.DeEntitize(link.GetAttributeValue("href", ""));
            if (!Uri.TryCreate(new Uri(BaseUrl), href, out var url) || url.Host != "kamuilan.sbb.gov.tr" || url.Scheme != "https")
                throw new FormatException("Kamu İlan bağlantısı geçersiz.");
            // kod is encrypted and changes on every page load. Never use it as database identity.
            var publishedNode = link.Ancestors("li").FirstOrDefault()?.SelectSingleNode(".//time");
            var publishedText = Text(publishedNode?.SelectSingleNode(".//h4")) + " " + Text(publishedNode?.SelectSingleNode(".//h3"));
            if (!DateTime.TryParse(publishedText + " " + now.Year, Turkish, DateTimeStyles.None, out var published))
                throw new FormatException("Kamu İlan yayın tarihi bulunamadı.");
            if (published > now.Date.AddDays(1)) published = published.AddYears(-1);
            var identity = $"{institution}|{title}|{published:yyyy-MM-dd}";
            var key = "sbb:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(keyword) && Turkish.CompareInfo.IndexOf(institution + " " + title, keyword, CompareOptions.IgnoreCase) < 0) continue;
            results[key] = new SearchIlanItem
            {
                Source = AnnouncementSource.KamuIlan, Guid = key, KurumAdi = institution, IlanBaslik = title,
                IlanTuru = "Kamu İlan", BasTarih = start, BitTarih = end, DetailUrl = url.AbsoluteUri
            };
        }
        return results.Values.ToList();
    }

    private static string Text(HtmlNode? node) => Regex.Replace(HtmlEntity.DeEntitize(node?.InnerText ?? ""), @"\s+", " ").Trim();

    private static (DateTime?, DateTime?) ParseDates(string text, DateTime now)
    {
        var matches = Regex.Matches(text, @"(?<day>\d{1,2})\s+(?<month>[\p{L}]+)(?:\s+(?<year>\d{4}))?");
        if (matches.Count != 2) return (null, null);
        DateTime Read(Match m, int year) => DateTime.Parse($"{m.Groups["day"].Value} {m.Groups["month"].Value} {year}", Turkish);
        var first = matches[0]; var last = matches[1];
        var year = last.Groups["year"].Success ? int.Parse(last.Groups["year"].Value) : now.Year;
        var end = Read(last, year);
        if (!last.Groups["year"].Success)
        {
            if (end < now.Date.AddMonths(-6)) end = end.AddYears(1);
            else if (end > now.Date.AddMonths(6)) end = end.AddYears(-1);
        }
        var start = Read(first, first.Groups["year"].Success ? int.Parse(first.Groups["year"].Value) : end.Year);
        if (!first.Groups["year"].Success && start > end) start = start.AddYears(-1);
        return (AppTime.ToUtc(start), AppTime.ToUtc(end.AddDays(1).AddTicks(-1)));
    }
}
