using System.Net;
using System.Text.Json;
using KariyerTakip.Models;
using KariyerTakip.Services;
using KariyerTakip.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KariyerTakip.Tests;

[Collection("ScanCoordinator")]
public sealed class StorageCorrectionsTests
{
    [Fact]
    public async Task EvaluationRetentionKeepsLatestAndBoundsHistory()
    {
        using var fixture = new Fixture(); await fixture.Repository.InitializeDatabaseAsync();
        for (var i = 0; i < 12; i++) await fixture.Repository.SaveEvaluationAsync(new EvaluationRecord {
            AnnouncementGuid = "a", PositionKey = "p", ProfileHash = "profile" + i, SummaryReason = i.ToString(), Status = "Eligible" });
        using var conn = new SqliteConnection($"Data Source={fixture.Path};Pooling=False"); await conn.OpenAsync();
        using var cmd = conn.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM Evaluations";
        Assert.Equal(3L, await cmd.ExecuteScalarAsync());
        Assert.Equal("11", (await fixture.Repository.GetLatestEvaluationsByAnnouncementAsync("a")).Single().SummaryReason);
    }
    [Fact]
    public async Task RemovedSourceDoesNotDeactivateOtherSourceOrEraseTracking()
    {
        using var fixture = new Fixture(); await fixture.Repository.InitializeDatabaseAsync();
        await fixture.Repository.UpsertAnnouncementAsync(new() { Guid = "a" });
        await fixture.Repository.UpsertAnnouncementAsync(new() { Guid = "sbb:b", Source = AnnouncementSource.KamuIlan });
        await fixture.Repository.SaveApplicationTrackingAsync("a", ApplicationStatus.Planning, "notlar");
        Assert.Single(await fixture.Repository.MarkMissingAnnouncementsAsync(AnnouncementSource.CareerGate, Array.Empty<string>()));
        var removed = await fixture.Repository.GetAnnouncementByGuidAsync("a");
        Assert.False(removed!.IsActive); Assert.Equal("Removed", removed.LastScanStatus); Assert.Equal("notlar", removed.ApplicationNotes);
        Assert.True((await fixture.Repository.GetAnnouncementByGuidAsync("sbb:b"))!.IsActive);
        Assert.Empty(await fixture.Repository.MarkMissingAnnouncementsAsync(AnnouncementSource.CareerGate, Array.Empty<string>()));
    }
    [Fact]
    public async Task QuotasAndDiffSurviveDatabaseRoundTripAndBackup()
    {
        using var fixture = new Fixture(); await fixture.Repository.InitializeDatabaseAsync();
        var json = JsonSerializer.Serialize(new[] { new ContentDifference("Genel", "Önce", "Şimdi") });
        await fixture.Repository.UpsertAnnouncementAsync(new() { Guid = "a", LastContentDiffJson = json });
        var positions = PositionIdentity.Build("a", new() { new() { IlanBaslik = "Kadro", KontenjanList = new() { new() { Il = "İl (Merkez, Kuzey)", Kontenjan = 2 } } } });
        await fixture.Repository.UpsertPositionsAsync("a", positions);
        Assert.Equal("İl (Merkez, Kuzey)", PositionIdentity.FromCache((await fixture.Repository.GetPositionsByAnnouncementGuidAsync("a")).Single()).KontenjanList!.Single().Il);
        var backupPath = fixture.Path + ".backup.db";
        await fixture.Repository.BackupDatabaseAsync(backupPath);
        var backup = new AnnouncementRepository(Options.Create(new AppConfig { DatabasePath = backupPath }), NullLogger<AnnouncementRepository>.Instance);
        await backup.InitializeDatabaseAsync(); Assert.Equal(json, (await backup.GetAnnouncementByGuidAsync("a"))!.LastContentDiffJson);
    }
    [Fact]
    public async Task JsonArchiveRoundTripsTrackingAndPositions()
    {
        using var fixture = new Fixture(); await fixture.Repository.InitializeDatabaseAsync();
        await fixture.Repository.UpsertAnnouncementAsync(new() { Guid = "a", Title = "Test" });
        await fixture.Repository.UpsertPositionsAsync("a", new() { new() { PositionKey = "p", Title = "Kadro" } });
        await fixture.Repository.SaveApplicationTrackingAsync("a", ApplicationStatus.Planning, "Başvuru notu");
        var archive = AnnouncementArchive.Parse(await AnnouncementArchive.ExportAsync(fixture.Repository));
        using var target = new Fixture(); await target.Repository.InitializeDatabaseAsync();
        await archive.MergeAsync(target.Repository);
        Assert.Equal("Başvuru notu", (await target.Repository.GetAnnouncementByGuidAsync("a"))!.ApplicationNotes);
        Assert.Single(await target.Repository.GetPositionsByAnnouncementGuidAsync("a"));
        Assert.Throws<FormatException>(() => AnnouncementArchive.Parse("{\"Version\":9}"));
    }
    [Fact]
    public void CalendarOnlyExportsPlannedDatedApplicationsAndFoldsUtf8()
    {
        var records = new[] { new AnnouncementRecord { Guid = "plan", ApplicationStatus = ApplicationStatus.Planning, EndDate = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc), Title = new string('Ş', 100), ApplicationNotes = "a,b;\nnot" },
            new AnnouncementRecord { Guid = "skip", ApplicationStatus = ApplicationStatus.Skipped, EndDate = DateTime.UtcNow },
            new AnnouncementRecord { Guid = "unknown", ApplicationStatus = ApplicationStatus.Planning } };
        var calendar = ApplicationCalendarExporter.Build(records);
        Assert.Contains("DTSTART:20261019T100000Z", calendar); Assert.Contains("TRIGGER:-P1D", calendar);
        Assert.DoesNotContain("skip@", calendar); Assert.DoesNotContain("unknown@", calendar);
        Assert.All(calendar.Split("\r\n"), line => Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75));
    }
    [Fact]
    public void ContentDiffIncludesChangedCondition()
    {
        var changes = AnnouncementDiff.Build(new() { RawGeneralText = "KPSS P93 en az 70 puan." }, new() { RawGeneralText = "KPSS P93 en az 75 puan." }, new(), new());
        var summary = AnnouncementDiff.Summary(changes);
        Assert.Contains("70", summary); Assert.Contains("75", summary);
    }
    [Fact]
    public async Task ProfileChangeDoesNotResendIndividualAnnouncementAndReviewCountIgnoresNotificationFilter()
    {
        using var fixture = new Fixture();
        var profile = new ProfileOptions { Department = "Muhasebe" };
        var coordinator = fixture.Coordinator(profile);
        Assert.Equal(1, (await coordinator.RunScanAsync()).EligibleCount);
        profile.CityPreferences.Add("İzmir");
        Assert.Equal(1, (await coordinator.RunScanAsync(profile: profile)).EligibleCount);
        Assert.Equal(1, await fixture.NotificationCountAsync("New"));
        Assert.Equal(0, await fixture.NotificationCountAsync("NewlyEligible"));
        await coordinator.ReevaluateCachedAnnouncementsAsync(profile);
        Assert.Equal(1, await fixture.NotificationCountAsync("ProfileSummary"));
        fixture.PositionText = "Muhasebe ön lisans mezunu olmak.";
        Assert.Equal(1, (await coordinator.RunScanAsync(profile: profile)).NeedsReviewCount);
    }
    [Fact]
    public async Task ExplicitPortalCancellationDeactivatesRecord()
    {
        using var fixture = new Fixture(); var coordinator = fixture.Coordinator(new ProfileOptions { Department = "Muhasebe" });
        await coordinator.RunScanAsync(); fixture.PortalStatus = "İPTAL EDİLDİ";
        var result = await coordinator.RunScanAsync();
        Assert.Equal(0, result.EligibleCount); Assert.False((await fixture.Repository.GetAnnouncementByGuidAsync("a"))!.IsActive);
        Assert.Equal(1, await fixture.NotificationCountAsync("Removed"));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kt-corrections-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "test.db");
        public string PositionText { get; set; } = "Muhasebe ön lisans mezunu olmak. KPSS şartı aranmamaktadır.";
        public string PortalStatus { get; set; } = "";
        public IOptions<AppConfig> Config { get; }
        public AnnouncementRepository Repository { get; }
        private readonly HttpClient _http;
        public Fixture()
        {
            Config = Options.Create(new AppConfig { DatabasePath = Path, Scan = new() { EvaluationHistoryLimit = 3, RequestDelayMs = 0, IncludeNeedsReview = false, ReminderDays = 0 } });
            Repository = new(Config, NullLogger<AnnouncementRepository>.Instance);
            _http = new HttpClient(new Handler(request => new(HttpStatusCode.OK) { Content = new StringContent(
                request.RequestUri!.AbsolutePath.Contains("GetIseAlimPage") ? JsonSerializer.Serialize(new GetIseAlimPageResponse { SearchIlan = new() { new() { Guid = "a", IlanBaslik = "İlan", SonDurumu = PortalStatus } } }) :
                request.RequestUri.AbsolutePath.Contains("Preview") ? "{\"ilanMetni\":\"Genel açıklama\"}" : JsonSerializer.Serialize(new[] { new AltIlanResponse { IlanBaslik = "Muhasebe", IlanMetni = PositionText } })) }));
        }
        public ScanCoordinator Coordinator(ProfileOptions profile)
        {
            var reader = new DocumentReader(); var notifier = new TelegramNotifier(_http, Config, NullLogger<TelegramNotifier>.Instance);
            return new(new CareerGateClient(_http, Config, NullLogger<CareerGateClient>.Instance), Repository,
                new EligibilityEvaluator(new RequirementExtractor(reader), reader), new ChangeDetector(), notifier,
                new NotificationDispatcher(Repository, notifier, NullLogger<NotificationDispatcher>.Instance), Config,
                Options.Create(profile), NullLogger<ScanCoordinator>.Instance, new DeadlineReminderService(Repository, notifier, Config));
        }
        public async Task<long> NotificationCountAsync(string type)
        {
            using var conn = new SqliteConnection($"Data Source={Path};Pooling=False"); await conn.OpenAsync();
            using var cmd = conn.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM NotificationOutbox WHERE NotificationType = @Type";
            cmd.Parameters.AddWithValue("@Type", type); return (long)(await cmd.ExecuteScalarAsync())!;
        }
        public void Dispose() { _http.Dispose(); SqliteConnection.ClearAllPools(); Directory.Delete(_directory, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(handler(request)); }
}
