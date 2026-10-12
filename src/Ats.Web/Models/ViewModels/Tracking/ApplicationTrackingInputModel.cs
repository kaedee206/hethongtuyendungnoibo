using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.Tracking;

/// <summary>
/// Model nhận thông tin tra cứu hồ sơ từ ứng viên (Scrum #35)
/// Yêu cầu cả Mã tra cứu và Email để bảo vệ quyền riêng tư và chống brute-force.
/// </summary>
public class ApplicationTrackingInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập mã tra cứu hồ sơ.")]
    [Display(Name = "Mã tra cứu hồ sơ")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Mã tra cứu không đúng định dạng.")]
    public string TrackingCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email bạn đã dùng khi nộp hồ sơ.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
    [Display(Name = "Email ứng tuyển")]
    public string Email { get; set; } = string.Empty;
}
