using System.Globalization;
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

        // Extract general requirements (like general age/military) from general text
        var genReq = _extractor.Extract(generalAnnouncementText);

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
        EvaluateDepartmentAndDegree(posReq, genReq, profile, positionSpecificText, posEval);

        // -------------------------------------------------------------
        // 2. KPSS Evaluation
        // -------------------------------------------------------------
        EvaluateKpss(posReq, genReq, profile, posEval);

        // -------------------------------------------------------------
        // 3. Experience Evaluation
        // -------------------------------------------------------------
        EvaluateExperience(posReq, profile, posEval);

        // -------------------------------------------------------------
        // 4. Driving License Evaluation
        // -------------------------------------------------------------
        EvaluateDrivingLicense(posReq, profile, posEval);

        // -------------------------------------------------------------
        // 5. Age Limit Evaluation
        // -------------------------------------------------------------
        EvaluateAgeLimit(posReq, genReq, profile, posEval, referenceDate);
        EvaluateMilitary(posReq, genReq, profile, posEval);

        // -------------------------------------------------------------
        // 6. Certificates Evaluation
        // -------------------------------------------------------------
        EvaluateCertificates(posReq, profile, posEval);

        // -------------------------------------------------------------
        // 7. City Preferences Evaluation
        // -------------------------------------------------------------
        EvaluateCityPreferences(altIlan, profile, posEval);
        EvaluateWorkPreferences(positionSpecificText + "\n" + generalAnnouncementText, profile, posEval);

        // -------------------------------------------------------------
        // Overall Decision Aggregation
        // -------------------------------------------------------------
        AggregateOverallStatus(posEval);

        return posEval;
    }

    private void EvaluateDepartmentAndDegree(
        ExtractedRequirements posReq,
        ExtractedRequirements genReq,
        ProfileOptions profile,
        string positionSpecificText,
        PositionEvaluation posEval)
    {
        if (string.IsNullOrWhiteSpace(profile.Department))
        {
            posEval.Conditions.Add(new ConditionEvaluation
            {
                CriterionName = "Öğrenim Düzeyi / Bölüm",
                Status = ConditionStatus.Unknown,
                RequiredValue = "Bölüm Bilgisi",
                UserValue = "Profilde bölüm adı boş bırakılmış",
                Explanation = "Profilinizde bölüm tanımlanmadığı için eşleştirme yapılamadı."
            });
            return;
        }

        var lowerProfileDept = profile.Department.ToLower(new CultureInfo("tr-TR")).Trim();
        var lowerProfileLevel = (profile.EducationLevel ?? "Ön Lisans").ToLower(new CultureInfo("tr-TR")).Trim();
        var lowerPosText = positionSpecificText.ToLower(new CultureInfo("tr-TR"));

        var isUserAssociate = lowerProfileLevel.Contains("ön lisans") || lowerProfileLevel.Contains("önlisans");
        var isUserBachelor = lowerProfileLevel.Contains("lisans") && !isUserAssociate;

        // Check if position strictly requires Bachelor while user is Associate
        var isBachelorOnly = posReq.HasBachelorDegreeRequirement && !posReq.HasAssociateDegreeRequirement;
        if (isUserAssociate && isBachelorOnly)
        {
            posEval.Conditions.Add(new ConditionEvaluation
            {
                CriterionName = "Öğrenim Düzeyi",
                Status = ConditionStatus.Unsatisfied,
                RequiredValue = "Lisans (4 Yıllık)",
                UserValue = profile.EducationLevel ?? string.Empty,
                Explanation = "Kadro yalnızca lisans mezuniyeti gerektirmektedir.",
                SourceText = posReq.EducationSourceText
            });
            return;
        }

        // Direct department match in position text
        var isDeptDirectMatch = lowerPosText.Contains(lowerProfileDept) ||
                                (lowerProfileDept.Contains("bilgisayar") && (lowerPosText.Contains("bilgisayar programcılığı") || lowerPosText.Contains("bilgisayar teknolojisi ve programlama"))) ||
                                posReq.HasAnyAssociateDegree;

        var matchingDeptCond = posReq.MentionedDepartments.FirstOrDefault(d => lowerProfileDept.Contains(d.Value) || d.Value.Contains(lowerProfileDept));

        var isTitleKeywordsMatch = lowerPosText.Contains("bilgisayar teknikeri") ||
                                   lowerPosText.Contains("programcı") ||
                                   lowerPosText.Contains("bilgi işlem") ||
                                   lowerPosText.Contains("bilişim personeli");

        if (isDeptDirectMatch)
        {
            posEval.Conditions.Add(new ConditionEvaluation
            {
                CriterionName = "Öğrenim Düzeyi / Bölüm",
                Status = ConditionStatus.Satisfied,
                RequiredValue = posReq.HasAnyAssociateDegree ? "Herhangi bir ön lisans" : profile.Department,
                UserValue = $"{profile.EducationLevel} - {profile.Department}",
                Explanation = posReq.HasAnyAssociateDegree
                    ? "Kadro tüm ön lisans mezunlarını kabul ediyor."
                    : $"Kadro '{profile.Department}' bölümünü doğrudan karşılıyor.",
                SourceText = matchingDeptCond?.SourceText ?? posReq.EducationSourceText
            });
        }
        else if (isTitleKeywordsMatch)
        {
            posEval.Conditions.Add(new ConditionEvaluation
            {
                CriterionName = "Öğrenim Düzeyi / Bölüm",
                Status = ConditionStatus.Unknown,
                RequiredValue = "Bilişim / Tekniker İlgili Bölüm",
                UserValue = $"{profile.EducationLevel} - {profile.Department}",
                Explanation = "Kadro unvanı bilişim/tekniker ile ilgili, ancak tam mezuniyet listesi için resmî kılavuz kontrol edilmelidir.",
                SourceText = posReq.EducationSourceText
            });
        }
        else
        {
            posEval.Conditions.Add(new ConditionEvaluation
            {
                CriterionName = "Öğrenim Düzeyi / Bölüm",
                Status = ConditionStatus.Unsatisfied,
                RequiredValue = "İlgili Bölüm Mezuniyeti",
                UserValue = $"{profile.EducationLevel} - {profile.Department}",
                Explanation = "Kadro metninde bölümünüze uygun mezuniyet şartı tespit edilemedi.",
                SourceText = posReq.EducationSourceText
            });
        }
    }

    private void EvaluateKpss(
        ExtractedRequirements posReq,
        ExtractedRequirements genReq,
        ProfileOptions profile,
        PositionEvaluation posEval)
    {
        if (posReq.ExplicitlyNoKpss || genReq.ExplicitlyNoKpss)
        {
            posEval.Conditions.Add(new ConditionEvaluation
            {
                CriterionName = "KPSS Şartı",
                Status = ConditionStatus.Satisfied,
                RequiredValue = "Sınavsız / KPSS Aranmaz",
                UserValue = "Muaf",
                Explanation = "İlanda KPSS şartı aranmamaktadır (Sınavsız alım)."
            });
            return;
        }

        var reqType = posReq.RequiredKpssType?.Value ?? genReq.RequiredKpssType?.Value;
        var minScore = posReq.MinKpssScore?.Value ?? genReq.MinKpssScore?.Value;
        var reqYear = posReq.RequiredKpssYear?.Value ?? genReq.RequiredKpssYear?.Value;
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
        var targetType = reqType ?? "P93";
        var matchingScore = profile.KpssScores.FirstOrDefault(s =>
            (s.ScoreType.Equals(targetType, StringComparison.OrdinalIgnoreCase) ||
             (targetType == "P93" && s.ScoreType.Contains("93")) ||
             (targetType == "P3" && s.ScoreType.Contains("3") && !s.ScoreType.Contains("93"))) &&
            (!reqYear.HasValue || s.ExamYear == reqYear.Value));

        if (matchingScore == null)
        {
            var hasAnyScore = profile.KpssScores.Any(s => s.ScoreType.Equals(targetType, StringComparison.OrdinalIgnoreCase));
            if (hasAnyScore && reqYear.HasValue)
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "KPSS Sınav Yılı",
                    Status = ConditionStatus.Unsatisfied,
                    RequiredValue = $"{reqYear.Value} yılı {targetType}",
                    UserValue = string.Join(", ", profile.KpssScores.Select(s => $"{s.ExamYear} yılı ({s.ScoreType})")),
                    Explanation = $"İlan {reqYear.Value} KPSS sınav sonucunu şart koşmaktadır.",
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
                    RequiredValue = $"{targetType} >= {minScore?.ToString() ?? "Taban"} {(reqYear.HasValue ? $"({reqYear.Value})" : "")}",
                    UserValue = $"{matchingScore.ScoreType}: {matchingScore.Score} ({matchingScore.ExamYear})",
                    Explanation = "KPSS puanı ve sınav yılı şartı sağlanıyor.",
                    SourceText = sourceText
                });
            }
        }
    }

    private void EvaluateExperience(ExtractedRequirements posReq, ProfileOptions profile, PositionEvaluation posEval)
    {
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
            var hasLicense = profile.DrivingLicenses.Any(l => l.Equals(reqLicense, StringComparison.OrdinalIgnoreCase));

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
        var maxAgeLimit = posReq.MaxAgeLimit?.Value ?? genReq.MaxAgeLimit?.Value;
        var sourceText = posReq.MaxAgeLimit?.SourceText ?? genReq.MaxAgeLimit?.SourceText ?? "";

        if (maxAgeLimit.HasValue && !profile.BirthDate.HasValue)
        {
            posEval.Conditions.Add(new ConditionEvaluation { CriterionName = "Yaş Sınırı", Status = ConditionStatus.Unknown,
                RequiredValue = $"{maxAgeLimit} yaş", UserValue = "Doğum tarihi belirtilmemiş",
                Explanation = "Yaş şartı var; doğum tarihi bilinmediği için kontrol edilmelidir.", SourceText = sourceText });
        }

        if (maxAgeLimit.HasValue && profile.BirthDate.HasValue)
        {
            var today = AppTime.ToDisplay(referenceDate ?? DateTime.UtcNow).Date;
            var userAge = today.Year - profile.BirthDate.Value.Year;
            if (profile.BirthDate.Value.Date > today.AddYears(-userAge)) userAge--;

            if (userAge < maxAgeLimit.Value)
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "Yaş Sınırı",
                    Status = ConditionStatus.Satisfied,
                    RequiredValue = $"{maxAgeLimit.Value} yaşını doldurmamış olmak",
                    UserValue = $"{userAge} yaşında",
                    Explanation = $"Yaş sınırına uygunsunuz ({userAge} < {maxAgeLimit.Value}).",
                    SourceText = sourceText
                });
            }
            else
            {
                posEval.Conditions.Add(new ConditionEvaluation
                {
                    CriterionName = "Yaş Sınırı",
                    Status = ConditionStatus.Unsatisfied,
                    RequiredValue = $"{maxAgeLimit.Value} yaşını doldurmamış olmak",
                    UserValue = $"{userAge} yaşında",
                    Explanation = $"Yaş sınırı aşılmıştır ({userAge} >= {maxAgeLimit.Value}).",
                    SourceText = sourceText
                });
            }
        }
    }

    private static void EvaluateMilitary(ExtractedRequirements posReq, ExtractedRequirements genReq, ProfileOptions profile, PositionEvaluation evaluation)
    {
        var condition = posReq.MilitaryCondition ?? genReq.MilitaryCondition;
        if (condition == null) return;
        var status = profile.MilitaryStatus switch
        {
            "Muaf / Yapıldı" => ConditionStatus.Satisfied,
            "Yapılmadı" => ConditionStatus.Unsatisfied,
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
                var userHasCert = profile.Certificates.Any(c => !string.IsNullOrWhiteSpace(c) &&
                    cert.Value.Contains(c.Trim(), StringComparison.OrdinalIgnoreCase));
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

    private static void EvaluateWorkPreferences(string text, ProfileOptions profile, PositionEvaluation evaluation)
    {
        if (profile.WorkPreferences.Count == 0) return;
        var types = new[] { "Sözleşmeli", "Kadrolu", "İşçi", "Geçici" }.Where(type => text.Contains(type, StringComparison.OrdinalIgnoreCase)).ToList();
        if (types.Count == 0) return;
        var matches = types.Any(type => profile.WorkPreferences.Contains(type, StringComparer.OrdinalIgnoreCase));
        evaluation.Conditions.Add(new ConditionEvaluation
        {
            CriterionName = "Çalışma Tercihi", Status = matches ? ConditionStatus.Satisfied : ConditionStatus.Unsatisfied,
            RequiredValue = string.Join(", ", types), UserValue = string.Join(", ", profile.WorkPreferences),
            Explanation = matches ? "İlanın çalışma türü tercihlerinizle eşleşiyor." : "İlanın çalışma türü tercihlerinizle eşleşmiyor."
        });
    }

    private void EvaluateCityPreferences(AltIlanResponse altIlan, ProfileOptions profile, PositionEvaluation posEval)
    {
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
    }

    private void AggregateOverallStatus(PositionEvaluation posEval)
    {
        if (posEval.Conditions.Any(c => c.Status == ConditionStatus.Unsatisfied))
        {
            posEval.Status = EligibilityStatus.Ineligible;
            var failedConds = posEval.Conditions.Where(c => c.Status == ConditionStatus.Unsatisfied).ToList();
            posEval.SummaryReason = string.Join("; ", failedConds.Select(f => $"{f.CriterionName}: {f.Explanation}"));
        }
        else if (posEval.Conditions.Any(c => c.Status == ConditionStatus.Unknown))
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
