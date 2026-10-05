using System.Net;
using KariyerTakip.Models;
using KariyerTakip.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace KariyerTakip.Tests;

public class AnnouncementDocumentTests
{
    private const string ListHtml = """
        <ul id="nav2"><li><time><h4>5</h4><h3>Ekim</h3></time>
        <a href="ilanDetay.aspx?kod=fresh"><p class="alt_p1">TEST KURUM</p>
        <p class="alt_p2">PERSONEL ALIMI<em>5 Ekim 2099 - 19 Ekim 2099</em></p></a></li></ul>
        """;

    [Fact]
    public async Task OpeningOldRecord_RefreshesSessionAndCode_ThenReusesTheActualPdfOffline()
    {
        using var directory = new TemporaryDirectory();
        var cache = new AnnouncementPdfCache(directory.Path);
        var item = Assert.Single(KamuIlanClient.ParseAnnouncements(ListHtml, DateTime.UtcNow));
        var record = new AnnouncementRecord { Guid = item.Guid, Source = AnnouncementSource.KamuIlan,
            DetailUrl = KamuIlanClient.BaseUrl + "ilanDetay.aspx?kod=old", Title = item.IlanBaslik, InstitutionName = item.KurumAdi };
        var calls = new List<string>();
        using var http = new HttpClient(new Handler(request => {
            calls.Add(request.RequestUri!.AbsoluteUri);
            Assert.DoesNotContain("kod=old", request.RequestUri.AbsoluteUri);
            Assert.Equal(KamuIlanClient.BaseUrl, request.Headers.Referrer!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri.AbsolutePath == "/"
                ? new StringContent(ListHtml) : new ByteArrayContent(PdfAnnouncementTests.BatmanPdf()) };
        }));
        var client = new KamuIlanClient(http, config: Options.Create(new AppConfig { Scan = new ScanOptions { RequestDelayMs = 0 } }), pdfCache: cache);
        var service = new AnnouncementDocumentService(client, cache);
        var path = await service.GetKamuPdfPathAsync(record);
        Assert.EndsWith(".pdf", path);
        Assert.Equal(PdfAnnouncementTests.BatmanPdf(), File.ReadAllBytes(path));
        Assert.Equal(2, calls.Count);
        Assert.Equal(path, await service.GetKamuPdfPathAsync(record));
        Assert.Equal(2, calls.Count);
        Assert.Equal(path, cache.GetExistingPath(record.Guid));
    }

    [Fact]
    public async Task InvalidResponse_DoesNotOverwriteCachedPdf_OrBecomeAnOpenableHtmlFile()
    {
        using var directory = new TemporaryDirectory();
        var cache = new AnnouncementPdfCache(directory.Path);
        await cache.StoreAsync("../../../outside", PdfAnnouncementTests.BatmanPdf());
        var path = cache.GetExistingPath("../../../outside")!;
        Assert.Equal(System.IO.Path.GetFullPath(directory.Path), System.IO.Path.GetDirectoryName(path));
        await Assert.ThrowsAnyAsync<Exception>(() => cache.StoreAsync("../../../outside", System.Text.Encoding.UTF8.GetBytes("<html>404</html>")));
        Assert.Equal(path, cache.GetExistingPath("../../../outside"));
        Assert.Equal(PdfAnnouncementTests.BatmanPdf(), File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void TelegramSbbLinks_UsePublicList_AndNeverContainSessionBoundCodes()
    {
        using var http = new HttpClient();
        var notifier = new TelegramNotifier(http, Options.Create(new AppConfig()), Microsoft.Extensions.Logging.Abstractions.NullLogger<TelegramNotifier>.Instance);
        var message = notifier.FormatAnnouncementMessage(new AnnouncementRecord {
            Source = AnnouncementSource.KamuIlan, DetailUrl = KamuIlanClient.BaseUrl + "ilanDetay.aspx?kod=stale" }, new());
        Assert.DoesNotContain("kod=stale", message);
        Assert.Contains(KamuIlanClient.BaseUrl + "#section3", message);
        Assert.Contains("PDF", message);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "document-test-" + Guid.NewGuid());
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
