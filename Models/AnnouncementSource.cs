namespace KariyerTakip.Models;

public enum AnnouncementSource { CareerGate, KamuIlan }

public static class AnnouncementSources
{
    public static string DisplayName(this AnnouncementSource source) => source == AnnouncementSource.KamuIlan
        ? "Kamu İlan (SBB)" : "Kariyer Kapısı";
    public const string KamuReviewReason = "Kamu İlan kadro şartları PDF belgesinde yer alır. Resmî ilan belgesini inceleyin; otomatik uygunluk değerlendirmesi yapılmadı.";
}
