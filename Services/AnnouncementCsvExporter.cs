using System.Text;
using KariyerTakip.Common;
using KariyerTakip.Models;

namespace KariyerTakip.Services;

public static class AnnouncementCsvExporter
{
    public static string Build(IEnumerable<AnnouncementDisplayItem> items)
    {
        var result = new StringBuilder("Kaynak;Kurum;İlan Başlığı;Uygunluk;Son Başvuru;Başvuru Takibi;Kadro Sayısı;Resmî Site\r\n");
        foreach (var item in items)
        {
            var record = item.Record;
            var status = item.Status == EligibilityStatus.Eligible ? "Uygun" : item.Status == EligibilityStatus.Ineligible ? "Uygun değil" : item.Status == EligibilityStatus.LikelyIneligible ? "Büyük olasılıkla uygun değil" : "Kontrol gerekli";
            var tracking = record.ApplicationStatus switch { ApplicationStatus.Planning => "Başvuracağım", ApplicationStatus.Applied => "Başvurdum", ApplicationStatus.Skipped => "Geçtim", _ => "Takip edilmiyor" };
            result.AppendLine(string.Join(";", new[] { record.Source.DisplayName(), record.InstitutionName, record.Title,
                status, AppTime.Format(record.EndDate), tracking, item.Positions.Count.ToString(),
                record.Source == AnnouncementSource.KamuIlan ? KamuIlanClient.BaseUrl : record.DetailUrl }.Select(Cell)));
        }
        return result.ToString();
    }
    private static string Cell(string value)
    {
        // External announcement titles must remain text when the CSV is opened in Excel.
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
