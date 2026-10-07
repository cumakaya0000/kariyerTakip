using KariyerTakip.Models;

namespace KariyerTakip.Services;

public sealed record ContentDifference(string Scope, string OldValue, string NewValue);

public static class AnnouncementDiff
{
    public static List<ContentDifference> Build(AnnouncementRecord previous, AnnouncementRecord current,
        List<PositionRecord> oldPositions, List<PositionRecord> newPositions)
    {
        var changes = new List<ContentDifference>();
        void Add(string scope, string before, string after)
        {
            if (before != after) changes.Add(new(scope, before, after));
        }
        Add("Genel şartlar", previous.RawGeneralText, current.RawGeneralText);
        Add("Başlık", previous.Title, current.Title);
        foreach (var key in oldPositions.Select(p => p.PositionKey).Union(newPositions.Select(p => p.PositionKey)))
        {
            var oldPos = oldPositions.FirstOrDefault(p => p.PositionKey == key);
            var newPos = newPositions.FirstOrDefault(p => p.PositionKey == key);
            Add(newPos?.Title ?? oldPos!.Title, oldPos?.RawText ?? "", newPos?.RawText ?? "");
            Add((newPos?.Title ?? oldPos!.Title) + " — kontenjan", oldPos?.Cities ?? "", newPos?.Cities ?? "");
        }
        return changes;
    }
    public static string Summary(IEnumerable<ContentDifference> changes)
    {
        var reader = new DocumentReader();
        var lines = new List<string>();
        foreach (var change in changes.Take(3))
        {
            var oldClauses = reader.ExtractClauses(change.OldValue);
            var newClauses = reader.ExtractClauses(change.NewValue);
            var removed = string.Join("; ", oldClauses.Except(newClauses));
            var added = string.Join("; ", newClauses.Except(oldClauses));
            static string Trim(string value) => value.Length > 350 ? value[..350] + "…" : value;
            lines.Add($"{change.Scope}:\nÖnce: {Trim(removed)}\nŞimdi: {Trim(added)}");
        }
        return string.Join("\n\n", lines);
    }
}
