using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using KariyerTakip.Forms;
using KariyerTakip.Models;
using KariyerTakip.Services;
using KariyerTakip.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KariyerTakip.Tests;

public class DataFlowTests
{
    [Fact]
    public async Task FailedPositionsRequest_PreservesCachedKeyCityAndQuota()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();

        var result = await fixture.Coordinator.RunScanAsync();

        Assert.Equal(ScanStatus.Partial, result.Status);
        Assert.Equal(1, result.FailedCount);
        var position = Assert.Single(await fixture.Repository.GetPositionsByAnnouncementGuidAsync("test-ann"));
        Assert.Equal("cached-key", position.PositionKey);
        Assert.Equal("ANKARA (3)", position.Cities);
        Assert.Equal(3, position.Quota);
        var announcement = await fixture.Repository.GetAnnouncementByGuidAsync("test-ann");
        Assert.Equal("https://example.test/apply", announcement!.ApplicationUrl);
        Assert.Equal("Partial", announcement.LastScanStatus);
        await AssertEvaluationMetadataAsync(fixture);
    }

    [Fact]
    public async Task LocalReevaluation_PreservesCityAndQuota()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();

        await fixture.Coordinator.ReevaluateCachedAnnouncementsAsync(fixture.Profile.Value);

        await AssertEvaluationMetadataAsync(fixture);
    }

    [Fact]
    public void ReloadAndFilter_PopulateAndClearDetails_UnevaluatedIsNeedsReview()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var fixture = new Fixture();
                fixture.SeedAsync().GetAwaiter().GetResult();
                using var services = new ServiceCollection().BuildServiceProvider();
                using var form = new MainForm(services, fixture.Coordinator, fixture.Repository,
                    fixture.Notifier, new ChangeDetector(), fixture.Profile, fixture.Config,
                    NullLogger<MainForm>.Instance);
                InvokeLoad(form);
                var grid = Field<DataGridView>(form, "_gridAnnouncements");
                Assert.Single(grid.Rows.Cast<DataGridViewRow>());
                Assert.Equal("⚠️ İNCELE", grid.Rows[0].Cells["Status"].Value);
                Assert.Equal("Test Kurum", Field<Label>(form, "_lblDetailInstitution").Text);
                var content = Field<RichTextBox>(form, "_rtbDetailContent");
                Assert.Contains("Henüz değerlendirilmedi", content.Text);
                Assert.Contains("İlan açıklaması", content.Text);
                Assert.DoesNotContain("<p>", content.Text);
                InvokeLoad(form);
                Assert.Equal("Test Kurum", Field<Label>(form, "_lblDetailInstitution").Text);
                Field<TextBox>(form, "_txtSearch").Text = "eşleşmeyen arama";
                Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
                Assert.Equal("Seçili ilan yok", Field<Label>(form, "_lblDetailInstitution").Text);
                Assert.Empty(content.Text);
                Field<TextBox>(form, "_txtSearch").Clear();
                Assert.Equal("Test Kurum", Field<Label>(form, "_lblDetailInstitution").Text);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Form testi zaman aşımına uğradı.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void CheckboxSelection_PreservesChecksAcrossFilteringAndReload_AndBuildsBulkUrls()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var fixture = new Fixture();
                fixture.SeedAsync().GetAwaiter().GetResult();
                fixture.Repository.UpsertAnnouncementAsync(new AnnouncementRecord
                {
                    Guid = "second-ann", InstitutionName = "İkinci Kurum", Title = "İkinci İlan",
                    DetailUrl = "https://example.test/second"
                }).GetAwaiter().GetResult();
                var profileHash = new ChangeDetector().ComputeHash(JsonSerializer.Serialize(fixture.Profile.Value));
                var evaluation = new PositionEvaluation
                {
                    PositionKey = "cached-key", Status = EligibilityStatus.NeedsReview,
                    Conditions = new List<ConditionEvaluation>
                    {
                        new() { CriterionName = "Öğrenim Düzeyi / Bölüm", Status = ConditionStatus.Unknown,
                            Explanation = "Kadro unvanı bilişim alanıyla ilgili. Tam mezuniyet listesi için resmî kılavuz kontrol edilmelidir." },
                        new() { CriterionName = "KPSS Şartı", Status = ConditionStatus.Unknown,
                            Explanation = "İlan metninden KPSS taban puanı çıkarılamadı. Kılavuzdan puan şartı doğrulanmalıdır." },
                        new() { CriterionName = "Mesleki Tecrübe", Status = ConditionStatus.Unknown,
                            Explanation = "İlan 5 yıl mesleki deneyim talep ediyor. Profilinizde deneyim belirtilmediğinden kontrol edilmelidir." }
                    }
                };
                fixture.Repository.SaveEvaluationAsync(new EvaluationRecord
                {
                    AnnouncementGuid = "test-ann", PositionKey = "cached-key", ProfileHash = profileHash,
                    Status = evaluation.Status.ToString(), DetailsJson = JsonSerializer.Serialize(evaluation)
                }).GetAwaiter().GetResult();
                using var services = new ServiceCollection().BuildServiceProvider();
                using var form = new MainForm(services, fixture.Coordinator, fixture.Repository,
                    fixture.Notifier, new ChangeDetector(), fixture.Profile, fixture.Config,
                    NullLogger<MainForm>.Instance);
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.Show();
                InvokeLoad(form);
                var grid = Field<DataGridView>(form, "_gridAnnouncements");
                var button = Field<Button>(form, "_btnOpenSelectedAnnouncements");
                var content = Field<RichTextBox>(form, "_rtbDetailContent");
                Assert.Contains("Öğrenim Düzeyi / Bölüm — Kontrol gerekli", content.Text);
                Assert.Contains("Mesleki Tecrübe — Kontrol gerekli", content.Text);
                Assert.DoesNotContain("NeedsReview", content.Text);
                content.Select(content.Text.IndexOf("Tekniker", StringComparison.Ordinal), "Tekniker".Length);
                Assert.True(content.SelectionFont!.Bold);
                content.Select(0, 0);
                Assert.False(button.Visible);
                Assert.False(grid.Columns["Checked"]!.ReadOnly);
                Assert.True(grid.Columns["Title"]!.ReadOnly);
                grid.Rows[0].Cells["Checked"].Value = true;
                Assert.False(button.Visible);
                grid.Rows[1].Cells["Checked"].Value = true;
                Assert.True(button.Visible);
                var urls = (List<string>)typeof(MainForm).GetMethod("GetCheckedAnnouncementUrls",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null)!;
                Assert.Equal(2, urls.Count);
                Assert.Contains("https://example.test/second", urls);
                Assert.Contains("https://kariyerkapisi.gov.tr/IlanDetay?i=test-ann", urls);
                var previewPath = Environment.GetEnvironmentVariable("KARIYERTAKIP_TEST_PREVIEW_PATH");
                if (!string.IsNullOrWhiteSpace(previewPath))
                {
                    form.Size = new System.Drawing.Size(1400, 900);
                    form.PerformLayout();
                    using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(previewPath);
                    var tabs = Field<TabControl>(form, "_tabControl");
                    foreach (var page in new[] { (Index: 1, Name: "profiller.png"), (Index: 2, Name: "ayarlar.png") })
                    {
                        tabs.SelectedIndex = page.Index;
                        form.PerformLayout();
                        using var pageBitmap = new System.Drawing.Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(pageBitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                        pageBitmap.Save(Path.Combine(Path.GetDirectoryName(previewPath)!, page.Name));
                    }
                    tabs.SelectedIndex = 0;
                }
                Field<TextBox>(form, "_txtSearch").Text = "İkinci";
                Assert.Single(grid.Rows.Cast<DataGridViewRow>());
                Assert.True(button.Visible);
                Assert.Contains("filtre dışında", Field<Label>(form, "_lblCheckedAnnouncements").Text);
                Field<TextBox>(form, "_txtSearch").Clear();
                InvokeLoad(form);
                Assert.All(grid.Rows.Cast<DataGridViewRow>(), row => Assert.Equal(true, row.Cells["Checked"].Value));
                grid.Rows[0].Cells["Checked"].Value = false;
                Assert.False(button.Visible);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Çoklu seçim testi zaman aşımına uğradı.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static T Field<T>(MainForm form, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static void InvokeLoad(MainForm form) =>
        ((Task)typeof(MainForm).GetMethod("LoadAnnouncementsFromDbAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null)!).GetAwaiter().GetResult();

    private static async Task AssertEvaluationMetadataAsync(Fixture fixture)
    {
        var record = Assert.Single(await fixture.Repository.GetLatestEvaluationsByAnnouncementAsync("test-ann"));
        var evaluation = JsonSerializer.Deserialize<PositionEvaluation>(record.DetailsJson)!;
        Assert.Equal("ANKARA (3)", evaluation.Cities);
        Assert.Equal(3, evaluation.TotalQuota);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"kariyertakip-test-{Guid.NewGuid():N}.db");
        private readonly HttpClient _http = new(new ApiHandler());
        public IOptions<AppConfig> Config { get; }
        public IOptions<ProfileOptions> Profile { get; } = Options.Create(new ProfileOptions());
        public AnnouncementRepository Repository { get; }
        public TelegramNotifier Notifier { get; }
        public ScanCoordinator Coordinator { get; }

        public Fixture()
        {
            Config = Options.Create(new AppConfig { DatabasePath = _path,
                Scan = new ScanOptions { RequestDelayMs = 0, IncludeNeedsReview = false } });
            Repository = new AnnouncementRepository(Config, NullLogger<AnnouncementRepository>.Instance);
            Notifier = new TelegramNotifier(_http, Config, NullLogger<TelegramNotifier>.Instance);
            var reader = new DocumentReader();
            Coordinator = new ScanCoordinator(
                new CareerGateClient(_http, Config, NullLogger<CareerGateClient>.Instance), Repository,
                new EligibilityEvaluator(new RequirementExtractor(reader), reader), new ChangeDetector(),
                Notifier, new NotificationDispatcher(Repository, Notifier, NullLogger<NotificationDispatcher>.Instance),
                Config, Profile, NullLogger<ScanCoordinator>.Instance);
        }

        public async Task SeedAsync()
        {
            await Repository.InitializeDatabaseAsync();
            await Repository.UpsertAnnouncementAsync(new AnnouncementRecord { Guid = "test-ann",
                InstitutionName = "Test Kurum", Title = "Test İlan", RawGeneralText = "<p>İlan açıklaması</p>",
                ApplicationUrl = "https://example.test/apply" });
            await Repository.UpsertPositionsAsync("test-ann", new List<PositionRecord> {
                new() { PositionKey = "cached-key", AnnouncementGuid = "test-ann", Title = "Tekniker",
                    Unvan = "Tekniker", Cities = "ANKARA (3)", Quota = 3, RawText = "Ön lisans mezunu olmak." } });
        }

        public void Dispose()
        {
            _http.Dispose();
            using var connection = new SqliteConnection($"Data Source={_path}");
            SqliteConnection.ClearPool(connection);
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
        }
    }

    private sealed class ApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.RequestUri!.AbsolutePath switch
            {
                "/api/ilan/GetIseAlimPage" => "{\"searchIlan\":[{\"guid\":\"test-ann\",\"kurumAdi\":\"Test Kurum\",\"ilanBaslik\":\"Test İlan\"}]}",
                "/api/ilan/GetIlanPreviewPublic" => "{\"ilanMetni\":\"<p>İlan açıklaması</p>\"}",
                _ => "invalid-json"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
