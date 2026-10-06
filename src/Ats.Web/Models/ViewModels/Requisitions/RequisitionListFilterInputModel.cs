using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Requisitions;

/// <summary>
/// Model nhận các tham số lọc, tìm kiếm, sắp xếp và phân trang từ Query String.
/// </summary>
public class RequisitionListFilterInputModel
{
    public string? Search { get; set; }
    public RequisitionStatus? Status { get; set; }
    public Guid? JobPositionId { get; set; }
    public string SortBy { get; set; } = "created_desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
