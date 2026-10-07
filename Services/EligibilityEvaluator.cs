using System.Globalization;
using System.Text.RegularExpressions;
using KariyerTakip.Models;
using KariyerTakip.Common;

namespace KariyerTakip.Services;

public class EligibilityEvaluator
{
    private readonly RequirementExtractor _extractor;
    private readonly DocumentReader _documentReader;

    public EligibilityEvaluator(RequirementExtractor extractor, DocumentReader documentReader)
    {
        _extractor = extractor;
        _documentReader = documentReader;
    }

    public PositionEvaluation EvaluatePosition(
        AltIlanResponse altIlan,
        string generalAnnouncementText,
        ProfileOptions profile,
        string positionKey = "", DateTime? referenceDate = null)
    {
        var posEval = new PositionEvaluation
        {
            PositionKey = !string.IsNullOrEmpty(positionKey) ? positionKey : (altIlan.IlanBaslik ?? "Pos_" + Guid.NewGuid().ToString("N")),
            PositionTitle = altIlan.IlanBaslik ?? "Belirtilmemiş Kadro",
            Unvan = altIlan.Unvan,
            TotalQuota = altIlan.KontenjanList?.Sum(x => x.Kontenjan) ?? 0,
            Cities = altIlan.KontenjanList != null && altIlan.KontenjanList.Any()
                ? string.Join(", ", altIlan.KontenjanList.Select(k => $"{k.Il} ({k.Kontenjan})"))
                : "Belirtilmemiş"
        };

        var positionRawText = altIlan.IlanMetni ?? string.Empty;
        var positionTitleText = $"{altIlan.IlanBaslik} {altIlan.Unvan}";
        var positionSpecificText = $"{positionTitleText}\n{positionRawText}";

        // Extract requirements strictly from position specific text first
        var posReq = _extractor.Extract(positionSpecificText);
        profile.PositionRules.TryGetValue(posEval.PositionKey, out var rules);
        var rulesAreCurrent = rules != null && rules.SourceHash == new ChangeDetector().ComputeHash(positionRawText + "\n" + generalAnnouncementText);
        if (rulesAreCurrent) rules!.Apply(posReq);
        else if (rules != null) posEval.Conditions.Add(new ConditionEvaluation { CriterionName = "Doğrulanmış kadro şartları", Status = ConditionStatus.Unknown,
            IsInferred = true, Explanation = "İlan metni değişti; kaydedilen kadro düzeltmeleri yeniden doğrulanmalıdır." });

        // Extract general requirements (like general age/military) from general text
        var genReq = _extractor.Extract(generalAnnouncementText);
        if (rulesAreCurrent && rules!.ResolveConflicts) genReq.ConflictingCriteria.Clear();

        posEval.ExtractedDepartmentText = posReq.MentionedDepartments.Any()
            ? string.Join(", ", posReq.MentionedDepartments.Select(d => d.Value))
            : "Belirtilmemiş / Genel Şart";

        posEval.ExtractedKpssText = posReq.MinKpssScore != null
            ? $"{posReq.RequiredKpssType?.Value ?? "KPSS"} en az {posReq.MinKpssScore.Value} (Yıl: {posReq.RequiredKpssYear?.Value.ToString() ?? "Serbest"})"
            : (posReq.RequiredKpssType != null ? $"{posReq.RequiredKpssType.Value} puanı" : (posReq.ExplicitlyNoKpss ? "Sınavsız / KPSS Şartsız" : "KPSS bilgisi çıkarılamadı"));

        posEval.ExtractedExperienceText = posReq.MinExperienceMonths != null
            ? $"En az {posReq.MinExperienceMonths.Value} ay ({posReq.MinExperienceMonths.Value / 12.0:0.#} yıl) tecrübe"
            : "Tecrübe şartı tespit edilmedi";

        // -------------------------------------------------------------
        // 1. Department & Degree Evaluation
        // -------------------------------------------------------------
        EvaluateCriterion(!string.IsNullOrEmpty(posReq.EducationSourceText) ? posReq : genReq, RequirementCriterion.Education,
            () => EvaluateDepartmentAndDegree(posReq, genReq, profile, positionSpecificText, posEval));

        // -------------------------------------------------------------
        // 2. KPSS Evaluation
        // -------------------------------------------------------------
        var kpssGeneral = genReq.ConflictingCriteria.Contains(RequirementCriterion.Kpss) &&
            (posReq.ExplicitlyNoKpss || posReq.RequiredKpssType != null && posReq.MinKpssScore != null) ? new ExtractedRequirements() : genReq;
        if (kpssGeneral != genReq && !posReq.ExplicitlyNoKpss && posReq.RequiredKpssYear == null &&
            posReq.AllowedKpssYears.Count == 0 && !posReq.MinKpssYear.HasValue && !posReq.MaxKpssYear.HasValue)
        {
            // An explicit position score resolves score/type conflicts, but must not discard a common exam year.
            kpssGeneral.RequiredKpssYear = genReq.RequiredKpssYear;
            kpssGeneral.AllowedKpssYears = new(genReq.AllowedKpssYears);
            kpssGeneral.MinKpssYear = genReq.MinKpssYear;
            kpssGeneral.MaxKpssYear = genReq.MaxKpssYear;
            if (genReq.AllowedKpssYears.Count > 1 || genReq.MinKpssYear.HasValue || genReq.MaxKpssYear.HasValue)
                kpssGeneral.ConflictingCriteria.Add(RequirementCriterion.Kpss);
        }
        EvaluateCriterion(posReq.ConflictingCriteria.Contains(RequirementCriterion.Kpss) ? posReq : kpssGeneral, RequirementCriterion.Kpss,
            () => EvaluateKpss(posReq, kpssGeneral, profile, posEval));

        // -------------------------------------------------------------
        // 3. Experience Evaluation
        // -------------------------------------------------------------
        var experience = posReq.MinExperienceMonths != null || posReq.MaxExperienceMonths != null || posReq.HasUnparsedExperience ? posReq : genReq;
        EvaluateCriterion(experience, RequirementCriterion.Experience, () => EvaluateExperience(experience, profile, posEval));

        // -------------------------------------------------------------
        // 4. Driving License Evaluation
        // -------------------------------------------------------------
        EvaluateCriterion(posReq, RequirementCriterion.DrivingLicense, () => EvaluateDrivingLicense(posReq, profile, posEval));

        // -------------------------------------------------------------
        // 5. Age Limit Evaluation
        // -------------------------------------------------------------
        EvaluateCriterion(posReq.MaxAgeLimit != null || posReq.HasUnparsedAge ? posReq : genReq, RequirementCriterion.Age,
            () => EvaluateAgeLimit(posReq, genReq, profile, posEval, referenceDate));
        EvaluateCriterion(posReq.MilitaryCondition != null ? posReq : genReq, RequirementCriterion.Military,
            () => EvaluateMilitary(posReq, genReq, profile, posEval));

        // -------------------------------------------------------------
        // 6. Certificates Evaluation
        // -------------------------------------------------------------
        EvaluateCriterion(posReq, RequirementCriterion.Certificates, () => EvaluateCertificates(posReq, profile, posEval));

        // -------------------------------------------------------------
        // 7. City Preferences Evaluation
        // -------------------------------------------------------------
        EvaluateCityPreferences(altIlan, profile, posEval);
        EvaluateWorkPreferences(positionTitleText, profile, posEval, positionRawText);

        // -------------------------------------------------------------
        // Overall Decision Aggregation
        // -------------------------------------------------------------
        AggregateOverallStatus(posEval);

        return posEval;

        void EvaluateCriterion(ExtractedRequirements requirements, RequirementCriterion criterion, Action evaluate)
        {
            var start = posEval.Conditions.Count; evaluate();
            if (!requirements.ConflictingCriteria.Contains(criterion)) return;
            foreach (var condition in posEval.Conditions.Skip(start))
            { condition.IsInferred = true; condition.Status = ConditionStatus.Unknown; }
            posEval.Conditions.Add(new ConditionEvaluation { CriterionName = $"Kural çakışması ({criterion})", Status = ConditionStatus.Unknown,
                IsInferred = true, Explanation = "Bu kriterde birden fazla kural var; kadroya uygulanan şart doğrulanmalıdır." });
        }
    }

    private void EvaluateDepartmentAndDegree(
        ExtractedRequirements posReq, ExtractedRequirements genReq, ProfileOptions profile,
        string positionSpecificText, PositionEvaluation posEval)
    {
        var req = !string.IsNullOrEmpty(posReq.EducationSourceText) ? posReq : genReq;
        var level = Normalize(profile.EducationLevel);
        var associate = level.Contains("ön lisans") || level.Contains("önlisans");
        var bachelor = !associate && level == "lisans";
        var school = level.Contains("lise") || level.Contains("ortaöğretim");
        var postgraduate = level.Contains("yüksek lisans") || level.Contains("doktora");
        var hasLevels = req.HasAssociateDegreeRequirement || req.HasBachelorDegreeRequirement || req.HasHighSchoolRequirement;
        var levelMatches = associate && req.HasAssociateDegreeRequirement ||
            bachelor && req.HasBachelorDegreeRequirement || school && req.HasHighSchoolRequirement;
        if (req.AcceptsHigherEducation) levelMatches |=
            (bachelor || postgraduate) && req.HasAssociateDegreeRequirement || postgraduate && req.HasBachelorDegreeRequirement ||
            (associate || bachelor || postgraduate) && req.HasHighSchoolRequirement;
        if (hasLevels && !levelMatches)
        {
            posEval.Conditions.Add(new ConditionEvaluation { CriterionName = "Öğrenim Düzeyi",
                Status = level.Contains("yüksek") ? ConditionStatus.Unknown : ConditionStatus.Unsatisfied,
                RequiredValue = req.EducationSourceText, UserValue = profile.EducationLevel,
                Explanation = $"Kadroda belirtilen mezuniyet düzeyi profilinizle uyuşmuyor: {req.EducationSourceText}",
                SourceText = req.EducationSourceText });
            return;
        }
        var educationText = _documentReader.ExtractClauses(!string.IsNullOrEmpty(posReq.EducationSourceText) ? positionSpecificText : genReq.EducationSourceText)
            .Where(c => Regex.IsMatch(Normalize(c), @"mezun|diploma|öğrenim|öğretim")).ToList();
        var names = new List<string> { profile.Department };
        foreach (var group in profile.DepartmentAliases)
            if (Normalize(group.Key) == Normalize(profile.Department) || group.Value.Any(v => Normalize(v) == Normalize(profile.Department)))
            { names.Add(group.Key); names.AddRange(group.Value); }
        var evidence = educationText.FirstOrDefault(c => names.Any(n => DepartmentMatches(c, n)));
        var anyDegree = (associate || req.AcceptsHigherEducation && (bachelor || postgraduate)) && req.HasAnyAssociateDegree ||
            (bachelor || req.AcceptsHigherEducation && postgraduate) && req.HasAnyBachelorDegree ||
            school && req.HasHighSchoolRequirement && !Regex.IsMatch(Normalize(req.EducationSourceText), @"bölüm|alan");
        var status = evidence != null || anyDegree ? ConditionStatus.Satisfied : ConditionStatus.Unknown;
        var explicitMismatch = evidence == null && !anyDegree && !string.IsNullOrWhiteSpace(profile.Department) &&
            educationText.Any(c => Regex.IsMatch(Normalize(c), @"(?:bölüm|program)(?:ler|lar)?(?:inden|ından|ünden|undan|i|ı|ü|u).*mezun") &&
                !Regex.IsMatch(Normalize(c), @"denk|eşdeğer|ilgili|diğer|gibi|herhangi|tüm"));
        if (status == ConditionStatus.Satisfied && profile.GraduationStatus != "Mezun")
            status = profile.GraduationStatus == "Öğrenci" ? ConditionStatus.Unsatisfied : ConditionStatus.Unknown;
        posEval.Conditions.Add(new ConditionEvaluation { CriterionName = "Öğrenim Düzeyi / Bölüm",
            Status = status, IsInferred = evidence == null && !anyDegree, IsLikelyMismatch = explicitMismatch,
            RequiredValue = req.EducationSourceText, UserValue = $"{profile.EducationLevel} - {profile.Department} ({profile.GraduationStatus})",
            Explanation = status == ConditionStatus.Satisfied ? "Mezuniyet düzeyi ve bölüm şartı karşılanıyor." :
                evidence != null || anyDegree ? "Başvuru için mezuniyet durumunuz kontrol edilmelidir." :
                explicitMismatch ? "İlanın açık bölüm listesi profilinizle eşleşmiyor; büyük olasılıkla uygun değil. Resmî listeyi kontrol edin." :
                "Bölümün kabul edildiği metinden kesin çıkarılamadı; resmî mezuniyet listesi kontrol edilmelidir.",
            SourceText = evidence ?? req.EducationSourceText });
    }

    private static string Normalize(string? text) =>
        Regex.Replace((text ?? "").ToLower(new CultureInfo("tr-TR")), @"\s+", " ").Trim();

    private static bool DepartmentMatches(string clause, string name)
    {
        var normalized = Normalize(clause);
        var department = Normalize(name);
        if (department.Length < 4 || !Regex.IsMatch(normalized, @"mezun|diploma")) return false;
        if (Regex.IsMatch(normalized, @"olmamak|mezun olmamış|belgesi")) return false;
        // Require a degree/program delimiter after the complete department name.
        return Regex.IsMatch(normalized, @"(?<![\p{L}])" + Regex.Escape(department) +
            @"(?=\s*(?:[,;/()]|$|veya\b|ile\b|ve\b(?=\s+[\p{L}]+\s*(?:[,;]|\s+(?:bölüm|program|mezun|lisans|ön\s*lisans)))|ön\s*lisans|önlisans|lisans|bölüm|program|mezun|diploma)|(?:ndan|nden|dan|den|nın|nin|ları|leri)\b)");
    }
    private void EvaluateKpss(
        ExtractedRequirements posReq,
        ExtractedRequirements genReq,
        ProfileOptions profile,
        PositionEvaluation posEval)
    {
        var hasPositionKpss = posReq.RequiredKpssType != null || posReq.MinKpssScore != null || posReq.RequiredKpssYear != null || posReq.AllowedKpssYears.Count > 0 || posReq.MinKpssYear.HasValue || posReq.MaxKpssYear.HasValue;
        if ((posReq.ExplicitlyNoKpss && !hasPositionKpss) || (!hasPositionKpss && genReq.ExplicitlyNoKpss && genReq.RequiredKpssType == null && genReq.MinKpssScore == null))
        {
            posEval.Conditions.Add(new ConditionEvaluation
            {
                CriterionName = "KPSS Şartı",
                Status = ConditionStatus.Satisfied,
                RequiredValue = "Sınavsız / KPSS Aranmaz",
                UserValue = "Muaf",
                Explanation = "İlanda açık KPSS muafiyeti belirtilmiştir.",
                SourceText = posReq.ExplicitlyNoKpss ? posReq.NoKpssSourceText : genReq.NoKpssSourceText
            });
            return;
        }

        var reqType = posReq.RequiredKpssType?.Value ?? genReq.RequiredKpssType?.Value;
        var minScore = posReq.MinKpssScore?.Value ?? genReq.MinKpssScore?.Value;
        var reqYear = posReq.RequiredKpssYear?.Value ?? genReq.RequiredKpssYear?.Value;
        var yearReq = posReq.AllowedKpssYears.Count > 0 || posReq.MinKpssYear.HasValue || posReq.MaxKpssYear.HasValue || posReq.RequiredKpssYear != null ? posReq : genReq;
        bool YearMatches(int year) => (!yearReq.MinKpssYear.HasValue || year >= yearReq.MinKpssYear) &&
            (!yearReq.MaxKpssYear.HasValue || year <= yearReq.MaxKpssYear) &&
            (yearReq.AllowedKpssYears.Count == 0 || yearReq.AllowedKpssYears.Contains(year));
        var yearLabel = yearReq.AllowedKpssYears.Count > 0 ? string.Join(", ", yearReq.AllowedKpssYears.Order()) :
            yearReq.MinKpssYear.HasValue || yearReq.MaxKpssYear.HasValue ? $"{yearReq.MinKpssYear?.ToString() ?? "…"}–{yearReq.MaxKpssYear?.ToString() ?? "…"}" : "Serbest";
        var sourceText = posReq.MinKpssScore?.SourceText ?? posReq.RequiredKpssType?.SourceText ?? genReq.MinKpssScore?.SourceText ?? "";

        // If no KPSS condition was detected in text
        if (string.IsNullOrEmpty(reqType) && minScore == null && reqYear == null)
        {
            posEval.Conditions.Add(new ConditionEvaluation
            {
                CriterionName = "KPSS Şartı",
                Status = ConditionStatus.Unknown,
                RequiredValue = "KPSS Şartı Belirsiz",
                UserValue = "-",
                Explanation = "İlan metninden KPSS taban puanı veya türü çıkarılamadı. Kılavuzdan puan şartı doğrulanmalıdır."
            });
            return;
        }

        // Match user's KPSS scores
        if (reqType == null)
        {
            posEval.Conditions.Add(new ConditionEvaluation { CriterionName = "KPSS Puan Türü", Status = ConditionStatus.Unknown,
                IsInferred = true, Explanation = "KPSS puan türü belirtilmedi; resmî kılavuzdan doğrulayın.", SourceText = sourceText });
            return;
        }
        var targetType = reqType;
        static string ScoreType(string value) => Regex.Replace(value.ToUpperInvariant(), @"^KPSS\s*|\s", "");
        var matchingScore = profile.KpssScores.Where(s => ScoreType(s.ScoreType) == targetType && s.ExamYear >= 2000 &&
            double.IsFinite(s.Score) && s.Score >= 0 && s.Score <= 100 && YearMatches(s.ExamYear)).OrderByDescending(s => s.Score).FirstOrDefault();

        if (matchingScore == null)
        {
            var hasAnyScore = profile.KpssScores.Any(s => ScoreType(s.ScoreType) == targetType);
            if (hasAnyScore && (yearReq.AllowedKpssYears.Count > 0 || yearReq.MinKpssYear.HasValue || yearReq.MaxKpssYear.HasValue))
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "KPSS Sınav Yılı",
                    Status = ConditionStatus.Unsatisfied,
                    RequiredValue = $"{yearLabel} yılı {targetType}",
                    UserValue = string.Join(", ", profile.KpssScores.Select(s => $"{s.ExamYear} yılı ({s.ScoreType})")),
                    Explanation = $"İlanın kabul ettiği KPSS yılları: {yearLabel}.",
                    SourceText = sourceText
                });
            }
            else
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "KPSS Puanı",
                    Status = profile.KpssStatus == "Var" ? ConditionStatus.Unknown : ConditionStatus.Unsatisfied,
                    RequiredValue = $"{targetType} {(minScore.HasValue ? $"en az {minScore.Value}" : "")} {(reqYear.HasValue ? $"({reqYear.Value})" : "")}",
                    UserValue = "Puan kaydı bulunamadı",
                    Explanation = $"Profilinizde {targetType} türünde geçerli bir puan kaydı bulunmamaktadır.",
                    SourceText = sourceText
                });
            }
        }
        else
        {
            if (minScore.HasValue && matchingScore.Score < minScore.Value)
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "KPSS Taban Puanı",
                    Status = ConditionStatus.Unsatisfied,
                    RequiredValue = $"En az {minScore.Value} puan",
                    UserValue = $"{matchingScore.Score} puan",
                    Explanation = $"KPSS puanınız taban puanın altındadır (İstenen: {minScore.Value}, Mevcut: {matchingScore.Score}).",
                    SourceText = sourceText
                });
            }
            else
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "KPSS Puanı",
                    Status = ConditionStatus.Satisfied,
                    RequiredValue = $"{targetType} >= {minScore?.ToString() ?? "Taban"} ({yearLabel})",
                    UserValue = $"{matchingScore.ScoreType}: {matchingScore.Score} ({matchingScore.ExamYear})",
                    Explanation = "KPSS puanı ve sınav yılı şartı sağlanıyor.",
                    SourceText = sourceText
                });
            }
        }
    }

    private void EvaluateExperience(ExtractedRequirements posReq, ProfileOptions profile, PositionEvaluation posEval)
    {
        if (posReq.MaxExperienceMonths != null)
            posEval.Conditions.Add(new ConditionEvaluation { CriterionName = "Tecrübe Üst Sınırı",
                Status = !profile.Experience.IsKnown ? ConditionStatus.Unknown : profile.Experience.TotalMonths <= posReq.MaxExperienceMonths.Value ? ConditionStatus.Satisfied : ConditionStatus.Unsatisfied,
                RequiredValue = $"En çok {posReq.MaxExperienceMonths.Value} ay", UserValue = profile.Experience.IsKnown ? $"{profile.Experience.TotalMonths} ay" : "Bilinmiyor",
                Explanation = "Tecrübe üst sınırı ayrıca değerlendirildi.", SourceText = posReq.MaxExperienceMonths.SourceText });
        if (posReq.HasUnparsedExperience)
            posEval.Conditions.Add(new ConditionEvaluation { CriterionName = "Mesleki Tecrübe", Status = ConditionStatus.Unknown,
                IsInferred = true, Explanation = "Tecrübe şartı görüldü ancak süre kesin çıkarılamadı." });
        if ((posReq.MinExperienceMonths != null || posReq.MaxExperienceMonths != null) && Regex.IsMatch(Normalize(posReq.ExperienceSourceText), @"alanında|sektör|konusunda|bilişim|yazılım|bilgisayar"))
        {
            var fieldMatches = posReq.RequiredExperienceField != null && ExperienceFieldMatcher.Matches(posReq.RequiredExperienceField.Value, profile.Experience.Field);
            var verified = fieldMatches && profile.Experience.IsKnown && profile.Experience.IsDocumented;
            posEval.Conditions.Add(new ConditionEvaluation { CriterionName = "Tecrübe Alanı", Status = verified ? ConditionStatus.Satisfied : ConditionStatus.Unknown,
                IsInferred = !verified, RequiredValue = posReq.RequiredExperienceField?.Value ?? posReq.ExperienceSourceText, UserValue = profile.Experience.Field,
                SourceText = posReq.ExperienceSourceText, Explanation = verified ? "Belgelenebilir tecrübenizin alanı şartla eşleşiyor." : "Süreye ek olarak tecrübenin istenen alanda olduğu belgelerle doğrulanmalıdır." });
        }
        if (posReq.MinExperienceMonths != null && posReq.MinExperienceMonths.Value > 0)
        {
            var reqMonths = posReq.MinExperienceMonths.Value;
            var reqYears = reqMonths / 12.0;

            if (!profile.Experience.IsKnown)
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "Mesleki Tecrübe",
                    Status = ConditionStatus.Unknown,
                    RequiredValue = $"En az {reqMonths} ay ({reqYears:0.#} yıl) tecrübe",
                    UserValue = "Deneyim bilgisi belirtilmemiş",
                    Explanation = $"İlan {reqYears:0.#} yıl mesleki deneyim talep ediyor. Profilinizde deneyim belirtilmediğinden kontrol edilmelidir.",
                    SourceText = posReq.ExperienceSourceText
                });
            }
            else if (profile.Experience.TotalMonths >= reqMonths)
            {
                if (posReq.RequiresExperienceDocumentation && !profile.Experience.IsDocumented)
                {
                    posEval.Conditions.Add(new ConditionEvaluation
                    {
                        CriterionName = "Belgeli Tecrübe",
                        Status = ConditionStatus.Unknown,
                        RequiredValue = $"En az {reqYears:0.#} yıl SGK/çalışma belgesi",
                        UserValue = $"{profile.Experience.Years:0.#} yıl (Belge durumu belirsiz)",
                        Explanation = "Deneyim süresi yetiyor ancak ilanın istediği resmi belge/SGK dökümünün doğrulanması gereklidir.",
                        SourceText = posReq.ExperienceSourceText
                    });
                }
                else
                {
                    posEval.Conditions.Add(new ConditionEvaluation
                    {
                        CriterionName = "Mesleki Tecrübe",
                        Status = ConditionStatus.Satisfied,
                        RequiredValue = $"En az {reqYears:0.#} yıl",
                        UserValue = $"{profile.Experience.Years:0.#} yıl ({profile.Experience.Field})",
                        Explanation = "Tecrübe süresi şartı karşılanıyor.",
                        SourceText = posReq.ExperienceSourceText
                    });
                }
            }
            else
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "Mesleki Tecrübe",
                    Status = ConditionStatus.Unsatisfied,
                    RequiredValue = $"En az {reqMonths} ay ({reqYears:0.#} yıl)",
                    UserValue = $"{profile.Experience.TotalMonths} ay ({profile.Experience.Years:0.#} yıl)",
                    Explanation = $"Mesleki tecrübe süresi yetersizdir (İstenen: {reqYears:0.#} yıl, Mevcut: {profile.Experience.Years:0.#} yıl).",
                    SourceText = posReq.ExperienceSourceText
                });
            }
        }
    }

    private void EvaluateDrivingLicense(ExtractedRequirements posReq, ProfileOptions profile, PositionEvaluation posEval)
    {
        if (posReq.RequiredDrivingLicense != null)
        {
            var reqLicense = posReq.RequiredDrivingLicense.Value;
            var hasLicense = profile.DrivingLicenses.Any(l => DrivingLicenseCoverage.Covers(l, reqLicense));

            if (hasLicense)
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "Sürücü Belgesi",
                    Status = ConditionStatus.Satisfied,
                    RequiredValue = $"{reqLicense} Sınıfı Ehliyet",
                    UserValue = string.Join(", ", profile.DrivingLicenses),
                    Explanation = $"İstenen {reqLicense} sınıfı ehliyet profilinizde mevcuttur.",
                    SourceText = posReq.RequiredDrivingLicense.SourceText
                });
            }
            else
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "Sürücü Belgesi",
                    Status = ConditionStatus.Unsatisfied,
                    RequiredValue = $"{reqLicense} Sınıfı Ehliyet",
                    UserValue = profile.DrivingLicenses.Any() ? string.Join(", ", profile.DrivingLicenses) : "Ehliyet yok",
                    Explanation = $"Kadro için gerekli {reqLicense} sınıfı sürücü belgesi bulunamadı.",
                    SourceText = posReq.RequiredDrivingLicense.SourceText
                });
            }
        }
    }

    private void EvaluateAgeLimit(ExtractedRequirements posReq, ExtractedRequirements genReq, ProfileOptions profile, PositionEvaluation posEval, DateTime? referenceDate)
    {
        var req = posReq.MaxAgeLimit != null || posReq.HasUnparsedAge ? posReq : genReq;
        if (req.HasUnparsedAge)
            posEval.Conditions.Add(new ConditionEvaluation { CriterionName = "Yaş Sınırı", Status = ConditionStatus.Unknown,
                IsInferred = true, Explanation = "Yaş şartı görüldü ancak sınır kesin çıkarılamadı." });
        if (req.MaxAgeLimit == null) return;
        var condition = new ConditionEvaluation { CriterionName = "Yaş Sınırı",
            SourceText = req.MaxAgeLimit.SourceText, RequiredValue = req.MaxAgeLimit.SourceText };
        if (!profile.BirthDate.HasValue)
        {
            condition.Status = ConditionStatus.Unknown; condition.UserValue = "Doğum tarihi belirtilmemiş";
            condition.Explanation = "Doğum tarihi bilinmediği için yaş şartı kontrol edilmelidir.";
        }
        else
        {
            var date = req.AgeReferenceDate ?? AppTime.ToDisplay(referenceDate ?? DateTime.UtcNow).Date;
            var birth = profile.BirthDate.Value.Date;
            var age = date.Year - birth.Year; if (birth > date.AddYears(-age)) age--;
            condition.UserValue = $"{birth:dd.MM.yyyy} ({date:dd.MM.yyyy} tarihinde {age} yaşında)";
            if (req.EarliestBirthDate is DateTime boundary)
                condition.Status = (req.BirthDateBoundaryInclusive ? birth >= boundary : birth > boundary) ? ConditionStatus.Satisfied : ConditionStatus.Unsatisfied;
            else if (req.AgeCountsNextYear)
            {
                // Official examples disagree only on the exact cutoff birthday; explicit birth-date boundaries take precedence.
                var cutoff = date.AddYears(-(req.MaxAgeLimit.Value - 1));
                condition.Status = birth == cutoff ? ConditionStatus.Unknown : birth > cutoff ? ConditionStatus.Satisfied : ConditionStatus.Unsatisfied;
                condition.IsInferred = birth == cutoff;
            }
            else condition.Status = age < req.MaxAgeLimit.Value ? ConditionStatus.Satisfied : ConditionStatus.Unsatisfied;
            condition.Explanation = condition.Status switch {
                ConditionStatus.Satisfied => "İlandaki yaş/doğum tarihi sınırı karşılanıyor.",
                ConditionStatus.Unsatisfied => "İlandaki yaş/doğum tarihi sınırı karşılanmıyor.",
                _ => "Tam sınır doğum günündesiniz; 'gün almamış' ifadesinin kurum tarafından belirtilen doğum tarihi sınırı kontrol edilmelidir."
            };
        }
        posEval.Conditions.Add(condition);
    }
    private static void EvaluateMilitary(ExtractedRequirements posReq, ExtractedRequirements genReq, ProfileOptions profile, PositionEvaluation evaluation)
    {
        var condition = posReq.MilitaryCondition ?? genReq.MilitaryCondition;
        if (condition == null) return;
        var status = profile.MilitaryStatus switch
        {
            "Muaf / Yapıldı" => ConditionStatus.Satisfied,
            "Tecilli" when Regex.IsMatch(condition.Value, @"ertelen|tecil", RegexOptions.IgnoreCase) => ConditionStatus.Satisfied,
            "Yapılmadı" when !Regex.IsMatch(Normalize(condition.Value), @"ertelen|tecil") => ConditionStatus.Unsatisfied,
            _ => ConditionStatus.Unknown
        };
        evaluation.Conditions.Add(new ConditionEvaluation { CriterionName = "Askerlik", Status = status,
            RequiredValue = condition.Value, UserValue = profile.MilitaryStatus, SourceText = condition.SourceText,
            Explanation = status == ConditionStatus.Satisfied ? "Askerlik durumunuz uygun." : "Askerlik şartı ve durumunuz kontrol edilmelidir." });
    }

    private void EvaluateCertificates(ExtractedRequirements posReq, ProfileOptions profile, PositionEvaluation posEval)
    {
        if (posReq.RequiredCertificates.Any())
        {
            foreach (var cert in posReq.RequiredCertificates)
            {
                var userHasCert = profile.Certificates.Any(c => Normalize(c).Length >= 3 &&
                    Regex.IsMatch(Normalize(cert.Value), @"(?<![\p{L}\p{N}])" + Regex.Escape(Normalize(c)) + @"(?![\p{L}\p{N}])") &&
                    !Regex.IsMatch(Normalize(cert.Value), @"olmamak|sahip olmamış|aranma"));
                if (userHasCert)
                {
                    posEval.Conditions.Add(new ConditionEvaluation
                    {
                        CriterionName = "Sertifika / Belge",
                        Status = ConditionStatus.Satisfied,
                        RequiredValue = cert.Value,
                        UserValue = "Sertifika mevcut",
                        Explanation = "İstenen sertifika/belge profilinizde mevcuttur.",
                        SourceText = cert.SourceText
                    });
                }
                else
                {
                    posEval.Conditions.Add(new ConditionEvaluation
                    {
                        CriterionName = "Sertifika / Belge",
                        Status = ConditionStatus.Unknown,
                        RequiredValue = cert.Value,
                        UserValue = "Profilde belirtilmemiş",
                        Explanation = "İlan özel bir sertifika/belge şartı içermektedir. Belgeye sahip olup olmadığınızı kontrol ediniz.",
                        SourceText = cert.SourceText
                    });
                }
            }
        }
    }

    private static void EvaluateWorkPreferences(string text, ProfileOptions profile, PositionEvaluation evaluation, string rawText)
    {
        if (profile.WorkPreferences.Count == 0) return;
        var types = new[] { "Sözleşmeli", "Kadrolu", "İşçi", "Geçici" }.Where(type => Regex.IsMatch(Normalize(text), @"\b" + Normalize(type) + @"\b")).ToList();
        if (types.Count == 0) types = new[] { "Sözleşmeli", "Kadrolu", "İşçi", "Geçici" }
            .Where(type => Regex.IsMatch(Normalize(rawText), @"\b" + Normalize(type) + @"\s+(?:personel|işçi|statü|olarak|alım)")).ToList();
        if (types.Count == 0) return;
        var matches = types.Any(type => profile.WorkPreferences.Contains(type, StringComparer.OrdinalIgnoreCase));
        evaluation.Conditions.Add(new ConditionEvaluation
        {
            CriterionName = "Çalışma Tercihi", IsPreference = true, Status = matches ? ConditionStatus.Satisfied : ConditionStatus.Unsatisfied,
            RequiredValue = string.Join(", ", types), UserValue = string.Join(", ", profile.WorkPreferences),
            Explanation = matches ? "İlanın çalışma türü tercihlerinizle eşleşiyor." : "İlanın çalışma türü tercihlerinizle eşleşmiyor."
        });
    }

    private void EvaluateCityPreferences(AltIlanResponse altIlan, ProfileOptions profile, PositionEvaluation posEval)
    {
        if (profile.CityPreferences != null && profile.CityPreferences.Any() && altIlan.KontenjanList != null && altIlan.KontenjanList.Any())
        {
            var matchingCity = altIlan.KontenjanList.Any(k => profile.CityPreferences.Any(p => k.Il != null && Regex.IsMatch(Normalize(k.Il), @"^" + Regex.Escape(Normalize(p)) + @"(?:$|\s*[/,(])")));
            if (!matchingCity)
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "Şehir Tercihi",
                    IsPreference = true,
                    Status = ConditionStatus.Unsatisfied,
                    RequiredValue = string.Join(", ", profile.CityPreferences),
                    UserValue = posEval.Cities ?? "",
                    Explanation = "Kadro kontenjanı tercih ettiğiniz iller dışında kalmaktadır."
                });
            }
        }
    }

    private void AggregateOverallStatus(PositionEvaluation posEval)
    {
        if (posEval.Conditions.Any(c => !c.IsPreference && !c.IsInferred && c.Status == ConditionStatus.Unsatisfied))
        {
            posEval.Status = EligibilityStatus.Ineligible;
            var failedConds = posEval.Conditions.Where(c => !c.IsPreference && c.Status == ConditionStatus.Unsatisfied).ToList();
            posEval.SummaryReason = string.Join("; ", failedConds.Select(f => $"{f.CriterionName}: {f.Explanation}"));
        }
        else if (posEval.Conditions.Any(c => c.IsLikelyMismatch))
        {
            posEval.Status = EligibilityStatus.LikelyIneligible;
            posEval.SummaryReason = string.Join("; ", posEval.Conditions.Where(c => c.IsLikelyMismatch).Select(c => c.Explanation));
        }
        else if (posEval.Conditions.Any(c => !c.IsPreference && (c.Status == ConditionStatus.Unknown || c.IsInferred && c.Status == ConditionStatus.Unsatisfied)))
        {
            posEval.Status = EligibilityStatus.NeedsReview;
            var unknownConds = posEval.Conditions.Where(c => c.Status == ConditionStatus.Unknown).ToList();
            posEval.SummaryReason = string.Join("; ", unknownConds.Select(u => $"{u.CriterionName}: {u.Explanation}"));
        }
        else
        {
            posEval.Status = EligibilityStatus.Eligible;
            posEval.SummaryReason = "Tüm zorunlu koşullar ve bölüm kriteri tam olarak karşılanıyor.";
        }
    }
}
