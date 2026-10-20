using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Candidates;

/// <summary>
/// DTO chứa điểm của một tiêu chí đánh giá cho một vòng / một người chấm.
/// </summary>
public class CandidateCriteriaEvaluationDto
{
    public Guid? CriteriaId { get; set; }
    public string CriteriaName { get; set; } = string.Empty;
    public string? CriteriaType { get; set; } // HARD_SKILL, SOFT_SKILL, CULTURE
    public decimal Weight { get; set; } = 1.0m;
    public int Score { get; set; } // 1-5
    public string? Comment { get; set; }
}

/// <summary>
/// DTO tổng hợp điểm trung bình cho một tiêu chí cụ thể qua toàn bộ các vòng phỏng vấn của ứng viên.
/// </summary>
public class CriteriaAverageScoreDto
{
    public string CriteriaName { get; set; } = string.Empty;
    public string CriteriaType { get; set; } = string.Empty;
    public decimal Weight { get; set; } = 1.0m;
    public double AverageScore { get; set; }
    public int EvaluationCount { get; set; }
    public string ScoreRatingClass => AverageScore switch
    {
        >= 4.5 => "text-success fw-bold",
        >= 3.5 => "text-primary fw-semibold",
        >= 2.5 => "text-warning fw-semibold",
        _ => "text-danger fw-bold"
    };
    public string ProgressBarColor => AverageScore switch
    {
        >= 4.0 => "bg-success",
        >= 3.0 => "bg-primary",
        >= 2.0 => "bg-warning",
        _ => "bg-danger"
    };
}

/// <summary>
/// DTO chi tiết một phiếu đánh giá của một giám khảo trong một vòng phỏng vấn.
/// </summary>
public class RoundEvaluationSheetDto
{
    public Guid EvaluationId { get; set; }
    public Guid InterviewerId { get; set; }
    public string InterviewerName { get; set; } = string.Empty;
    public string InterviewerEmail { get; set; } = string.Empty;
    public decimal? OverallScore { get; set; }
    public RecommendationType? Recommendation { get; set; }
    public string RecommendationDisplay { get; set; } = string.Empty;
    public string RecommendationBadgeClass { get; set; } = "bg-secondary text-white";
    public string? Strengths { get; set; }
    public string? Weaknesses { get; set; }
    public string? DetailedFeedback { get; set; }
    public bool IsSubmitted { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string FormattedSubmittedAt => SubmittedAt.HasValue
        ? SubmittedAt.Value.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm")
        : "Chưa nộp";
    public List<CandidateCriteriaEvaluationDto> CriteriaScores { get; set; } = [];
}

/// <summary>
/// DTO tổng hợp thông tin của một vòng phỏng vấn (bao gồm tất cả phiếu đánh giá của hội đồng).
/// </summary>
public class CandidateInterviewRoundSummaryDto
{
    public Guid InterviewId { get; set; }
    public int RoundNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public InterviewType InterviewType { get; set; }
    public string InterviewTypeDisplay => InterviewType switch
    {
        InterviewType.ONLINE_MEET => "Phỏng vấn trực tuyến (Online)",
        InterviewType.OFFLINE_OFFICE => "Phỏng vấn trực tiếp tại văn phòng",
        _ => InterviewType.ToString()
    };
    public string? LocationOrLink { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public string FormattedTime => StartTime.HasValue
        ? StartTime.Value.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm")
        : "Chưa ấn định";
    public InterviewStatus Status { get; set; }
    public string StatusDisplay => Status switch
    {
        InterviewStatus.COMPLETED => "Đã hoàn thành",
        InterviewStatus.SCHEDULED => "Đã lên lịch",
        InterviewStatus.CANCELLED => "Đã hủy",
        _ => Status.ToString()
    };
    public List<string> Panelists { get; set; } = [];
    public List<RoundEvaluationSheetDto> EvaluationSheets { get; set; } = [];

    // Thống kê vòng
    public double? RoundAverageScore => EvaluationSheets.Where(e => e.OverallScore.HasValue && e.IsSubmitted).Any()
        ? Math.Round((double)EvaluationSheets.Where(e => e.OverallScore.HasValue && e.IsSubmitted).Average(e => e.OverallScore!.Value), 2)
        : null;
}

/// <summary>
/// DTO ghi nhận nhật ký quyết định tuyển dụng đã được lưu.
/// </summary>
public class HiringDecisionLogDto
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty; // OFFER, REJECTED
    public string ActionDisplay { get; set; } = string.Empty;
    public string BadgeClass { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? Note { get; set; }
    public Guid DecidedByUserId { get; set; }
    public string DecidedByUserName { get; set; } = string.Empty;
    public string DecidedByUserRole { get; set; } = string.Empty;
    public DateTimeOffset DecidedAt { get; set; }
    public string FormattedDecidedAt => DecidedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm");
}

/// <summary>
/// ViewModel chính cho màn hình gộp toàn bộ phiếu đánh giá của mọi vòng và ra quyết định tuyển (SP - 7).
/// </summary>
public class CandidateMultiRoundEvaluationSummaryViewModel
{
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string? CandidatePhone { get; set; }
    public string? AvatarUrl { get; set; }

    // Thông tin vị trí tuyển dụng
    public Guid JobPostingId { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string JobCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string CurrentStageName { get; set; } = string.Empty;
    public CandidateStage CurrentStage { get; set; }
    public ApplicationStatus ApplicationStatus { get; set; }

    // Quyền hạn người xem
    public bool CanMakeDecision { get; set; } = true;
    public string CurrentUserName { get; set; } = string.Empty;
    public string CurrentUserRole { get; set; } = string.Empty;

    // Tổng hợp kết quả
    public List<CandidateInterviewRoundSummaryDto> Rounds { get; set; } = [];
    public List<CriteriaAverageScoreDto> CriteriaAverages { get; set; } = [];

    // Chỉ số KPI toàn diện
    public int TotalRoundsCount => Rounds.Count;
    public int TotalCompletedRoundsCount => Rounds.Count(r => r.Status == InterviewStatus.COMPLETED || r.EvaluationSheets.Any(e => e.IsSubmitted));
    public int TotalSubmittedEvaluationsCount => Rounds.SelectMany(r => r.EvaluationSheets).Count(e => e.IsSubmitted);

    public double OverallAverageScore => CriteriaAverages.Any()
        ? Math.Round(CriteriaAverages.Average(c => c.AverageScore), 2)
        : (Rounds.SelectMany(r => r.EvaluationSheets).Where(e => e.OverallScore.HasValue && e.IsSubmitted).Any()
            ? Math.Round((double)Rounds.SelectMany(r => r.EvaluationSheets).Where(e => e.OverallScore.HasValue && e.IsSubmitted).Average(e => e.OverallScore!.Value), 2)
            : 0.0);

    // Thống kê kết luận của hội đồng qua các vòng
    public int StrongHireCount => Rounds.SelectMany(r => r.EvaluationSheets).Count(e => e.IsSubmitted && e.Recommendation == RecommendationType.STRONG_HIRE);
    public int HireCount => Rounds.SelectMany(r => r.EvaluationSheets).Count(e => e.IsSubmitted && e.Recommendation == RecommendationType.HIRE);
    public int ConsiderCount => Rounds.SelectMany(r => r.EvaluationSheets).Count(e => e.IsSubmitted && e.Recommendation == RecommendationType.CONSIDER);
    public int StrongNoHireCount => Rounds.SelectMany(r => r.EvaluationSheets).Count(e => e.IsSubmitted && e.Recommendation == RecommendationType.STRONG_NO_HIRE);

    // Nhật ký các quyết định trước đây
    public List<HiringDecisionLogDto> DecisionLogs { get; set; } = [];

    // Trạng thái quyết định hiện tại nếu đã xong
    public bool HasFinalDecision => CurrentStage == CandidateStage.OFFER || CurrentStage == CandidateStage.HIRED || CurrentStage == CandidateStage.REJECTED;
}
