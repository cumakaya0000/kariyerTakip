using System.Text.Json;
using KariyerTakip.Models;
using KariyerTakip.Storage;
using Microsoft.Extensions.Options;

namespace KariyerTakip.Services;

public sealed class DeadlineReminderService
{
    private readonly IAnnouncementRepository _repository;
    private readonly TelegramNotifier _notifier;
    private readonly AppConfig _config;
    public DeadlineReminderService(IAnnouncementRepository repository, TelegramNotifier notifier, IOptions<AppConfig> config)
    { _repository = repository; _notifier = notifier; _config = config.Value; }
    public async Task<int> QueueAsync(ProfileOptions profile, CancellationToken cancellationToken = default)
    {
        if (_config.Scan.ReminderDays <= 0) return 0;
        var profileHash = new ChangeDetector().ComputeHash(JsonSerializer.Serialize(profile));
        var now = DateTime.UtcNow;
        var queued = 0;
        foreach (var announcement in await _repository.GetAllAnnouncementsAsync(activeOnly: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (announcement.EndDate == null || announcement.EndDate <= now || announcement.EndDate > now.AddDays(_config.Scan.ReminderDays) ||
                announcement.ApplicationStatus is ApplicationStatus.Applied or ApplicationStatus.Skipped) continue;
            var positions = await _repository.GetPositionsByAnnouncementGuidAsync(announcement.Guid);
            var keys = positions.Select(p => p.PositionKey).ToHashSet();
            var evaluations = await _repository.GetLatestEvaluationsByAnnouncementAsync(announcement.Guid, profileHash);
            var matching = new List<PositionEvaluation>();
            foreach (var evaluation in evaluations.Where(e => keys.Contains(e.PositionKey) &&
                (e.Status == "Eligible" || (_config.Scan.IncludeNeedsReview && e.Status == "NeedsReview"))))
            {
                try
                {
                    var details = JsonSerializer.Deserialize<PositionEvaluation>(evaluation.DetailsJson);
                    if (details != null) matching.Add(details);
                }
                catch (JsonException) { }
            }
            if (matching.Count == 0) continue;
            var key = $"{announcement.Guid}:DeadlineReminder:{announcement.EndDate:o}:{_config.Scan.ReminderDays}";
            if (await _repository.QueueNotificationAsync(new OutboxNotificationRecord
            {
                AnnouncementGuid = announcement.Guid, DeduplicationKey = key, NotificationType = "DeadlineReminder",
                MessagePayload = _notifier.FormatAnnouncementMessage(announcement, matching, "⏰ <b>SON BAŞVURU TARİHİ YAKLAŞIYOR</b>")
            })) queued++;
        }
        return queued;
    }
}
