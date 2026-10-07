using System.Text.Json;
using KariyerTakip.Models;
using KariyerTakip.Services;
using Xunit;

namespace KariyerTakip.Tests;

public sealed class EligibilityCorrectionsTests
{
    private static ExtractedRequirements Extract(string text) => new RequirementExtractor(new DocumentReader()).Extract(text);
    private static PositionEvaluation Evaluate(string text, ProfileOptions? profile = null, string general = "")
    {
        var reader = new DocumentReader();
        return new EligibilityEvaluator(new RequirementExtractor(reader), reader).EvaluatePosition(
            new AltIlanResponse { IlanBaslik = "Kadro", IlanMetni = text }, general,
            profile ?? new ProfileOptions { Department = "Muhasebe", EducationLevel = "Ön Lisans" });
    }
    [Theory]
    [InlineData("En az üç (3) yıl mesleki deneyim", 36)]
    [InlineData("En az 5 yıllık tecrübe", 60)]
    [InlineData("En az 3 (üç) yıl deneyim", 36)]
    [InlineData("En az altı (6) ay tecrübe", 6)]
    public void ExperienceVariants(string text, int months) => Assert.Equal(months, Extract(text).MinExperienceMonths!.Value);
    [Fact]
    public void MaximumExperienceIsNotMinimum()
    {
        var requirements = Extract("En çok 5 yıllık tecrübe.");
        Assert.Null(requirements.MinExperienceMonths); Assert.Equal(60, requirements.MaxExperienceMonths!.Value);
        var result = Evaluate("Muhasebe ön lisans mezunu olmak. KPSS şartı aranmaz. En çok 5 yıllık tecrübe.",
            new ProfileOptions { Department = "Muhasebe", Experience = new() { IsKnown = true, TotalMonths = 72 } });
        Assert.Equal(EligibilityStatus.Ineligible, result.Status);
    }

    [Theory]
    [InlineData("35 yaşından gün almamış olmak")]
    [InlineData("otuz beş (35) yaşını doldurmamış olmak")]
    [InlineData("35 (otuz beş) yaşını tamamlamamış olmak")]
    public void AgeVariants(string text) => Assert.Equal(35, Extract(text).MaxAgeLimit!.Value);

    [Fact]
    public void GenericExamFreePhraseDoesNotOverridePositionScore()
    {
        var result = Evaluate("Muhasebe ön lisans mezunu olmak. KPSS P93 en az 75 puan almış olmak.",
            new ProfileOptions { Department = "Muhasebe", KpssStatus = "Var", KpssScores = new() { new() { ScoreType = "P93", ExamYear = 2024, Score = 70 } } },
            "Sınavsız alım. KPSS şartı aranmamaktadır.");
        Assert.Equal(EligibilityStatus.Ineligible, result.Status);
    }
    [Fact]
    public void CommonExemptionIsRecognized() => Assert.True(Extract("KPSS şartı aranmamaktadır.").ExplicitlyNoKpss);

    [Theory]
    [InlineData("2023, 2024 yılı KPSS P93 en az 75 puan almış olmak", 2023)]
    [InlineData("2022 ve sonrası KPSS P93 en az 75 puan almış olmak", 2024)]
    [InlineData("2022-2024 KPSS P93 en az 75 puan almış olmak", 2023)]
    public void HighestScoreFromAcceptedYears(string rule, int year)
    {
        var profile = new ProfileOptions { Department = "Muhasebe", KpssStatus = "Var", KpssScores = new() {
            new() { ScoreType = "P93", ExamYear = year, Score = 70 }, new() { ScoreType = "KPSS P93", ExamYear = year, Score = 80 } } };
        Assert.Equal(EligibilityStatus.Eligible, Evaluate("Muhasebe ön lisans mezunu olmak. " + rule, profile).Status);
    }
    [Fact]
    public void ScoreTypeDoesNotMatchSubstring()
    {
        var result = Evaluate("Herhangi bir lisans mezunu olmak. KPSS P3 en az 70 puan almış olmak.",
            new ProfileOptions { Department = "Muhasebe", EducationLevel = "Lisans", KpssStatus = "Yok",
                KpssScores = new() { new() { ScoreType = "P123", ExamYear = 2024, Score = 90 } } });
        Assert.Equal(EligibilityStatus.Ineligible, result.Status);
    }
    [Fact]
    public void ExperienceAndInstitutionAreNotDegreeRequirements()
    {
        var req = Extract("Tıp Fakültesi. Muhasebe ön lisans mezunu olmak. En az 4 yıllık deneyim.");
        Assert.False(req.HasBachelorDegreeRequirement); Assert.True(req.HasAssociateDegreeRequirement);
    }
    [Fact]
    public void BroadDepartmentDoesNotMatchSpecificProgram() => Assert.Equal(ConditionStatus.Unknown,
        Evaluate("Bilgisayar Mühendisliği lisans mezunu olmak. KPSS şartı aranmaz.",
            new ProfileOptions { Department = "Bilgisayar", EducationLevel = "Lisans" }).Conditions.Single(c => c.CriterionName.Contains("Bölüm")).Status);

    [Fact]
    public void ShortCertificateDoesNotSatisfyRequirement() => Assert.Contains(
        Evaluate("Muhasebe ön lisans mezunu olmak. CCNA sertifikasına sahip olmak. KPSS şartı aranmaz.",
            new ProfileOptions { Department = "Muhasebe", Certificates = new() { "A", "B" } }).Conditions,
        c => c.CriterionName == "Sertifika / Belge" && c.Status == ConditionStatus.Unknown);

    [Fact]
    public void StudentDoesNotMeetGraduationRequirement() => Assert.Equal(EligibilityStatus.Ineligible,
        Evaluate("Herhangi bir ön lisans mezunu olmak. KPSS şartı aranmaz.", new ProfileOptions { GraduationStatus = "Öğrenci" }).Status);

    [Fact]
    public void ConflictingRulesRequireReview() => Assert.Equal(EligibilityStatus.NeedsReview,
        Evaluate("Muhasebe ön lisans mezunu olmak. 30 yaşını doldurmamış olmak. 35 yaşını doldurmamış olmak. KPSS şartı aranmaz.",
            new ProfileOptions { Department = "Muhasebe", BirthDate = new DateTime(1980, 1, 1) }).Status);

    [Fact]
    public void AliasTableWorksForAnyDepartment() => Assert.Equal(EligibilityStatus.Eligible,
        Evaluate("Muhasebe ve Vergi Uygulamaları ön lisans mezunu olmak. KPSS şartı aranmaz.",
            new ProfileOptions { Department = "Muhasebe", DepartmentAliases = new() { ["Muhasebe"] = new() { "Muhasebe ve Vergi Uygulamaları" } } }).Status);

    [Fact]
    public void SpecificExperienceFieldRequiresVerification() => Assert.Contains(
        Evaluate("Muhasebe ön lisans mezunu olmak. Bilişim sektöründe en az 3 yıl tecrübe.",
            new ProfileOptions { Department = "Muhasebe", Experience = new() { IsKnown = true, TotalMonths = 50, Field = "Satış" } }).Conditions,
        c => c.CriterionName == "Tecrübe Alanı" && c.Status == ConditionStatus.Unknown);

    [Fact]
    public void PreferenceMismatchDoesNotRejectCandidate()
    {
        var reader = new DocumentReader();
        var result = new EligibilityEvaluator(new RequirementExtractor(reader), reader).EvaluatePosition(
            new AltIlanResponse { IlanBaslik = "İŞÇİ", IlanMetni = "Muhasebe ön lisans mezunu olmak. KPSS şartı aranmaz.", KontenjanList = new() { new() { Il = "İSTANBUL", Kontenjan = 1 } } }, "",
            new ProfileOptions { Department = "Muhasebe", CityPreferences = new() { "Ankara" }, WorkPreferences = new() { "Kadrolu" } });
        Assert.Equal(EligibilityStatus.Eligible, result.Status); Assert.False(result.PreferencesMatch);
        Assert.Equal(2, result.Conditions.Count(c => c.IsPreference && c.Status == ConditionStatus.Unsatisfied));
    }
    [Fact]
    public void CityCacheRoundTripsPunctuation()
    {
        var quotas = new List<KontenjanItem> { new() { Il = "İl (Merkez, Kuzey)", Kontenjan = 4 } };
        var record = PositionIdentity.Build("a", new() { new() { IlanBaslik = "Kadro", KontenjanList = quotas } }).Single();
        Assert.Equal(quotas[0].Il, PositionIdentity.FromCache(record).KontenjanList!.Single().Il);
    }
    [Fact]
    public void DeadlineChangeWinsOverEligibility() => Assert.Equal(ChangeType.DeadlineChanged,
        new ChangeDetector().DetectChanges(new AnnouncementRecord { EndDate = new DateTime(2026, 1, 1), RawContentHash = "a" },
            new AnnouncementRecord { EndDate = new DateTime(2026, 2, 1) }, "a", false, true));
    [Fact]
    public void NewScoreHasNoImplicitTypeOrYear()
    { var score = new KpssScoreEntry(); Assert.Empty(score.ScoreType); Assert.Equal(0, score.ExamYear); }
    [Fact]
    public void NewProfileHasNoAssumedDepartment() => Assert.Empty(new ProfileOptions().Department);
    [Fact]
    public void ManualRulesApplyOnlyToReviewedContentVersion()
    {
        const string text = "Muhasebe ön lisans mezunu olmak. KPSS P93 en az 75 puan almış olmak.";
        var profile = new ProfileOptions { Department = "Muhasebe", KpssStatus = "Var", KpssScores = new() { new() { ScoreType = "P93", ExamYear = 2024, Score = 70 } },
            PositionRules = new() { ["Kadro"] = new() { SourceHash = new ChangeDetector().ComputeHash(text + "\n"), MinKpssScore = 65 } } };
        Assert.Equal(EligibilityStatus.Eligible, Evaluate(text, profile).Status);
        Assert.NotEqual(EligibilityStatus.Eligible, Evaluate(text.Replace("75", "80"), profile).Status);
    }
    [Fact]
    public void NumericTypeSuffixIsNotMisreadAsScore()
    { var req = Extract("KPSS P394 puan türü"); Assert.Equal("P394", req.RequiredKpssType!.Value); Assert.Null(req.MinKpssScore); }
    [Fact]
    public void DocumentedExperienceInMatchingFieldIsSatisfied() => Assert.Contains(
        Evaluate("Muhasebe ön lisans mezunu olmak. Bilişim sektöründe en az 3 yıl tecrübe. KPSS şartı aranmaz.",
            new ProfileOptions { Department = "Muhasebe", Experience = new() { IsKnown = true, IsDocumented = true, TotalMonths = 50, Field = "Bilişim" } }).Conditions,
        c => c.CriterionName == "Tecrübe Alanı" && c.Status == ConditionStatus.Satisfied);
}
