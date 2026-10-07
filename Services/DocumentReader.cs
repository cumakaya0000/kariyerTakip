using System.Net;
using System.Text.RegularExpressions;

namespace KariyerTakip.Services;

public class DocumentReader
{
    private static readonly Regex BbCodeRegex = new Regex(@"\[/?[a-zA-Z0-9_=\.\#\%\:\-\s\""']+\]", RegexOptions.Compiled);
    private static readonly Regex MultipleSpacesRegex = new Regex(@"[ \t]+", RegexOptions.Compiled);
    private static readonly Regex MultipleNewlinesRegex = new Regex(@"(\r\n|\n|\r){3,}", RegexOptions.Compiled);
    private static readonly Regex HtmlBreaksRegex = new Regex(@"<(?:br|p|div|tr|li|/tr|/p|/div|/li)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TableCellRegex = new Regex(@"<(?:td|th)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string CleanAndNormalizeText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        // 1. Remove BBCode tags
        var cleaned = BbCodeRegex.Replace(rawText, " ");

        // 2. Replace HTML line breaks/cells with spaces and newlines
        cleaned = TableCellRegex.Replace(cleaned, "  ");
        cleaned = HtmlBreaksRegex.Replace(cleaned, "\n");

        // 3. Strip any remaining HTML tags safely
        if (cleaned.Contains('<') && cleaned.Contains('>'))
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(cleaned);
            cleaned = doc.DocumentNode.InnerText;
        }

        // 4. Decode HTML entities
        cleaned = WebUtility.HtmlDecode(cleaned);

        // 5. Normalize whitespace
        cleaned = MultipleSpacesRegex.Replace(cleaned, " ");
        cleaned = MultipleNewlinesRegex.Replace(cleaned, "\n\n");

        return cleaned.Trim();
    }

    public List<string> ExtractClauses(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        var normalized = CleanAndNormalizeText(text);
        var lines = normalized.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);

        var clauses = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // If line contains multiple semicolon-separated items, split them
            if (trimmed.Contains(';') && trimmed.Length > 80)
            {
                var subParts = trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var sub in subParts)
                {
                    var tSub = sub.Trim();
                    if (!string.IsNullOrWhiteSpace(tSub))
                        clauses.Add(tSub);
                }
            }
            else
            {
                clauses.Add(trimmed);
            }
        }

        const string marker = "\uE000";
        return clauses.Select(c => Regex.Replace(c, @"\b(?:Fak|Prog|Prof|Doç|Dr|Üniv|No|Md|vb|vs)\.",
                m => m.Value[..^1] + marker, RegexOptions.IgnoreCase))
            .SelectMany(c => Regex.Split(c, @"(?<!\d)\.(?:\s+|$)|;"))
            .Select(c => c.Replace(marker, "."))
            .Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
    }
}
