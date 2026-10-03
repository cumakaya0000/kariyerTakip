namespace KariyerTakip.Models;

public class AnnouncementRecord
{
    public string Guid { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string AnnouncementType { get; set; } = string.Empty;
    public string DetailUrl { get; set; } = string.Empty;
    public string ApplicationUrl { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string RawContentHash { get; set; } = string.Empty;
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

public class PositionRecord
{
    public long Id { get; set; }
    public string AnnouncementGuid { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Unvan { get; set; } = string.Empty;
    public string Cities { get; set; } = string.Empty;
    public int Quota { get; set; }
    public string RawText { get; set; } = string.Empty;
}

public class EvaluationRecord
{
    public long Id { get; set; }
    public string AnnouncementGuid { get; set; } = string.Empty;
    public long? PositionId { get; set; }
    public string ProfileHash { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SummaryReason { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = string.Empty;
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
}

public class OutboxNotificationRecord
{
    public long Id { get; set; }
    public string AnnouncementGuid { get; set; } = string.Empty;
    public string NotificationType { get; set; } = "New"; // New, Updated, Reminder
    public string MessagePayload { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Sent, Failed
    public int RetryCount { get; set; } = 0;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}

public class ScanRunRecord
{
    public long Id { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public bool Success { get; set; }
    public int TotalAnnouncementsFound { get; set; }
    public int EligibleCount { get; set; }
    public int NeedsReviewCount { get; set; }
    public string? ErrorMessage { get; set; }
}
