using System.Globalization;
using System.Text.RegularExpressions;
using KariyerTakip.Models;

namespace KariyerTakip.Services;

public class ExtractedRequirements
{
    public bool HasAssociateDegreeRequirement { get; set; } // Ön lisans
    public bool HasBachelorDegreeRequirement { get; set; }  // Lisans
    public bool HasHighSchoolRequirement { get; set; }      // Ortaöğretim / Lise
    public bool HasAnyAssociateDegree { get; set; }         // Herhangi bir ön lisans programından mezun olmak
    public List<string> MentionedDepartments { get; set; } = new();

    public string? RequiredKpssType { get; set; } // P93, P3, P94 etc.
    public double? MinKpssScore { get; set; }
    public int? RequiredKpssYear { get; set; }

    public double? MinExperienceYears { get; set; }
    public bool RequiresExperienceDocumentation { get; set; }
    public List<string> RequiredCertificates { get; set; } = new();
    public string? RequiredDrivingLicense { get; set; }
    public int? MaxAgeLimit { get; set; }

    public List<string> RawRelevantClauses { get; set; } = new();
}

public class RequirementExtractor
{
    private readonly DocumentReader _documentReader;

    private static readonly Regex KpssScoreRegex = new Regex(@"(?:KPSS|KPSSP|P)\s*[-_]?\s*(93|3|94)\s*(?:puan\s*t[uü]r[uü]nden)?\s*(?:en\s*az\s*)?(\d{2}(?:[,\.]\d+)?)\s*(?:puan|ve\s*üzeri|almış)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KpssYearRegex = new Regex(@"(2022|2023|2024|2025|2026)\s*(?:yılı)?\s*(?:KPSS|Kamu\s*Personel)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExperienceYearsRegex = new Regex(@"en\s*az\s*(\d+)\s*(?:\([0-9]+\)\s*)?(?:yıl|sene|ay)\s*(?:mesleki\s*)?(?:tecr[uü]be|deneyim|çalışmış)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AgeLimitRegex = new Regex(@"(\d{2})\s*yaşını\s*(?:doldurmamış|bitirmemiş)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DrivingLicenseRegex = new Regex(@"([A-Z][0-9]?)\s*sınıfı\s*sürücü\s*belges?i", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public RequirementExtractor(DocumentReader documentReader)
    {
        _documentReader = documentReader;
    }

    public ExtractedRequirements Extract(string combinedText)
    {
        var req = new ExtractedRequirements();
        if (string.IsNullOrWhiteSpace(combinedText))
            return req;

        var clauses = _documentReader.ExtractClauses(combinedText);
        var lowerFullText = combinedText.ToLower(new CultureInfo("tr-TR"));

        // Education level detection
        if (lowerFullText.Contains("ön lisans") || lowerFullText.Contains("önlisans") || lowerFullText.Contains("meslek yüksekokul") || lowerFullText.Contains("myo"))
        {
            req.HasAssociateDegreeRequirement = true;
        }

        if (lowerFullText.Contains("lisans mezun") || lowerFullText.Contains("fakülte") || lowerFullText.Contains("4 yıllık"))
        {
            req.HasBachelorDegreeRequirement = true;
        }

        if (lowerFullText.Contains("ortaöğretim") || lowerFullText.Contains("lise mezun"))
        {
            req.HasHighSchoolRequirement = true;
        }

        if (lowerFullText.Contains("herhangi bir ön lisans") || lowerFullText.Contains("herhangi bir önlisans"))
        {
            req.HasAnyAssociateDegree = true;
        }

        // Department keywords
        string[] targetKeywords = {
            "bilgisayar programcılığı",
            "bilgisayar teknolojisi",
            "bilgisayar ve enformasyon",
            "bilgisayar operatörlüğü",
            "bilişim",
            "bilgi işlem",
            "bilgisayar teknikeri",
            "bilgisayar işletmeni",
            "programcı",
            "yazılım"
        };

        foreach (var kw in targetKeywords)
        {
            if (lowerFullText.Contains(kw))
            {
                req.MentionedDepartments.Add(kw);
            }
        }

        // KPSS extraction
        var kpssMatch = KpssScoreRegex.Match(combinedText);
        if (kpssMatch.Success)
        {
            req.RequiredKpssType = "P" + kpssMatch.Groups[1].Value;
            var scoreStr = kpssMatch.Groups[2].Value.Replace(',', '.');
            if (double.TryParse(scoreStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var score))
            {
                req.MinKpssScore = score;
            }
        }
        else if (lowerFullText.Contains("p93"))
        {
            req.RequiredKpssType = "P93";
        }
        else if (lowerFullText.Contains("p3"))
        {
            req.RequiredKpssType = "P3";
        }

        var yearMatch = KpssYearRegex.Match(combinedText);
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var year))
        {
            req.RequiredKpssYear = year;
        }

        // Experience extraction
        var expMatch = ExperienceYearsRegex.Match(combinedText);
        if (expMatch.Success && double.TryParse(expMatch.Groups[1].Value, out var expYears))
        {
            req.MinExperienceYears = expYears;
        }
        if (lowerFullText.Contains("belgelemek") || lowerFullText.Contains("sgk hizmet dökümü") || lowerFullText.Contains("çalışma belgesi"))
        {
            req.RequiresExperienceDocumentation = true;
        }

        // Age limit
        var ageMatch = AgeLimitRegex.Match(combinedText);
        if (ageMatch.Success && int.TryParse(ageMatch.Groups[1].Value, out var age))
        {
            req.MaxAgeLimit = age;
        }

        // Driving license
        var dlMatch = DrivingLicenseRegex.Match(combinedText);
        if (dlMatch.Success)
        {
            req.RequiredDrivingLicense = dlMatch.Groups[1].Value.ToUpperInvariant();
        }

        // Collect relevant clause lines
        foreach (var clause in clauses)
        {
            var lowerClause = clause.ToLower(new CultureInfo("tr-TR"));
            if (targetKeywords.Any(k => lowerClause.Contains(k)) ||
                lowerClause.Contains("ön lisans") || lowerClause.Contains("önlisans") ||
                lowerClause.Contains("kpss") || lowerClause.Contains("p93") ||
                lowerClause.Contains("tecrübe") || lowerClause.Contains("deneyim") ||
                lowerClause.Contains("yaşını"))
            {
                req.RawRelevantClauses.Add(clause);
            }
        }

        return req;
    }
}
