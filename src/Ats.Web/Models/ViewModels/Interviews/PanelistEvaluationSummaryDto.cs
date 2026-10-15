namespace Ats.Web.Models.ViewModels.Interviews;

/// <summary>
/// DTO chứa thông tin tóm tắt phiếu đánh giá của từng giám khảo trong hội đồng
/// </summary>
public class PanelistEvaluationSummaryDto
{
    public Guid EvaluationId { get; set; }
    public Guid InterviewerId { get; set; }
    public string InterviewerName { get; set; } = string.Empty;
    public string InterviewerEmail { get; set; } = string.Empty;
    public decimal? OverallScore { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public string RecommendationDisplay { get; set; } = string.Empty;
    public string RecommendationBadgeClass { get; set; } = "bg-secondary";
    public string? Strengths { get; set; }
    public string? Weaknesses { get; set; }
    public string? DetailedFeedback { get; set; }
    public bool IsSubmitted { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string SubmittedAtDisplay { get; set; } = string.Empty;
    public bool IsReopened { get; set; }
    public DateTimeOffset? ReopenedAt { get; set; }
    public string? ReopenedByUserName { get; set; }
    public string? ReopenReason { get; set; }

    /// <summary>
    /// Recruiter có thể mở lại phiếu nếu đã nộp và chưa quá 24h
    /// </summary>
    public bool CanRecruiterReopen { get; set; }
    public double HoursRemainingToReopen { get; set; }
}
