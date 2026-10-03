using System.Net.Http.Headers;

namespace KariyerTakip.Services;

public static class RetryPolicy
{
    public static TimeSpan GetDelay(int attempt, RetryConditionHeaderValue? retryAfter = null, DateTimeOffset? now = null)
    {
        if (retryAfter?.Delta is TimeSpan delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (retryAfter?.Date is DateTimeOffset date) return date > (now ?? DateTimeOffset.UtcNow) ? date - (now ?? DateTimeOffset.UtcNow) : TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(Math.Min(30000, 1000 * Math.Pow(2, attempt - 1)) + Random.Shared.Next(0, 251));
    }
}
