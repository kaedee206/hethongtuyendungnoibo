using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.Account;

/// <summary>
/// Composite ViewModel cho Cổng Đăng nhập Nhân sự Nội bộ (/Account/StaffLogin).
/// Bao gồm cả model cho panel Đăng nhập và panel Quên mật khẩu, hỗ trợ sliding transition mượt mà.
/// </summary>
public class StaffAuthCompositeViewModel
{
    public StaffLoginInputModel LoginInput { get; set; } = new();

    public StaffForgotPasswordInputModel ForgotPasswordInput { get; set; } = new();

    /// <summary>
    /// Panel đang kích hoạt: "login" hoặc "forgot". Đồng bộ với hash URL và fallback ModelState từ server.
    /// </summary>
    public string ActivePanel { get; set; } = "login";

    /// <summary>
    /// Thông báo trạng thái (thành công hoặc thông tin hệ thống) hiển thị trên view.
    /// </summary>
    public string? StatusMessage { get; set; }

    /// <summary>
    /// Xác định thông báo là thông báo thành công (true) hay cảnh báo/thông tin (false).
    /// </summary>
    public bool IsSuccess { get; set; }
}

public class StaffLoginInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập email công ty.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng.")]
    [RegularExpression(@"^[a-zA-Z0-9._%+-]+@noveratech\.digital$", ErrorMessage = "Vui lòng sử dụng email tổ chức có đuôi @noveratech.digital.")]
    [Display(Name = "Email công ty")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Ghi nhớ thiết bị này")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class StaffForgotPasswordInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập email công ty.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng.")]
    [RegularExpression(@"^[a-zA-Z0-9._%+-]+@noveratech\.digital$", ErrorMessage = "Vui lòng sử dụng email tổ chức có đuôi @noveratech.digital.")]
    [Display(Name = "Email công ty")]
    public string Email { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
