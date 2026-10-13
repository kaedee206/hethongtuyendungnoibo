namespace Ats.Web.Models.DTOs;

/// <summary>
/// DTO tra cứu danh mục lý do loại hồ sơ (SCRUM-42).
/// Dùng để hiển thị danh sách dropdown chọn lý do loại khi chuyển ứng viên sang REJECTED.
/// </summary>
public class RejectionReasonLookupDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
}
