using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KariyerTakip.Models;
using KariyerTakip.Storage;

namespace KariyerTakip.Services;

public class ScanRunResult
{
    public ScanStatus Status { get; set; }
    public int TotalFound { get; set; }
    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }
    public int EligibleCount { get; set; }
    public int NeedsReviewCount { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ScanCoordinator
{
    private static readonly SemaphoreSlim _scanLock = new SemaphoreSlim(1, 1);

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

    public async Task<ScanRunResult> RunScanAsync(CancellationToken cancellationToken = default)
    {
        if (!await _scanLock.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Zaten devam eden bir tarama işlemi bulunmaktadır.");
            return new ScanRunResult { Status = ScanStatus.Running, ErrorMessage = "Devam eden başka bir tarama mevcut." };
        }

        var result = new ScanRunResult { Status = ScanStatus.Running };

        try
        {
            _logger.LogInformation("==================================================");
            _logger.LogInformation("🚀 KariyerTakip İlan Taraması Başlatılıyor...");
            _logger.LogInformation("Profil: {EducationLevel} - {Department} | KPSS: {KpssCount} puan kayıtlı",
                _profile.EducationLevel, _profile.Department, _profile.KpssScores.Count);
            _logger.LogInformation("==================================================");

            await _repository.InitializeDatabaseAsync();
            var scanId = await _repository.RecordScanStartAsync();

            var activeListResult = await _client.GetActiveAnnouncementsAsync(_config.Scan.SearchKeyword, cancellationToken);
            if (!activeListResult.IsSuccess)
            {
                var errMsg = activeListResult.ErrorMessage ?? "İlan listesi alınamadı.";
                _logger.LogError("Tarama başlatılamadı: {Error}", errMsg);
                await _repository.RecordScanEndAsync(scanId, ScanStatus.Failed, 0, 0, 0, 0, 0, errMsg);
                result.Status = ScanStatus.Failed;
                result.ErrorMessage = errMsg;
                return result;
            }

            var announcements = activeListResult.Data ?? new List<SearchIlanItem>();
            result.TotalFound = announcements.Count;
            var profileHash = _changeDetector.ComputeHash(JsonSerializer.Serialize(_profile));

            foreach (var item in announcements)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.Status = ScanStatus.Cancelled;
                    break;
                }

                _logger.LogInformation("🔍 İlan inceleniyor: [{Guid}] {Kurum} - {Baslik}",
                    item.Guid, item.KurumAdi, item.IlanBaslik);

                if (_config.Scan.RequestDelayMs > 0)
                {
                    await Task.Delay(_config.Scan.RequestDelayMs, cancellationToken);
                }

                var existing = await _repository.GetAnnouncementByGuidAsync(item.Guid);

                // 1. Fetch preview
                var previewResult = await _client.GetAnnouncementPreviewAsync(item.Guid, cancellationToken);
                var generalText = previewResult.Data?.IlanMetni ?? existing?.RawGeneralText ?? string.Empty;

                // 2. Fetch positions
                var posResult = await _client.GetPositionsAsync(item.Guid, cancellationToken);

                // If fetching failed completely on network error, do not delete existing positions!
                var positionsToUse = new List<AltIlanResponse>();
                if (posResult.IsSuccess && posResult.Data != null && posResult.Data.Any())
                {
                    positionsToUse = posResult.Data;
                }
                else if (existing != null)
                {
                    // Fallback to cached positions from DB
                    var cachedDbPositions = await _repository.GetPositionsByAnnouncementGuidAsync(item.Guid);
                    positionsToUse = cachedDbPositions.Select(p => new AltIlanResponse
                    {
                        IlanBaslik = p.Title,
                        Unvan = p.Unvan,
                        IlanMetni = p.RawText
                    }).ToList();
                    _logger.LogWarning("Kadro API yanıt vermedi, mevcut önbellekteki kadrolar korundu: {Guid}", item.Guid);
                }

                if (!previewResult.IsSuccess && !posResult.IsSuccess && existing == null)
                {
                    result.FailedCount++;
                    continue;
                }

                result.ProcessedCount++;

                var detailUrl = $"{_config.PortalBaseUrl.TrimEnd('/')}/IlanDetay?i={item.Guid}";
                var applicationUrl = !string.IsNullOrWhiteSpace(previewResult.Data?.EDevletServisURL)
                    ? previewResult.Data.EDevletServisURL
                    : (!string.IsNullOrWhiteSpace(previewResult.Data?.BasvuruLinki) ? previewResult.Data.BasvuruLinki : item.BasvuruLinki);

                var rawCombinedContent = $"{generalText}\n" + string.Join("\n", positionsToUse.Select(p => $"{p.IlanBaslik} {p.Unvan} {p.IlanMetni}"));
                var contentHash = _changeDetector.ComputeHash(rawCombinedContent);

                var record = new AnnouncementRecord
                {
                    Guid = item.Guid,
                    InstitutionName = item.KurumAdi,
                    UnitName = item.BirimAdi,
                    Title = item.IlanBaslik,
                    AnnouncementType = item.IlanTuru,
                    DetailUrl = detailUrl,
                    ApplicationUrl = applicationUrl ?? string.Empty,
                    StartDate = item.BasTarih ?? previewResult.Data?.BasTarih,
                    EndDate = item.BitTarih ?? previewResult.Data?.BitTarih,
                    RawGeneralText = generalText,
                    RawContentHash = contentHash,
                    FirstSeenAt = existing?.FirstSeenAt ?? DateTime.UtcNow,
                    LastCheckedAt = DateTime.UtcNow,
                    IsActive = true,
                    LastScanStatus = "Success"
                };

                await _repository.UpsertAnnouncementAsync(record);

                // Build stable position records
                var dbPositions = new List<PositionRecord>();
                for (int i = 0; i < positionsToUse.Count; i++)
                {
                    var p = positionsToUse[i];
                    var title = p.IlanBaslik ?? "Kadro";
                    var unvan = p.Unvan ?? string.Empty;
                    var keyHash = _changeDetector.ComputeHash($"{title}_{unvan}");
                    var stableKey = $"{item.Guid}_pos_{i}_{keyHash.Substring(0, 8)}";

                    dbPositions.Add(new PositionRecord
                    {
                        PositionKey = stableKey,
                        AnnouncementGuid = item.Guid,
                        Title = title,
                        Unvan = unvan,
                        Cities = p.KontenjanList != null ? string.Join(", ", p.KontenjanList.Select(k => $"{k.Il} ({k.Kontenjan})")) : string.Empty,
                        Quota = p.KontenjanList?.Sum(k => k.Kontenjan) ?? 0,
                        RawText = p.IlanMetni ?? string.Empty,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                await _repository.UpsertPositionsAsync(item.Guid, dbPositions);

                // 3. Evaluate positions against user profile
                var evaluatedPositions = new List<PositionEvaluation>();
                for (int i = 0; i < positionsToUse.Count; i++)
                {
                    var p = positionsToUse[i];
                    var posKey = dbPositions[i].PositionKey;

                    var eval = _evaluator.EvaluatePosition(p, generalText, _profile, posKey);
                    evaluatedPositions.Add(eval);

                    await _repository.SaveEvaluationAsync(new EvaluationRecord
                    {
                        AnnouncementGuid = item.Guid,
                        PositionKey = posKey,
                        ProfileHash = profileHash,
                        Status = eval.Status.ToString(),
                        SummaryReason = eval.SummaryReason,
                        DetailsJson = JsonSerializer.Serialize(eval),
                        EvaluatedAt = DateTime.UtcNow
                    });
                }

                // Check previously eligible state
                var previousEvals = await _repository.GetLatestEvaluationsByAnnouncementAsync(item.Guid, profileHash);
                var wasPreviouslyEligible = previousEvals.Any(e => e.Status == EligibilityStatus.Eligible.ToString());
                var isCurrentlyEligible = evaluatedPositions.Any(e => e.Status == EligibilityStatus.Eligible);

                var changeType = _changeDetector.DetectChanges(existing, record, contentHash, wasPreviouslyEligible, isCurrentlyEligible);

                var matchingPositions = evaluatedPositions
                    .Where(p => p.Status == EligibilityStatus.Eligible ||
                                (_config.Scan.IncludeNeedsReview && p.Status == EligibilityStatus.NeedsReview))
                    .ToList();

                if (matchingPositions.Any())
                {
                    var isAnyEligible = matchingPositions.Any(p => p.Status == EligibilityStatus.Eligible);
                    if (isAnyEligible)
                        result.EligibleCount++;
                    else
                        result.NeedsReviewCount++;

                    _logger.LogInformation("🎯 UYGUN / DİKKAT ÇEKEN İLAN BULUNDU! [{Kurum}] ({Count} kadro)", item.KurumAdi, matchingPositions.Count);

                    var notificationType = changeType switch
                    {
                        ChangeType.DeadlineChanged => "DeadlineChanged",
                        ChangeType.ContentChanged => "ContentChanged",
                        ChangeType.NewlyEligible => "NewlyEligible",
                        _ => "New"
                    };

                    var dedupKey = _changeDetector.GenerateDeduplicationKey(item.Guid, "all", notificationType, contentHash.Substring(0, 12));
                    var alreadySent = await _repository.HasNotificationBeenSentAsync(dedupKey);

                    if (!alreadySent)
                    {
                        var header = notificationType switch
                        {
                            "DeadlineChanged" => "🔄 <b>İLAN GÜNCELLENDİ (Son Başvuru Tarihi Değişti)</b>",
                            "ContentChanged" => "📝 <b>İLAN ŞARTLARI GÜNCELLENDİ</b>",
                            "NewlyEligible" => "⭐ <b>PROFİLİNİZE YENİ UYGUN HALE GELEN İLAN</b>",
                            _ => (isAnyEligible ? "📢 <b>YENİ UYGUN KAMU İLANI</b>" : "⚠️ <b>YENİ İLAN (Kontrol Gerekli)</b>")
                        };

                        var message = _telegramNotifier.FormatAnnouncementMessage(record, matchingPositions, header);

                        await _repository.QueueNotificationAsync(new OutboxNotificationRecord
                        {
                            DeduplicationKey = dedupKey,
                            AnnouncementGuid = item.Guid,
                            NotificationType = notificationType,
                            MessagePayload = message,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            // 4. Dispatch pending notifications
            await _dispatcher.ProcessOutboxAsync(cancellationToken);

            result.Status = result.FailedCount > 0 ? ScanStatus.Partial : ScanStatus.Success;
            await _repository.RecordScanEndAsync(
                scanId,
                result.Status,
                result.TotalFound,
                result.ProcessedCount,
                result.FailedCount,
                result.EligibleCount,
                result.NeedsReviewCount,
                null);

            _logger.LogInformation("==================================================");
            _logger.LogInformation("🏁 Tarama Tamamlandı. Durum: {Status} | Toplam: {Total} | İşlenen: {Proc} | Uygun: {Eligible} | Kontrol Gerekli: {Review}",
                result.Status, result.TotalFound, result.ProcessedCount, result.EligibleCount, result.NeedsReviewCount);
            _logger.LogInformation("==================================================");
        }
        catch (Exception ex)
        {
            result.Status = ScanStatus.Failed;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Tarama yürütülürken hata meydana geldi.");
        }
        finally
        {
            _scanLock.Release();
        }

        return result;
    }

    public async Task ReevaluateCachedAnnouncementsAsync(ProfileOptions profile)
    {
        var announcements = await _repository.GetAllAnnouncementsAsync(activeOnly: true);
        var profileHash = _changeDetector.ComputeHash(JsonSerializer.Serialize(profile));

        _logger.LogInformation("Önbellekteki {Count} aktif ilan yeni profile göre yerel olarak yeniden değerlendiriliyor...", announcements.Count);

        foreach (var ann in announcements)
        {
            var positions = await _repository.GetPositionsByAnnouncementGuidAsync(ann.Guid);
            foreach (var pos in positions)
            {
                var altIlan = new AltIlanResponse
                {
                    IlanBaslik = pos.Title,
                    Unvan = pos.Unvan,
                    IlanMetni = pos.RawText
                };

                var eval = _evaluator.EvaluatePosition(altIlan, ann.RawGeneralText, profile, pos.PositionKey);

                await _repository.SaveEvaluationAsync(new EvaluationRecord
                {
                    AnnouncementGuid = ann.Guid,
                    PositionKey = pos.PositionKey,
                    ProfileHash = profileHash,
                    Status = eval.Status.ToString(),
                    SummaryReason = eval.SummaryReason,
                    DetailsJson = JsonSerializer.Serialize(eval),
                    EvaluatedAt = DateTime.UtcNow
                });
            }
        }
        _logger.LogInformation("Yeniden değerlendirme tamamlandı.");
    }
}
