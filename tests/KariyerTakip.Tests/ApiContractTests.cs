using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using KariyerTakip.Common;
using KariyerTakip.Models;
using KariyerTakip.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KariyerTakip.Tests;

public class ApiContractTests
{
    [Fact]
    public async Task RecordedPublicApiResponses_PreserveIdentityContentQuotaAndDates()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Api", "2026-10-03");
        var announcements = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "announcements.json")))!;
        var original = announcements["searchIlan"]!.AsArray();
        Assert.NotEmpty(original);
        foreach (var entry in original) entry!["bitTarih"] = "2099-12-31T23:59:00"; // Do not let fixture dates expire the contract test.
        using var http = new HttpClient(new Handler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("GetIseAlimPage") ? announcements.ToJsonString() :
                File.ReadAllText(Path.Combine(directory, request.RequestUri.AbsolutePath.Contains("Preview") ? "preview.json" : "positions.json")))
        }));
        var client = Client(http);
        var list = await client.GetActiveAnnouncementsAsync();
        Assert.True(list.IsSuccess, list.ErrorMessage); Assert.Equal(original.Count, list.Data!.Count);
        var item = Assert.Single(list.Data, a => a.Guid == "30329a62-04f3-445a-985f-f6a0b8c2e1b7");
        Assert.Equal("GÖÇ İDARESİ BAŞKANLIĞI", item.KurumAdi);
        var preview = await client.GetAnnouncementPreviewAsync(item.Guid);
        Assert.True(preview.IsSuccess, preview.ErrorMessage);
        Assert.Contains("375", preview.Data!.IlanMetni);
        Assert.Equal(DateTimeKind.Utc, preview.Data.BitTarih!.Value.Kind);
        var positions = await client.GetPositionsAsync(item.Guid);
        Assert.True(positions.IsSuccess, positions.ErrorMessage); Assert.NotEmpty(positions.Data!);
        Assert.All(positions.Data!, p => Assert.False(string.IsNullOrWhiteSpace(p.IlanMetni)));
        Assert.True(positions.Data!.Sum(p => p.KontenjanList?.Sum(k => k.Kontenjan) ?? 0) > 0);
        Assert.Contains("KariyerTakip/1.0", http.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"searchIlan\":null}")]
    [InlineData("{\"searchIlan\":[{\"ilanBaslik\":\"başlık\"}]}")]
    public async Task MissingOrRenamedFields_AreParseErrorsInsteadOfSilentEmptyLists(string json)
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(json) }));
        Assert.Equal(ApiCallStatus.ParseError, (await Client(http).GetActiveAnnouncementsAsync()).Status);
    }
    private static CareerGateClient Client(HttpClient http) => new(http, Options.Create(new AppConfig { Scan = new() { RequestDelayMs = 0 } }), NullLogger<CareerGateClient>.Instance);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request)); }
}
