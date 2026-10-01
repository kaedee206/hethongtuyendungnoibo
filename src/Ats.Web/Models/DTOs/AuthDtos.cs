using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.DTOs;

public record LoginRequestDto(
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    string Email,

    [Required(ErrorMessage = "Mật khẩu không được để trống.")]
    string Password
);

public record AuthResponseDto(
    bool IsSuccess,
    string Message,
    UserInfoDto? Data
);

public record UserInfoDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    List<string> Roles,
    string RedirectUrl
);

public record ForgotPasswordRequestDto(
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    string Email
);

public record ResetPasswordRequestDto(
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    string Email,
    
    [Required(ErrorMessage = "Token không được để trống.")]
    string Token,
    
    [Required(ErrorMessage = "Mật khẩu mới không được để trống.")]
    [MinLength(8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$", ErrorMessage = "Mật khẩu phải chứa ít nhất 1 chữ hoa, 1 chữ thường, 1 số và 1 ký tự đặc biệt.")]
    string NewPassword
);

public record ChangePasswordRequestDto(
    [Required(ErrorMessage = "Mật khẩu hiện tại không được để trống.")]
    string CurrentPassword,
    
    [Required(ErrorMessage = "Mật khẩu mới không được để trống.")]
    [MinLength(8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$", ErrorMessage = "Mật khẩu phải chứa ít nhất 1 chữ hoa, 1 chữ thường, 1 số và 1 ký tự đặc biệt.")]
    string NewPassword
);

public record CreateAccountRequestDto(
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    string Email,
    
    [Required(ErrorMessage = "Họ tên không được để trống.")]
    string FullName,

    string? Role,
    List<string>? Roles = null,
    string? Department = null
);