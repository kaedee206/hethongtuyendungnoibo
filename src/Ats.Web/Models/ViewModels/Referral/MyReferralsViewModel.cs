using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Tracking;

namespace Ats.Web.Models.ViewModels.Referral;

/// <summary>
/// ViewModel cho trang/tab "Hồ sơ tôi đã giới thiệu" dành cho nhân viên nội bộ
/// </summary>
public class MyReferralsViewModel
{
    public int TotalReferrals { get; set; }
    public int ReceivedCount { get; set; }
    public int UnderReviewCount { get; set; }
    public int InterviewInvitedCount { get; set; }
    public int ConcludedCount { get; set; }

    public List<ReferralApplicationItemDto> Referrals { get; set; } = new();
    public List<JobPostingSelectDto> OpenJobs { get; set; } = new();
}

public class JobPostingSelectDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}

public class ReferralApplicationItemDto
{
    public Guid ApplicationId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string MaskedEmail { get; set; } = string.Empty;
    public string? CandidatePhone { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string? TrackingCode { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public string AppliedDateDisplay { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái ở 4 mức tổng quát an toàn: Đã nhận, Đang xem xét, Mời phỏng vấn, Kết thúc
    /// </summary>
    public PublicApplicationStatus GeneralStatus { get; set; }
    public string GeneralStatusDisplay { get; set; } = string.Empty;
    public string StatusBadgeClass { get; set; } = string.Empty;
    public int ProgressPercentage { get; set; }

    public string? ReferralNote { get; set; }
    public string? ResumeFileName { get; set; }
    public string? ResumeFilePath { get; set; }

    public List<TrackingStageStepViewModel> StepperSteps { get; set; } = new();
}
