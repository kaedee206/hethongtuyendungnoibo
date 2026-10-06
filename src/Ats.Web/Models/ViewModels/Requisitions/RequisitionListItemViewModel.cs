using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Requisitions;

/// <summary>
/// DTO đại diện cho một hàng trong bảng Danh sách Yêu cầu Tuyển dụng của Trưởng bộ phận.
/// </summary>
public class RequisitionListItemViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? JobPositionTitle { get; set; }
    public string? JobPositionCode { get; set; }
    public string? DepartmentName { get; set; }
    public int Quantity { get; set; }
    public HeadcountType HeadcountType { get; set; }
    public string HeadcountTypeName { get; set; } = string.Empty;

    public RequisitionStatus Status { get; set; }
    public string StatusDisplayName { get; set; } = string.Empty;
    public string StatusBadgeClass { get; set; } = string.Empty;

    public decimal? MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
    public string SalaryDisplay { get; set; } = "Thỏa thuận";

    public DateTimeOffset CreatedAt { get; set; }
    public DateOnly? TargetHireDate { get; set; }

    /// <summary>
    /// Kiểm tra yêu cầu tuyển dụng có còn ở trạng thái nháp hay không.
    /// Nếu là nháp, nhấp vào hàng sẽ tiếp tục chỉnh sửa. Nếu không, chuyển sang xem chi tiết.
    /// </summary>
    public bool IsDraft => Status == RequisitionStatus.DRAFT;

    /// <summary>
    /// Kiểm tra ngày cần người có sắp đến hạn (trong vòng 7 ngày) hoặc đã quá hạn hay không.
    /// </summary>
    public bool IsUrgentOrOverdue
    {
        get
        {
            if (!TargetHireDate.HasValue || Status == RequisitionStatus.FULFILLED || Status == RequisitionStatus.CANCELLED)
                return false;

            var today = DateOnly.FromDateTime(DateTime.Today);
            return TargetHireDate.Value <= today.AddDays(7);
        }
    }
}
