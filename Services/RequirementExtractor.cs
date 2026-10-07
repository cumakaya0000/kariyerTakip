using System.Globalization;
using System.Text.RegularExpressions;

namespace KariyerTakip.Services;

public enum RequirementCriterion { Education, Kpss, Experience, Age, DrivingLicense, Military, Certificates }

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
    public HashSet<int> AllowedKpssYears { get; set; } = new();
    public int? MinKpssYear { get; set; }
    public int? MaxKpssYear { get; set; }
    public HashSet<RequirementCriterion> ConflictingCriteria { get; set; } = new();
    public bool HasConflictingRules => ConflictingCriteria.Count > 0;
    public bool AcceptsHigherEducation { get; set; }
    public DateTime? AgeReferenceDate { get; set; }
    public DateTime? EarliestBirthDate { get; set; }
    public bool BirthDateBoundaryInclusive { get; set; }
    public bool HasUnparsedExperience { get; set; }
    public bool HasUnparsedAge { get; set; }
    public bool AgeCountsNextYear { get; set; }
    public ExtractedCondition<int>? MaxExperienceMonths { get; set; }
    public ExtractedCondition<string>? RequiredExperienceField { get; set; }
    public string NoKpssSourceText { get; set; } = "";
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

    private static readonly Regex KpssScoreRegex = new Regex(@"\b(?:KPSS\s*[\(\[]?\s*)?P\s*[\(\[-]?\s*(\d+)\b\s*[\)\]-]?\s+(?:puan\s*t[uü]r[uü]nden)?\s*(?:en\s*az\s*)?(\d{2}(?:[,\.]\d+)?)\s*(?:puan|ve\s*üzeri|almış)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KpssAltScoreRegex = new Regex(@"(?:en\s*az|taban)\s*(\d{2}(?:[,\.]\d+)?)\s*puan\s*(?:almış|şartı|ve\s*üzeri)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExperienceRegex = new Regex(@"(?:(?<comparison>en\s*(?:az|çok|fazla))\s*)?(?:(?:(\d+)\s*(?:\([^\)]+\))?)|(bir|iki|üç|dört|beş|altı|yedi|sekiz|dokuz|on)(?:\s*\(\d+\))?)\s*(yıl|sene|ay)(?:lık|lik)?\s*(?:mesleki\s*)?(?:(?:[\p{L}/-]+\s+){1,6}(?:alanında|sektöründe|konusunda)\s*)?(?:tecr[uü]be|deneyim|çalışmış|hizmet|çalışma)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AgeLimitRegex = new Regex(@"(?:\b(\d{2})\s*(?:\([^)]*\))?|[a-zçğıöşü]+(?:\s+[a-zçğıöşü]+)?\s*\((\d{2})\))\s*(?:yaşını\s*(?:doldurmamış|bitirmemiş|tamamlamamış)|yaşından\s*gün\s*almamış)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DrivingLicenseRegex = new Regex(@"\b([A-Z][0-9]?E?)\s*sınıfı\s*(?:sürücü|ehliyet)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NoKpssRegex = new Regex(@"KPSS\s*(?:şartı|puanı|puan\s*şartı)\s*(?:aranmaz|aranmamaktadır|aranmayacaktır)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            if (!Regex.IsMatch(lClause, @"mezun|öğrenim|öğretim|diploma")) continue;
            if (Regex.IsMatch(lClause, @"en\s*az\s*(?:ön\s*lisans|lisans|lise|ortaöğretim)|(?:ön\s*lisans|lisans|lise|ortaöğretim)\s*(?:ve\s*)?(?:üzeri|üstü)")) req.AcceptsHigherEducation = true;
            if (Regex.IsMatch(lClause, @"^(?:en\s*az\s*)?(?:herhangi\s*bir\s*)?ön\s*lisans(?:\s*ve\s*(?:üzeri|üstü))?\s*mezun")) req.HasAnyAssociateDegree = true;
            if (Regex.IsMatch(lClause, @"^(?:en\s*az\s*)?(?:herhangi\s*bir\s*)?lisans(?:\s*ve\s*(?:üzeri|üstü))?\s*mezun")) req.HasAnyBachelorDegree = true;

            if (lClause.Contains("herhangi bir ön lisans") || lClause.Contains("herhangi bir önlisans"))
            {
                req.HasAnyAssociateDegree = true;
                req.HasAssociateDegreeRequirement = true;
                req.EducationSourceText = clause;
            }
            else if (lClause.Contains("ön lisans") || lClause.Contains("önlisans") || lClause.Contains("meslek yüksekokul") || lClause.Contains("myo") || Regex.IsMatch(lClause, @"2\s*yıllık\s*(?:öğrenim|program|yüksek)"))
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
            else if (Regex.IsMatch(lClause, @"(?<!ön )(?<!ön)\blisans\b") || Regex.IsMatch(lClause, @"fakültelerin|fakültesinden\s*mezun|4\s*yıllık\s*(?:öğrenim|program|yüksek)"))
            {
                req.HasBachelorDegreeRequirement = true;
                if (string.IsNullOrEmpty(req.EducationSourceText))
                    req.EducationSourceText = clause;
            }

            if (lClause.Contains("ortaöğretim") || lClause.Contains("lise mezun"))
            {
                req.HasHighSchoolRequirement = true;
                if (string.IsNullOrEmpty(req.EducationSourceText)) req.EducationSourceText = clause;
            }
        }

        // 2. Department Detection
        foreach (var clause in clauses.Where(c => Regex.IsMatch(c.ToLower(new CultureInfo("tr-TR")), @"mezun|diploma")))
        {
            req.RawRelevantClauses.Add(clause);
            foreach (Match match in Regex.Matches(clause, @"([\p{L}][\p{L}\s/,-]{2,100}?)\s+(?:bölüm|program)", RegexOptions.IgnoreCase))
                req.MentionedDepartments.Add(new ExtractedCondition<string>(match.Groups[1].Value.Trim(), clause));
        }
        // 3. KPSS Extraction
        if (NoKpssRegex.IsMatch(normalizedFull))
        {
            req.ExplicitlyNoKpss = true;
            req.NoKpssSourceText = clauses.First(c => NoKpssRegex.IsMatch(c));
        }

        foreach (var clause in clauses)
        {
            if (KpssScoreRegex.Matches(clause).Select(m => m.Groups[1].Value + ":" + m.Groups[2].Value).Distinct().Count() > 1) req.ConflictingCriteria.Add(RequirementCriterion.Kpss);
            var kpssMatch = KpssScoreRegex.Match(clause);
            if (kpssMatch.Success)
            {
                var type = "P" + kpssMatch.Groups[1].Value;
                if (req.RequiredKpssType != null && req.RequiredKpssType.Value != type) req.ConflictingCriteria.Add(RequirementCriterion.Kpss);
                req.RequiredKpssType = new ExtractedCondition<string>(type, clause);

                var scoreStr = kpssMatch.Groups[2].Value.Replace(',', '.');
                if (double.TryParse(scoreStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var score))
                {
                    if (req.MinKpssScore != null && req.MinKpssScore.Value != score) req.ConflictingCriteria.Add(RequirementCriterion.Kpss);
                    req.MinKpssScore = new ExtractedCondition<double>(score, clause);
                }
            }

            var lClause = clause.ToLower(new CultureInfo("tr-TR"));
            if (req.RequiredKpssType == null)
            {
                var typeMatch = Regex.Match(lClause, @"\b(?:kpss\s*)?p\s*(\d+)\b");
                if (typeMatch.Success) req.RequiredKpssType = new ExtractedCondition<string>("P" + typeMatch.Groups[1].Value, clause);
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

            ExtractKpssYears(clause, req);
        }
        // 4. Experience Extraction
        foreach (var clause in clauses)
        {
            var expMatch = ExperienceRegex.Match(clause);
            if (ExperienceRegex.Matches(clause).Select(m => m.Value).Distinct().Count() > 1) req.ConflictingCriteria.Add(RequirementCriterion.Experience);
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
                    if (Regex.IsMatch(expMatch.Groups["comparison"].Value, @"çok|fazla", RegexOptions.IgnoreCase))
                    {
                        if (req.MaxExperienceMonths != null && req.MaxExperienceMonths.Value != months) req.ConflictingCriteria.Add(RequirementCriterion.Experience);
                        req.MaxExperienceMonths = new(months, clause);
                    }
                    else
                    {
                        if (req.MinExperienceMonths != null && req.MinExperienceMonths.Value != months) req.ConflictingCriteria.Add(RequirementCriterion.Experience);
                        req.MinExperienceMonths = new ExtractedCondition<int>(months, clause);
                    }
                    req.ExperienceSourceText = clause;
                    var fieldMatch = Regex.Match(clause, @"([\p{L}][\p{L}\s/-]+?)\s+(?:alanında|sektöründe|konusunda)", RegexOptions.IgnoreCase);
                    if (fieldMatch.Success)
                    {
                        var field = Regex.Replace(fieldMatch.Groups[1].Value.Trim(), @"^(?:yıl|ay|mesleki)\s+", "", RegexOptions.IgnoreCase);
                        req.RequiredExperienceField = new(field, clause);
                    }
                }
            }

            var lClause = clause.ToLower(new CultureInfo("tr-TR"));
            if (lClause.Contains("belgelemek") || lClause.Contains("sgk hizmet dökümü") || lClause.Contains("çalışma belgesi"))
            {
                req.RequiresExperienceDocumentation = true;
            }
        }

        // 5. Age Limit
        for (var clauseIndex = 0; clauseIndex < clauses.Count; clauseIndex++)
        {
            var clause = clauses[clauseIndex];
            var ageMatch = AgeLimitRegex.Match(clause);
            if (AgeLimitRegex.Matches(clause).Select(m => m.Value).Distinct().Count() > 1) req.ConflictingCriteria.Add(RequirementCriterion.Age);
            if (ageMatch.Success && int.TryParse(ageMatch.Groups[1].Success ? ageMatch.Groups[1].Value : ageMatch.Groups[2].Value, out var age))
            {
                if (req.MaxAgeLimit != null && req.MaxAgeLimit.Value != age) req.ConflictingCriteria.Add(RequirementCriterion.Age);
                req.MaxAgeLimit = new ExtractedCondition<int>(age, clause);
                req.AgeCountsNextYear = Regex.IsMatch(ageMatch.Value, @"yaşından\s*gün\s*almamış", RegexOptions.IgnoreCase);
                var reference = Regex.Match(clause, @"(?<date>\d{1,2}[./]\d{1,2}[./]\d{4})\s*tarihi?\s*itibarı?yla|(?<date>\d{1,2}[./]\d{1,2}[./]\d{4})\s*tarihi?\s*itibariyle", RegexOptions.IgnoreCase);
                if (reference.Success && TryDate(reference.Groups["date"].Value, out var referenceDate)) req.AgeReferenceDate = referenceDate;
                var ageContext = clause;
                if (clauseIndex + 1 < clauses.Count && clauses[clauseIndex + 1].TrimStart().StartsWith('('))
                    ageContext += " " + clauses[clauseIndex + 1];
                var birth = Regex.Match(ageContext, @"(?<date>\d{1,2}[./]\d{1,2}[./]\d{4})\s*(?:tarihi?(?:nde|nden)?\s*)?(?<inclusive>ve(?:ya)?\s*sonrasında|ve\s*sonrası|ve\s*daha\s*sonra|veya\s*sonra)?\s*(?<after>sonra(?:sında)?|önce)?\s*doğmuş", RegexOptions.IgnoreCase);
                if (birth.Success && birth.Groups["after"].Value != "önce" && TryDate(birth.Groups["date"].Value, out var birthDate))
                { req.EarliestBirthDate = birthDate; req.BirthDateBoundaryInclusive = birth.Groups["inclusive"].Success;
                    req.MaxAgeLimit.SourceText = ageContext; }
            }
        }

        // 6. Driving License
        foreach (var clause in clauses)
        {
            var dlMatch = DrivingLicenseRegex.Match(clause);
            if (DrivingLicenseRegex.Matches(clause).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().Count() > 1) req.ConflictingCriteria.Add(RequirementCriterion.DrivingLicense);
            if (dlMatch.Success)
            {
                if (req.RequiredDrivingLicense != null && req.RequiredDrivingLicense.Value != dlMatch.Groups[1].Value.ToUpperInvariant()) req.ConflictingCriteria.Add(RequirementCriterion.DrivingLicense);
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
                // A later document checklist must not replace an actual military eligibility clause.
                var isRequirement = Regex.IsMatch(lClause, @"yapmış|yapmak|muaf|ertelen|tecil|ilişiği|ilgisi bulunmamak");
                var existingIsRequirement = req.MilitaryCondition != null && Regex.IsMatch(
                    req.MilitaryCondition.Value.ToLower(new CultureInfo("tr-TR")), @"yapmış|yapmak|muaf|ertelen|tecil|ilişiği|ilgisi bulunmamak");
                if (isRequirement || !existingIsRequirement)
                    req.MilitaryCondition = new ExtractedCondition<string>(clause.Trim(), clause);
            }
        }

        var kpssTypes = Regex.Matches(lowerFull, @"\b(?:kpss\s*)?p\s*\d+\b").Select(m => Regex.Replace(m.Value, @"kpss|\s", "")).Distinct().ToList();
        if (kpssTypes.Count > 1) req.ConflictingCriteria.Add(RequirementCriterion.Kpss);
        if (kpssTypes.Count == 1) req.RequiredKpssType = new ExtractedCondition<string>(kpssTypes[0].ToUpperInvariant(), normalizedFull);
        var scores = clauses.Where(c => c.Contains("kpss", StringComparison.OrdinalIgnoreCase)).SelectMany(c => KpssAltScoreRegex.Matches(c).Select(m => m.Groups[1].Value)).Distinct().ToList();
        if (scores.Count > 1) req.ConflictingCriteria.Add(RequirementCriterion.Kpss);
        if (req.ExplicitlyNoKpss && (req.RequiredKpssType != null || req.MinKpssScore != null)) req.ConflictingCriteria.Add(RequirementCriterion.Kpss);
        if (req.MinExperienceMonths?.Value > req.MaxExperienceMonths?.Value) req.ConflictingCriteria.Add(RequirementCriterion.Experience);
        req.HasUnparsedExperience = req.MinExperienceMonths == null && req.MaxExperienceMonths == null && Regex.IsMatch(lowerFull, @"tecrübe|deneyim|çalışmış") && !Regex.IsMatch(lowerFull, @"(?:tecrübe|deneyim).*aranma");
        req.HasUnparsedAge = req.MaxAgeLimit == null && Regex.IsMatch(lowerFull, @"yaşını|yaşından|yaş sınırı");
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

    private static bool TryDate(string value, out DateTime date) => DateTime.TryParseExact(value,
        new[] { "d.M.yyyy", "dd.MM.yyyy", "d/M/yyyy", "dd/MM/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static void ExtractKpssYears(string clause, ExtractedRequirements req)
    {
        // Remove calendar dates before matching only year phrases adjacent to the exam name.
        var text = Regex.Replace(clause, @"\b\d{1,2}[./-]\d{1,2}[./-](?:19|20)\d{2}\b|\b(?:19|20)\d{2}-\d{1,2}-\d{1,2}\b", " ");
        text = Regex.Replace(text, @"\b\d{1,2}\s+(?:ocak|şubat|mart|nisan|mayıs|haziran|temmuz|ağustos|eylül|ekim|kasım|aralık)\s+20\d{2}\b", " ", RegexOptions.IgnoreCase);
        const string years = @"\b(?<years>20\d{2}(?:\s*(?:,|veya|ve|/|-|–)\s*20\d{2})*)\b";
        const string relation = @"(?:\s*(?<relation>ve\s*sonrası|ve\s*öncesi|sonrası|öncesi|itibaren))?";
        var matches = Regex.Matches(text, years + @"\s*(?:yıl[\p{L}]*\s*)?" + relation + @"\s*(?:(?:yapılan|uygulanan|yapılmış)\s*)?(?:KPSS|Kamu\s*Personel)", RegexOptions.IgnoreCase)
            .Cast<Match>().Concat(Regex.Matches(text, @"(?:KPSS|Kamu\s*Personel)(?:\s*(?:sınavı|sınavının|sınavından|yılı))*\s*[:(]?\s*" + years + relation, RegexOptions.IgnoreCase).Cast<Match>());
        foreach (var match in matches)
        {
            var values = Regex.Matches(match.Groups["years"].Value, @"20\d{2}").Select(y => int.Parse(y.Value)).ToList();
            var direction = match.Groups["relation"].Value.ToLower(new CultureInfo("tr-TR"));
            if (direction.Length > 0)
            {
                if (direction.Contains("öncesi")) req.MaxKpssYear = values[0]; else req.MinKpssYear = values[0];
            }
            else if (Regex.IsMatch(match.Groups["years"].Value, @"[-–]"))
            { req.MinKpssYear = values[0]; req.MaxKpssYear = values[^1]; }
            else foreach (var value in values) req.AllowedKpssYears.Add(value);
            req.RequiredKpssYear = req.AllowedKpssYears.Count == 1 && !req.MinKpssYear.HasValue && !req.MaxKpssYear.HasValue
                ? new(req.AllowedKpssYears.Single(), clause) : null;
        }
    }
}
