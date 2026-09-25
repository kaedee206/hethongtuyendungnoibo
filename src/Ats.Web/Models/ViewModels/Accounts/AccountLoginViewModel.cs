using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.Accounts;

/// <summary>
/// ViewModel cho màn hình đăng nhập (Views/Accounts/Login.cshtml).
/// </summary>
public class AccountLoginViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập email công ty.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [Display(Name = "Email công ty")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Ghi nhớ đăng nhập")]
    public bool RememberMe { get; set; }

    /// <summary>
    /// Đường dẫn quay lại sau khi đăng nhập thành công.
    /// </summary>
    public string? ReturnUrl { get; set; }
}
