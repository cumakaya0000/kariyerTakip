using System.Text;
using KariyerTakip.Models;
using KariyerTakip.Common;

namespace KariyerTakip.Services;

public static class ApplicationCalendarExporter
{
    public static string Build(IEnumerable<AnnouncementRecord> announcements)
    {
        var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//KariyerTakip//Başvurular//TR", "CALSCALE:GREGORIAN" };
        foreach (var record in announcements.Where(a => a.ApplicationStatus == ApplicationStatus.Planning && a.EndDate.HasValue))
        {
            var end = AppTime.ToUtc(record.EndDate)!.Value;
            lines.AddRange(new[] { "BEGIN:VEVENT", "UID:" + Escape(record.Guid) + "@kariyertakip",
                "DTSTAMP:" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'"),
                "DTSTART:" + end.ToString("yyyyMMdd'T'HHmmss'Z'"), "DTEND:" + end.AddMinutes(1).ToString("yyyyMMdd'T'HHmmss'Z'"),
                "SUMMARY:" + Escape("Son başvuru: " + record.Title),
                "DESCRIPTION:" + Escape(record.InstitutionName + "\n" + record.ApplicationNotes + "\n" + record.DetailUrl),
                "BEGIN:VALARM", "TRIGGER:-P1D", "ACTION:DISPLAY", "DESCRIPTION:Son başvuru yarın", "END:VALARM", "END:VEVENT" });
        }
        lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines.Select(Fold)) + "\r\n";
    }
    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n").Replace(";", "\\;").Replace(",", "\\,");
    private static string Fold(string line)
    {
        var output = new StringBuilder(); var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var count = rune.Utf8SequenceLength;
            if (bytes + count > 75) { output.Append("\r\n "); bytes = 1; }
            output.Append(rune.ToString()); bytes += count;
        }
        return output.ToString();
    }
}
