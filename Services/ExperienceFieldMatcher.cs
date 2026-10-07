using System.Globalization;
using System.Text.RegularExpressions;

namespace KariyerTakip.Services;

public static class ExperienceFieldMatcher
{
    private static readonly (string Alias, string Canonical)[] Aliases = {
        ("bilişim teknolojileri", "bilişim"), ("bilgi teknolojileri", "bilişim"), ("bilgi işlem", "bilişim"),
        ("information technology", "bilişim"), ("bt", "bilişim"), ("it", "bilişim"),
        ("software development", "yazılım geliştirme"), ("yazılım geliştiriciliği", "yazılım geliştirme"),
        ("muhasebecilik", "muhasebe")
    };
    public static bool Matches(string required, string actual)
    {
        var expected = Tokens(required); var known = Tokens(actual);
        return expected.Count > 0 && expected.All(known.Contains);
    }
    private static HashSet<string> Tokens(string text)
    {
        var normalized = text.ToLower(new CultureInfo("tr-TR"));
        foreach (var pair in Aliases)
            normalized = Regex.Replace(normalized, @"(?<!\p{L})" + Regex.Escape(pair.Alias) + @"(?!\p{L})", pair.Canonical);
        var stop = new HashSet<string> { "alan", "alanı", "alanında", "sektör", "sektörü", "sektöründe", "konusunda", "işleri", "işlerinde", "deneyim", "tecrübe", "mesleki", "yıl", "ay", "bir", "en", "az" };
        return Regex.Matches(normalized, @"\p{L}+").Select(m => m.Value).Where(v => !stop.Contains(v)).ToHashSet();
    }
}
