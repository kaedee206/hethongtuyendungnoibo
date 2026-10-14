using System.ComponentModel.DataAnnotations;
using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Requisitions;

/// <summary>
/// InputModel xử lý submit thay đổi trạng thái yêu cầu tuyển dụng (PAUSED, CLOSED, CANCELLED) kèm lý do bắt buộc.
/// Tuân thủ quy chuẩn Naming Convention {Feature}{Action}InputModel theo AGENTS.md.
/// </summary>
public class RequisitionStatusChangeInputModel
{
    [Required(ErrorMessage = "Mã yêu cầu tuyển dụng không được để trống.")]
    public Guid RequisitionId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn trạng thái tuyển dụng cần chuyển sang.")]
    public RequisitionStatus TargetStatus { get; set; }

    [Required(ErrorMessage = "Lý do thay đổi trạng thái là bắt buộc.")]
    [MinLength(5, ErrorMessage = "Lý do phải có ít nhất 5 ký tự để đảm bảo tính minh bạch khi audit.")]
    [MaxLength(1000, ErrorMessage = "Lý do không được vượt quá 1000 ký tự.")]
    [Display(Name = "Lý do thay đổi")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Cờ xác nhận người dùng đã hiểu cảnh báo khi có ứng viên đang trong pipeline tuyển dụng dở dang.
    /// </summary>
    public bool ConfirmInProgressCandidates { get; set; } = false;
}

/// <summary>
/// ViewModel hiển thị cảnh báo pipeline ứng viên dở dang và tin đăng liên quan trước khi quyết định đóng/tạm dừng/hủy.
/// Tuân thủ quy chuẩn Naming Convention {Feature/Entity}{Usage}ViewModel theo AGENTS.md.
/// </summary>
public class RequisitionPipelineWarningViewModel
{
    public Guid RequisitionId { get; set; }
    public string RequisitionCode { get; set; } = string.Empty;
    public string PositionTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public RequisitionStatus CurrentStatus { get; set; }
    public string CurrentStatusDisplay { get; set; } = string.Empty;

    // Tổng số lượng ứng viên dở dang
    public int TotalInProgressCandidates { get; set; }
    public int ScreeningCandidatesCount { get; set; }
    public int InterviewingCandidatesCount { get; set; }
    public int OfferingCandidatesCount { get; set; }
    public int OtherStagesCandidatesCount { get; set; }

    // Thông tin tin tuyển dụng đang treo trên cổng
    public int ActiveJobPostingsCount { get; set; }
    public List<string> ActiveJobPostingTitles { get; set; } = new();

    // Danh sách tóm tắt ứng viên dở dang
    public List<RequisitionCandidateSummaryDto> InProgressCandidates { get; set; } = new();

    public bool HasInProgressCandidates => TotalInProgressCandidates > 0;
    public bool HasActiveJobPostings => ActiveJobPostingsCount > 0;
    public string WarningMessage { get; set; } = string.Empty;
}

/// <summary>
/// DTO chứa thông tin tóm tắt của một ứng viên đang trong pipeline của yêu cầu tuyển dụng.
/// </summary>
public class RequisitionCandidateSummaryDto
{
    public Guid ApplicationId { get; set; }
    public string CandidateFullName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string CurrentStageName { get; set; } = string.Empty;
    public string? JobPostingTitle { get; set; }
    public string AppliedAtDisplay { get; set; } = string.Empty;
}

/// <summary>
/// DTO kết quả thực thi thay đổi trạng thái yêu cầu tuyển dụng.
/// </summary>
public class RequisitionStatusChangeResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public RequisitionStatus? NewStatus { get; set; }
    public int ClosedPostingsCount { get; set; }
    public int InProgressCandidatesCount { get; set; }
}
