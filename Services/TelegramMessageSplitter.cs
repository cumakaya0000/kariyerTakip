using System.Net;
using System.Text;
using HtmlAgilityPack;

namespace KariyerTakip.Services;

public record TelegramChunk(string Text, string? ParseMode);

public static class TelegramMessageSplitter
{
    public static List<TelegramChunk> Split(string html, int limit = 4000)
    {
        if (limit < 2) throw new ArgumentOutOfRangeException(nameof(limit));
        if (html.Length <= limit) return new() { new(html, "HTML") };
        // Long messages use plain text; a split cannot break HTML tags or entities.
        // Preserve link targets when removing markup.
        var document = new HtmlAgilityPack.HtmlDocument();
        document.LoadHtml(html);
        foreach (var link in document.DocumentNode.SelectNodes("//a[@href]") ?? new HtmlNodeCollection(null))
            link.ParentNode?.ReplaceChild(document.CreateTextNode(link.InnerText + " (" + WebUtility.HtmlEncode(WebUtility.HtmlDecode(link.GetAttributeValue("href", ""))) + ")"), link);
        var text = WebUtility.HtmlDecode(document.DocumentNode.InnerText);
        var result = new List<TelegramChunk>();
        var current = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (current.Length + rune.Utf16SequenceLength > limit)
            {
                result.Add(new(current.ToString(), null));
                current.Clear();
            }
            current.Append(rune.ToString());
        }
        if (current.Length > 0) result.Add(new(current.ToString(), null));
        return result;
    }
}
