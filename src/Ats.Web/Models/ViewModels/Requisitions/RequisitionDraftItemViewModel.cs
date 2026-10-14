namespace Ats.Web.Models.ViewModels.Requisitions;

/// <summary>
/// DTO hiển thị từng bản ghi trong danh sách "Yêu cầu tuyển dụng nháp".
/// </summary>
public class RequisitionDraftItemViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? JobPositionTitle { get; set; }
    public string? JobPositionCode { get; set; }
    public string? DepartmentName { get; set; }
    public int Quantity { get; set; }
    public decimal? MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
    public DateOnly? TargetHireDate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool HasContent { get; set; }
    public bool IsOverQuota { get; set; }
    public string? OverQuotaReason { get; set; }
}
