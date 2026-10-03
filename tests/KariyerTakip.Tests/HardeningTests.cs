using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using KariyerTakip.Common;
using KariyerTakip.Models;
using KariyerTakip.Services;
using KariyerTakip.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using Xunit;

namespace KariyerTakip.Tests;

public class HardeningTests
{
    [Fact]
    public async Task Bootstrap_RecoversInvalidJsonAndWrongTypes_PreservingExactBackups()
    {
        using var temp = new Temp();
        var settings = Path.Combine(temp.Path, "appsettings.json");
        var profile = Path.Combine(temp.Path, "profile.json");
        await File.WriteAllTextAsync(settings, "{broken");
        await File.WriteAllTextAsync(profile, "{\"Experience\":null}");
        await ConfigurationStore.BootstrapAsync(temp.Path, temp.Path);
        Assert.Equal("{broken", await File.ReadAllTextAsync(settings + ".bak"));
        Assert.Equal("{\"Experience\":null}", await File.ReadAllTextAsync(profile + ".bak"));
        Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(await File.ReadAllTextAsync(settings)).RootElement.GetProperty("KariyerTakip").ValueKind);
        Assert.True(ConfigurationStore.ProfileIsValid(JsonSerializer.Deserialize<ProfileOptions>(await File.ReadAllTextAsync(profile))));
        await ConfigurationStore.BootstrapAsync(temp.Path, temp.Path);
        Assert.Single(Directory.GetFiles(temp.Path, "appsettings.json.bak*"));
    }

    [Fact]
    public async Task ProfileCatalog_RecoversAndDeduplicatesCaseInsensitiveNames()
    {
        using var temp = new Temp();
        var path = Path.Combine(temp.Path, "profiles.json");
        await File.WriteAllTextAsync(path, "not-json");
        var store = new ProfileStore(path, Path.Combine(temp.Path, "profile.json"));
        Assert.Single(store.Load(new ProfileOptions()).Profiles);
        Assert.Equal("not-json", File.ReadAllText(path + ".bak"));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new ProfileCatalog
        {
            ActiveName = "ana", Profiles = new() { new() { Name = "Ana" }, new() { Name = "ANA" } }
        }));
        var catalog = store.Load(new ProfileOptions());
        Assert.Equal("Ana", catalog.ActiveName); Assert.Single(catalog.Profiles);
        await store.SaveAsync(catalog);
    }

    [Theory]
    [InlineData("invalid-base64")]
    [InlineData("AQIDBA==")]
    public async Task UnreadableSecret_ReturnsEmptyWithoutThrowing(string data)
    {
        using var temp = new Temp();
        var path = Path.Combine(temp.Path, "telegram.secret");
        await File.WriteAllTextAsync(path, data);
        Assert.Equal("", new SecretStore(path).Read());
        Assert.Equal(data, await File.ReadAllTextAsync(path)); // User can retry with the correct account.
    }

    [Fact]
    public void Mutex_ExcludesAnotherProcessAndReleasesOnDispose()
    {
        var name = "Local\\KariyerTakip-Test-" + Guid.NewGuid().ToString("N");
        using (var gate = new InstanceGate(name))
        {
            Assert.True(gate.TryAcquire());
            // The child is an actual second process, not a second mutex on the owner thread.
            var code = "$m=[Threading.Mutex]::new($false,'" + name + "'); if($m.WaitOne(0)){$m.ReleaseMutex();$m.Dispose();exit 0};$m.Dispose();exit 4";
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command"); start.ArgumentList.Add(code);
            using var process = Process.Start(start)!;
            Assert.True(process.WaitForExit(15000)); Assert.Equal(4, process.ExitCode);
        }
        using var next = new InstanceGate(name); Assert.True(next.TryAcquire());
    }

    [Fact]
    public void PortalZone_UsesWindowsThenFixedOffsetFallback()
    {
        var calls = new List<string>();
        var zone = AppTime.ResolvePortalTimeZone(id =>
        {
            calls.Add(id);
            if (id == "Europe/Istanbul") throw new TimeZoneNotFoundException();
            return TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.FromHours(3), id, id);
        });
        Assert.Equal(new[] { "Europe/Istanbul", "Turkey Standard Time" }, calls);
        Assert.Equal(TimeSpan.FromHours(3), zone.BaseUtcOffset);
        Assert.Equal(TimeSpan.FromHours(3), AppTime.ResolvePortalTimeZone(_ => throw new TimeZoneNotFoundException()).BaseUtcOffset);
    }

    [Fact]
    public async Task LongRetryAfter_DefersWithoutWaitingOrRetrying()
    {
        Assert.Throws<RetryDeferredException>(() => RetryPolicy.GetDelay(1, new RetryConditionHeaderValue(TimeSpan.FromHours(1))));
        Assert.Equal(TimeSpan.FromSeconds(60), RetryPolicy.GetDelay(1, new RetryConditionHeaderValue(TimeSpan.FromSeconds(60))));
        var calls = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1)); return response;
        }));
        var result = await Client(http).GetActiveAnnouncementsAsync();
        Assert.Equal(ApiCallStatus.HttpError, result.Status); Assert.Equal(1, calls);
        Assert.Contains("sonraki taramaya", result.ErrorMessage);
    }

    [Fact]
    public async Task HealthCounter_PersistsWarnsOnceAndResetsAfterRecovery()
    {
        using var temp = new Temp();
        var options = Options.Create(new AppConfig { DatabasePath = Path.Combine(temp.Path, "test.db") });
        var repo = new AnnouncementRepository(options, NullLogger<AnnouncementRepository>.Instance);
        await repo.InitializeDatabaseAsync();
        var state = Path.Combine(temp.Path, "health.json");
        for (int i = 0; i < 4; i++) await new ApiHealthMonitor(repo, options, state).ObserveAsync(true, false, false);
        Assert.Single(await repo.GetPendingNotificationsAsync());
        await new ApiHealthMonitor(repo, options, state).ObserveAsync(false, false, true);
        for (int i = 0; i < 3; i++) await new ApiHealthMonitor(repo, options, state).ObserveAsync(false, true, false);
        Assert.Equal(2, (await repo.GetPendingNotificationsAsync()).Count);
    }

    [Fact]
    public async Task SqliteConnections_EnableBusyTimeoutAndDatabaseUsesWal()
    {
        using var temp = new Temp();
        var repo = new AnnouncementRepository(Options.Create(new AppConfig { DatabasePath = Path.Combine(temp.Path, "test.db") }), NullLogger<AnnouncementRepository>.Instance);
        await repo.InitializeDatabaseAsync();
        using var connection = (SqliteConnection)typeof(AnnouncementRepository).GetMethod("CreateConnection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(repo, null)!;
        await connection.OpenAsync();
        using var query = connection.CreateCommand(); query.CommandText = "PRAGMA busy_timeout";
        Assert.Equal(5000L, await query.ExecuteScalarAsync()); query.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", await query.ExecuteScalarAsync());
    }

    [Fact]
    public async Task FeedbackExport_ContainsPostingButNotPrivateApplicationNotes()
    {
        using var temp = new Temp();
        var path = await new FeedbackStore(temp.Path).SaveAsync(new AnnouncementRecord
        { Guid = "public", RawGeneralText = "İlan metni", ApplicationNotes = "PRIVATE-NOTES" }, new(), "Eligible", "Ineligible", "Mezuniyet yanlış");
        var text = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("PRIVATE-NOTES", text);
        Assert.Equal("Ineligible", JsonDocument.Parse(text).RootElement.GetProperty("ExpectedStatus").GetString());
    }

    [Fact]
    public void Startup_OnlyChangesSpecifiedRegistryValue_AndQuotesPaths()
    {
        using var key = Registry.CurrentUser.CreateSubKey("Software\\KariyerTakipTests\\" + Guid.NewGuid().ToString("N"));
        key.SetValue("Unrelated", "keep");
        WindowsStartup.ApplyTo(key, true, @"C:\My App\dotnet.exe", @"C:\My App");
        Assert.Equal("\"C:\\My App\\dotnet.exe\" \"C:\\My App\\KariyerTakip.dll\"", key.GetValue("KariyerTakip"));
        WindowsStartup.ApplyTo(key, false, "unused", "unused");
        Assert.Null(key.GetValue("KariyerTakip")); Assert.Equal("keep", key.GetValue("Unrelated"));
        var keyPath = key.Name["HKEY_CURRENT_USER\\".Length..]; key.Close(); Registry.CurrentUser.DeleteSubKey(keyPath);
    }

    private static CareerGateClient Client(HttpClient http) => new(http, Options.Create(new AppConfig { Scan = new() { RequestDelayMs = 0 } }), NullLogger<CareerGateClient>.Instance);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request)); }
    private sealed class Temp : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kariyertakip-hardening-" + Guid.NewGuid().ToString("N"));
        public Temp() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            foreach (var database in Directory.GetFiles(Path, "*.db"))
            {
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database }.ToString());
                SqliteConnection.ClearPool(connection);
            }
            Directory.Delete(Path, true);
        }
    }
}
