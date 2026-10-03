using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KariyerTakip.Models;
using KariyerTakip.Storage;

namespace KariyerTakip.Services;

public class ScanCoordinator
{
    private readonly CareerGateClient _client;
    private readonly IAnnouncementRepository _repository;
    private readonly EligibilityEvaluator _evaluator;
    private readonly ChangeDetector _changeDetector;
    private readonly TelegramNotifier _telegramNotifier;
    private readonly NotificationDispatcher _dispatcher;
    private readonly AppConfig _config;
    private readonly ProfileOptions _profile;
    private readonly ILogger<ScanCoordinator> _logger;

    public ScanCoordinator(
        CareerGateClient client,
        IAnnouncementRepository repository,
        EligibilityEvaluator evaluator,
        ChangeDetector changeDetector,
        TelegramNotifier telegramNotifier,
        NotificationDispatcher dispatcher,
        IOptions<AppConfig> config,
        IOptions<ProfileOptions> profile,
        ILogger<ScanCoordinator> logger)
    {
        _client = client;
        _repository = repository;
        _evaluator = evaluator;
        _changeDetector = changeDetector;
        _telegramNotifier = telegramNotifier;
        _dispatcher = dispatcher;
        _config = config.Value;
        _profile = profile.Value;
        _logger = logger;
    }

    public async Task RunScanAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("==================================================");
        _logger.LogInformation("🚀 KariyerTakip İlan Taraması Başlatılıyor...");
        _logger.LogInformation("Profil: {EducationLevel} - {Department} | KPSS: {KpssCount} puan kayıtlı",
            _profile.EducationLevel, _profile.Department, _profile.KpssScores.Count);
        _logger.LogInformation("==================================================");

        await _repository.InitializeDatabaseAsync();
        var scanId = await _repository.RecordScanStartAsync();

        var totalFound = 0;
        var eligibleCount = 0;
        var needsReviewCount = 0;
        string? scanError = null;

        try
        {
            var announcements = await _client.GetActiveAnnouncementsAsync(_config.Scan.SearchKeyword, cancellationToken);
            totalFound = announcements.Count;

            foreach (var item in announcements)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                _logger.LogInformation("🔍 İlan inceleniyor: [{Guid}] {Kurum} - {Baslik}",
                    item.Guid, item.KurumAdi, item.IlanBaslik);

                // Polite request delay
                if (_config.Scan.RequestDelayMs > 0)
                {
                    await Task.Delay(_config.Scan.RequestDelayMs, cancellationToken);
                }

                // 1. Fetch details
                var preview = await _client.GetAnnouncementPreviewAsync(item.Guid, cancellationToken);
                var positions = await _client.GetPositionsAsync(item.Guid, cancellationToken);

                var generalText = preview?.IlanMetni ?? string.Empty;
                var rawCombinedContent = $"{generalText}\n" + string.Join("\n", positions.Select(p => $"{p.IlanBaslik} {p.IlanMetni}"));
                var contentHash = _changeDetector.ComputeHash(rawCombinedContent);

                var detailUrl = $"{_config.PortalBaseUrl.TrimEnd('/')}/IlanDetay?i={item.Guid}";
                var applicationUrl = !string.IsNullOrWhiteSpace(preview?.EDevletServisURL)
                    ? preview.EDevletServisURL
                    : (!string.IsNullOrWhiteSpace(preview?.BasvuruLinki) ? preview.BasvuruLinki : item.BasvuruLinki);

                var record = new AnnouncementRecord
                {
                    Guid = item.Guid,
                    InstitutionName = item.KurumAdi,
                    UnitName = item.BirimAdi,
                    Title = item.IlanBaslik,
                    AnnouncementType = item.IlanTuru,
                    DetailUrl = detailUrl,
                    ApplicationUrl = applicationUrl,
                    StartDate = item.BasTarih ?? preview?.BasTarih,
                    EndDate = item.BitTarih ?? preview?.BitTarih,
                    RawContentHash = contentHash,
                    FirstSeenAt = DateTime.UtcNow,
                    LastCheckedAt = DateTime.UtcNow,
                    IsActive = true
                };

                var existing = await _repository.GetAnnouncementByGuidAsync(item.Guid);
                var changeType = _changeDetector.DetectChanges(existing, record, contentHash);

                // Save announcement
                await _repository.UpsertAnnouncementAsync(record);

                // Save positions
                var dbPositions = positions.Select(p => new PositionRecord
                {
                    AnnouncementGuid = item.Guid,
                    Title = p.IlanBaslik ?? "Kadro",
                    Unvan = p.Unvan ?? string.Empty,
                    Cities = p.KontenjanList != null ? string.Join(", ", p.KontenjanList.Select(k => $"{k.Il} ({k.Kontenjan})")) : string.Empty,
                    Quota = p.KontenjanList?.Sum(k => k.Kontenjan) ?? 0,
                    RawText = p.IlanMetni ?? string.Empty
                }).ToList();
                await _repository.SavePositionsAsync(item.Guid, dbPositions);

                // 2. Evaluate positions
                var evaluatedPositions = new List<PositionEvaluation>();
                foreach (var pos in positions)
                {
                    var eval = _evaluator.EvaluatePosition(pos, generalText, _profile);
                    evaluatedPositions.Add(eval);

                    // Save evaluation
                    await _repository.SaveEvaluationAsync(new EvaluationRecord
                    {
                        AnnouncementGuid = item.Guid,
                        ProfileHash = _changeDetector.ComputeHash(JsonSerializer.Serialize(_profile)),
                        Status = eval.Status.ToString(),
                        SummaryReason = eval.SummaryReason,
                        DetailsJson = JsonSerializer.Serialize(eval),
                        EvaluatedAt = DateTime.UtcNow
                    });
                }

                // Filter matching positions for notifications
                var matchingPositions = evaluatedPositions
                    .Where(p => p.Status == EligibilityStatus.Eligible ||
                                (_config.Scan.IncludeNeedsReview && p.Status == EligibilityStatus.NeedsReview))
                    .ToList();

                if (matchingPositions.Any())
                {
                    var isAnyEligible = matchingPositions.Any(p => p.Status == EligibilityStatus.Eligible);
                    if (isAnyEligible)
                        eligibleCount++;
                    else
                        needsReviewCount++;

                    _logger.LogInformation("🎯 UYGUN / DİKKAT ÇEKEN İLAN BULUNDU! ({Count} kadro)", matchingPositions.Count);

                    var notificationType = changeType == ChangeType.DeadlineChanged ? "Updated" : "New";
                    var alreadySent = await _repository.HasNotificationBeenSentAsync(item.Guid, notificationType);

                    if (!alreadySent)
                    {
                        var header = changeType == ChangeType.DeadlineChanged
                            ? "🔄 <b>İLAN GÜNCELLENDİ (Son Tarih Değişti)</b>"
                            : (isAnyEligible ? "📢 <b>YENİ UYGUN KAMU İLANI</b>" : "⚠️ <b>YENİ İLAN (Kontrol Gerekli)</b>");

                        var message = _telegramNotifier.FormatAnnouncementMessage(record, matchingPositions, header);

                        await _repository.QueueNotificationAsync(new OutboxNotificationRecord
                        {
                            AnnouncementGuid = item.Guid,
                            NotificationType = notificationType,
                            MessagePayload = message,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            // 3. Dispatch notifications
            await _dispatcher.ProcessOutboxAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            scanError = ex.Message;
            _logger.LogError(ex, "Tarama yürütülürken hata meydana geldi.");
        }
        finally
        {
            await _repository.RecordScanEndAsync(
                scanId,
                string.IsNullOrEmpty(scanError),
                totalFound,
                eligibleCount,
                needsReviewCount,
                scanError);

            _logger.LogInformation("==================================================");
            _logger.LogInformation("🏁 Tarama Tamamlandı. Toplam İlan: {Total} | Uygun: {Eligible} | Kontrol Gerekli: {Review}",
                totalFound, eligibleCount, needsReviewCount);
            _logger.LogInformation("==================================================");
        }
    }
}
