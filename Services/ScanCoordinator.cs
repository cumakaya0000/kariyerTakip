using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KariyerTakip.Models;
using KariyerTakip.Storage;
using KariyerTakip.Common;

namespace KariyerTakip.Services;

public class ScanRunResult
{
    public ScanStatus Status { get; set; }
    public int TotalFound { get; set; }
    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }
    public int EligibleCount { get; set; }
    public int NeedsReviewCount { get; set; }
    public int ParseErrorCount { get; set; }
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
    private readonly DeadlineReminderService _reminders;
    private readonly ApiHealthMonitor? _health;
    private readonly KamuIlanClient? _kamuClient;

    public ScanCoordinator(
        CareerGateClient client,
        IAnnouncementRepository repository,
        EligibilityEvaluator evaluator,
        ChangeDetector changeDetector,
        TelegramNotifier telegramNotifier,
        NotificationDispatcher dispatcher,
        IOptions<AppConfig> config,
        IOptions<ProfileOptions> profile,
        ILogger<ScanCoordinator> logger,
        DeadlineReminderService reminders,
        ApiHealthMonitor? health = null,
        KamuIlanClient? kamuClient = null)
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
        _reminders = reminders;
        _health = health;
        _kamuClient = kamuClient;
    }

    public async Task<ScanRunResult> RunScanAsync(CancellationToken cancellationToken = default, ProfileOptions? profile = null)
    {
        if (cancellationToken.IsCancellationRequested) return new ScanRunResult { Status = ScanStatus.Cancelled };
        if (!await _scanLock.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Zaten devam eden bir tarama işlemi bulunmaktadır.");
            return new ScanRunResult { Status = ScanStatus.Running, ErrorMessage = "Devam eden başka bir tarama mevcut." };
        }

        var result = new ScanRunResult { Status = ScanStatus.Running };
        long? scanId = null;
        var scanProfile = JsonSerializer.Deserialize<ProfileOptions>(JsonSerializer.Serialize(profile ?? _profile))!;

        try
        {
            _logger.LogInformation("==================================================");
            _logger.LogInformation("🚀 KariyerTakip İlan Taraması Başlatılıyor...");
            _logger.LogInformation("Profil: {EducationLevel} - {Department} | KPSS: {KpssCount} puan kayıtlı",
                _profile.EducationLevel, _profile.Department, _profile.KpssScores.Count);
            _logger.LogInformation("==================================================");

            await _repository.InitializeDatabaseAsync();
            await _repository.MarkExpiredAnnouncementsAsync();
            scanId = await _repository.RecordScanStartAsync();

            var activeListResult = await _client.GetActiveAnnouncementsAsync(_config.Scan.SearchKeyword, cancellationToken);
            var kamuListResult = _kamuClient == null ? null :
                await _kamuClient.GetActiveAnnouncementsAsync(_config.Scan.SearchKeyword, cancellationToken);
            if (!activeListResult.IsSuccess)
            {
                var errMsg = activeListResult.ErrorMessage ?? "İlan listesi alınamadı.";
                _logger.LogError("Tarama başlatılamadı: {Error}", errMsg);
                result.ErrorMessage = errMsg;
                result.FailedCount++;
                if (_health != null)
                {
                    await _health.ObserveAsync(activeListResult.Status == ApiCallStatus.EmptyResponse, activeListResult.Status == ApiCallStatus.ParseError, false);
                }
            }

            if (kamuListResult != null && !kamuListResult.IsSuccess)
            {
                result.FailedCount++;
                result.ErrorMessage = string.Join(" | ", new[] { result.ErrorMessage, kamuListResult.ErrorMessage }.Where(x => !string.IsNullOrEmpty(x)));
                _logger.LogWarning("Kamu İlan taranamadı: {Error}. Önbellek korunuyor.", kamuListResult.ErrorMessage);
            }
            if (!activeListResult.IsSuccess && (kamuListResult == null || !kamuListResult.IsSuccess))
            {
                result.Status = ScanStatus.Failed;
                await _dispatcher.ProcessOutboxAsync(cancellationToken);
                return result;
            }

            var announcements = activeListResult.Data ?? new List<SearchIlanItem>();
            if (kamuListResult?.Data != null) announcements.AddRange(kamuListResult.Data);
            _logger.LogInformation("Kaynaklar: Kariyer Kapısı {CareerCount}, Kamu İlan (SBB) {KamuCount}",
                announcements.Count(x => x.Source == AnnouncementSource.CareerGate), announcements.Count(x => x.Source == AnnouncementSource.KamuIlan));
            result.TotalFound = announcements.Count;
            var profileHash = _changeDetector.ComputeHash(JsonSerializer.Serialize(scanProfile));

            var aggregateLock = new object();
            var careerParseErrors = 0;
            var careerFailures = 0;
            await Parallel.ForEachAsync(announcements, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(_config.Scan.MaxConcurrency, 1, 8),
                CancellationToken = cancellationToken
            }, async (item, token) =>
            {
                ScanRunResult itemResult;
                try { itemResult = await ProcessAnnouncementAsync(item, scanProfile, profileHash, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "İlan işlenemedi: {Guid}", item.Guid);
                    itemResult = new ScanRunResult { FailedCount = 1 };
                }
                lock (aggregateLock)
                {
                    result.ProcessedCount += itemResult.ProcessedCount;
                    result.FailedCount += itemResult.FailedCount;
                    result.EligibleCount += itemResult.EligibleCount;
                    result.NeedsReviewCount += itemResult.NeedsReviewCount;
                    result.ParseErrorCount += itemResult.ParseErrorCount;
                    if (item.Source == AnnouncementSource.CareerGate)
                    {
                        careerParseErrors += itemResult.ParseErrorCount;
                        careerFailures += itemResult.FailedCount;
                    }
                }
            });
            // 4. Dispatch pending notifications
            if (_health != null && activeListResult.IsSuccess) await _health.ObserveAsync(
                announcements.All(x => x.Source != AnnouncementSource.CareerGate), careerParseErrors > 0,
                announcements.Any(x => x.Source == AnnouncementSource.CareerGate) && careerParseErrors == 0 && careerFailures == 0);
            await _reminders.QueueAsync(scanProfile, cancellationToken);
            await _dispatcher.ProcessOutboxAsync(cancellationToken);

            if (result.Status != ScanStatus.Cancelled)
                result.Status = result.FailedCount > 0 ? ScanStatus.Partial : ScanStatus.Success;
            _logger.LogInformation("==================================================");
            _logger.LogInformation("🏁 Tarama Tamamlandı. Durum: {Status} | Toplam: {Total} | İşlenen: {Proc} | Uygun: {Eligible} | Kontrol Gerekli: {Review}",
                result.Status, result.TotalFound, result.ProcessedCount, result.EligibleCount, result.NeedsReviewCount);
            _logger.LogInformation("==================================================");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.Status = ScanStatus.Cancelled;
        }
        catch (Exception ex)
        {
            result.Status = ScanStatus.Failed;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Tarama yürütülürken hata meydana geldi.");
        }
        finally
        {
            try
            {
                if (scanId.HasValue)
                    await _repository.RecordScanEndAsync(scanId.Value, result.Status, result.TotalFound,
                        result.ProcessedCount, result.FailedCount, result.EligibleCount, result.NeedsReviewCount, result.ErrorMessage);
            }
            catch (Exception ex) { _logger.LogError(ex, "Tarama sonucu kaydedilemedi."); }
            finally { _scanLock.Release(); }
        }

        return result;
    }

    private async Task<ScanRunResult> ProcessAnnouncementAsync(SearchIlanItem item, ProfileOptions profile, string profileHash, CancellationToken cancellationToken)
    {
        var result = new ScanRunResult();
        _logger.LogInformation("🔍 İlan inceleniyor: [{Guid}] {Kurum} - {Baslik}",
            item.Guid, item.KurumAdi, item.IlanBaslik);

        var existing = await _repository.GetAnnouncementByGuidAsync(item.Guid);
        var previousEvals = await _repository.GetLatestEvaluationsByAnnouncementAsync(item.Guid, profileHash);
        var wasPreviouslyEligible = previousEvals.Any(e => e.Status == EligibilityStatus.Eligible.ToString());

        // 1. Fetch preview
        var isKamu = item.Source == AnnouncementSource.KamuIlan;
        var documentResult = isKamu && _kamuClient != null ? await _kamuClient.GetAnnouncementDocumentAsync(item, cancellationToken) : null;
        var previewResult = isKamu
            ? documentResult?.IsSuccess == true
                ? ApiResult<IlanPreviewResponse>.Ok(new IlanPreviewResponse {
                    IlanMetni = documentResult.Data!.FullText, BasTarih = documentResult.Data.StartDate,
                    BitTarih = documentResult.Data.EndDate, BasvuruLinki = documentResult.Data.ApplicationUrl })
                : ApiResult<IlanPreviewResponse>.Fail(documentResult?.Status ?? ApiCallStatus.ParseError, documentResult?.ErrorMessage ?? "PDF okunamadı.")
            : await _client.GetAnnouncementPreviewAsync(item.Guid, cancellationToken);
        var generalText = previewResult.Data?.IlanMetni ?? existing?.RawGeneralText ?? (isKamu ? documentResult?.ErrorMessage ?? "PDF okunamadı." : string.Empty);
        var conditionsText = isKamu ? documentResult?.Data?.GeneralConditionsText ?? existing?.GeneralConditionsText ?? "" : generalText;

        // 2. Fetch positions
        var posResult = isKamu
            ? documentResult?.IsSuccess == true
                ? ApiResult<List<AltIlanResponse>>.Ok(documentResult.Data!.Positions)
                : ApiResult<List<AltIlanResponse>>.Fail(documentResult?.Status ?? ApiCallStatus.ParseError, documentResult?.ErrorMessage ?? "PDF okunamadı.")
            : await _client.GetPositionsAsync(item.Guid, cancellationToken);

        // If fetching failed completely on network error, do not delete existing positions!
        var positionsToUse = new List<AltIlanResponse>();
        List<PositionRecord>? cachedDbPositions = null;
        if (posResult.IsSuccess && posResult.Data != null)
        {
            positionsToUse = posResult.Data;
        }
        else if (existing != null)
        {
            // Fallback to cached positions from DB
            cachedDbPositions = await _repository.GetPositionsByAnnouncementGuidAsync(item.Guid);
            positionsToUse = cachedDbPositions.Select(PositionIdentity.FromCache).ToList();
            _logger.LogWarning("Kadro kaynağı okunamadı, mevcut önbellekteki kadrolar korundu: {Guid}", item.Guid);
        }
        if (isKamu && positionsToUse.Count == 0)
        {
            cachedDbPositions = null;
            positionsToUse.Add(new AltIlanResponse { IlanBaslik = item.IlanBaslik,
                IlanMetni = PdfAnnouncementReader.ReviewMarker + "\n" + (documentResult?.ErrorMessage ?? "PDF kadroları okunamadı.") });
        }
        if (isKamu && documentResult?.IsSuccess != true)
            _logger.LogWarning("Kamu İlan PDF okunamadı: {Title}. {Reason}", item.IlanBaslik, documentResult?.ErrorMessage);

        if (!previewResult.IsSuccess || !posResult.IsSuccess)
        {
            result.FailedCount++;
        }
        if (previewResult.Status == ApiCallStatus.ParseError || posResult.Status == ApiCallStatus.ParseError) result.ParseErrorCount++;

        if (!isKamu && !previewResult.IsSuccess && !posResult.IsSuccess && existing == null)
        {
            return result;
        }

        result.ProcessedCount++;

        var detailUrl = isKamu ? documentResult?.Data?.PdfUrl ?? item.DetailUrl : $"{_config.PortalBaseUrl.TrimEnd('/')}/IlanDetay?i={item.Guid}";
        var applicationUrl = !string.IsNullOrWhiteSpace(previewResult.Data?.EDevletServisURL)
            ? previewResult.Data.EDevletServisURL
            : (!string.IsNullOrWhiteSpace(previewResult.Data?.BasvuruLinki) ? previewResult.Data.BasvuruLinki :
                (!string.IsNullOrWhiteSpace(item.BasvuruLinki) ? item.BasvuruLinki : existing?.ApplicationUrl));

        var rawCombinedContent = $"{generalText}\n" + string.Join("\n", positionsToUse.Select(p => JsonSerializer.Serialize(p)).OrderBy(p => p, StringComparer.Ordinal));
        var contentHash = _changeDetector.ComputeHash(rawCombinedContent);

        var record = new AnnouncementRecord
        {
            Source = item.Source,
            Guid = item.Guid,
            InstitutionName = item.KurumAdi,
            UnitName = item.BirimAdi,
            Title = item.IlanBaslik,
            AnnouncementType = item.IlanTuru,
            DetailUrl = detailUrl,
            ApplicationUrl = applicationUrl ?? string.Empty,
            StartDate = AppTime.ToUtc(isKamu
                ? previewResult.Data?.BasTarih ?? existing?.StartDate ?? item.BasTarih
                : item.BasTarih ?? previewResult.Data?.BasTarih ?? existing?.StartDate),
            EndDate = AppTime.ToUtc(isKamu
                ? previewResult.Data?.BitTarih ?? existing?.EndDate ?? item.BitTarih
                : item.BitTarih ?? previewResult.Data?.BitTarih ?? existing?.EndDate),
            RawGeneralText = generalText,
            GeneralConditionsText = conditionsText,
            RawContentHash = contentHash,
            FirstSeenAt = existing?.FirstSeenAt ?? DateTime.UtcNow,
            LastCheckedAt = DateTime.UtcNow,
            IsActive = true,
            LastScanStatus = previewResult.IsSuccess && posResult.IsSuccess ? "Success" : "Partial"
        };

        await _repository.UpsertAnnouncementAsync(record);

        // Build stable position records
        var previousPositions = cachedDbPositions ?? await _repository.GetPositionsByAnnouncementGuidAsync(item.Guid);
        var dbPositions = cachedDbPositions ?? PositionIdentity.Build(item.Guid, positionsToUse, previousPositions);

        await _repository.UpsertPositionsAsync(item.Guid, dbPositions);

        // 3. Evaluate positions against user profile
        var evaluatedPositions = new List<PositionEvaluation>();
        for (int i = 0; i < positionsToUse.Count; i++)
        {
            var p = positionsToUse[i];
            var posKey = dbPositions[i].PositionKey;

            var eval = isKamu ? EvaluateKamuPosition(p, conditionsText, profile, posKey, record.EndDate, !posResult.IsSuccess) :
                _evaluator.EvaluatePosition(p, generalText, profile, posKey, record.EndDate);
            eval.Cities = dbPositions[i].Cities;
            eval.TotalQuota = dbPositions[i].Quota;
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

            var eventVersion = _changeDetector.ComputeHash($"{contentHash}|{record.EndDate:o}|{profileHash}")[..20];
            var dedupKey = _changeDetector.GenerateDeduplicationKey(item.Guid, "all", notificationType, eventVersion);
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
                var altIlan = PositionIdentity.FromCache(pos);

                var eval = ann.Source == AnnouncementSource.KamuIlan ? EvaluateKamuPosition(altIlan, ann.GeneralConditionsText, profile, pos.PositionKey, ann.EndDate, ann.LastScanStatus != "Success") :
                    _evaluator.EvaluatePosition(altIlan, ann.RawGeneralText, profile, pos.PositionKey, ann.EndDate);
                eval.Cities = pos.Cities;
                eval.TotalQuota = pos.Quota;

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

    private PositionEvaluation EvaluateKamuPosition(AltIlanResponse position, string generalText, ProfileOptions profile, string key, DateTime? endDate, bool stale)
    {
        var evaluation = _evaluator.EvaluatePosition(position, generalText, profile, key, endDate);
        if (stale || position.IlanMetni?.Contains(PdfAnnouncementReader.ReviewMarker) == true || position.IlanMetni == AnnouncementSources.KamuReviewReason)
        {
            evaluation.Status = EligibilityStatus.NeedsReview;
            evaluation.SummaryReason = stale ? "PDF güncel taramada okunamadı; mevcut belge/kadro bilgilerini resmî ilanla kontrol edin." :
                "PDF metni okundu; kadro sınırları veya özel şartlar için belge kontrolü gerekiyor.";
            evaluation.Conditions.Add(new ConditionEvaluation {
                CriterionName = "PDF belgesi / kadro ayrımı", Status = ConditionStatus.Unknown,
                Explanation = evaluation.SummaryReason
            });
        }
        return evaluation;
    }
}
