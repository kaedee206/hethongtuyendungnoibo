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

public class ExecuteImportRequestDto
{
    [Required]
    public List<ImportExcelRowDto> ValidRows { get; set; } = new();
}

public class ExecuteImportResultDto
{
    public int TotalSuccess { get; set; }
    public int TotalFailed { get; set; }
    public List<ImportExcelErrorRowDto> Errors { get; set; } = new();
}

public class UpdateProfileRequestDto
{
    [Required(ErrorMessage = "Họ tên không được để trống.")]
    [StringLength(100, ErrorMessage = "Họ tên không được vượt quá 100 ký tự.")]
    public string FullName { get; set; } = default!;

    [RegularExpression(@"^(0|\+84)[3|5|7|8|9][0-9]{8}$", ErrorMessage = "Số điện thoại không hợp lệ.")]
    [RegularExpression(@"^(0|\+84|84)[35789][0-9]{8}$", ErrorMessage = "Số điện thoại không hợp lệ.")]
    public string? PhoneNumber { get; set; }

    [StringLength(100, ErrorMessage = "Chức danh không được vượt quá 100 ký tự.")]
    public string? JobTitle { get; set; }

    // SCRUM-189: Các trường bị khóa không cho phép đổi
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? Role { get; set; }
}

public class UserProfileResponseDto
{
    public string FullName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? PhoneNumber { get; set; }
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    public string Role { get; set; } = default!;
    
    // Gợi ý: Phân biệt rõ editable vs read-only trong response (vd: metadata/flags) để FE render đúng
    public List<string> ReadOnlyFields { get; set; } = new List<string> { "email", "department", "role" };
    public List<string> EditableFields { get; set; } = new List<string> { "fullName", "phoneNumber", "jobTitle" };
}
