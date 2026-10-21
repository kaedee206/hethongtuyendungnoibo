using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Jobs;
using Ats.Web.Models.ViewModels.Referral;

namespace Ats.Web.Models.ViewModels.Workspace;

public class WorkspaceDashboardViewModel
{
    public bool IsAuthenticated { get; set; }
    public bool IsCandidate { get; set; }
    public bool IsStaff { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public List<string> UserRolesList { get; set; } = new();
    public string PrimaryRoleDisplayName { get; set; } = string.Empty;

    // Dành cho Phân hệ Ứng viên
    public CandidateProfileDto? CandidateProfile { get; set; }
    public ApplicationTrackerDto? ActiveApplication { get; set; }
    public UpcomingInterviewDto? CandidateInterview { get; set; }
    public List<JobItemViewModel> CandidateOpenJobs { get; set; } = new();

    // Dành cho Phân hệ Quản trị & Nhân sự (Staff)
    public int TotalOpenJobs { get; set; }
    public int TotalApplications { get; set; }
    public int TotalInterviewsThisWeek { get; set; }
    public int TotalPendingOffers { get; set; }

    public List<PipelineFunnelStageDto> PipelineFunnel { get; set; } = new();
    public List<UpcomingInterviewDto> UpcomingInterviews { get; set; } = new();
    public List<PendingOfferDto> PendingOffers { get; set; } = new();
    public List<InterviewEvaluationCandidateDto> EvaluationCandidates { get; set; } = new();
    public List<CandidatePipelineItemDto> CandidatePipelineItems { get; set; } = new();
    public MyReferralsViewModel? MyReferrals { get; set; }
    public int DuplicateCandidatesCount { get; set; }

    // SCRUM-319: Bộ lọc danh sách vị trí tuyển dụng cho Kanban pipeline (Mặc định vị trí Recruiter phụ trách)
    public List<JobPostingFilterOptionDto> JobPostingFilterOptions { get; set; } = new();
    public Guid? DefaultJobPostingId { get; set; }

    // SCRUM-327: Thống kê số lượng hồ sơ đang bị quá hạn SLA trên pipeline
    public int OverdueSlaCandidateCount { get; set; }

    // Thông báo dành cho Staff / Recruiter
    public int UnreadNotificationsCount { get; set; }
    public List<NotificationDto> RecentNotifications { get; set; } = new();

    /// <summary>
    /// true nếu giao diện đang được giới hạn chỉ hiển thị ứng viên thuộc vị trí được giao (Scrum 26).
    /// </summary>
    public bool IsRecruiterScoped { get; set; } = false;
    public int AssignedPositionsCount { get; set; } = 0;
    public List<Ats.Web.Models.ViewModels.RequisitionAssignments.RecruiterOptionViewModel> RecruitersFilterList { get; set; } = new();

    // SCRUM-51: Pham vi va so luong lich phong van danh cho hoi dong
    public string InterviewScope { get; set; } = "all";
    public int MyUpcomingInterviewsCount { get; set; }
    public bool IsInterviewer { get; set; }

    // SCRUM-328: Bộ lọc vị trí tuyển dụng phục vụ xuất Excel rút gọn
    public List<JobFilterOptionDto> AvailableJobsForFilter { get; set; } = new();
}

public class JobPostingFilterOptionDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public bool IsAssignedToCurrentUser { get; set; }
    public int ActiveCandidateCount { get; set; }
}

public class JobFilterOptionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DisplayText => string.IsNullOrWhiteSpace(Code) ? Title : $"[{Code}] {Title}";
}

public class NotificationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public string CreatedAtDisplay { get; set; } = string.Empty;
}

public class CandidatePipelineItemDto
{
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public Guid JobPostingId { get; set; }
    public string JobCode { get; set; } = string.Empty;
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string CandidatePhone { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public Guid CurrentStageId { get; set; }
    public string CurrentStageName { get; set; } = string.Empty;
    public int CurrentStageOrder { get; set; }
    public string StageColor { get; set; } = "#059669";
    public string AppliedDateDisplay { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? Rating { get; set; }
    public string ResumeFileName { get; set; } = string.Empty;
    public string ResumeFilePath { get; set; } = string.Empty;

    public bool IsReferral { get; set; }
    public string? ReferrerName { get; set; }
    public string? ReferrerEmail { get; set; }
    public string? ReferralNote { get; set; }

    // Cảnh báo trùng lặp (Scrum #40)
    public bool HasDuplicateWarning { get; set; }
    public string? DuplicateReason { get; set; }
    public Guid? MatchedCandidateId { get; set; }
    public int DuplicateCount { get; set; }

    // SCRUM-327: Cờ và thông tin chi tiết quá hạn SLA
    public bool IsOverdueSla { get; set; }
    public int SlaDays { get; set; } = 3;
    public int DaysInCurrentStage { get; set; }
    public int OverdueDays { get; set; }
}

public class CandidateProfileDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string ResumeFileName { get; set; } = "Chưa có CV";
    public string ResumeUpdatedAt { get; set; } = string.Empty;
    public string? LinkedinUrl { get; set; }
    public bool HasResume { get; set; }
}

public class ApplicationTrackerDto
{
    public Guid ApplicationId { get; set; }
    public string ApplicationCode { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string CurrentStageName { get; set; } = string.Empty;
    public int CurrentStageOrder { get; set; }
    public int ProgressPercentage { get; set; }
    public string StatusDisplay { get; set; } = string.Empty;
    public string AppliedDateDisplay { get; set; } = string.Empty;
    public string? WithdrawToken { get; set; }
    public bool IsWithdrawn { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public string? WithdrawReason { get; set; }
    public List<StageStepDto> Steps { get; set; } = new();
}

public class StageStepDto
{
    public int StepNumber { get; set; }
    public string StepName { get; set; } = string.Empty;
    public string Status { get; set; } = "pending"; // completed, active, pending
    public string Subtitle { get; set; } = string.Empty;
}

public class UpcomingInterviewDto
{
    public Guid InterviewId { get; set; }
    public Guid ApplicationId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string CandidateAvatarLetter { get; set; } = "C";
    public string JobTitle { get; set; } = string.Empty;
    public string RoundTitle { get; set; } = string.Empty;
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public string TimeDisplay { get; set; } = string.Empty;
    public string DateDisplay { get; set; } = string.Empty;
    public string LocationOrLink { get; set; } = "https://meet.google.com";
    public string PanelistNames { get; set; } = string.Empty;
    public string StatusBadgeClass { get; set; } = "bg-warning-subtle text-warning-emphasis";
    public string StatusDisplay { get; set; } = "Chờ phỏng vấn";
    public CandidateConfirmStatus CandidateConfirmed { get; set; }
    public string? CandidateNotes { get; set; }
    public InterviewStatus Status { get; set; }
    public string? EvaluationResult { get; set; }
    public string? InvitationToken { get; set; }
    public string? ConfirmationUrl { get; set; }

    // Scrum - Số 57: Quyền hạn và trạng thái phiếu đánh giá
    public bool IsCurrentUserPanelist { get; set; }
    public bool HasCurrentUserSubmitted { get; set; }
    public bool CanCurrentUserEvaluate { get; set; }
    public bool CanRecruiterReopen { get; set; }
    public Guid? CurrentUserEvaluationId { get; set; }
    public Guid? FirstEvaluationId { get; set; }
    public bool IsReopened { get; set; }

    // Scrum - Số 55: Dời và huỷ lịch phỏng vấn
    public bool HasSubmittedEvaluation { get; set; }
    public bool CanRescheduleOrCancel { get; set; }
    public string? CancellationReason { get; set; }
    public string? RescheduleReason { get; set; }
    public string InterviewTypeString { get; set; } = "ONLINE_MEET";
    public List<Guid> InterviewerIds { get; set; } = new();
}

public class PendingOfferDto
{
    public Guid OfferId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public decimal BaseSalary { get; set; }
    public decimal? BonusAllowance { get; set; }
    public decimal TotalPackage { get; set; }
    public DateOnly? ProposedJoinDate { get; set; }
    public string? ContractType { get; set; }
    public string SalaryDisplay { get; set; } = string.Empty;
    public OfferStatus Status { get; set; }
    public string CreatedDateDisplay { get; set; } = string.Empty;
    public string StatusText => Status switch
    {
        OfferStatus.DRAFT => "Bản nháp",
        OfferStatus.PENDING_APPROVAL => "Chờ duyệt",
        OfferStatus.APPROVED => "Đã duyệt",
        OfferStatus.SENT_TO_CANDIDATE => "Đã gửi",
        OfferStatus.ACCEPTED => "Đồng ý",
        OfferStatus.REJECTED => "Từ chối",
        OfferStatus.REPLACED => "Đã thay thế",
        _ => Status.ToString()
    };
    public string StatusBadgeClass => Status switch
    {
        OfferStatus.APPROVED => "bg-success text-white",
        OfferStatus.ACCEPTED => "bg-success text-white",
        OfferStatus.REJECTED => "bg-danger text-white",
        OfferStatus.REPLACED => "bg-secondary text-white",
        _ => "bg-warning text-dark"
    };
}

public class PipelineFunnelStageDto
{
    public int StageOrder { get; set; }
    public string StageName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string ColorCode { get; set; } = "#0d6efd";
    public int Count { get; set; }
    public double Percentage { get; set; }
    public string Subtitle { get; set; } = string.Empty;
    public bool IsTerminal { get; set; } = false;
}

public class InterviewEvaluationCandidateDto
{
    public Guid InterviewId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string DisplayText => $"{CandidateName} — {JobTitle}";
}
