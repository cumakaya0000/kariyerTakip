using System.Net;
using KariyerTakip.Models;
using KariyerTakip.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace KariyerTakip.Tests;

public class PdfAnnouncementTests
{
    internal static byte[] BatmanPdf() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "KamuIlan", "batman-2026-10-05.pdf"));

    [Fact]
    public void RealPdf_SeparatesSevenPositions_AndKeepsRequirementsInsideTheirOwnRow()
    {
        var details = new PdfAnnouncementReader().Read(BatmanPdf(), "Batman personel alımı");
        Assert.Equal(7, details.Positions.Count);
        Assert.Equal(15, details.Positions.Sum(p => p.KontenjanList?.Sum(k => k.Kontenjan) ?? 0));
        var technician = Assert.Single(details.Positions, p => p.IlanBaslik!.StartsWith("2026-03"));
        Assert.Contains("Bilgisayar", technician.IlanMetni);
        Assert.Contains("60", technician.IlanMetni);
        Assert.DoesNotContain("soğutma", technician.IlanMetni);
        Assert.DoesNotContain("asansör", technician.IlanMetni);
        Assert.DoesNotContain("D sınıfı", technician.IlanMetni);
        Assert.DoesNotContain("Radyoloji", technician.IlanMetni);
        Assert.Contains("Askerlik", details.GeneralConditionsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ağız ve Diş Sağlığı önlisans mezunu", details.GeneralConditionsText);
        Assert.Equal(new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc), details.EndDate);
        Assert.Equal("https://kariyerkapisi.gov.tr/isealim", details.ApplicationUrl);
    }

    [Fact]
    public void PdfKpssThreshold_ChangesResult_WithEvidenceFromTheSpecificPosition()
    {
        var details = new PdfAnnouncementReader().Read(BatmanPdf(), "Batman personel alımı");
        var position = Assert.Single(details.Positions, p => p.IlanBaslik!.StartsWith("2026-03"));
        var reader = new DocumentReader();
        var evaluator = new EligibilityEvaluator(new RequirementExtractor(reader), reader);
        var profile = new ProfileOptions { Department = "Bilgisayar Teknolojisi", MilitaryStatus = "Tecilli", KpssStatus = "Var",
            KpssScores = new() { new() { ScoreType = "P93", ExamYear = 2024, Score = 75 } } };
        var passing = evaluator.EvaluatePosition(position, details.GeneralConditionsText, profile);
        Assert.True(passing.Status == EligibilityStatus.Eligible, System.Text.Json.JsonSerializer.Serialize(passing));
        Assert.Contains(passing.Conditions, c => c.CriterionName == "KPSS Puanı" && c.Status == ConditionStatus.Satisfied && c.SourceText.Contains("60"));
        Assert.DoesNotContain(passing.Conditions, c => c.CriterionName.Contains("Ehliyet") && c.Status == ConditionStatus.Unsatisfied);
        profile.KpssScores[0].Score = 59;
        var failing = evaluator.EvaluatePosition(position, details.GeneralConditionsText, profile);
        Assert.Equal(EligibilityStatus.Ineligible, failing.Status);
        Assert.Contains(failing.Conditions, c => c.CriterionName == "KPSS Taban Puanı" && c.Status == ConditionStatus.Unsatisfied);
    }

    [Fact]
    public void HtmlErrorAndCorruptPdf_AreNotTreatedAsReadablePostings()
    {
        var reader = new PdfAnnouncementReader();
        Assert.Throws<FormatException>(() => reader.Read(System.Text.Encoding.UTF8.GetBytes("<html>404</html>"), "İlan"));
        Assert.ThrowsAny<Exception>(() => reader.Read(System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 invalid"), "İlan"));
    }

    [Fact]
    public async Task Client_SendsOfficialReferer_ReadsPdf_AndPropagatesCancellation()
    {
        using var http = new HttpClient(new Handler(request => {
            Assert.Equal(KamuIlanClient.BaseUrl, request.Headers.Referrer!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(BatmanPdf()) };
        }));
        var client = new KamuIlanClient(http, config: Options.Create(new AppConfig { Scan = new ScanOptions { RequestDelayMs = 0 } }));
        var item = new SearchIlanItem { DetailUrl = KamuIlanClient.BaseUrl + "ilanDetay.aspx?kod=test", IlanBaslik = "Batman personel alımı" };
        var result = await client.GetAnnouncementDocumentAsync(item);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(7, result.Data!.Positions.Count);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAnnouncementDocumentAsync(item, new CancellationToken(true)));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
}
