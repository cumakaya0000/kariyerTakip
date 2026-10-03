using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using KariyerTakip.Common;
using KariyerTakip.Models;
using KariyerTakip.Services;
using KariyerTakip.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KariyerTakip.Tests;

public sealed class ServiceReliabilityTests
{
    [Fact]
    public void ProfileBinding_DoesNotAppendInventedDefaultKpssScore()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"KpssScores\":[{\"ScoreType\":\"P3\",\"ExamYear\":2024,\"Score\":80}]}"));
        var config = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var profile = new ProfileOptions(); config.Bind(profile);
        Assert.Equal("P3", Assert.Single(profile.KpssScores).ScoreType);
        Assert.Empty(new ProfileOptions().KpssScores);
        Assert.Null(profile.BirthDate);
    }
    [Fact]
    public async Task Api_Retries429_RespectsRetryAfter_AndNormalizesPortalDates()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((request, token) =>
        {
            calls++;
            if (calls == 1) { var retry = Response(429, "{}"); retry.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero); return Task.FromResult(retry); }
            return Task.FromResult(Response(200, "{\"searchIlan\":[{\"guid\":\"date\",\"bitTarih\":\"2099-10-03T21:00:00\"}]}"));
        }));
        var client = new CareerGateClient(http, Options.Create(new AppConfig { Scan = new ScanOptions { RequestDelayMs = 0 } }), NullLogger<CareerGateClient>.Instance);
        var result = await client.GetActiveAnnouncementsAsync();
        Assert.True(result.IsSuccess); Assert.Equal(2, calls);
        Assert.Equal(new DateTime(2099, 10, 3, 18, 0, 0, DateTimeKind.Utc), Assert.Single(result.Data!).BitTarih);
    }

    [Theory]
    [InlineData("null", ApiCallStatus.EmptyResponse)]
    [InlineData("bad-json", ApiCallStatus.ParseError)]
    public async Task Api_EmptyAndMalformedResponses_AreNotSuccessful(string body, ApiCallStatus expected)
    {
        using var http = new HttpClient(new Handler((request, token) => Task.FromResult(Response(200, body))));
        var result = await new CareerGateClient(http, Options.Create(new AppConfig()), NullLogger<CareerGateClient>.Instance).GetActiveAnnouncementsAsync();
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Api_Permanent400_IsNotRetried()
    {
        var count = 0;
        using var http = new HttpClient(new Handler((request, token) => { count++; return Task.FromResult(Response(400, "{}")); }));
        var result = await new CareerGateClient(http, Options.Create(new AppConfig()), NullLogger<CareerGateClient>.Instance).GetActiveAnnouncementsAsync();
        Assert.Equal(ApiCallStatus.HttpError, result.Status); Assert.Equal(1, count);
    }

    [Fact]
    public void RetryDelay_IsExponentialWithJitter_AndHonorsDateHeader()
    {
        Assert.InRange(RetryPolicy.GetDelay(1).TotalMilliseconds, 1000, 1250);
        Assert.InRange(RetryPolicy.GetDelay(3).TotalMilliseconds, 4000, 4250);
        var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(42), RetryPolicy.GetDelay(1, new RetryConditionHeaderValue(now.AddSeconds(42)), now));
    }

    [Fact]
    public void LongTelegramMessage_PreservesEmojiEntitiesAndLinks_WithinLimits()
    {
        var content = string.Concat(Enumerable.Repeat("😀<&", 2200));
        var html = "<b>" + content.Replace("&", "&amp;").Replace("<", "&lt;") + "</b><a href=\"https://example.test?a=1&amp;b=2\">Başvur</a>";
        var chunks = TelegramMessageSplitter.Split(html);
        Assert.All(chunks, c => { Assert.InRange(c.Text.Length, 1, 4000); Assert.Null(c.ParseMode); Assert.False(char.IsHighSurrogate(c.Text[^1])); });
        Assert.Equal(content + "Başvur (https://example.test?a=1&b=2)", string.Concat(chunks.Select(c => c.Text)));
        Assert.Equal("HTML", Assert.Single(TelegramMessageSplitter.Split("<b>Kısa</b>")).ParseMode);
    }

    [Fact]
    public async Task Dispatcher_ResumesAfterSuccessfulChunk_WithoutRepeatingFirstChunk()
    {
        var messages = new List<string>();
        var count = 0;
        using var fixture = new RepositoryFixture();
        await fixture.Repository.InitializeDatabaseAsync();
        await fixture.Repository.QueueNotificationAsync(new OutboxNotificationRecord { DeduplicationKey = "resume", AnnouncementGuid = "a", MessagePayload = new string('A', 4000) + new string('B', 4000) + "C" });
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            messages.Add(json.RootElement.GetProperty("text").GetString()!);
            count++;
            if (count == 2) { var failure = Response(500, "{}"); failure.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero); return failure; }
            return Response(200, "{\"ok\":true}");
        }));
        var notifier = new TelegramNotifier(http, TelegramConfig(), NullLogger<TelegramNotifier>.Instance);
        var dispatcher = new NotificationDispatcher(fixture.Repository, notifier, NullLogger<NotificationDispatcher>.Instance);
        await dispatcher.ProcessOutboxAsync();
        Assert.Equal(1, Assert.Single(await fixture.Repository.GetPendingNotificationsAsync()).NextChunkIndex);
        await dispatcher.ProcessOutboxAsync();
        Assert.Equal(new[] { new string('A', 4000), new string('B', 4000), new string('B', 4000), "C" }, messages);
        Assert.Empty(await fixture.Repository.GetPendingNotificationsAsync());
        Assert.True(await fixture.Repository.HasNotificationBeenSentAsync("resume"));
    }

    [Fact]
    public async Task Telegram429_DefersEntireQueue_AndDoesNotLeakToken()
    {
        using var fixture = new RepositoryFixture(); await fixture.Repository.InitializeDatabaseAsync();
        foreach (var key in new[] { "one", "two" }) await fixture.Repository.QueueNotificationAsync(new OutboxNotificationRecord { DeduplicationKey = key, AnnouncementGuid = "a", MessagePayload = "test" });
        using var http = new HttpClient(new Handler((request, token) => Task.FromResult(Response(429, "{\"parameters\":{\"retry_after\":90}}"))));
        var notifier = new TelegramNotifier(http, TelegramConfig(), NullLogger<TelegramNotifier>.Instance);
        var result = await notifier.SendMessageAsync("test");
        Assert.InRange((result.RetryAfterUtc!.Value - DateTime.UtcNow).TotalSeconds, 88, 91);
        Assert.DoesNotContain("test-token", result.ErrorMessage!);
        await new NotificationDispatcher(fixture.Repository, notifier, NullLogger<NotificationDispatcher>.Instance).ProcessOutboxAsync();
        Assert.Empty(await fixture.Repository.GetPendingNotificationsAsync());
    }

    [Fact]
    public async Task TelegramPermanentFailure_IsNotRetriedByDispatcher()
    {
        using var fixture = new RepositoryFixture(); await fixture.Repository.InitializeDatabaseAsync();
        await fixture.Repository.QueueNotificationAsync(new OutboxNotificationRecord { DeduplicationKey = "bad", AnnouncementGuid = "a", MessagePayload = "test" });
        using var http = new HttpClient(new Handler((request, token) => Task.FromResult(Response(401, "bad token"))));
        var notifier = new TelegramNotifier(http, TelegramConfig(), NullLogger<TelegramNotifier>.Instance);
        await new NotificationDispatcher(fixture.Repository, notifier, NullLogger<NotificationDispatcher>.Instance).ProcessOutboxAsync();
        Assert.Empty(await fixture.Repository.GetPendingNotificationsAsync());
        Assert.False(await fixture.Repository.HasNotificationBeenSentAsync("bad"));
    }

    [Fact]
    public void PositionIdentity_IsIndependentOfApiOrder_AndKeepsLegacyKeys()
    {
        var a = new AltIlanResponse { IlanBaslik = "Tekniker", Unvan = "Tekniker", IlanMetni = "A" };
        var b = new AltIlanResponse { IlanBaslik = "Tekniker", Unvan = "Tekniker", IlanMetni = "B" };
        var first = PositionIdentity.Build("guid", new() { a, b });
        var reversed = PositionIdentity.Build("guid", new() { b, a });
        Assert.Equal(first[0].PositionKey, reversed[1].PositionKey);
        Assert.Equal(first[1].PositionKey, reversed[0].PositionKey);
        Assert.NotEqual(first[0].PositionKey, first[1].PositionKey);
        first[0].PositionKey = "old-index-key";
        var matched = PositionIdentity.Build("guid", new() { b, a }, first);
        Assert.Equal("old-index-key", matched[1].PositionKey);
    }

    [Fact]
    public async Task SettingsSave_PreservesLoggingAndCustomSections_AndEncryptsToken()
    {
        using var fixture = new RepositoryFixture();
        var path = Path.Combine(fixture.DirectoryPath, "appsettings.json");
        var secretPath = Path.Combine(fixture.DirectoryPath, "telegram.secret");
        await File.WriteAllTextAsync(path, "{\"Logging\":{\"LogLevel\":{\"Default\":\"Warning\"}},\"Custom\":42}");
        var secret = new SecretStore(secretPath);
        var store = new ConfigurationStore(path, secret);
        await store.SaveAsync(new AppConfig { Telegram = new TelegramOptions { BotToken = "12345:test-token" } });
        var data = await File.ReadAllTextAsync(path);
        using var json = JsonDocument.Parse(data);
        Assert.Equal("Warning", json.RootElement.GetProperty("Logging").GetProperty("LogLevel").GetProperty("Default").GetString());
        Assert.Equal(42, json.RootElement.GetProperty("Custom").GetInt32());
        Assert.DoesNotContain("test-token", data);
        Assert.Equal("12345:test-token", secret.Read());
        Assert.DoesNotContain("test-token", await File.ReadAllTextAsync(secretPath));
        await store.SaveAsync(new AppConfig()); Assert.Equal("", secret.Read());
    }

    [Fact]
    public async Task Bootstrap_MigratesLegacyProfileAndPlaintextToken_WithoutInventingBirthDate()
    {
        using var fixture = new RepositoryFixture();
        var settingsPath = Path.Combine(fixture.DirectoryPath, "appsettings.json");
        var profilePath = Path.Combine(fixture.DirectoryPath, "profile.json");
        await File.WriteAllTextAsync(settingsPath, "{\"Logging\":{\"LogLevel\":{\"Default\":\"Warning\"}},\"KariyerTakip\":{\"Telegram\":{\"BotToken\":\"12345:test-token\"}}}");
        await File.WriteAllTextAsync(profilePath, "{\"Experience\":{\"Years\":2,\"IsKnown\":true},\"OtherConditions\":{\"MilitaryStatus\":\"Tecilli\",\"MaxAge\":35}}");
        await ConfigurationStore.BootstrapAsync(fixture.DirectoryPath, fixture.DirectoryPath);
        await ConfigurationStore.BootstrapAsync(fixture.DirectoryPath, fixture.DirectoryPath);
        var profile = JsonSerializer.Deserialize<ProfileOptions>(await File.ReadAllTextAsync(profilePath))!;
        Assert.Equal(24, profile.Experience.TotalMonths); Assert.Equal("Tecilli", profile.MilitaryStatus); Assert.Null(profile.BirthDate);
        Assert.DoesNotContain("OtherConditions", await File.ReadAllTextAsync(profilePath));
        Assert.DoesNotContain("test-token", await File.ReadAllTextAsync(settingsPath));
        Assert.Contains("Warning", await File.ReadAllTextAsync(settingsPath));
        Assert.Equal("12345:test-token", new SecretStore(Path.Combine(fixture.DirectoryPath, "telegram.secret")).Read());
    }

    [Fact]
    public async Task ProfileCatalog_RoundTripsMultipleProfilesAndScores()
    {
        using var fixture = new RepositoryFixture();
        var store = new ProfileStore(Path.Combine(fixture.DirectoryPath, "profiles.json"), Path.Combine(fixture.DirectoryPath, "profile.json"));
        var catalog = new ProfileCatalog { ActiveName = "Lisans", Profiles = new() {
            new() { Name = "Ön Lisans", Profile = new ProfileOptions() },
            new() { Name = "Lisans", Profile = new ProfileOptions { EducationLevel = "Lisans", KpssScores = new() {
                new() { ScoreType = "P3", ExamYear = 2024, Score = 80 }, new() { ScoreType = "P93", ExamYear = 2022, Score = 75 } } } }
        } };
        await store.SaveAsync(catalog);
        var loaded = store.Load(new ProfileOptions());
        Assert.Equal("Lisans", loaded.ActiveName); Assert.Equal(2, loaded.Profiles.Count);
        Assert.Equal(2, loaded.Profiles.Single(p => p.Name == "Lisans").Profile.KpssScores.Count);
    }

    [Fact]
    public void Paths_RespectExplicitDirectory_AndPortableMarker()
    {
        using var fixture = new RepositoryFixture();
        Assert.Equal(Path.Combine("C:\\AppData", "KariyerTakip"), AppPaths.ResolveBaseDirectory(fixture.DirectoryPath, "C:\\AppData"));
        Assert.Equal(Path.GetFullPath(fixture.DirectoryPath), AppPaths.ResolveBaseDirectory("C:\\App", "C:\\AppData", fixture.DirectoryPath));
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, "portable.flag"), "");
        Assert.Equal(Path.GetFullPath(fixture.DirectoryPath), AppPaths.ResolveBaseDirectory(fixture.DirectoryPath, "C:\\AppData"));
    }

    [Fact]
    public void UtcAndLogRedaction_AreConsistent()
    {
        Assert.Equal(new DateTime(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc), AppTime.ParseUtc("2026-10-03T21:00:00", portalDate: true));
        Assert.Equal("03.10.2026 21:00", AppTime.Format(new DateTime(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("https://api.telegram.org/bot[REDACTED]/sendMessage", FileLoggerProvider.Redact("https://api.telegram.org/bot12345:secret-token/sendMessage"));
    }

    [Fact]
    public async Task FileLogger_WritesHeadlessFailuresWithUtcTimestamp()
    {
        using var fixture = new RepositoryFixture();
        using var provider = new FileLoggerProvider(fixture.DirectoryPath);
        provider.CreateLogger("Scan").LogError("API erişilemiyor");
        var text = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(fixture.DirectoryPath, "*.log")));
        Assert.Contains("[Error] [Scan] API erişilemiyor", text); Assert.Contains("Z", text);
    }

    [Theory]
    [InlineData(ScanStatus.Success, 0)] [InlineData(ScanStatus.Failed, 1)]
    [InlineData(ScanStatus.Partial, 2)] [InlineData(ScanStatus.Cancelled, 3)] [InlineData(ScanStatus.Running, 4)]
    public void HeadlessExitCodes_AreDistinct(ScanStatus status, int code) => Assert.Equal(code, Program.GetExitCode(status));

    [Fact]
    public async Task Tracking_AndReminder_ArePersistedAndDeduplicated()
    {
        using var fixture = new RepositoryFixture(); await fixture.Repository.InitializeDatabaseAsync();
        var profile = new ProfileOptions(); var announcement = new AnnouncementRecord { Guid = "reminder", EndDate = DateTime.UtcNow.AddDays(1) };
        await fixture.Repository.UpsertAnnouncementAsync(announcement);
        await fixture.Repository.UpsertPositionsAsync(announcement.Guid, new() { new() { PositionKey = "pos", Title = "Tekniker" } });
        await fixture.Repository.SaveEvaluationAsync(new EvaluationRecord { AnnouncementGuid = announcement.Guid, PositionKey = "pos",
            ProfileHash = new ChangeDetector().ComputeHash(JsonSerializer.Serialize(profile)), Status = "Eligible",
            DetailsJson = JsonSerializer.Serialize(new PositionEvaluation { PositionKey = "pos", Status = EligibilityStatus.Eligible }) });
        using var http = new HttpClient(new Handler((request, token) => Task.FromResult(Response(200, "{\"ok\":true}"))));
        var options = Options.Create(new AppConfig());
        var reminders = new DeadlineReminderService(fixture.Repository, new TelegramNotifier(http, options, NullLogger<TelegramNotifier>.Instance), options);
        Assert.Equal(1, await reminders.QueueAsync(profile)); Assert.Equal(0, await reminders.QueueAsync(profile));
        await fixture.Repository.SaveApplicationTrackingAsync(announcement.Guid, ApplicationStatus.Applied, "Başvuru no: 42");
        await fixture.Repository.UpsertAnnouncementAsync(announcement); // A scan must never erase user's notes.
        var loaded = await fixture.Repository.GetAnnouncementByGuidAsync(announcement.Guid);
        Assert.Equal(ApplicationStatus.Applied, loaded!.ApplicationStatus); Assert.Equal("Başvuru no: 42", loaded.ApplicationNotes);
        options.Value.Scan.ReminderDays = 2; Assert.Equal(0, await reminders.QueueAsync(profile));
    }

    [Fact]
    public async Task ScanCancellation_IsRecordedAsCancelled()
    {
        using var fixture = new RepositoryFixture(); using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler((request, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(Response(200, "{}")); }));
        var options = Options.Create(new AppConfig { Scan = new ScanOptions { RequestDelayMs = 0 } });
        var notifier = new TelegramNotifier(http, options, NullLogger<TelegramNotifier>.Instance);
        var reader = new DocumentReader();
        var coordinator = new ScanCoordinator(new CareerGateClient(http, options, NullLogger<CareerGateClient>.Instance), fixture.Repository,
            new EligibilityEvaluator(new RequirementExtractor(reader), reader), new ChangeDetector(), notifier,
            new NotificationDispatcher(fixture.Repository, notifier, NullLogger<NotificationDispatcher>.Instance), options,
            Options.Create(new ProfileOptions()), NullLogger<ScanCoordinator>.Instance);
        Assert.Equal(ScanStatus.Cancelled, (await coordinator.RunScanAsync(cancellation.Token)).Status);
        using var conn = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False"); await conn.OpenAsync();
        using var query = conn.CreateCommand(); query.CommandText = "SELECT Status FROM ScanRuns ORDER BY Id DESC LIMIT 1";
        Assert.Equal("Cancelled", await query.ExecuteScalarAsync());
    }

    private static IOptions<AppConfig> TelegramConfig() => Options.Create(new AppConfig { Telegram = new TelegramOptions { Enabled = true, BotToken = "12345:test-token", ChatId = "42" } });
    private static HttpResponseMessage Response(int code, string body) => new((HttpStatusCode)code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => callback(request, token); }
    private sealed class RepositoryFixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "kariyertakip-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DirectoryPath, "test.db");
        public AnnouncementRepository Repository { get; }
        public RepositoryFixture()
        { Directory.CreateDirectory(DirectoryPath); Repository = new AnnouncementRepository(Options.Create(new AppConfig { DatabasePath = DatabasePath }), NullLogger<AnnouncementRepository>.Instance); }
        public void Dispose()
        { using var conn = new SqliteConnection($"Data Source={DatabasePath}"); SqliteConnection.ClearPool(conn); Directory.Delete(DirectoryPath, recursive: true); }
    }
}
