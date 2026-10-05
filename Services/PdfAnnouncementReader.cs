using System.Globalization;
using System.Text.RegularExpressions;
using KariyerTakip.Common;
using KariyerTakip.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace KariyerTakip.Services;

public sealed class PdfAnnouncementDetails
{
    public string FullText { get; set; } = "";
    public string GeneralConditionsText { get; set; } = "";
    public List<AltIlanResponse> Positions { get; set; } = new();
    public string PdfUrl { get; set; } = "";
    public string ApplicationUrl { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public sealed class PdfAnnouncementReader
{
    public const string ReviewMarker = "[PDF_KADRO_KONTROL_GEREKLI]";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    public PdfAnnouncementDetails Read(byte[] bytes, string announcementTitle, CancellationToken token = default)
    {
        if (bytes.Length < 5 || System.Text.Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
            throw new FormatException("Resmî bağlantı PDF yerine hata sayfası döndürdü.");
        using var document = PdfDocument.Open(bytes);
        if (document.NumberOfPages > 100) throw new FormatException("PDF 100 sayfa sınırını aşıyor; belgeyi elle kontrol edin.");
        var fullPages = new List<string>();
        var generalPages = new List<string>();
        var result = new PdfAnnouncementDetails();
        TableSchema? schema = null;
        foreach (var page in document.GetPages())
        {
            token.ThrowIfCancellationRequested();
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();
            fullPages.Add($"--- PDF Sayfa {page.Number} ---\n" + ReadWords(words));
            var tableWords = new HashSet<Word>();
            ExtractTablePositions(page, words, result.Positions, tableWords, ref schema);
            generalPages.Add(ReadWords(words.Where(w => !tableWords.Contains(w))));
        }
        result.FullText = string.Join("\n\n", fullPages);
        result.GeneralConditionsText = string.Join("\n\n", generalPages);
        if (Regex.Matches(result.FullText, @"[\p{L}]").Count < 100)
            throw new FormatException("PDF taranmış görüntü içeriyor veya okunabilir metni yok. OCR / belge kontrolü gerekiyor.");
        if (result.FullText.Count(c => c == '\uFFFD') > result.FullText.Length / 100)
            throw new FormatException("PDF metninin karakter eşlemesi bozuk; şartlar güvenilir biçimde okunamadı.");
        if (result.Positions.Count == 0)
        {
            result.Positions.Add(new AltIlanResponse
            {
                IlanBaslik = announcementTitle,
                IlanMetni = ReviewMarker + "\nPDF metni okundu; kadro sınırları güvenilir biçimde ayrılamadı.\n" + result.FullText
            });
            // Do not infer common requirements from a document with unseparated positions.
            result.GeneralConditionsText = "";
        }
        result.StartDate = ReadLabeledDate(result.FullText, @"Başlangıç\s+Tarihi");
        result.EndDate = ReadLabeledDate(result.FullText, @"Bitiş\s+Tarihi");
        var urls = Regex.Matches(result.FullText, @"https://[^\s<>]+")
            .Select(m => m.Value.TrimEnd('.', ',', ')', ';', '\''));
        result.ApplicationUrl = urls.FirstOrDefault(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) &&
            (uri.Host == "kariyerkapisi.gov.tr" && uri.AbsolutePath.Contains("isealim", StringComparison.OrdinalIgnoreCase))) ?? "";
        return result;
    }

    private sealed record TableSchema(int Code, int Title, int Quota, int Requirements, int Score, int Count);
    private static void ExtractTablePositions(Page page, List<Word> words, List<AltIlanResponse> output,
        HashSet<Word> tableWords, ref TableSchema? schema)
    {
        var rectangles = page.Paths.SelectMany(path => path.SelectMany(subpath => subpath.Commands))
            .Select(command => command.GetBoundingRectangle()).Where(r => r.HasValue).Select(r => r!.Value).ToList();
        var horizontal = rectangles.Where(r => r.Height <= 2 && r.Width > page.Width * .25).ToList();
        var levels = Cluster(horizontal.Select(r => (r.Top + r.Bottom) / 2)).OrderDescending().ToList();
        var vertical = rectangles.Where(r => r.Width <= 2 && r.Height > 10).ToList();
        for (var index = 0; index + 1 < levels.Count; index++)
        {
            var top = levels[index]; var bottom = levels[index + 1];
            if (top - bottom < 8) continue;
            var mid = (top + bottom) / 2;
            var columns = Cluster(vertical.Where(r => r.Bottom < mid && r.Top > mid).Select(r => (r.Left + r.Right) / 2)).Order().ToList();
            if (columns.Count < 4) continue;
            var rowWords = words.Where(w => CenterY(w) < top && CenterY(w) > bottom &&
                CenterX(w) >= columns[0] && CenterX(w) <= columns[^1]).ToList();
            var cells = Enumerable.Range(0, columns.Count - 1).Select(i => ReadWords(rowWords.Where(w =>
                CenterX(w) >= columns[i] && CenterX(w) < columns[i + 1]))).ToArray();
            var header = cells.Select(Normalize).ToArray();
            var titleIndex = Array.FindIndex(header, c => c.Contains("unvan") || c.Contains("ünvan"));
            var reqIndex = Array.FindIndex(header, c => c.Contains("nitelik") || c.Contains("açıklama") || c.Contains("özel şart"));
            if (titleIndex >= 0 && reqIndex >= 0)
            {
                schema = new TableSchema(Array.FindIndex(header, c => c.Contains("kod") || c.Contains("sıra")), titleIndex,
                    Array.FindIndex(header, c => c.Contains("adet") || c.Contains("sayı") || c.Contains("kontenjan")), reqIndex,
                    Array.FindIndex(header, c => c.Contains("puan") || c.Contains("kpss")), cells.Length);
                foreach (var w in rowWords) tableWords.Add(w);
                continue;
            }
            if (schema == null || cells.Length != schema.Count) continue;
            var code = schema.Code < 0 ? "" : Regex.Replace(cells[schema.Code], @"\s+", "");
            if (schema.Code >= 0 && !Regex.IsMatch(code, @"^(?:\d{1,4}(?:[-/][A-Za-z0-9]+)*|[A-Za-z]{1,5}[-/]\d+)$")) continue;
            var title = Regex.Replace(cells[schema.Title], @"\s+", " ").Trim();
            var requirements = cells[schema.Requirements];
            if (title.Length < 3 || requirements.Length < 20) continue;
            var quota = 0;
            if (schema.Quota >= 0) int.TryParse(cells[schema.Quota].Trim(), out quota);
            var text = requirements;
            if (schema.Score >= 0) text += "\nKPSS puan türü: " + cells[schema.Score];
            // Academic requirements (doctorate, publications, language scores) exceed the profile schema.
            if (Regex.IsMatch(title + " " + requirements, @"profesör|doçent|doktor|öğretim üyesi", RegexOptions.IgnoreCase))
                text = ReviewMarker + "\nAkademik / özel şartların ayrıca kontrol edilmesi gerekiyor.\n" + text;
            output.Add(new AltIlanResponse
            {
                IlanBaslik = code.Length > 0 ? $"{code} — {title}" : title, Unvan = title, IlanMetni = text,
                KontenjanList = quota > 0 ? new List<KontenjanItem> { new() { Il = "Belirtilmemiş", Kontenjan = quota } } : null
            });
            foreach (var w in rowWords) tableWords.Add(w);
        }
    }

    private static IEnumerable<double> Cluster(IEnumerable<double> coordinates)
    {
        var group = new List<double>();
        foreach (var coordinate in coordinates.Order())
        {
            if (group.Count > 0 && coordinate - group[^1] > 2)
            { yield return group.Average(); group.Clear(); }
            group.Add(coordinate);
        }
        if (group.Count > 0) yield return group.Average();
    }

    private static double CenterX(Word word) => (word.BoundingBox.Left + word.BoundingBox.Right) / 2;
    private static double CenterY(Word word) => (word.BoundingBox.Top + word.BoundingBox.Bottom) / 2;
    private static string Normalize(string text) => Regex.Replace(text.ToLower(Turkish), @"\s+", " ");
    private static string ReadWords(IEnumerable<Word> words)
    {
        var lines = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(CenterY).ThenBy(CenterX))
        {
            if (lines.Count == 0 || Math.Abs(CenterY(lines[^1][0]) - CenterY(word)) > 2.5) lines.Add(new());
            lines[^1].Add(word);
        }
        return string.Join("\n", lines.Select(line => string.Join(" ", line.OrderBy(CenterX).Select(w => w.Text)))).Normalize();
    }

    private static DateTime? ReadLabeledDate(string text, string label)
    {
        var match = Regex.Match(text, label + @"\s*:\s*(?<date>\d{1,2}[./]\d{1,2}[./]\d{4})\s*[-–]?\s*(?:Saat\s*:\s*)?(?<time>\d{1,2}[:.]\d{2})", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        if (!DateTime.TryParse(match.Groups["date"].Value.Replace('/', '.') + " " + match.Groups["time"].Value.Replace('.', ':'), Turkish, DateTimeStyles.None, out var date)) return null;
        return AppTime.ToUtc(DateTime.SpecifyKind(date, DateTimeKind.Unspecified));
    }
}
