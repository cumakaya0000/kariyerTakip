using Microsoft.Extensions.Logging;
using KariyerTakip.Storage;

namespace KariyerTakip.Services;

public class NotificationDispatcher
{
    private readonly IAnnouncementRepository _repository;
    private readonly TelegramNotifier _notifier;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IAnnouncementRepository repository,
        TelegramNotifier notifier,
        ILogger<NotificationDispatcher> logger)
    {
        _repository = repository;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task ProcessOutboxAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _repository.GetPendingNotificationsAsync(limit: 50);
        if (!pending.Any())
        {
            _logger.LogInformation("Kuyrukta bekleyen bildirim bulunmamaktadır.");
            return;
        }

        _logger.LogInformation("Kuyruktan {Count} bildirim işleniyor...", pending.Count);

        foreach (var item in pending)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var success = await _notifier.SendMessageAsync(item.MessagePayload, cancellationToken);
            if (success)
            {
                await _repository.MarkNotificationSentAsync(item.Id);
            }
            else
            {
                await _repository.MarkNotificationFailedAsync(item.Id, "Telegram gönderim hatası veya ağ sorunu");
            }

            // Small delay to prevent Telegram rate limit (e.g., 30 msg/sec limit)
            await Task.Delay(300, cancellationToken);
        }
    }
}
