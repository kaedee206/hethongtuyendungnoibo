using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.CandidateInterviews;

/// <summary>
/// ViewModel hiển thị trang xác nhận lịch phỏng vấn dành cho ứng viên (Scrum - Số 54).
/// Tuân thủ quy chuẩn: PascalCase, không lưu logic nghiệp vụ tại ViewModel.
/// </summary>
public class CandidateInterviewConfirmationViewModel
{
    public Guid InterviewId { get; set; }
    public Guid ApplicationId { get; set; }
    public string Token { get; set; } = string.Empty;

    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public Guid? CandidateUserId { get; set; }

    public string JobTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string RoundTitle { get; set; } = string.Empty;
    public int RoundNumber { get; set; } = 1;

    public InterviewType InterviewType { get; set; }
    public string LocationOrLink { get; set; } = string.Empty;

    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public string TimeDisplay { get; set; } = string.Empty;
    public string DateDisplay { get; set; } = string.Empty;

    public string PanelistNames { get; set; } = string.Empty;

    public CandidateConfirmStatus CandidateConfirmed { get; set; } = CandidateConfirmStatus.PENDING;
    public string? CandidateNotes { get; set; }
    public InterviewStatus InterviewStatus { get; set; } = InterviewStatus.SCHEDULED;

    public string PreparationGuide { get; set; } = string.Empty;

    public bool IsExpired { get; set; }
    public bool CanRespond => InterviewStatus == InterviewStatus.SCHEDULED && !IsExpired;

    public string StatusDisplay => CandidateConfirmed switch
    {
        CandidateConfirmStatus.ACCEPTED => "Đã xác nhận tham gia",
        CandidateConfirmStatus.DECLINED => "Đã gửi đề nghị đổi lịch",
        _ => "Chờ xác nhận tham gia"
    };

    public string StatusBadgeClass => CandidateConfirmed switch
    {
        CandidateConfirmStatus.ACCEPTED => "bg-success-subtle text-success border border-success-subtle",
        CandidateConfirmStatus.DECLINED => "bg-warning-subtle text-warning-emphasis border border-warning-subtle",
        _ => "bg-info-subtle text-info border border-info-subtle"
    };

    /// <summary>
    /// Thông báo phản hồi sau khi ứng viên thực hiện hành động (Confirm/Reschedule).
    /// </summary>
    public string? FeedbackMessage { get; set; }
    public bool FeedbackIsSuccess { get; set; }
}
