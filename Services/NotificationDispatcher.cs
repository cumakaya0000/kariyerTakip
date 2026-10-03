using Microsoft.Extensions.Logging;
using KariyerTakip.Storage;

namespace KariyerTakip.Services;

public class NotificationDispatcher
{
    private static readonly SemaphoreSlim DispatchLock = new(1, 1);
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
        await DispatchLock.WaitAsync(cancellationToken);
        try
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

            var result = await _notifier.SendMessageAsync(item.MessagePayload, cancellationToken, item.NextChunkIndex,
                next => _repository.SaveNotificationProgressAsync(item.Id, next));

            if (result.Status == TelegramSendStatus.Success)
            {
                await _repository.MarkNotificationSentAsync(item.Id);
            }
            else if (result.Status == TelegramSendStatus.Disabled)
            {
                await _repository.MarkNotificationDisabledAsync(item.Id);
            }
            else
            {
                await _repository.MarkNotificationFailedAsync(item.Id, result.ErrorMessage ?? "Gönderim başarısız",
                    result.RetryAfterUtc ?? DateTime.UtcNow.Add(RetryPolicy.GetDelay(item.RetryCount + 1)),
                    result.Status == TelegramSendStatus.PermanentFailure);
                if (result.StatusCode == 429)
                {
                    await _repository.DeferPendingNotificationsAsync(result.RetryAfterUtc ?? DateTime.UtcNow.AddMinutes(1));
                    break;
                }
            }

            // Small delay to prevent Telegram rate limit (e.g., 30 msg/sec limit)
            await Task.Delay(300, cancellationToken);
        }
        }
        finally { DispatchLock.Release(); }
    }
}
