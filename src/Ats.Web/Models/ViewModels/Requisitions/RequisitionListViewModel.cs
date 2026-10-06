using Ats.Web.Models.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models.ViewModels.Requisitions;

/// <summary>
/// ViewModel chứa toàn bộ dữ liệu hiển thị cho màn hình Danh sách Yêu cầu Tuyển dụng.
/// </summary>
public class RequisitionListViewModel
{
    public List<RequisitionListItemViewModel> Items { get; set; } = [];

    // Bộ lọc & Sắp xếp hiện tại
    public string? Search { get; set; }
    public RequisitionStatus? Status { get; set; }
    public Guid? JobPositionId { get; set; }
    public string SortBy { get; set; } = "created_desc";

    // Phân trang
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalItems { get; set; }
    public int TotalPages => TotalItems == 0 ? 1 : (int)Math.Ceiling((double)TotalItems / Math.Max(1, PageSize));
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;

    // Số lượng đếm cho Quick Filter Tabs
    public int TotalCount { get; set; }
    public int DraftCount { get; set; }
    public int PendingCount { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }

    // SelectLists cho các dropdown bộ lọc
    public List<SelectListItem> JobPositionOptions { get; set; } = [];
    public List<SelectListItem> StatusOptions { get; set; } = [];
    public List<SelectListItem> SortOptions { get; set; } = [];
}
