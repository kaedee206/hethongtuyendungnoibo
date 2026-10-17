using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.CandidateInterviews;

/// <summary>
/// InputModel khi ứng viên bấm Xác nhận tham dự phỏng vấn (Scrum - Số 54).
/// </summary>
public class CandidateInterviewConfirmInputModel
{
    [Required(ErrorMessage = "Mã xác thực phiên phỏng vấn không hợp lệ.")]
    public string Token { get; set; } = string.Empty;

    [MaxLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự.")]
    public string? Notes { get; set; }
}

/// <summary>
/// InputModel khi ứng viên bấm Đề nghị đổi lịch phỏng vấn (Scrum - Số 54).
/// </summary>
public class CandidateInterviewRescheduleInputModel
{
    [Required(ErrorMessage = "Mã xác thực phiên phỏng vấn không hợp lệ.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập lý do không thể tham dự phỏng vấn theo lịch hẹn.")]
    [MaxLength(1000, ErrorMessage = "Lý do không được vượt quá 1000 ký tự.")]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Đề xuất thời gian không được vượt quá 500 ký tự.")]
    public string? ProposedTime { get; set; }
}
