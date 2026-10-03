namespace KariyerTakip.Models;

public class ProfileOptions
{
    public string Department { get; set; } = "Bilgisayar Programcılığı";
    public string EducationLevel { get; set; } = "Ön Lisans";
    public string GraduationStatus { get; set; } = "Mezun"; // Mezun, Öğrenci, Bilinmiyor
    public string KpssStatus { get; set; } = "Var"; // Var, Yok, Bilinmiyor
    public List<KpssScoreEntry> KpssScores { get; set; } = new()
    {
        new KpssScoreEntry { ScoreType = "P93", ExamYear = 2024, Score = 75.0 }
    };
    public DateTime? BirthDate { get; set; } = new DateTime(1998, 1, 1);
    public string MilitaryStatus { get; set; } = "Muaf / Yapıldı"; // Muaf / Yapıldı, Tecilli, Yapılmadı, Bilinmiyor
    public ExperienceEntry Experience { get; set; } = new()
    {
        Field = "Bilgi İşlem / Yazılım",
        TotalMonths = 0,
        IsDocumented = false,
        IsKnown = false // If false, treated as unknown, which triggers 'NeedsReview' if required
    };
    public List<string> Certificates { get; set; } = new();
    public List<string> DrivingLicenses { get; set; } = new() { "B" };
    public List<string> CityPreferences { get; set; } = new(); // Empty = Tüm Türkiye
    public List<string> WorkPreferences { get; set; } = new() { "Sözleşmeli", "Kadrolu" };
}

public class KpssScoreEntry
{
    public string ScoreType { get; set; } = "P93"; // P93, P3, P94, etc.
    public int ExamYear { get; set; } = 2024;
    public double Score { get; set; } = 0.0;
}

public class ExperienceEntry
{
    public string Field { get; set; } = string.Empty;
    public int TotalMonths { get; set; } = 0; // Stored in months for exact precision (e.g. 6 months = 6)
    public double Years => TotalMonths / 12.0;
    public bool IsDocumented { get; set; } = false;
    public bool IsKnown { get; set; } = false;
}
