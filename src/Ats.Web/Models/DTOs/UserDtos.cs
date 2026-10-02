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

public class ImportExcelResultDto
{
    public List<ImportExcelRowDto> ValidRows { get; set; } = new();
    public List<ImportExcelErrorRowDto> InvalidRows { get; set; } = new();
}

public class ImportExcelRowDto
{
    public int RowIndex { get; set; }
    public string FullName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? PhoneNumber { get; set; }
    public string? Department { get; set; }
    public string? JobPosition { get; set; }
    public string Roles { get; set; } = default!;
}

public class ImportExcelErrorRowDto
{
    public int RowIndex { get; set; }
    public List<ImportExcelErrorDetailDto> Errors { get; set; } = new();
}

public class ImportExcelErrorDetailDto
{
    public string ColumnName { get; set; } = default!;
    public string ErrorMessage { get; set; } = default!;
}
