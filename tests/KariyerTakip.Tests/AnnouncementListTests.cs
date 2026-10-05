using KariyerTakip.Models;
using KariyerTakip.Services;
using Xunit;

namespace KariyerTakip.Tests;

public class AnnouncementListTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    private static AnnouncementDisplayItem Item(string id, DateTime? deadline, ApplicationStatus status = ApplicationStatus.None) => new()
    {
        Record = new() { Guid = id, Title = "İlan " + id, InstitutionName = "İZMİR", EndDate = deadline, ApplicationStatus = status },
        Status = EligibilityStatus.Eligible, Positions = new() { new() { Title = "Programcı", Cities = "Ankara" } }
    };

    [Fact]
    public void DatesSortChronologically_InBothDirections_AndUnknownDeadlinesStayLast()
    {
        var items = new[] { Item("nov", Now.AddMonths(1)), Item("unknown", null), Item("oct", Now.AddDays(2)) };
        Assert.Equal(new[] { "oct", "nov", "unknown" }, Query(items).Select(x => x.Record.Guid));
        Assert.Equal(new[] { "nov", "oct", "unknown" }, Query(items, descending: true).Select(x => x.Record.Guid));
    }

    [Fact]
    public void FiltersCombineWithoutMixingSources_AndSearchMatchesTurkishNamesAndPositions()
    {
        var soon = Item("soon", Now.AddDays(2), ApplicationStatus.Planning);
        var expired = Item("expired", Now.AddDays(-1), ApplicationStatus.Planning);
        var far = Item("far", Now.AddDays(40), ApplicationStatus.Planning);
        var other = Item("other", Now.AddDays(2), ApplicationStatus.Planning); other.Record.Source = AnnouncementSource.KamuIlan;
        var items = new[] { soon, expired, far, other };
        Assert.Same(soon, Assert.Single(Query(items, application: 2, deadline: 2, search: "izmir")));
        Assert.Same(expired, Assert.Single(Query(items, deadline: 4)));
        Assert.Equal(3, Query(items, search: "programcı").Count);
        Assert.Equal(3, Query(items, search: "ankara").Count);
        Assert.Empty(Query(items, application: 3));
    }

    [Fact]
    public void CsvEscapesTitlesAndFormulaPrefixes_AndUsesPublicSbbLink()
    {
        var item = Item("a", Now); item.Record.Title = "=HYPERLINK(\"bad\");\nBaşlık";
        item.Record.Source = AnnouncementSource.KamuIlan; item.Record.DetailUrl = KamuIlanClient.BaseUrl + "?kod=old";
        var csv = AnnouncementCsvExporter.Build(new[] { item });
        Assert.Contains("\"'=HYPERLINK(\"\"bad\"\");\nBaşlık\"", csv);
        Assert.Contains(KamuIlanClient.BaseUrl, csv);
        Assert.DoesNotContain("kod=old", csv);
    }

    [Fact]
    public void RemainingTimeShowsExpiredHoursDaysAndUnknown()
    {
        Assert.Equal("Süresi doldu", AnnouncementListQuery.RemainingTime(Now.AddMinutes(-1), Now));
        Assert.Equal("3 saat", AnnouncementListQuery.RemainingTime(Now.AddHours(2.5), Now));
        Assert.Equal("2 gün", AnnouncementListQuery.RemainingTime(Now.AddHours(25), Now));
        Assert.Equal("Belirsiz", AnnouncementListQuery.RemainingTime(null, Now));
    }

    private static List<AnnouncementDisplayItem> Query(IEnumerable<AnnouncementDisplayItem> items, bool descending = false,
        int application = 0, int deadline = 0, string search = "") =>
        AnnouncementListQuery.Apply(items, AnnouncementSource.CareerGate, 0, application, deadline, search, "EndDate", descending, Now);
}
