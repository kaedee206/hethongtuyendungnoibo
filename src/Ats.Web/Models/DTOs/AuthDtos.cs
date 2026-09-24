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
    string? Message,
    UserInfoDto? User
);

public record UserInfoDto(
    Guid Id,
    string Email,
    string FullName
);