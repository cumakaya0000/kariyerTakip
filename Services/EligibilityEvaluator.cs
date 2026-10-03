using System.Globalization;
using KariyerTakip.Models;

namespace KariyerTakip.Services;

public class EligibilityEvaluator
{
    private readonly RequirementExtractor _extractor;

    public EligibilityEvaluator(RequirementExtractor extractor)
    {
        _extractor = extractor;
    }

    public PositionEvaluation EvaluatePosition(
        AltIlanResponse altIlan,
        string generalAnnouncementText,
        ProfileOptions profile)
    {
        var posEval = new PositionEvaluation
        {
            PositionTitle = altIlan.IlanBaslik ?? "Belirtilmemiş Kadro",
            Unvan = altIlan.Unvan,
            TotalQuota = altIlan.KontenjanList?.Sum(x => x.Kontenjan) ?? 0,
            Cities = altIlan.KontenjanList != null && altIlan.KontenjanList.Any()
                ? string.Join(", ", altIlan.KontenjanList.Select(k => $"{k.Il} ({k.Kontenjan})"))
                : "Belirtilmemiş"
        };

        var positionText = altIlan.IlanMetni ?? string.Empty;
        var combinedText = $"{altIlan.IlanBaslik} \n {altIlan.Unvan} \n {positionText} \n {generalAnnouncementText}";

        var req = _extractor.Extract(combinedText);
        posEval.ExtractedDepartmentText = string.Join(", ", req.MentionedDepartments);
        posEval.ExtractedKpssText = req.MinKpssScore.HasValue
            ? $"{req.RequiredKpssType ?? "KPSS"} en az {req.MinKpssScore} ({req.RequiredKpssYear?.ToString() ?? "Yıl serbest"})"
            : (req.RequiredKpssType ?? "KPSS belirtilmemiş");
        posEval.ExtractedExperienceText = req.MinExperienceYears.HasValue
            ? $"En az {req.MinExperienceYears} yıl tecrübe"
            : "Tecrübe şartı yok / belirtilmemiş";

        // 1. Department & Degree Evaluation
        var lowerComb = combinedText.ToLower(new CultureInfo("tr-TR"));
        var lowerProfileDept = profile.Department.ToLower(new CultureInfo("tr-TR"));

        var isDeptDirectMatch = lowerComb.Contains(lowerProfileDept) ||
                                lowerComb.Contains("bilgisayar programcılığı") ||
                                lowerComb.Contains("bilgisayar teknolojisi ve programlama") ||
                                req.HasAnyAssociateDegree;

        var isTitleKeywordsMatch = lowerComb.Contains("bilgisayar teknikeri") ||
                                   lowerComb.Contains("programcı") ||
                                   lowerComb.Contains("bilgi işlem") ||
                                   lowerComb.Contains("bilişim personeli");

        if (req.HasBachelorDegreeRequirement && !req.HasAssociateDegreeRequirement && !isDeptDirectMatch)
        {
            posEval.Conditions.Add(new ConditionEvaluation
            {
                CriterionName = "Öğrenim Düzeyi / Bölüm",
                Status = ConditionStatus.Unsatisfied,
                RequiredValue = "Lisans Mezuniyeti",
                UserValue = $"{profile.EducationLevel} - {profile.Department}",
                Explanation = "Kadro yalnızca lisans mezuniyeti gerektirmektedir."
            });
        }
        else if (isDeptDirectMatch)
        {
            posEval.Conditions.Add(new ConditionEvaluation
            {
                CriterionName = "Öğrenim Düzeyi / Bölüm",
                Status = ConditionStatus.Satisfied,
                RequiredValue = req.HasAnyAssociateDegree ? "Herhangi bir ön lisans" : profile.Department,
                UserValue = $"{profile.EducationLevel} - {profile.Department}",
                Explanation = req.HasAnyAssociateDegree
                    ? "Kadro tüm ön lisans mezunlarını kabul ediyor."
                    : $"Kadro '{profile.Department}' bölümünü doğrudan karşılıyor."
            });
        }
        else if (isTitleKeywordsMatch)
        {
            posEval.Conditions.Add(new ConditionEvaluation
            {
                CriterionName = "Öğrenim Düzeyi / Bölüm",
                Status = ConditionStatus.Unknown,
                RequiredValue = "Bilişim / Programcı İlgili Kadro",
                UserValue = $"{profile.EducationLevel} - {profile.Department}",
                Explanation = "Kadro unvanı/açıklaması bilişim ile ilgili, ancak detaylı bölüm listesi kontrol edilmeli."
            });
        }
        else
        {
            posEval.Conditions.Add(new ConditionEvaluation
            {
                CriterionName = "Öğrenim Düzeyi / Bölüm",
                Status = ConditionStatus.Unsatisfied,
                RequiredValue = "İlgili Bölüm",
                UserValue = profile.Department,
                Explanation = "Kadro açıklamasında uygun bölüm şartı bulunamadı."
            });
        }

        // 2. KPSS Evaluation
        if (req.MinKpssScore.HasValue || !string.IsNullOrWhiteSpace(req.RequiredKpssType))
        {
            var targetType = req.RequiredKpssType ?? "P93";
            var userScore = profile.KpssScores.FirstOrDefault(s => s.ScoreType.Equals(targetType, StringComparison.OrdinalIgnoreCase) ||
                                                                  (targetType == "P93" && s.ScoreType.Contains("93")));

            if (userScore == null)
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "KPSS Puanı",
                    Status = profile.KpssStatus == "Var" ? ConditionStatus.Unknown : ConditionStatus.Unsatisfied,
                    RequiredValue = $"{targetType} Puanı {(req.MinKpssScore.HasValue ? $"en az {req.MinKpssScore}" : "")}",
                    UserValue = "Puan kaydı bulunamadı",
                    Explanation = $"Profilde {targetType} türünde puan tanımlı değil."
                });
            }
            else
            {
                var yearMatches = !req.RequiredKpssYear.HasValue || userScore.ExamYear == req.RequiredKpssYear.Value;
                var scoreMatches = !req.MinKpssScore.HasValue || userScore.Score >= req.MinKpssScore.Value;

                if (yearMatches && scoreMatches)
                {
                    posEval.Conditions.Add(new ConditionEvaluation
                    {
                        CriterionName = "KPSS Puanı",
                        Status = ConditionStatus.Satisfied,
                        RequiredValue = $"{targetType} >= {req.MinKpssScore?.ToString() ?? "Taban puan"} ({req.RequiredKpssYear?.ToString() ?? "Tüm yıllar"})",
                        UserValue = $"{userScore.ScoreType}: {userScore.Score} ({userScore.ExamYear})",
                        Explanation = "KPSS puanı ve sınav yılı şartını karşılıyor."
                    });
                }
                else if (!scoreMatches)
                {
                    posEval.Conditions.Add(new ConditionEvaluation
                    {
                        CriterionName = "KPSS Puanı",
                        Status = ConditionStatus.Unsatisfied,
                        RequiredValue = $"En az {req.MinKpssScore}",
                        UserValue = $"{userScore.Score}",
                        Explanation = $"KPSS puanı yetersiz (İstenen: {req.MinKpssScore}, Mevcut: {userScore.Score})."
                    });
                }
                else
                {
                    posEval.Conditions.Add(new ConditionEvaluation
                    {
                        CriterionName = "KPSS Sınav Yılı",
                        Status = ConditionStatus.Unsatisfied,
                        RequiredValue = $"{req.RequiredKpssYear} yılı KPSS",
                        UserValue = $"{userScore.ExamYear} yılı",
                        Explanation = "İlan farklı bir KPSS sınav yılı talep ediyor."
                    });
                }
            }
        }
        else
        {
            posEval.Conditions.Add(new ConditionEvaluation
            {
                CriterionName = "KPSS Şartı",
                Status = ConditionStatus.Satisfied,
                RequiredValue = "Belirtilmemiş / Sınavsız",
                UserValue = "-",
                Explanation = "Kadro için özel bir KPSS taban puanı tespit edilmedi."
            });
        }

        // 3. Experience Evaluation
        if (req.MinExperienceYears.HasValue && req.MinExperienceYears.Value > 0)
        {
            if (!profile.Experience.IsKnown)
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "Mesleki Tecrübe",
                    Status = ConditionStatus.Unknown,
                    RequiredValue = $"En az {req.MinExperienceYears} yıl tecrübe",
                    UserValue = "Deneyim bilgisi girilmemiş",
                    Explanation = $"İlan {req.MinExperienceYears} yıl deneyim istiyor, profilinizde tecrübe detayı belirtilmediğinden kontrol edilmelidir."
                });
            }
            else if (profile.Experience.Years >= req.MinExperienceYears.Value)
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "Mesleki Tecrübe",
                    Status = ConditionStatus.Satisfied,
                    RequiredValue = $"En az {req.MinExperienceYears} yıl",
                    UserValue = $"{profile.Experience.Years} yıl ({profile.Experience.Field})",
                    Explanation = "Tecrübe süresi şartını sağlıyor."
                });
            }
            else
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "Mesleki Tecrübe",
                    Status = ConditionStatus.Unsatisfied,
                    RequiredValue = $"En az {req.MinExperienceYears} yıl",
                    UserValue = $"{profile.Experience.Years} yıl",
                    Explanation = "Tecrübe süresi yetersiz."
                });
            }
        }

        // 4. City Preferences
        if (profile.CityPreferences != null && profile.CityPreferences.Any() && altIlan.KontenjanList != null && altIlan.KontenjanList.Any())
        {
            var matchingCity = altIlan.KontenjanList.Any(k => profile.CityPreferences.Any(p => k.Il != null && k.Il.Contains(p, StringComparison.OrdinalIgnoreCase)));
            if (!matchingCity)
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "Şehir Tercihi",
                    Status = ConditionStatus.Unsatisfied,
                    RequiredValue = string.Join(", ", profile.CityPreferences),
                    UserValue = posEval.Cities ?? "",
                    Explanation = "Kadro kontenjanı tercih ettiğiniz iller dışında kalmaktadır."
                });
            }
        }

        // Aggregate Overall Status
        if (posEval.Conditions.Any(c => c.Status == ConditionStatus.Unsatisfied))
        {
            posEval.Status = EligibilityStatus.Ineligible;
            var failedCond = posEval.Conditions.First(c => c.Status == ConditionStatus.Unsatisfied);
            posEval.SummaryReason = $"{failedCond.CriterionName} şartı sağlanmıyor: {failedCond.Explanation}";
        }
        else if (posEval.Conditions.Any(c => c.Status == ConditionStatus.Unknown))
        {
            posEval.Status = EligibilityStatus.NeedsReview;
            var unknownCond = posEval.Conditions.First(c => c.Status == ConditionStatus.Unknown);
            posEval.SummaryReason = $"Kontrol gerekli: {unknownCond.Explanation}";
        }
        else
        {
            posEval.Status = EligibilityStatus.Eligible;
            posEval.SummaryReason = "Tüm zorunlu koşullar ve bölüm kriteri tam olarak karşılanıyor.";
        }

        return posEval;
    }
}
