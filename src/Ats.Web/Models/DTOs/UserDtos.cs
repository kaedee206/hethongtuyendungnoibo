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
