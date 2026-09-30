using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.DTOs;

public record UserSearchResponseDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    string? Department,
    string Status,
    DateTimeOffset CreatedAt
);

public record UpdateUserRequestDto(
    [Required(ErrorMessage = "Họ tên không được để trống.")]
    string FullName,
    string? Department,
    [Required(ErrorMessage = "Vai trò không được để trống.")]
    string Role,
    [Required(ErrorMessage = "Trạng thái không được để trống.")]
    string Status
);
