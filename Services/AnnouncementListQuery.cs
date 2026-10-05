using System.Globalization;
using KariyerTakip.Models;

namespace KariyerTakip.Services;

public static class AnnouncementListQuery
{
    public static List<AnnouncementDisplayItem> Apply(IEnumerable<AnnouncementDisplayItem> items, AnnouncementSource source,
        int eligibilityFilter, int applicationFilter, int deadlineFilter, string search, string sortColumn, bool descending, DateTime nowUtc)
    {
        var culture = CultureInfo.GetCultureInfo("tr-TR");
        var filtered = items.Where(item =>
        {
            if (item.Record.Source != source) return false;
            if (eligibilityFilter > 0 && item.Status != (eligibilityFilter == 1 ? EligibilityStatus.Eligible :
                eligibilityFilter == 2 ? EligibilityStatus.NeedsReview : EligibilityStatus.Ineligible)) return false;
            if (applicationFilter > 0 && (int)item.Record.ApplicationStatus != applicationFilter - 1) return false;
            var deadline = item.Record.EndDate;
            var active = item.Record.IsActive && (!deadline.HasValue || deadline >= nowUtc);
            if (deadlineFilter == 1 && !active) return false;
            if (deadlineFilter is 2 or 3 && (!active || deadline == null || deadline > nowUtc.AddDays(deadlineFilter == 2 ? 7 : 30))) return false;
            if (deadlineFilter == 4 && active) return false;
            if (search.Length == 0) return true;
            var searchable = string.Join(" ", new[] { item.Record.InstitutionName, item.Record.Title, item.Record.UnitName }
                .Concat(item.Positions.Select(p => p.Title + " " + p.Unvan + " " + p.Cities)));
            return culture.CompareInfo.IndexOf(searchable, search.Trim(), CompareOptions.IgnoreCase) >= 0;
        });
        IOrderedEnumerable<AnnouncementDisplayItem> ordered = sortColumn switch
        {
            "Institution" => Order(filtered, x => x.Record.InstitutionName, descending, StringComparer.Create(culture, true)),
            "Title" => Order(filtered, x => x.Record.Title, descending, StringComparer.Create(culture, true)),
            "Status" => Order(filtered, x => (int)x.Status, descending),
            "ApplicationStatus" => Order(filtered, x => (int)x.Record.ApplicationStatus, descending),
            "PositionsCount" => Order(filtered, x => x.Positions.Count, descending),
            _ => descending ? filtered.OrderBy(x => x.Record.EndDate == null).ThenByDescending(x => x.Record.EndDate)
                : filtered.OrderBy(x => x.Record.EndDate == null).ThenBy(x => x.Record.EndDate)
        };
        return ordered.ThenBy(x => x.Record.Guid, StringComparer.Ordinal).ToList();
    }

    private static IOrderedEnumerable<T> Order<T, TKey>(IEnumerable<T> items, Func<T, TKey> key, bool descending, IComparer<TKey>? comparer = null) =>
        descending ? items.OrderByDescending(key, comparer) : items.OrderBy(key, comparer);

    public static string RemainingTime(DateTime? deadline, DateTime nowUtc)
    {
        if (deadline == null) return "Belirsiz";
        var remaining = deadline.Value - nowUtc;
        if (remaining <= TimeSpan.Zero) return "Süresi doldu";
        if (remaining.TotalHours < 24) return $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalHours))} saat";
        return $"{(int)Math.Ceiling(remaining.TotalDays)} gün";
    }
}
