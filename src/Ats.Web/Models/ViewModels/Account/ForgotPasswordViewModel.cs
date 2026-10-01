using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.Account;

/// <summary>
/// ViewModel cho màn hình yêu cầu đặt lại mật khẩu (S1-03: Quên mật khẩu).
/// </summary>
public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập email công ty.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [Display(Name = "Email công ty")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Cờ đánh dấu đã gửi link thành công để hiển thị thông báo an toàn.
    /// </summary>
    public bool EmailSent { get; set; }
}
