using KariyerTakip.Models;
using KariyerTakip.Services;
using Xunit;

namespace KariyerTakip.Tests;

public sealed class PostingRegressionTests
{
    // Short excerpts from public institution postings; surrounding profile/position data is synthetic.
    // https://pdb.bingol.edu.tr/duyurular/universitemiz-sozlesmeli-personel-alim-ilani_25-02-2026/
    [Fact]
    public void Bingol2026_KpssAndDocumentedExperience_AreExtracted()
    {
        var extractor = new RequirementExtractor(new DocumentReader());
        var score = extractor.Extract("2024 KPSS P93 sınavından en az 70 puan almış olmak");
        Assert.Equal("P93", score.RequiredKpssType!.Value); Assert.Equal(2024, score.RequiredKpssYear!.Value);
        Assert.Equal(70, score.MinKpssScore!.Value);
        var experience = extractor.Extract("SGK hizmet dökümü; en az 6 ay deneyime sahip olmak");
        Assert.Equal(6, experience.MinExperienceMonths!.Value); Assert.True(experience.RequiresExperienceDocumentation);
    }

    // https://www.mgu.edu.tr/wp-content/uploads/2025/03/Ankara-Muzik-ve-Guzel-Sanatlar-Universitesi-Rektorlugunden-Sozlesmeli-Personel-Alim-Ilani-14.03.2025.pdf
    [Fact]
    public void Mgu2025_ParenthesizedAgeAndKpss_AreExtracted()
    {
        var extractor = new RequirementExtractor(new DocumentReader());
        var age = extractor.Extract("Son başvuru tarihi itibarıyla 35(otuzbeş) yaşını doldurmamış olmak");
        Assert.Equal(35, age.MaxAgeLimit!.Value);
        var score = extractor.Extract("2024 yılı KPSS (P3) puan türünden en az 70 puan almış olmak");
        Assert.Equal("P3", score.RequiredKpssType!.Value); Assert.Equal(70, score.MinKpssScore!.Value);
    }

    [Fact]
    public void UnknownBirthDate_MustNotSatisfyAgeRequirement()
    {
        var reader = new DocumentReader(); var evaluator = new EligibilityEvaluator(new RequirementExtractor(reader), reader);
        var evaluation = evaluator.EvaluatePosition(new AltIlanResponse { IlanBaslik = "Tekniker", IlanMetni = "Bilgisayar Programcılığı ön lisans mezunu olmak. 35 yaşını doldurmamış olmak." }, "", new ProfileOptions { BirthDate = null });
        Assert.Contains(evaluation.Conditions, c => c.CriterionName == "Yaş Sınırı" && c.Status == ConditionStatus.Unknown);
    }

    [Fact]
    public void WorkPreferencesAndCertificates_AffectEvaluation()
    {
        var reader = new DocumentReader(); var evaluator = new EligibilityEvaluator(new RequirementExtractor(reader), reader);
        var profile = new ProfileOptions { WorkPreferences = new() { "Kadrolu" }, Certificates = new() { "CCNA" } };
        var evaluation = evaluator.EvaluatePosition(new AltIlanResponse { IlanBaslik = "Sözleşmeli Tekniker", IlanMetni = "Bilgisayar Programcılığı ön lisans mezunu olmak. CCNA sertifikasına sahip olmak." }, "", profile);
        Assert.Contains(evaluation.Conditions, c => c.CriterionName == "Çalışma Tercihi" && c.Status == ConditionStatus.Unsatisfied);
        Assert.Contains(evaluation.Conditions, c => c.CriterionName == "Sertifika / Belge" && c.Status == ConditionStatus.Satisfied);
        Assert.Equal(EligibilityStatus.Ineligible, evaluation.Status);
    }

    [Fact]
    public void CachedCityPreferences_AreEvaluatedFromPreservedQuotas()
    {
        var reader = new DocumentReader(); var evaluator = new EligibilityEvaluator(new RequirementExtractor(reader), reader);
        var position = PositionIdentity.FromCache(new PositionRecord { Title = "Tekniker", Cities = "ANKARA / MERKEZ (2), İSTANBUL (1)" });
        var evaluation = evaluator.EvaluatePosition(position, "", new ProfileOptions { CityPreferences = new() { "İZMİR" } });
        Assert.Equal(3, evaluation.TotalQuota);
        Assert.Contains(evaluation.Conditions, c => c.CriterionName == "Şehir Tercihi" && c.Status == ConditionStatus.Unsatisfied);
    }
}
