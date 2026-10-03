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
    public string RawGeneralText { get; set; } = string.Empty;
    public string RawContentHash { get; set; } = string.Empty;
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public string LastScanStatus { get; set; } = "Success";
    public ApplicationStatus ApplicationStatus { get; set; }
    public string ApplicationNotes { get; set; } = "";
}

public enum ApplicationStatus { None, Planning, Applied, Skipped }

public class PositionRecord
{
    public long Id { get; set; }
    public string PositionKey { get; set; } = string.Empty; // Stable unique identifier (e.g. Guid_PosIndex_Hash)
    public string AnnouncementGuid { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Unvan { get; set; } = string.Empty;
    public string Cities { get; set; } = string.Empty;
    public int Quota { get; set; }
    public string RawText { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class EvaluationRecord
{
    public long Id { get; set; }
    public string AnnouncementGuid { get; set; } = string.Empty;
    public string PositionKey { get; set; } = string.Empty;
    public string ProfileHash { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Eligible, NeedsReview, Ineligible
    public string SummaryReason { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = string.Empty;
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
}

public class OutboxNotificationRecord
{
    public long Id { get; set; }
    public string DeduplicationKey { get; set; } = string.Empty; // Unique key to prevent duplicates: {Guid}:{PositionKey}:{EventType}:{Hash}
    public string AnnouncementGuid { get; set; } = string.Empty;
    public string NotificationType { get; set; } = "New"; // New, Updated, DeadlineReminder
    public string MessagePayload { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Sent, Failed, Disabled, Skipped
    public int RetryCount { get; set; } = 0;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public int NextChunkIndex { get; set; }
    public DateTime? RetryAfterUtc { get; set; }
}

public enum ScanStatus
{
    Running,
    Success,
    Partial,
    Failed,
    Cancelled
}

public class ScanRunRecord
{
    public long Id { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public string Status { get; set; } = "Running"; // Success, Partial, Failed, Cancelled
    public int TotalAnnouncementsFound { get; set; }
    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }
    public int EligibleCount { get; set; }
    public int NeedsReviewCount { get; set; }
    public string? ErrorMessage { get; set; }
}

public class AnnouncementDisplayItem
{
    public AnnouncementRecord Record { get; set; } = new();
    public List<EvaluationRecord> Evaluations { get; set; } = new();
    public List<PositionRecord> Positions { get; set; } = new();
    public EligibilityStatus Status { get; set; }
}
