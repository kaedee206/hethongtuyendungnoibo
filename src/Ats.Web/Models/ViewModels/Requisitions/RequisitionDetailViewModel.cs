using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Requisitions;

/// <summary>
/// ViewModel hiển thị chi tiết đầy đủ của một Yêu cầu Tuyển dụng.
/// </summary>
public class RequisitionDetailViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;

    // Vị trí & Cơ cấu
    public string? JobPositionTitle { get; set; }
    public string? JobPositionCode { get; set; }
    public string? DepartmentName { get; set; }

    // Người tạo & Trách nhiệm
    public Guid HiringManagerId { get; set; }
    public string? HiringManagerName { get; set; }
    public string? HiringManagerEmail { get; set; }

    // Thông số tuyển dụng
    public int Quantity { get; set; }
    public HeadcountType HeadcountType { get; set; }
    public string HeadcountTypeName { get; set; } = string.Empty;
    public string? Reason { get; set; }

    // Đãi ngộ & Mục tiêu
    public decimal? MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
    public string? SalaryBandExplanation { get; set; }
    public string Currency { get; set; } = "VND";
    public string SalaryDisplay { get; set; } = "Thỏa thuận";
    public DateOnly? TargetHireDate { get; set; }

    // Trạng thái
    public RequisitionStatus Status { get; set; }
    public string StatusDisplayName { get; set; } = string.Empty;
    public string StatusBadgeClass { get; set; } = string.Empty;

    // Nội dung chi tiết Rich Text
    public string? JobDescription { get; set; }
    public string? Requirements { get; set; }

    // Thời gian
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Cho phép tiếp tục chỉnh sửa nếu yêu cầu vẫn còn ở trạng thái nháp.
    /// </summary>
    public bool CanEdit => Status == RequisitionStatus.DRAFT;
}
