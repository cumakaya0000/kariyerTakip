using System.Globalization;
using System.Text.RegularExpressions;

namespace KariyerTakip.Services;

public class ExtractedCondition<T>
{
    public T Value { get; set; }
    public string SourceText { get; set; } = string.Empty;

    public ExtractedCondition(T value, string sourceText = "")
    {
        Value = value;
        SourceText = sourceText;
    }
}

public class ExtractedRequirements
{
    public bool HasAssociateDegreeRequirement { get; set; } // Ön Lisans (2 yıllık)
    public bool HasBachelorDegreeRequirement { get; set; }  // Lisans (4 yıllık)
    public bool HasHighSchoolRequirement { get; set; }      // Ortaöğretim / Lise
    public bool HasAnyAssociateDegree { get; set; }         // Herhangi bir ön lisans
    public bool HasAnyBachelorDegree { get; set; }          // Herhangi bir lisans
    public string EducationSourceText { get; set; } = string.Empty;

    public List<ExtractedCondition<string>> MentionedDepartments { get; set; } = new();

    public ExtractedCondition<string>? RequiredKpssType { get; set; }   // P93, P3, P94, etc.
    public ExtractedCondition<double>? MinKpssScore { get; set; }        // 60.0, 70.0, etc.
    public ExtractedCondition<int>? RequiredKpssYear { get; set; }       // 2024, 2022, etc.
    public bool ExplicitlyNoKpss { get; set; }                           // KPSS aranmamaktadır / Sınavsız

    public ExtractedCondition<int>? MinExperienceMonths { get; set; }    // Stored in months (e.g. 6 ay = 6, 2 yıl = 24)
    public bool RequiresExperienceDocumentation { get; set; }
    public string ExperienceSourceText { get; set; } = string.Empty;

    public List<ExtractedCondition<string>> RequiredCertificates { get; set; } = new();
    public ExtractedCondition<string>? RequiredDrivingLicense { get; set; }
    public ExtractedCondition<int>? MaxAgeLimit { get; set; }
    public ExtractedCondition<string>? MilitaryCondition { get; set; }

    public List<string> RawRelevantClauses { get; set; } = new();
}

public class RequirementExtractor
{
    private readonly DocumentReader _documentReader;

    private static readonly Regex KpssScoreRegex = new Regex(@"(?:KPSS|KPSSP|P)\s*[\(\[-]?\s*(93|3|94)\s*[\)\]-]?\s*(?:puan\s*t[uü]r[uü]nden)?\s*(?:en\s*az\s*)?(\d{2}(?:[,\.]\d+)?)\s*(?:puan|ve\s*üzeri|almış)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KpssAltScoreRegex = new Regex(@"(?:en\s*az|taban)\s*(\d{2}(?:[,\.]\d+)?)\s*puan\s*(?:almış|şartı|ve\s*üzeri)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KpssYearRegex = new Regex(@"\b(20\d{2})\s*(?:yılı)?\s*(?:KPSS|Kamu\s*Personel)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KpssYearAltRegex = new Regex(@"(?:KPSS|Kamu\s*Personel)[^\.\n]{0,25}\b(20\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExperienceRegex = new Regex(@"en\s*az\s*(?:(?:(\d+)\s*(?:\([^\)]+\))?)|(bir|iki|üç|dört|beş|altı|yedi|sekiz|dokuz|on))\s*(yıl|sene|ay)\s*(?:mesleki\s*)?(?:tecr[uü]be|deneyim|çalışmış|hizmet|çalışma)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AgeLimitRegex = new Regex(@"\b(\d{2})\s*yaşını\s*(?:doldurmamış|bitirmemiş|tamamlamamış)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DrivingLicenseRegex = new Regex(@"\b([A-Z][0-9]?)\s*sınıfı\s*(?:sürücü|ehliyet)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NoKpssRegex = new Regex(@"(?:KPSS\s*şartı\s*aranmaz|KPSS\s*puanı\s*aranmamaktadır|sınavsız\s*alım)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public RequirementExtractor(DocumentReader documentReader)
    {
        _documentReader = documentReader;
    }

    public ExtractedRequirements Extract(string text)
    {
        var req = new ExtractedRequirements();
        if (string.IsNullOrWhiteSpace(text))
            return req;

        var clauses = _documentReader.ExtractClauses(text);
        var normalizedFull = _documentReader.CleanAndNormalizeText(text);
        var lowerFull = normalizedFull.ToLower(new CultureInfo("tr-TR"));

        // 1. Education Level Detection
        foreach (var clause in clauses)
        {
            var lClause = clause.ToLower(new CultureInfo("tr-TR"));

            if (lClause.Contains("herhangi bir ön lisans") || lClause.Contains("herhangi bir önlisans"))
            {
                req.HasAnyAssociateDegree = true;
                req.HasAssociateDegreeRequirement = true;
                req.EducationSourceText = clause;
            }
            else if (lClause.Contains("ön lisans") || lClause.Contains("önlisans") || lClause.Contains("meslek yüksekokul") || lClause.Contains("myo") || lClause.Contains("2 yıllık"))
            {
                req.HasAssociateDegreeRequirement = true;
                if (string.IsNullOrEmpty(req.EducationSourceText))
                    req.EducationSourceText = clause;
            }

            if (lClause.Contains("herhangi bir lisans"))
            {
                req.HasAnyBachelorDegree = true;
                req.HasBachelorDegreeRequirement = true;
                req.EducationSourceText = clause;
            }
            else if (lClause.Contains("lisans mezun") || lClause.Contains("fakülte") || lClause.Contains("4 yıllık"))
            {
                req.HasBachelorDegreeRequirement = true;
                if (string.IsNullOrEmpty(req.EducationSourceText))
                    req.EducationSourceText = clause;
            }

            if (lClause.Contains("ortaöğretim") || lClause.Contains("lise mezun"))
            {
                req.HasHighSchoolRequirement = true;
            }
        }

        // 2. Department Detection
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

        foreach (var clause in clauses)
        {
            var lClause = clause.ToLower(new CultureInfo("tr-TR"));
            foreach (var kw in targetKeywords)
            {
                if (lClause.Contains(kw) && !req.MentionedDepartments.Any(d => d.Value == kw))
                {
                    req.MentionedDepartments.Add(new ExtractedCondition<string>(kw, clause));
                }
            }
        }

        // 3. KPSS Extraction
        if (NoKpssRegex.IsMatch(normalizedFull))
        {
            req.ExplicitlyNoKpss = true;
        }

        foreach (var clause in clauses)
        {
            var kpssMatch = KpssScoreRegex.Match(clause);
            if (kpssMatch.Success)
            {
                var type = "P" + kpssMatch.Groups[1].Value;
                req.RequiredKpssType = new ExtractedCondition<string>(type, clause);

                var scoreStr = kpssMatch.Groups[2].Value.Replace(',', '.');
                if (double.TryParse(scoreStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var score))
                {
                    req.MinKpssScore = new ExtractedCondition<double>(score, clause);
                }
            }

            var lClause = clause.ToLower(new CultureInfo("tr-TR"));
            if (req.RequiredKpssType == null)
            {
                if (lClause.Contains("p93"))
                    req.RequiredKpssType = new ExtractedCondition<string>("P93", clause);
                else if (lClause.Contains("p3"))
                    req.RequiredKpssType = new ExtractedCondition<string>("P3", clause);
                else if (lClause.Contains("p94"))
                    req.RequiredKpssType = new ExtractedCondition<string>("P94", clause);
            }

            if (req.MinKpssScore == null && lClause.Contains("kpss"))
            {
                var altScoreMatch = KpssAltScoreRegex.Match(clause);
                if (altScoreMatch.Success)
                {
                    var scoreStr = altScoreMatch.Groups[1].Value.Replace(',', '.');
                    if (double.TryParse(scoreStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var score))
                    {
                        req.MinKpssScore = new ExtractedCondition<double>(score, clause);
                    }
                }
            }

            // Year extraction
            var yearMatch = KpssYearRegex.Match(clause);
            if (!yearMatch.Success)
                yearMatch = KpssYearAltRegex.Match(clause);

            if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var year))
            {
                req.RequiredKpssYear = new ExtractedCondition<int>(year, clause);
            }
        }

        // 4. Experience Extraction
        foreach (var clause in clauses)
        {
            var expMatch = ExperienceRegex.Match(clause);
            if (expMatch.Success)
            {
                var numStr = expMatch.Groups[1].Value;
                var wordStr = expMatch.Groups[2].Value;
                var unitStr = expMatch.Groups[3].Value.ToLower(new CultureInfo("tr-TR"));

                int num = 0;
                if (!string.IsNullOrEmpty(numStr) && int.TryParse(numStr, out var parsedNum))
                {
                    num = parsedNum;
                }
                else if (!string.IsNullOrEmpty(wordStr))
                {
                    num = ConvertTurkishWordToNumber(wordStr);
                }

                if (num > 0)
                {
                    int months = (unitStr == "ay") ? num : num * 12;
                    req.MinExperienceMonths = new ExtractedCondition<int>(months, clause);
                    req.ExperienceSourceText = clause;
                }
            }

            var lClause = clause.ToLower(new CultureInfo("tr-TR"));
            if (lClause.Contains("belgelemek") || lClause.Contains("sgk hizmet dökümü") || lClause.Contains("çalışma belgesi"))
            {
                req.RequiresExperienceDocumentation = true;
            }
        }

        // 5. Age Limit
        foreach (var clause in clauses)
        {
            var ageMatch = AgeLimitRegex.Match(clause);
            if (ageMatch.Success && int.TryParse(ageMatch.Groups[1].Value, out var age))
            {
                req.MaxAgeLimit = new ExtractedCondition<int>(age, clause);
            }
        }

        // 6. Driving License
        foreach (var clause in clauses)
        {
            var dlMatch = DrivingLicenseRegex.Match(clause);
            if (dlMatch.Success)
            {
                req.RequiredDrivingLicense = new ExtractedCondition<string>(dlMatch.Groups[1].Value.ToUpperInvariant(), clause);
            }
        }

        // 7. Certificates
        foreach (var clause in clauses)
        {
            var lClause = clause.ToLower(new CultureInfo("tr-TR"));
            if (lClause.Contains("sertifika") || lClause.Contains("işletmenliği belgesi") || lClause.Contains("programcılık belgesi"))
            {
                req.RequiredCertificates.Add(new ExtractedCondition<string>(clause.Trim(), clause));
            }
        }

        // 8. Military Status
        foreach (var clause in clauses)
        {
            var lClause = clause.ToLower(new CultureInfo("tr-TR"));
            if (lClause.Contains("askerlik") || lClause.Contains("askerlikle ilişiği"))
            {
                req.MilitaryCondition = new ExtractedCondition<string>(clause.Trim(), clause);
            }
        }

        return req;
    }

    private static int ConvertTurkishWordToNumber(string word)
    {
        return word.ToLower(new CultureInfo("tr-TR")) switch
        {
            "bir" => 1,
            "iki" => 2,
            "üç" => 3,
            "dört" => 4,
            "beş" => 5,
            "altı" => 6,
            "yedi" => 7,
            "sekiz" => 8,
            "dokuz" => 9,
            "on" => 10,
            _ => 0
        };
    }
}
