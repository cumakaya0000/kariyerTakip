namespace KariyerTakip.Models;

public class ProfileOptions
{
    public string Department { get; set; } = "Bilgisayar Programcılığı";
    public string EducationLevel { get; set; } = "Ön Lisans";
    public string GraduationStatus { get; set; } = "Mezun";
    public string KpssStatus { get; set; } = "Var";
    public List<KpssScoreEntry> KpssScores { get; set; } = new();
    public List<string> CityPreferences { get; set; } = new();
    public ExperienceEntry Experience { get; set; } = new();
    public List<string> Certificates { get; set; } = new();
    public List<string> DrivingLicenses { get; set; } = new();
    public OtherConditionsEntry OtherConditions { get; set; } = new();
    public List<string> WorkPreferences { get; set; } = new();
}

public class KpssScoreEntry
{
    public string ScoreType { get; set; } = "P93"; // P93, P3, P94 etc.
    public int ExamYear { get; set; } = 2024;
    public double Score { get; set; } = 0.0;
}

public class ExperienceEntry
{
    public string Field { get; set; } = string.Empty;
    public double Years { get; set; } = 0;
    public bool IsDocumented { get; set; } = false;
    public bool IsKnown { get; set; } = false; // If false, treated as "unknown" rather than "0 years"
}

public class OtherConditionsEntry
{
    public string MilitaryStatus { get; set; } = string.Empty;
    public int? MaxAge { get; set; }
}
