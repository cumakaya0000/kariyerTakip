namespace KariyerTakip.Services;

// Null values preserve extraction; explicit values override only the selected position.
public sealed class PositionRuleOverrides
{
    public string SourceHash { get; set; } = "";
    public bool ResolveConflicts { get; set; }
    public string? KpssType { get; set; }
    public double? MinKpssScore { get; set; }
    public List<int>? KpssYears { get; set; }
    public int? MinKpssYear { get; set; }
    public int? MaxKpssYear { get; set; }
    public bool? NoKpss { get; set; }
    public int? ExperienceMonths { get; set; }
    public int? MaxExperienceMonths { get; set; }
    public int? AgeLimit { get; set; }
    public string? DrivingLicense { get; set; }

    public void Apply(ExtractedRequirements req)
    {
        const string source = "Kullanıcı tarafından resmî ilandan doğrulanan kadro kuralı";
        if (KpssType != null) req.RequiredKpssType = new(KpssType, source);
        if (MinKpssScore.HasValue) req.MinKpssScore = new(MinKpssScore.Value, source);
        if (KpssYears != null || MinKpssYear.HasValue || MaxKpssYear.HasValue)
        {
            req.RequiredKpssYear = null; req.AllowedKpssYears = KpssYears?.ToHashSet() ?? new();
            req.MinKpssYear = MinKpssYear; req.MaxKpssYear = MaxKpssYear;
        }
        if (NoKpss.HasValue)
        {
            req.ExplicitlyNoKpss = NoKpss.Value;
            req.NoKpssSourceText = source;
            if (NoKpss.Value) { req.RequiredKpssType = null; req.MinKpssScore = null; req.RequiredKpssYear = null; req.AllowedKpssYears.Clear(); req.MinKpssYear = null; req.MaxKpssYear = null; }
        }
        if (ExperienceMonths.HasValue)
        { req.MinExperienceMonths = new(ExperienceMonths.Value, source); req.HasUnparsedExperience = false; }
        if (AgeLimit.HasValue)
        { req.MaxAgeLimit = new(AgeLimit.Value, source); req.HasUnparsedAge = false; }
        if (DrivingLicense != null) req.RequiredDrivingLicense = new(DrivingLicense, source);
        if (MaxExperienceMonths.HasValue) req.MaxExperienceMonths = new(MaxExperienceMonths.Value, source);
        if (ResolveConflicts) req.HasConflictingRules = false;
    }
}
