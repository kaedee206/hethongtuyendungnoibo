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
    [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
    string NewPassword
);