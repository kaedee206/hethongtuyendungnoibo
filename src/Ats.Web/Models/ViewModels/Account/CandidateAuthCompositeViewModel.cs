using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.Account;

/// <summary>
/// Composite ViewModel cho Cổng Tuyển dụng Ứng viên (/Account/CandidateAuth).
/// Quản lý dữ liệu đa trạng thái (Đăng nhập, Đăng ký, Quên mật khẩu) trên slider mượt mà.
/// </summary>
public class CandidateAuthCompositeViewModel
{
    public CandidateLoginInputModel LoginInput { get; set; } = new();

    public CandidateRegisterInputModel RegisterInput { get; set; } = new();

    public CandidateForgotPasswordInputModel ForgotPasswordInput { get; set; } = new();

    /// <summary>
    /// Panel đang hiển thị: "signin", "signup" hoặc "reset". Đồng bộ URL Hash (#signin, #signup, #reset).
    /// </summary>
    public string ActivePanel { get; set; } = "signin";

    /// <summary>
    /// Thông báo trạng thái hoặc phản hồi từ hệ thống.
    /// </summary>
    public string? StatusMessage { get; set; }

    /// <summary>
    /// Cờ xác định trạng thái thành công hay lỗi.
    /// </summary>
    public bool IsSuccess { get; set; }
}

public class CandidateLoginInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng.")]
    [Display(Name = "Địa chỉ Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Ghi nhớ phiên đăng nhập")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class CandidateRegisterInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên của bạn.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email cá nhân.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng.")]
    [Display(Name = "Email cá nhân")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có độ dài tối thiểu 6 ký tự.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu.")]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "Mật khẩu xác nhận không trùng khớp.")]
    [Display(Name = "Xác nhận mật khẩu")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Display(Name = "Đồng ý điều khoản")]
    public bool AgreeToTerms { get; set; }


    public string? ReturnUrl { get; set; }
}

public class CandidateForgotPasswordInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email đã đăng ký.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng.")]
    [Display(Name = "Địa chỉ Email")]
    public string Email { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
