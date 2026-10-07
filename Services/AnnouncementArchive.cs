using System.Text.Json;
using KariyerTakip.Models;
using KariyerTakip.Storage;

namespace KariyerTakip.Services;

public sealed class AnnouncementArchive
{
    public int Version { get; set; } = 1;
    public List<AnnouncementRecord> Announcements { get; set; } = new();
    public List<PositionRecord> Positions { get; set; } = new();
    public List<EvaluationRecord> Evaluations { get; set; } = new();

    public static async Task<string> ExportAsync(IAnnouncementRepository repository)
    {
        var archive = new AnnouncementArchive { Announcements = await repository.GetAllAnnouncementsAsync() };
        foreach (var record in archive.Announcements)
        {
            archive.Positions.AddRange(await repository.GetPositionsByAnnouncementGuidAsync(record.Guid));
            archive.Evaluations.AddRange(await repository.GetLatestEvaluationsByAnnouncementAsync(record.Guid));
        }
        return JsonSerializer.Serialize(archive, new JsonSerializerOptions { WriteIndented = true });
    }
    public static AnnouncementArchive Parse(string json)
    {
        var data = JsonSerializer.Deserialize<AnnouncementArchive>(json) ?? throw new FormatException("Arşiv boş.");
        if (data.Version != 1 || data.Announcements == null || data.Positions == null || data.Evaluations == null ||
            data.Announcements.Any(a => a == null || string.IsNullOrWhiteSpace(a.Guid) || a.Title == null || a.RawGeneralText == null) ||
            data.Announcements.Select(a => a.Guid).Distinct().Count() != data.Announcements.Count ||
            data.Positions.Any(p => p == null || string.IsNullOrWhiteSpace(p.PositionKey) || p.Title == null || p.RawText == null || !data.Announcements.Any(a => a.Guid == p.AnnouncementGuid)) ||
            data.Positions.Select(p => p.PositionKey).Distinct().Count() != data.Positions.Count ||
            data.Evaluations.Any(e => e == null || !Enum.TryParse<EligibilityStatus>(e.Status, out _) || !data.Positions.Any(p => p.PositionKey == e.PositionKey && p.AnnouncementGuid == e.AnnouncementGuid)))
            throw new FormatException("Arşiv sürümü, kimlikleri veya kadro bağlantıları geçersiz.");
        foreach (var position in data.Positions)
            if (!string.IsNullOrWhiteSpace(position.QuotasJson)) _ = JsonSerializer.Deserialize<List<KontenjanItem>>(position.QuotasJson) ?? throw new FormatException("Kontenjan verisi geçersiz.");
        return data;
    }
    public async Task MergeAsync(IAnnouncementRepository repository)
    {
        var existing = await repository.GetAllAnnouncementsAsync();
        foreach (var announcement in existing)
            foreach (var position in await repository.GetPositionsByAnnouncementGuidAsync(announcement.Guid))
                if (Positions.Any(p => p.PositionKey == position.PositionKey && p.AnnouncementGuid != position.AnnouncementGuid))
                    throw new FormatException("Kadro kimliği başka bir ilana ait.");
        foreach (var record in Announcements)
        {
            var current = existing.FirstOrDefault(a => a.Guid == record.Guid);
            if (current != null && current.LastCheckedAt >= record.LastCheckedAt) continue;
            var oldPositions = current == null ? new List<PositionRecord>() : await repository.GetPositionsByAnnouncementGuidAsync(record.Guid);
            var incomingPositions = Positions.Where(p => p.AnnouncementGuid == record.Guid).ToDictionary(p => p.PositionKey);
            foreach (var position in oldPositions)
                if (incomingPositions.TryGetValue(position.PositionKey, out var incoming) ? position.UpdatedAt >= incoming.UpdatedAt : position.UpdatedAt > record.LastCheckedAt)
                    incomingPositions[position.PositionKey] = position;
            await repository.UpsertAnnouncementAsync(record);
            await repository.UpsertPositionsAsync(record.Guid, incomingPositions.Values.ToList());
            // Archive imports must preserve local application decisions and notes for existing records.
            if (current == null) await repository.SaveApplicationTrackingAsync(record.Guid, record.ApplicationStatus, record.ApplicationNotes);
        }
        foreach (var evaluation in Evaluations)
        {
            var incomingRecord = Announcements.Single(a => a.Guid == evaluation.AnnouncementGuid);
            var current = existing.FirstOrDefault(a => a.Guid == evaluation.AnnouncementGuid);
            if (current != null && current.LastCheckedAt >= incomingRecord.LastCheckedAt) continue;
            var previous = await repository.GetLatestEvaluationsByAnnouncementAsync(evaluation.AnnouncementGuid, evaluation.ProfileHash);
            if (previous.Any(e => e.PositionKey == evaluation.PositionKey && e.EvaluatedAt >= evaluation.EvaluatedAt)) continue;
            var positions = await repository.GetPositionsByAnnouncementGuidAsync(evaluation.AnnouncementGuid);
            var importedPosition = Positions.Single(p => p.PositionKey == evaluation.PositionKey);
            if (positions.Any(p => p.PositionKey == evaluation.PositionKey && p.RawText != importedPosition.RawText)) continue;
            await repository.SaveEvaluationAsync(evaluation);
        }
    }
}
