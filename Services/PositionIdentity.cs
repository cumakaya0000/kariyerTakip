using System.Text.Json;
using System.Text.RegularExpressions;
using KariyerTakip.Models;

namespace KariyerTakip.Services;

public static class PositionIdentity
{
    public static List<PositionRecord> Build(string guid, List<AltIlanResponse> positions, List<PositionRecord>? existing = null)
    {
        var detector = new ChangeDetector();
        var usedKeys = new HashSet<string>();
        var result = new PositionRecord[positions.Count];
        var groups = positions.Select((position, index) => new { position, index })
            .GroupBy(x => JsonSerializer.Serialize(new[] { Normalize(x.position.IlanBaslik ?? "Kadro"), Normalize(x.position.Unvan ?? "") }));
        foreach (var group in groups)
        {
            var sorted = group.OrderBy(x => x.position.IlanMetni, StringComparer.Ordinal)
                .ThenBy(x => JsonSerializer.Serialize(x.position.KontenjanList), StringComparer.Ordinal).ToList();
            var baseKey = $"{guid}_pos_{detector.ComputeHash(group.Key)[..20]}";
            var candidates = (existing ?? new()).Where(p =>
                JsonSerializer.Serialize(new[] { Normalize(p.Title), Normalize(p.Unvan) }) == group.Key).OrderBy(p => p.PositionKey).ToList();
            // Match unchanged content first so a changed sibling cannot take its identity.
            var assigned = new Dictionary<int, string>();
            foreach (var entry in sorted)
            {
                var match = candidates.FirstOrDefault(p => !usedKeys.Contains(p.PositionKey) && p.RawText == (entry.position.IlanMetni ?? ""));
                if (match != null) { assigned[entry.index] = match.PositionKey; usedKeys.Add(match.PositionKey); }
            }
            foreach (var entry in sorted)
            {
                var p = entry.position;
                if (!assigned.TryGetValue(entry.index, out var key))
                {
                    key = candidates.FirstOrDefault(c => !usedKeys.Contains(c.PositionKey))?.PositionKey ?? baseKey;
                    var suffix = 1;
                    while (usedKeys.Contains(key)) key = baseKey + "_" + suffix++;
                    usedKeys.Add(key);
                }
                result[entry.index] = new PositionRecord
                {
                    PositionKey = key, AnnouncementGuid = guid, Title = p.IlanBaslik ?? "Kadro", Unvan = p.Unvan ?? "",
                    Cities = p.KontenjanList == null ? "" : string.Join(", ", p.KontenjanList.Select(k => $"{k.Il} ({k.Kontenjan})")),
                    Quota = p.KontenjanList?.Sum(k => k.Kontenjan) ?? 0, RawText = p.IlanMetni ?? "", UpdatedAt = DateTime.UtcNow
                };
            }
        }
        return result.ToList();
    }
    private static string Normalize(string text) => Regex.Replace(text.Trim(), @"\s+", " ").ToUpperInvariant();
    public static AltIlanResponse FromCache(PositionRecord position) => new()
    {
        IlanBaslik = position.Title, Unvan = position.Unvan, IlanMetni = position.RawText,
        KontenjanList = Regex.Matches(position.Cities, @"(?<city>[^,]+?)\s*\((?<quota>\d+)\)(?:,|$)")
            .Select(m => new KontenjanItem { Il = m.Groups["city"].Value.Trim(), Kontenjan = int.Parse(m.Groups["quota"].Value) }).ToList()
    };
}
