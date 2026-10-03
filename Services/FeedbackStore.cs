using System.Text.Json;
using KariyerTakip.Common;
using KariyerTakip.Models;

namespace KariyerTakip.Services;

public sealed class FeedbackStore(string? directory = null)
{
    public async Task<string> SaveAsync(AnnouncementRecord announcement, List<PositionRecord> positions,
        string actualStatus, string expectedStatus, string note)
    {
        var path = Path.Combine(directory ?? Path.Combine(AppPaths.BaseDirectory, "feedback-fixtures"), $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        // Export public posting content, not the user's profile, token, notes or score values.
        var content = new
        {
            CapturedAtUtc = DateTime.UtcNow, announcement.Guid, announcement.InstitutionName, announcement.Title,
            announcement.DetailUrl, announcement.RawGeneralText, ActualStatus = actualStatus, ExpectedStatus = expectedStatus, Note = note,
            Positions = positions.Select(p => new { p.PositionKey, p.Title, p.Unvan, p.RawText, p.Cities, p.Quota })
        };
        await AtomicFile.WriteAsync(path, JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}
