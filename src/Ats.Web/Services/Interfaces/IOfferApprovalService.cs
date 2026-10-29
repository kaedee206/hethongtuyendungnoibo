using Ats.Web.Models.ViewModels.Offers;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao diện dịch vụ nghiệp vụ phê duyệt đề xuất Offer theo hạn mức lương (SCRUM-341).
/// </summary>
public interface IOfferApprovalService
{
    /// <summary>
    /// Lấy danh sách hàng đợi các đề xuất Offer đang chờ người dùng hiện tại phê duyệt theo vai trò.
    /// </summary>
    Task<OfferApprovalQueueViewModel> GetPendingOfferQueueAsync(
        Guid currentUserId,
        string role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy chi tiết đề xuất Offer, đối soát dải lương chuẩn, giải trình và toàn bộ lịch sử phê duyệt.
    /// </summary>
    Task<OfferApprovalDetailViewModel?> GetOfferApprovalDetailAsync(
        Guid offerId,
        Guid currentUserId,
        string role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Thực hiện 1 trong 3 hành động: DUYỆT (APPROVE), TỪ CHỐI (REJECT), hoặc YÊU CẦU SỬA (CHANGES_REQUESTED).
    /// </summary>
    Task<OfferApprovalOperationResult> SubmitApprovalDecisionAsync(
        OfferApprovalDecisionInputModel input,
        Guid currentUserId,
        string role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách lịch sử phê duyệt bất biến của một đề xuất Offer.
    /// </summary>
    Task<List<OfferApprovalHistoryItemViewModel>> GetApprovalHistoryAsync(
        Guid offerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Khởi tạo luồng phê duyệt theo hạn mức lương cho một Offer:
    /// - Trong dải chuẩn: 1 cấp duyệt (Level 1: Trưởng phòng Nhân sự).
    /// - Vượt dải chuẩn: 2 cấp duyệt (Level 1: Trưởng phòng Nhân sự + Level 2: Ban Giám Đốc).
    /// </summary>
    Task InitializeApprovalWorkflowAsync(
        Guid offerId,
        CancellationToken cancellationToken = default);
}
