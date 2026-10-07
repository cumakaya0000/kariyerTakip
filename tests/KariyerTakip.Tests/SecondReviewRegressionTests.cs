using KariyerTakip.Models;
using KariyerTakip.Services;
using Xunit;

namespace KariyerTakip.Tests;

public sealed class SecondReviewRegressionTests
{
    private static readonly DateTime Reference = new(2024, 12, 17, 12, 0, 0, DateTimeKind.Utc);
    private static ExtractedRequirements Extract(string text) => new RequirementExtractor(new DocumentReader()).Extract(text);
    private static PositionEvaluation Evaluate(string text, ProfileOptions? profile = null, string general = "")
    {
        var reader = new DocumentReader();
        return new EligibilityEvaluator(new RequirementExtractor(reader), reader).EvaluatePosition(
            new() { IlanBaslik = "Kadro", IlanMetni = text }, general,
            profile ?? new() { Department = "Muhasebe", EducationLevel = "Lisans" }, referenceDate: Reference);
    }

    [Theory]
    [InlineData("İktisat, Muhasebe ve Maliye bölümlerinden lisans mezunu olmak.")]
    [InlineData("İktisat, Muhasebe veya Maliye bölümlerinden lisans mezunu olmak.")]
    [InlineData("İktisat, Muhasebe ile Maliye bölümlerinden lisans mezunu olmak.")]
    public void DepartmentConjunctionsPreserveMiddleMember(string education) =>
        Assert.Equal(EligibilityStatus.Eligible, Evaluate(education + " KPSS şartı aranmaz.").Status);

    [Fact]
    public void CompoundDepartmentIsNotItsFirstWord() => Assert.NotEqual(EligibilityStatus.Eligible,
        Evaluate("Muhasebe ve Vergi Uygulamaları bölümü lisans mezunu olmak. KPSS şartı aranmaz.").Status);

    [Fact]
    public void ExplicitDifferentDepartmentHasQuietStatus() => Assert.Equal(EligibilityStatus.LikelyIneligible,
        Evaluate("İktisat ve Maliye bölümlerinden lisans mezunu olmak. KPSS şartı aranmaz.").Status);

    [Theory]
    [InlineData("En az ön lisans mezunu olmak.")]
    [InlineData("Ön lisans ve üzeri mezunu olmak.")]
    [InlineData("Herhangi bir ön lisans ve üzeri mezunu olmak.")]
    public void MinimumDegreeAcceptsHigherLevel(string education) => Assert.Equal(EligibilityStatus.Eligible,
        Evaluate(education + " KPSS şartı aranmaz.").Status);

    [Theory]
    [InlineData("Muhasebe lisans mezunu olmak. 35 yaşını doldurmamış olmak.", "Lisans", 1980)]
    [InlineData("Muhasebe ön lisans mezunu olmak.", "Lisans", 2000)]
    public void KpssConflictDoesNotDisableOtherCriteria(string text, string level, int birthYear)
    {
        var result = Evaluate(text, new() { Department = "Muhasebe", EducationLevel = level, BirthDate = new(birthYear, 1, 1) },
            "KPSS P93 en az 70 puan. KPSS P3 en az 80 puan.");
        Assert.Equal(EligibilityStatus.Ineligible, result.Status);
        Assert.Contains(result.Conditions, c => !c.IsInferred && c.Status == ConditionStatus.Unsatisfied);
    }

    [Fact]
    public void PositionKpssRuleOverridesConflictingGeneralRules()
    {
        var result = Evaluate("Muhasebe lisans mezunu olmak. KPSS P3 en az 70 puan.",
            new() { Department = "Muhasebe", EducationLevel = "Lisans", KpssScores = new() { new() { ScoreType = "P3", ExamYear = 2024, Score = 75 } } },
            "KPSS P93 en az 70 puan. KPSS P3 en az 80 puan.");
        Assert.Equal(EligibilityStatus.Eligible, result.Status);
    }
    [Theory]
    [InlineData("2022 KPSS P93 en az 70 puan. 2022 KPSS P3 en az 80 puan.", EligibilityStatus.Ineligible)]
    [InlineData("2022 KPSS P93 en az 70 puan. 2024 KPSS P3 en az 80 puan.", EligibilityStatus.NeedsReview)]
    public void PositionScoreDoesNotSilentlyDiscardGeneralExamYears(string general, EligibilityStatus expected)
    {
        var result = Evaluate("Muhasebe lisans mezunu olmak. KPSS P3 en az 70 puan.",
            new() { Department = "Muhasebe", EducationLevel = "Lisans", KpssScores = new() { new() { ScoreType = "P3", ExamYear = 2024, Score = 75 } } }, general);
        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData("Bugün 35 yaşını doldurmamış olmak.")]
    [InlineData("35 yaşını doldurmamış olmak ve 15 gün içinde başvurmak.")]
    public void UnrelatedDayWordDoesNotAdvanceAge(string text) => Assert.False(Extract(text).AgeCountsNextYear);

    [Theory]
    [InlineData(1995, 12, 16, ConditionStatus.Unsatisfied)]
    [InlineData(1995, 12, 17, ConditionStatus.Satisfied)]
    [InlineData(1995, 12, 18, ConditionStatus.Satisfied)]
    public void AfadOfficialBirthBoundaryTakesPrecedence(int year, int month, int day, ConditionStatus expected)
    {
        // AFAD 2024 announcement, §2.1.5; the explicit cutoff includes its birthday.
        var result = Evaluate("Muhasebe lisans mezunu olmak. KPSS şartı aranmaz. Son başvuru tarihi itibarıyla 30 (otuz) yaşından gün almamış olmak. (17.12.1995 tarihinde veya sonrasında doğmuş olmak.)",
            new() { Department = "Muhasebe", EducationLevel = "Lisans", BirthDate = new(year, month, day) });
        Assert.Equal(expected, result.Conditions.Single(c => c.CriterionName == "Yaş Sınırı").Status);
    }

    [Fact]
    public void BareAgeCutoffBirthdayNeedsOfficialConfirmation()
    {
        var result = Evaluate("Muhasebe lisans mezunu olmak. KPSS şartı aranmaz. 30 yaşından gün almamış olmak.",
            new() { Department = "Muhasebe", EducationLevel = "Lisans", BirthDate = new(1995, 12, 17) });
        Assert.Equal(EligibilityStatus.NeedsReview, result.Status);
    }

    [Theory]
    [InlineData("2022 KPSS P93 en az 70 puan, başvurular 2024 yılında yapılacaktır.")]
    [InlineData("2022 KPSS P93 en az 70 puan, 17.12.2024 tarihinde başvuru yapılacaktır.")]
    [InlineData("2022 KPSS P93 en az 70 puan, 17 Aralık 2024 son başvuru tarihidir.")]
    public void ApplicationYearsAreNotExamYears(string text) => Assert.Equal(new[] { 2022 }, Extract(text).AllowedKpssYears.Order());

    [Theory]
    [InlineData("Bilişim alanında en az 3 yıl tecrübe.", "Bilgi işlem uzmanlığı")]
    [InlineData("En az 3 yıl bilgi teknolojileri alanında tecrübe.", "Bilişim teknolojileri")]
    public void DocumentedExperienceMatchesAliases(string rule, string field)
    {
        var result = Evaluate("Muhasebe lisans mezunu olmak. KPSS şartı aranmaz. " + rule,
            new() { Department = "Muhasebe", EducationLevel = "Lisans", Experience = new() { IsKnown = true, IsDocumented = true, TotalMonths = 48, Field = field } });
        Assert.Equal(ConditionStatus.Satisfied, result.Conditions.Single(c => c.CriterionName == "Tecrübe Alanı").Status);
    }

    [Fact]
    public void UnrelatedExperienceRemainsUnverified() => Assert.False(ExperienceFieldMatcher.Matches("yazılım geliştirme", "Muhasebe"));

    [Theory]
    [InlineData("C", "B", true)]
    [InlineData("CE", "C1E", true)]
    [InlineData("C", "D", false)]
    [InlineData("B", "C", false)]
    public void LicenseCoverageIsDirectional(string held, string required, bool expected) =>
        Assert.Equal(expected, DrivingLicenseCoverage.Covers(held, required));

    [Fact]
    public void ExtendedLicenseClassIsParsed() => Assert.Equal("C1E", Extract("C1E sınıfı sürücü belgesi.").RequiredDrivingLicense!.Value);

    [Fact]
    public void AbbreviationsDoNotDetachDepartmentFromGraduation()
    {
        var clauses = new DocumentReader().ExtractClauses("İktisat Fak. Muhasebe Prog. lisans mezunu olmak. KPSS şartı aranmaz.");
        Assert.Equal(2, clauses.Count);
        Assert.Contains("Muhasebe Prog.", clauses[0]);
    }
}
