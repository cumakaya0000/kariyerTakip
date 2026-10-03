using System.Text.Json;
using KariyerTakip.Common;
using KariyerTakip.Models;
using KariyerTakip.Storage;
using Microsoft.Extensions.Options;

namespace KariyerTakip.Services;

public sealed class ApiHealthState
{
    public int EmptyRuns { get; set; }
    public int ParseFailures { get; set; }
    public string Episode { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class ApiHealthMonitor
{
    private readonly IAnnouncementRepository _repository;
    private readonly AppConfig _config;
    private readonly string _path;
    public ApiHealthMonitor(IAnnouncementRepository repository, IOptions<AppConfig> config, string? path = null)
    { _repository = repository; _config = config.Value; _path = path ?? Path.Combine(AppPaths.BaseDirectory, "api-health.json"); }

    public async Task ObserveAsync(bool emptyList, bool parseFailure, bool healthy)
    {
        var state = JsonRecovery.Read(_path, () => new ApiHealthState());
        state.EmptyRuns = emptyList ? state.EmptyRuns + 1 : 0;
        state.ParseFailures = parseFailure ? state.ParseFailures + 1 : 0;
        if (healthy) state.Episode = Guid.NewGuid().ToString("N");
        var threshold = Math.Max(1, _config.Scan.ApiWarningThreshold);
        if (state.EmptyRuns >= threshold || state.ParseFailures >= threshold)
        {
            await _repository.QueueNotificationAsync(new OutboxNotificationRecord
            {
                DeduplicationKey = $"ApiHealth:{state.Episode}", AnnouncementGuid = "system:api", NotificationType = "ApiHealth",
                MessagePayload = $"⚠️ API değişmiş olabilir: üst üste {threshold} taramada ilan listesi boş veya yanıt ayrıştırılamadı. Arama filtresini ve günlükleri kontrol edin; resmî portalı inceleyin."
            });
        }
        await AtomicFile.WriteAsync(_path, JsonSerializer.Serialize(state));
    }
}
