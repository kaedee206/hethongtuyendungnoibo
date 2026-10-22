using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Requisitions;

/// <summary>
/// ViewModel toàn diện cho màn hình Theo dõi tiến độ tuyển dụng vị trí sở hữu của Trưởng bộ phận (SCRUM-351).
/// </summary>
public class PositionRecruitmentProgressViewModel
{
    // Thông tin vị trí & yêu cầu tuyển dụng được chọn
    public Guid SelectedRequisitionId { get; set; }
    public string RequisitionCode { get; set; } = string.Empty;
    public string PositionTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string WorkLocation { get; set; } = string.Empty;
    public string EmploymentType { get; set; } = string.Empty;
    public RequisitionStatus RequisitionStatus { get; set; }
    public string RequisitionStatusDisplay { get; set; } = string.Empty;

    // Chỉ tiêu & Tiến độ đáp ứng Headcount
    public int Quantity { get; set; }
    public int HiredCount { get; set; }
    public int RemainingHeadcountNeeded => Math.Max(0, Quantity - HiredCount);
    public double HeadcountFulfillmentPercentage => Quantity > 0 ? Math.Min(100.0, Math.Round((double)HiredCount / Quantity * 100, 1)) : 0;

    // Ngày cần người & Đếm ngược thời hạn
    public DateOnly? TargetHireDate { get; set; }
    public string TargetHireDateDisplay { get; set; } = "Chưa thiết lập";
    public int? DaysRemaining { get; set; }
    public string DaysRemainingDisplay { get; set; } = "—";
    public string UrgencyLevel { get; set; } = "NORMAL"; // NORMAL, WARNING, DANGER, NOT_SET
    public string UrgencyBadgeClass { get; set; } = "bg-secondary-subtle text-secondary";
    public string UrgencyMessage { get; set; } = string.Empty;

    // Nhân sự phụ trách (Recruiter)
    public string AssignedRecruiterName { get; set; } = "Chưa phân công";
    public string AssignedRecruiterEmail { get; set; } = string.Empty;
    public string AssignedRecruiterPhone { get; set; } = string.Empty;

    // Báo cáo tóm tắt chuẩn văn phong dành cho Sếp (Executive Summary 1-Click Copy)
    public string ExecutiveBossReportText { get; set; } = string.Empty;

    // Phễu tuyển dụng theo từng giai đoạn của riêng vị trí này
    public int TotalApplications { get; set; }
    public int ActiveCandidatesCount { get; set; }
    public List<PositionStageFunnelDto> Stages { get; set; } = new();

    // Lịch phỏng vấn sắp tới của vị trí này
    public List<PositionInterviewDto> UpcomingInterviews { get; set; } = new();

    // Ứng viên đã phỏng vấn đang chờ Trưởng bộ phận cho ý kiến đánh giá
    public List<PositionPendingFeedbackDto> PendingFeedbackCandidates { get; set; } = new();

    // Danh sách toàn bộ các vị trí mà Trưởng bộ phận sở hữu để chuyển đổi nhanh
    public List<PositionProgressSummaryDto> OwnedPositions { get; set; } = new();
}

/// <summary>
/// DTO tóm tắt vị trí dùng cho thanh chọn nhanh (dropdown / tabs) giữa các vị trí sở hữu.
/// </summary>
public class PositionProgressSummaryDto
{
    public Guid RequisitionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int HiredCount { get; set; }
    public DateOnly? TargetHireDate { get; set; }
    public string TargetHireDateDisplay { get; set; } = "—";
    public int? DaysRemaining { get; set; }
    public string UrgencyLevel { get; set; } = "NORMAL";
    public string UrgencyBadgeClass { get; set; } = "bg-secondary-subtle text-secondary";
    public int ActiveCandidateCount { get; set; }
    public int PendingFeedbackCount { get; set; }
    public RequisitionStatus Status { get; set; }
    public string StatusDisplay { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}

/// <summary>
/// Thống kê hồ sơ tại một giai đoạn cụ thể của vị trí.
/// </summary>
public class PositionStageFunnelDto
{
    public Guid StageId { get; set; }
    public int StageOrder { get; set; }
    public string StageName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string ColorCode { get; set; } = "#2563EB";
    public int CandidateCount { get; set; }
    public double Percentage { get; set; }
    public bool IsTerminal { get; set; }
    public List<PositionCandidateItemDto> Candidates { get; set; } = new();
}

/// <summary>
/// Chi tiết ứng viên trong giai đoạn của vị trí tuyển dụng.
/// </summary>
public class PositionCandidateItemDto
{
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string AvatarLetter => !string.IsNullOrWhiteSpace(CandidateName) ? CandidateName[..1].ToUpper() : "U";
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AppliedDateDisplay { get; set; } = string.Empty;
    public int? Rating { get; set; }
    public int DaysInStage { get; set; }
    public bool IsSlaOverdue { get; set; }
    public string? ResumeFileName { get; set; }
    public string? ResumeFilePath { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Buổi phỏng vấn sắp tới của vị trí.
/// </summary>
public class PositionInterviewDto
{
    public Guid InterviewId { get; set; }
    public Guid ApplicationId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string AvatarLetter => !string.IsNullOrWhiteSpace(CandidateName) ? CandidateName[..1].ToUpper() : "U";
    public int RoundNumber { get; set; }
    public string RoundTitle { get; set; } = string.Empty;
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public string TimeDisplay { get; set; } = string.Empty;
    public string DateDisplay { get; set; } = string.Empty;
    public string PanelistNames { get; set; } = string.Empty;
    public string LocationOrLink { get; set; } = "https://meet.google.com";
    public CandidateConfirmStatus CandidateConfirmed { get; set; }
    public InterviewStatus Status { get; set; }
    public string StatusBadgeClass { get; set; } = "bg-warning-subtle text-warning-emphasis";
    public string StatusDisplay { get; set; } = "Chờ phỏng vấn";
}

/// <summary>
/// Ứng viên đã phỏng vấn đang chờ Trưởng bộ phận cho ý kiến đánh giá.
/// </summary>
public class PositionPendingFeedbackDto
{
    public Guid InterviewId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string AvatarLetter => !string.IsNullOrWhiteSpace(CandidateName) ? CandidateName[..1].ToUpper() : "U";
    public string JobTitle { get; set; } = string.Empty;
    public string RoundTitle { get; set; } = string.Empty;
    public DateTimeOffset? InterviewTime { get; set; }
    public string InterviewTimeDisplay { get; set; } = string.Empty;
    public string InterviewDateDisplay { get; set; } = string.Empty;
    public int DaysWaitingForFeedback { get; set; }
    public Guid? EvaluationId { get; set; }
    public bool IsInvitedPanelist { get; set; }
}
