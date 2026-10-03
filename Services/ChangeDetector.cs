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

    public ChangeType DetectChanges(AnnouncementRecord? existingRecord, AnnouncementRecord currentRecord, string currentContentHash)
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

        return ChangeType.None;
    }
}
