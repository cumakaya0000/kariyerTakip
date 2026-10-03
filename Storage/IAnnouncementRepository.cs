using KariyerTakip.Models;

namespace KariyerTakip.Storage;

public interface IAnnouncementRepository
{
    Task InitializeDatabaseAsync();
    Task<List<AnnouncementRecord>> GetAllAnnouncementsAsync();
    Task<AnnouncementRecord?> GetAnnouncementByGuidAsync(string guid);
    Task UpsertAnnouncementAsync(AnnouncementRecord record);
    Task SavePositionsAsync(string announcementGuid, List<PositionRecord> positions);
    Task<List<PositionRecord>> GetPositionsByAnnouncementGuidAsync(string announcementGuid);
    Task SaveEvaluationAsync(EvaluationRecord evaluation);
    Task<List<EvaluationRecord>> GetEvaluationsByAnnouncementGuidAsync(string announcementGuid);
    Task QueueNotificationAsync(OutboxNotificationRecord notification);
    Task<List<OutboxNotificationRecord>> GetPendingNotificationsAsync(int limit = 20);
    Task MarkNotificationSentAsync(long id);
    Task MarkNotificationFailedAsync(long id, string errorMessage);
    Task<long> RecordScanStartAsync();
    Task RecordScanEndAsync(long scanId, bool success, int totalFound, int eligibleCount, int needsReviewCount, string? errorMessage);
    Task<bool> HasNotificationBeenSentAsync(string announcementGuid, string notificationType);
}
