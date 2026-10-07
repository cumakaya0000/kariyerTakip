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
            await repository.UpsertAnnouncementAsync(record);
            await repository.UpsertPositionsAsync(record.Guid, Positions.Where(p => p.AnnouncementGuid == record.Guid).ToList());
            await repository.SaveApplicationTrackingAsync(record.Guid, record.ApplicationStatus, record.ApplicationNotes);
        }
        foreach (var evaluation in Evaluations) await repository.SaveEvaluationAsync(evaluation);
    }
}
