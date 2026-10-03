using System.Net;
using System.Text.RegularExpressions;

namespace KariyerTakip.Services;

public class DocumentReader
{
    private static readonly Regex BbCodeRegex = new Regex(@"\[/?[a-zA-Z0-9_=\.\#\%\:\-\s\""']+\]", RegexOptions.Compiled);
    private static readonly Regex MultipleSpacesRegex = new Regex(@"[ \t]+", RegexOptions.Compiled);
    private static readonly Regex MultipleNewlinesRegex = new Regex(@"(\r\n|\n|\r){3,}", RegexOptions.Compiled);

    public string CleanAndNormalizeText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        // 1. Remove BBCode tags
        var cleaned = BbCodeRegex.Replace(rawText, " ");

        // 2. Parse HTML if any HTML tags remain
        if (cleaned.Contains('<') && cleaned.Contains('>'))
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(cleaned);
            cleaned = doc.DocumentNode.InnerText;
        }

        // 3. Decode HTML entities
        cleaned = WebUtility.HtmlDecode(cleaned);

        // 4. Normalize whitespace
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

            clauses.Add(trimmed);
        }

        return clauses;
    }
}
