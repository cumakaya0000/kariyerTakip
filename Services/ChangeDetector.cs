using System.Security.Cryptography;
using System.Text;
using KariyerTakip.Models;

namespace KariyerTakip.Services;

public enum ChangeType
{
    None,
    NewAnnouncement,
    DeadlineChanged,
    ContentChanged,
    NewlyEligible
}

public class ChangeDetector
{
    public string ComputeHash(string content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes);
    }

    public string BuildPositionContentHash(PositionRecord position, string generalText)
    {
        var sb = new StringBuilder();
        sb.AppendLine(position.Title);
        sb.AppendLine(position.Unvan);
        sb.AppendLine(position.Cities);
        sb.AppendLine(position.Quota.ToString());
        sb.AppendLine(position.RawText);
        sb.AppendLine(generalText);
        return ComputeHash(sb.ToString());
    }

    public string GenerateDeduplicationKey(string announcementGuid, string positionKey, string eventType, string eventVersionHash)
    {
        return $"{announcementGuid}:{positionKey}:{eventType}:{eventVersionHash}";
    }

    public ChangeType DetectChanges(
        AnnouncementRecord? existingRecord,
        AnnouncementRecord currentRecord,
        string currentContentHash,
        bool wasPreviouslyEligible,
        bool isCurrentlyEligible)
    {
        if (existingRecord == null)
        {
            return ChangeType.NewAnnouncement;
        }

        if (existingRecord.EndDate != currentRecord.EndDate)
        {
            return ChangeType.DeadlineChanged;
        }

        if (existingRecord.RawContentHash != currentContentHash)
        {
            return ChangeType.ContentChanged;
        }

        if (!wasPreviouslyEligible && isCurrentlyEligible) return ChangeType.NewlyEligible;

        return ChangeType.None;
    }
}
