using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Jobs;

namespace Ats.Web.Models.ViewModels.Workspace;

public class WorkspaceDashboardViewModel
{
    public bool IsAuthenticated { get; set; }
    public bool IsCandidate { get; set; }
    public bool IsStaff { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public List<string> UserRolesList { get; set; } = new();

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
}

public class CandidatePipelineItemDto
{
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string CandidatePhone { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public Guid CurrentStageId { get; set; }
    public string CurrentStageName { get; set; } = string.Empty;
    public int CurrentStageOrder { get; set; }
    public string StageColor { get; set; } = "#059669";
    public string AppliedDateDisplay { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ResumeFileName { get; set; } = string.Empty;
    public string ResumeFilePath { get; set; } = string.Empty;
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
}

public class PendingOfferDto
{
    public Guid OfferId { get; set; }
    public Guid ApplicationId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public decimal BaseSalary { get; set; }
    public decimal TotalPackage { get; set; }
    public string SalaryDisplay { get; set; } = string.Empty;
    public OfferStatus Status { get; set; }
    public string CreatedDateDisplay { get; set; } = string.Empty;
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
