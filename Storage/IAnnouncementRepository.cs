using KariyerTakip.Models;

namespace KariyerTakip.Storage;

public interface IAnnouncementRepository
{
    Task InitializeDatabaseAsync();
    Task<List<AnnouncementRecord>> GetAllAnnouncementsAsync(bool activeOnly = false);
    Task<AnnouncementRecord?> GetAnnouncementByGuidAsync(string guid);
    Task UpsertAnnouncementAsync(AnnouncementRecord record);
    Task UpsertPositionsAsync(string announcementGuid, List<PositionRecord> positions);
    Task<List<PositionRecord>> GetPositionsByAnnouncementGuidAsync(string announcementGuid);
    Task SaveEvaluationAsync(EvaluationRecord evaluation);
    Task<List<EvaluationRecord>> GetLatestEvaluationsByAnnouncementAsync(string announcementGuid, string? profileHash = null);
    Task<Dictionary<string, List<EvaluationRecord>>> GetLatestEvaluationsMapAsync(string? profileHash = null);
    Task<bool> QueueNotificationAsync(OutboxNotificationRecord notification);
    Task<List<OutboxNotificationRecord>> GetPendingNotificationsAsync(int limit = 50);
    Task MarkNotificationSentAsync(long id);
    Task MarkNotificationFailedAsync(long id, string errorMessage, DateTime? retryAfterUtc = null, bool permanent = false);
    Task SaveNotificationProgressAsync(long id, int nextChunkIndex);
    Task DeferPendingNotificationsAsync(DateTime retryAfterUtc);
    Task SaveApplicationTrackingAsync(string guid, ApplicationStatus status, string notes);
    Task MarkExpiredAnnouncementsAsync();
    Task MarkNotificationDisabledAsync(long id);
    Task<bool> HasNotificationBeenSentAsync(string deduplicationKey);
    Task<long> RecordScanStartAsync();
    Task RecordScanEndAsync(long scanId, ScanStatus status, int totalFound, int processedCount, int failedCount, int eligibleCount, int needsReviewCount, string? errorMessage);
    Task<ScanRunRecord?> GetLastSuccessfulScanAsync();
}
