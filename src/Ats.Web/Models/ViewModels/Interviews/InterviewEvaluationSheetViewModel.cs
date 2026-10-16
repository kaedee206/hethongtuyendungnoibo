namespace Ats.Web.Models.ViewModels.Interviews;

/// <summary>
/// ViewModel hiển thị giao diện phiếu đánh giá phỏng vấn (Scrum - Số 57)
/// </summary>
public class InterviewEvaluationSheetViewModel
{
    public Guid InterviewId { get; set; }
    public Guid ApplicationId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string RoundTitle { get; set; } = string.Empty;
    public int RoundNumber { get; set; } = 1;
    public string InterviewDateDisplay { get; set; } = string.Empty;
    public string TimeDisplay { get; set; } = string.Empty;
    public string LocationOrLink { get; set; } = string.Empty;
    public string PanelistNames { get; set; } = string.Empty;

    /// <summary>
    /// Điều kiện 1: Chỉ người phỏng vấn được mời mới mở được phiếu của buổi đó
    /// </summary>
    public bool IsInvitedPanelist { get; set; }
    public bool IsStaffManager { get; set; } // Admin, HRManager, Recruiter

    // Trạng thái phiếu của người dùng hiện tại
    public Guid? CurrentUserEvaluationId { get; set; }
    public decimal? OverallScore { get; set; }
    public string? Recommendation { get; set; }
    public string? RecommendationDisplay { get; set; }
    public string? Strengths { get; set; }
    public string? Weaknesses { get; set; }
    public string? DetailedFeedback { get; set; }

    /// <summary>
    /// Điều kiện 3: Đã nộp thì không sửa được, trừ khi recruiter mở lại trong 24 giờ
    /// </summary>
    public bool IsSubmitted { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? SubmittedAtDisplay { get; set; }
    public bool IsReopened { get; set; }
    public DateTimeOffset? ReopenedAt { get; set; }
    public string? ReopenedByUserName { get; set; }
    public string? ReopenReason { get; set; }

    public bool CanEdit => !IsSubmitted || IsReopened;
    public bool CanRecruiterReopen { get; set; }
    public double HoursRemainingToReopen { get; set; }

    /// <summary>
    /// Điều kiện 4: Không nhìn thấy đánh giá của người khác trước khi nộp phiếu của mình
    /// </summary>
    public bool CanViewOtherEvaluations { get; set; }
    public string? BlindReviewNotice { get; set; }

    /// <summary>
    /// Danh sách đánh giá của các giám khảo trong hội đồng (chỉ hiển thị khi đủ điều kiện)
    /// </summary>
    public List<PanelistEvaluationSummaryDto> PanelEvaluations { get; set; } = new();
}
