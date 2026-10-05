using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.DTOs;

public class DepartmentCreateRequestDto
{
    [Required(ErrorMessage = "Tên phòng ban là bắt buộc.")]
    public string Name { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Mã phòng ban là bắt buộc.")]
    public string Code { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }
    public Guid? ManagerId { get; set; }
}

public class DepartmentUpdateRequestDto
{
    [Required(ErrorMessage = "Tên phòng ban là bắt buộc.")]
    public string Name { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Mã phòng ban là bắt buộc.")]
    public string Code { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }
    public Guid? ManagerId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AssignManagerRequestDto
{
    [Required(ErrorMessage = "Người phụ trách là bắt buộc.")]
    public Guid ManagerId { get; set; }
}

public class DepartmentResponseDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public Guid? ManagerId { get; set; }
    public string? ManagerName { get; set; }
    public string? ManagerEmail { get; set; }
    public string? ManagerTitle { get; set; }
    public bool IsActive { get; set; }
    public int Level { get; set; }
    public string Path { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    
    public List<DepartmentResponseDto> Children { get; set; } = new List<DepartmentResponseDto>();
}
