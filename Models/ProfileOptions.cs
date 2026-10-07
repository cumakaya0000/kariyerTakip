namespace KariyerTakip.Models;

public class ProfileOptions
{
    public Dictionary<string, List<string>> DepartmentAliases { get; set; } = new();
    public Dictionary<string, KariyerTakip.Services.PositionRuleOverrides> PositionRules { get; set; } = new();
    public string Department { get; set; } = "";
    public string EducationLevel { get; set; } = "Ön Lisans";
    public string GraduationStatus { get; set; } = "Mezun"; // Mezun, Öğrenci, Bilinmiyor
    public string KpssStatus { get; set; } = "Bilinmiyor"; // Var, Yok, Bilinmiyor
    public List<KpssScoreEntry> KpssScores { get; set; } = new();
    public DateTime? BirthDate { get; set; }
    public string MilitaryStatus { get; set; } = "Bilinmiyor"; // Muaf / Yapıldı, Tecilli, Yapılmadı, Bilinmiyor
    public ExperienceEntry Experience { get; set; } = new()
    {
        Field = "",
        TotalMonths = 0,
        IsDocumented = false,
        IsKnown = false // If false, treated as unknown, which triggers 'NeedsReview' if required
    };
    public List<string> Certificates { get; set; } = new();
    public List<string> DrivingLicenses { get; set; } = new();
    public List<string> CityPreferences { get; set; } = new(); // Empty = Tüm Türkiye
    public List<string> WorkPreferences { get; set; } = new(); // Empty = all employment types
}

public class KpssScoreEntry
{
    public string ScoreType { get; set; } = ""; // Requires explicit entry
    public int ExamYear { get; set; }
    public double Score { get; set; } = 0.0;
}

public class ExperienceEntry
{
    public string Field { get; set; } = string.Empty;
    public int TotalMonths { get; set; } = 0; // Stored in months for exact precision (e.g. 6 months = 6)
    [System.Text.Json.Serialization.JsonIgnore]
    public double Years => TotalMonths / 12.0;
    public bool IsDocumented { get; set; } = false;
    public bool IsKnown { get; set; } = false;
}
