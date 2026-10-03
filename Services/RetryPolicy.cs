using System.Net.Http.Headers;

namespace KariyerTakip.Services;

public static class RetryPolicy
{
    public static readonly TimeSpan MaximumInlineDelay = TimeSpan.FromSeconds(60);
    public static TimeSpan GetDelay(int attempt, RetryConditionHeaderValue? retryAfter = null, DateTimeOffset? now = null)
    {
        var delay = GetScheduledDelay(attempt, retryAfter, now);
        if (delay > MaximumInlineDelay) throw new RetryDeferredException(delay);
        return delay;
    }
    // Queue scheduling never sleeps; it may preserve a server cooldown longer than the inline limit.
    public static TimeSpan GetScheduledDelay(int attempt, RetryConditionHeaderValue? retryAfter = null, DateTimeOffset? now = null)
    {
        if (retryAfter?.Delta is TimeSpan delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (retryAfter?.Date is DateTimeOffset date) return date > (now ?? DateTimeOffset.UtcNow) ? date - (now ?? DateTimeOffset.UtcNow) : TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(Math.Min(30000, 1000 * Math.Pow(2, attempt - 1)) + Random.Shared.Next(0, 251));
    }
}

public sealed class RetryDeferredException(TimeSpan delay) : Exception("Sunucu 60 saniyeden uzun bekleme istedi; işlem sonraki taramaya bırakıldı.")
{
    public TimeSpan Delay { get; } = delay;
}
