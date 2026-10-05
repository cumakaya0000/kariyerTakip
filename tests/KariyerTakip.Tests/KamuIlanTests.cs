using System.Net;
using KariyerTakip.Common;
using KariyerTakip.Models;
using KariyerTakip.Services;
using KariyerTakip.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KariyerTakip.Tests;

[Collection("ScanCoordinator")]
public class KamuIlanTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    private static string Page(string code = "first", string dates = "5 Ekim - 19 Ekim") => $"""
        <ul id="nav2"><li><time><h4>5</h4><h3>Ekim</h3></time><div>
        <a href="ilanDetay.aspx?kod={code}"><p class="alt_p1">ANKARA ÜNİVERSİTESİ</p>
        <p class="alt_p2">103 SÖZLEŞMELİ PERSONEL ALACAK<em>({dates})</em></p></a>
        </div></li></ul>
        """;

    [Fact]
    public void RealPublicTimeline_ParsesInstitutionsDatesAndOfficialUrls()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "KamuIlan", "2026-10-05.html"));
        var items = KamuIlanClient.ParseAnnouncements(html, Now);
        Assert.True(items.Count > 30);
        Assert.All(items, item => {
            Assert.Equal(AnnouncementSource.KamuIlan, item.Source);
            Assert.StartsWith("sbb:", item.Guid);
            Assert.StartsWith(KamuIlanClient.BaseUrl + "ilanDetay.aspx?kod=", item.DetailUrl);
            Assert.True(item.BitTarih >= Now);
        });
        Assert.Contains(items, x => x.KurumAdi == "ANKARA ÜNİVERSİTESİ" && x.IlanBaslik == "103 SÖZLEŞMELİ PERSONEL ALACAK");
    }

    [Fact]
    public void RotatingCodeAndDeadlineChange_PreserveIdentity_RefreshLink()
    {
        var before = Assert.Single(KamuIlanClient.ParseAnnouncements(Page(), Now));
        var after = Assert.Single(KamuIlanClient.ParseAnnouncements(Page("second", "5 Ekim - 21 Ekim"), Now));
        Assert.Equal(before.Guid, after.Guid);
        Assert.NotEqual(before.DetailUrl, after.DetailUrl);
        Assert.Equal("103 SÖZLEŞMELİ PERSONEL ALACAK", after.IlanBaslik);
        Assert.Equal(new DateTime(2026, 10, 21, 20, 59, 59, DateTimeKind.Utc).AddTicks(9999999), after.BitTarih);
    }

    [Fact]
    public void FiltersExpiredAndSearch_AndHandlesNewYear()
    {
        Assert.Empty(KamuIlanClient.ParseAnnouncements(Page(dates: "1 Ekim - 4 Ekim"), Now));
        Assert.Single(KamuIlanClient.ParseAnnouncements(Page(), Now, "üniversitesi"));
        Assert.Empty(KamuIlanClient.ParseAnnouncements(Page(), Now, "eşleşmeyen"));
        var html = Page(dates: "28 Aralık - 7 Ocak").Replace("<h4>5</h4><h3>Ekim", "<h4>20</h4><h3>Aralık");
        var item = Assert.Single(KamuIlanClient.ParseAnnouncements(html, new DateTime(2027, 1, 2, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(2026, item.BasTarih!.Value.Year);
        Assert.Equal(2027, item.BitTarih!.Value.Year);
    }

    [Fact]
    public void ChangedLayout_IsFailureRatherThanEmptySuccess() =>
        Assert.Throws<FormatException>(() => KamuIlanClient.ParseAnnouncements("<html>error</html>", Now));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SourceFailure_DoesNotStopOtherSource_AndPdfCacheCanBeReevaluated(bool careerFails, bool kamuFails)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "kamu-test-" + Guid.NewGuid() + ".db");
        var config = Options.Create(new AppConfig { DatabasePath = dbPath, Scan = new ScanOptions { RequestDelayMs = 0 } });
        var profile = Options.Create(new ProfileOptions());
        var repository = new AnnouncementRepository(config, NullLogger<AnnouncementRepository>.Instance);
        using var careerHttp = new HttpClient(new Handler(request => careerFails
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"searchIlan\":[]}") }));
        var documentFails = false;
        using var kamuHttp = new HttpClient(new Handler(request => kamuFails || (documentFails && request.RequestUri!.AbsolutePath.Contains("ilanDetay"))
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.Contains("ilanDetay")
                ? new ByteArrayContent(PdfAnnouncementTests.BatmanPdf())
                : new StringContent(Page(dates: "5 Ekim 2099 - 19 Ekim 2099")) }));
        using var telegramHttp = new HttpClient();
        var notifier = new TelegramNotifier(telegramHttp, config, NullLogger<TelegramNotifier>.Instance);
        var detector = new ChangeDetector();
        var reader = new DocumentReader();
        var coordinator = new ScanCoordinator(new CareerGateClient(careerHttp, config, NullLogger<CareerGateClient>.Instance),
            repository, new EligibilityEvaluator(new RequirementExtractor(reader), reader), detector, notifier,
            new NotificationDispatcher(repository, notifier, NullLogger<NotificationDispatcher>.Instance), config, profile,
            NullLogger<ScanCoordinator>.Instance, new DeadlineReminderService(repository, notifier, config),
            kamuClient: new KamuIlanClient(kamuHttp));
        try
        {
            Assert.Equal(ScanStatus.Partial, (await coordinator.RunScanAsync()).Status);
            if (kamuFails) { Assert.Empty(await repository.GetAllAnnouncementsAsync()); return; }
            var announcement = Assert.Single(await repository.GetAllAnnouncementsAsync());
            Assert.Equal(AnnouncementSource.KamuIlan, announcement.Source);
            await repository.SaveApplicationTrackingAsync(announcement.Guid, ApplicationStatus.Planning, "belgeye bak");
            await coordinator.RunScanAsync();
            var saved = Assert.Single(await repository.GetAllAnnouncementsAsync());
            Assert.Equal("belgeye bak", saved.ApplicationNotes);
            Assert.Equal(ApplicationStatus.Planning, saved.ApplicationStatus);
            await coordinator.ReevaluateCachedAnnouncementsAsync(profile.Value);
            var evaluations = await repository.GetLatestEvaluationsByAnnouncementAsync(saved.Guid);
            Assert.Equal(7, evaluations.Count);
            Assert.Contains("KPSS", saved.RawGeneralText);
            Assert.Contains("askerlik", saved.GeneralConditionsText, StringComparison.OrdinalIgnoreCase);
            documentFails = true;
            var failedScan = await coordinator.RunScanAsync();
            Assert.Equal(ScanStatus.Partial, failedScan.Status);
            Assert.Equal(7, (await repository.GetPositionsByAnnouncementGuidAsync(saved.Guid)).Count);
            var afterFailure = await repository.GetAnnouncementByGuidAsync(saved.Guid);
            Assert.Equal(saved.RawGeneralText, afterFailure!.RawGeneralText);
            Assert.Equal(saved.EndDate, afterFailure.EndDate);
            Assert.Equal("belgeye bak", afterFailure.ApplicationNotes);
            Assert.All(await repository.GetLatestEvaluationsByAnnouncementAsync(saved.Guid), e => Assert.Equal("NeedsReview", e.Status));
            var message = notifier.FormatAnnouncementMessage(saved, new());
            Assert.Contains("Kamu İlan (SBB)", message);
            Assert.DoesNotContain("Kariyer Kapısı İlan Detayı", message);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
}
