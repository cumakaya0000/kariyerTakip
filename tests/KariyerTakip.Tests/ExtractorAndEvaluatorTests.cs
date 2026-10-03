using Xunit;
using KariyerTakip.Models;
using KariyerTakip.Services;

namespace KariyerTakip.Tests;

public class ExtractorAndEvaluatorTests
{
    private readonly DocumentReader _reader = new();
    private readonly RequirementExtractor _extractor;
    private readonly EligibilityEvaluator _evaluator;

    public ExtractorAndEvaluatorTests()
    {
        _extractor = new RequirementExtractor(_reader);
        _evaluator = new EligibilityEvaluator(_extractor, _reader);
    }

    [Fact]
    public void RequirementExtractor_ShouldExtractExperienceInMonthsCorrectly()
    {
        // 6 ay tecrübe test
        var text6Ay = "Kadroya başvurabilmek için alanında en az 6 ay mesleki tecrübe sahibi olmak.";
        var req6Ay = _extractor.Extract(text6Ay);

        Assert.NotNull(req6Ay.MinExperienceMonths);
        Assert.Equal(6, req6Ay.MinExperienceMonths.Value);

        // 2 (iki) yıl tecrübe test
        var text2Yil = "Bilişim sektöründe en az 2 (iki) yıl mesleki deneyim sahibi olmak ve bunu belgelemek.";
        var req2Yil = _extractor.Extract(text2Yil);

        Assert.NotNull(req2Yil.MinExperienceMonths);
        Assert.Equal(24, req2Yil.MinExperienceMonths.Value);
        Assert.True(req2Yil.RequiresExperienceDocumentation);
    }

    [Fact]
    public void RequirementExtractor_ShouldExtractKpssScoreAndYearVariations()
    {
        var text = "2024 yılı KPSS (P93) puan türünden en az 70 puan almış olmak.";
        var req = _extractor.Extract(text);

        Assert.NotNull(req.RequiredKpssType);
        Assert.Equal("P93", req.RequiredKpssType.Value);
        Assert.NotNull(req.MinKpssScore);
        Assert.Equal(70.0, req.MinKpssScore.Value);
        Assert.NotNull(req.RequiredKpssYear);
        Assert.Equal(2024, req.RequiredKpssYear.Value);
    }

    [Fact]
    public void EligibilityEvaluator_ShouldReturnEligible_WhenAllConditionsSatisfied()
    {
        var profile = new ProfileOptions
        {
            Department = "Bilgisayar Programcılığı",
            EducationLevel = "Ön Lisans",
            KpssStatus = "Var",
            KpssScores = new List<KpssScoreEntry>
            {
                new KpssScoreEntry { ScoreType = "P93", ExamYear = 2024, Score = 75.0 }
            },
            Experience = new ExperienceEntry { TotalMonths = 24, IsDocumented = true, IsKnown = true }
        };

        var altIlan = new AltIlanResponse
        {
            IlanBaslik = "TEKNİKER (BİLGİSAYAR)",
            Unvan = "Tekniker",
            IlanMetni = "Bilgisayar Programcılığı ön lisans mezunu olmak. 2024 KPSS (P93) puan türünden en az 70 puan almış olmak. En az 1 yıl mesleki tecrübe sahibi olmak.",
            KontenjanList = new List<KontenjanItem> { new KontenjanItem { Il = "ANKARA", Kontenjan = 1 } }
        };

        var result = _evaluator.EvaluatePosition(altIlan, "", profile);

        Assert.Equal(EligibilityStatus.Eligible, result.Status);
    }

    [Fact]
    public void EligibilityEvaluator_ShouldReturnIneligible_WhenBachelorRequiredForAssociateUser()
    {
        var profile = new ProfileOptions
        {
            Department = "Bilgisayar Programcılığı",
            EducationLevel = "Ön Lisans"
        };

        var altIlan = new AltIlanResponse
        {
            IlanBaslik = "MÜHENDİS",
            Unvan = "Mühendis",
            IlanMetni = "Fakültelerin 4 yıllık Bilgisayar Mühendisliği lisans programından mezun olmak."
        };

        var result = _evaluator.EvaluatePosition(altIlan, "", profile);

        Assert.Equal(EligibilityStatus.Ineligible, result.Status);
        Assert.Contains("lisans", result.SummaryReason.ToLower());
    }

    [Fact]
    public void EligibilityEvaluator_ShouldReturnNeedsReview_WhenKpssNotDetectedInText()
    {
        var profile = new ProfileOptions
        {
            Department = "Bilgisayar Programcılığı",
            EducationLevel = "Ön Lisans",
            KpssStatus = "Var",
            KpssScores = new List<KpssScoreEntry>
            {
                new KpssScoreEntry { ScoreType = "P93", ExamYear = 2024, Score = 75.0 }
            }
        };

        // Text without explicit KPSS condition
        var altIlan = new AltIlanResponse
        {
            IlanBaslik = "PROGRAMCI",
            Unvan = "Programcı",
            IlanMetni = "Bilgisayar Programcılığı ön lisans programından mezun olmak."
        };

        var result = _evaluator.EvaluatePosition(altIlan, "", profile);

        // Crucial fix: Unknown KPSS must NOT be treated as satisfied!
        Assert.Equal(EligibilityStatus.NeedsReview, result.Status);
        Assert.Contains("KPSS", result.SummaryReason);
    }

    [Fact]
    public void EligibilityEvaluator_ShouldReturnIneligible_WhenKpssScoreBelowMinimum()
    {
        var profile = new ProfileOptions
        {
            Department = "Bilgisayar Programcılığı",
            EducationLevel = "Ön Lisans",
            KpssStatus = "Var",
            KpssScores = new List<KpssScoreEntry>
            {
                new KpssScoreEntry { ScoreType = "P93", ExamYear = 2024, Score = 65.0 }
            }
        };

        var altIlan = new AltIlanResponse
        {
            IlanBaslik = "TEKNİKER",
            Unvan = "Tekniker",
            IlanMetni = "Bilgisayar Programcılığı ön lisans mezunu olmak. KPSS P93 puan türünden en az 70 puan almış olmak."
        };

        var result = _evaluator.EvaluatePosition(altIlan, "", profile);

        Assert.Equal(EligibilityStatus.Ineligible, result.Status);
        Assert.Contains("KPSS Taban Puanı", result.SummaryReason);
    }

    [Fact]
    public void ChangeDetector_ShouldGenerateUniqueDeduplicationKey()
    {
        var detector = new ChangeDetector();
        var key1 = detector.GenerateDeduplicationKey("guid-123", "pos-1", "New", "hash-abc");
        var key2 = detector.GenerateDeduplicationKey("guid-123", "pos-1", "New", "hash-abc");
        var key3 = detector.GenerateDeduplicationKey("guid-123", "pos-1", "DeadlineChanged", "hash-def");

        Assert.Equal(key1, key2);
        Assert.NotEqual(key1, key3);
        Assert.Equal("guid-123:pos-1:New:hash-abc", key1);
    }
}
