namespace KariyerTakip.Models;

public enum ConditionStatus
{
    Satisfied,    // Sağlanıyor
    Unsatisfied,  // Sağlanmıyor
    Unknown       // Bilinmiyor / Eksik bilgi
}

public enum EligibilityStatus
{
    Eligible,      // Bilinen şartlarına uygun
    NeedsReview,   // Kontrol gerekli
    Ineligible     // Uygun değil
}

public class ConditionEvaluation
{
    public string CriterionName { get; set; } = string.Empty;
    public ConditionStatus Status { get; set; }
    public string RequiredValue { get; set; } = string.Empty;
    public string UserValue { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
}

public class PositionEvaluation
{
    public string PositionTitle { get; set; } = string.Empty;
    public string? Unvan { get; set; }
    public string? Cities { get; set; }
    public int TotalQuota { get; set; }
    public EligibilityStatus Status { get; set; }
    public string SummaryReason { get; set; } = string.Empty;
    public List<ConditionEvaluation> Conditions { get; set; } = new();
    public string ExtractedDepartmentText { get; set; } = string.Empty;
    public string ExtractedKpssText { get; set; } = string.Empty;
    public string ExtractedExperienceText { get; set; } = string.Empty;
}

public class AnnouncementEvaluation
{
    public string AnnouncementGuid { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string DetailUrl { get; set; } = string.Empty;
    public EligibilityStatus OverallStatus { get; set; }
    public List<PositionEvaluation> EvaluatedPositions { get; set; } = new();
}
