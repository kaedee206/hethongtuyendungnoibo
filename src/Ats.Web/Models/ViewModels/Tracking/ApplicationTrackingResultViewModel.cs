using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Tracking;

/// <summary>
/// ViewModel kết quả tra cứu hồ sơ trả về cho Ứng viên (Scrum #35)
/// Tuân thủ quy định bảo mật: Tuyệt đối không chứa ghi chú nội bộ, điểm số, hoặc tên người phỏng vấn.
/// </summary>
public class ApplicationTrackingResultViewModel
{
    public bool IsFound { get; set; }
    public string? SearchTrackingCode { get; set; }
    public string? SearchEmail { get; set; }
    public string? ErrorMessage { get; set; }

    // Thông tin hiển thị công khai an toàn
    public string TrackingCode { get; set; } = string.Empty;
    public string CandidateDisplayName { get; set; } = string.Empty;
    public string MaskedEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string? WorkLocation { get; set; }
    public string? EmploymentType { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
    public string AppliedDateDisplay => AppliedAt.ToString("dd/MM/yyyy HH:mm");
    public DateTimeOffset? LastUpdatedAt { get; set; }
    public string LastUpdatedDateDisplay => (LastUpdatedAt ?? AppliedAt).ToString("dd/MM/yyyy");

    // 4 mức trạng thái tổng quát
    public PublicApplicationStatus Status { get; set; } = PublicApplicationStatus.Received;
    
    public string StatusDisplayName => Status switch
    {
        PublicApplicationStatus.Received => "Đã nhận",
        PublicApplicationStatus.UnderReview => "Đang xem xét",
        PublicApplicationStatus.InterviewInvited => "Mời phỏng vấn",
        PublicApplicationStatus.Concluded => "Kết thúc",
        _ => "Đang xem xét"
    };

    public string StatusBadgeClass => Status switch
    {
        PublicApplicationStatus.Received => "status-badge-received",
        PublicApplicationStatus.UnderReview => "status-badge-review",
        PublicApplicationStatus.InterviewInvited => "status-badge-interview",
        PublicApplicationStatus.Concluded => "status-badge-concluded",
        _ => "status-badge-review"
    };

    public string StatusIcon => Status switch
    {
        PublicApplicationStatus.Received => "bi-inbox-fill",
        PublicApplicationStatus.UnderReview => "bi-search",
        PublicApplicationStatus.InterviewInvited => "bi-calendar-check-fill",
        PublicApplicationStatus.Concluded => "bi-check2-circle",
        _ => "bi-info-circle-fill"
    };

    public string StatusDescription { get; set; } = string.Empty;

    // Visual Stepper 4 bước
    public List<TrackingStageStepViewModel> StepperSteps { get; set; } = [];
}

/// <summary>
/// Thông tin từng bước trong thanh tiến trình 4 giai đoạn
/// </summary>
public class TrackingStageStepViewModel
{
    public int StepIndex { get; set; } // 1, 2, 3, 4
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string State { get; set; } = "upcoming"; // "completed", "active", "upcoming"
    public string? DateDisplay { get; set; }
}
