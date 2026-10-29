using Ats.Web.Models.ViewModels.Offers;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao diện dịch vụ nghiệp vụ soạn thảo, lưu nháp, kiểm tra dải lương và xem trước Offer (SCRUM-340).
/// </summary>
public interface IOfferService
{
    /// <summary>
    /// Lấy dữ liệu khởi tạo cho form soạn đề xuất Offer (thông tin ứng viên, dải lương chuẩn, vị trí, offer hiện có).
    /// </summary>
    Task<JobOfferFormInitViewModel> GetOfferFormInitDataAsync(Guid applicationId, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sinh dữ liệu xem trước Thư mời nhận việc (Offer Letter) từ form đầu vào.
    /// </summary>
    Task<JobOfferPreviewViewModel> GenerateOfferPreviewAsync(JobOfferInputModel input, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lưu đề xuất Offer: Hỗ trợ Lưu nháp (DRAFT) hoặc Gửi duyệt (PENDING_APPROVAL), kiểm tra dải chuẩn và ràng buộc 1 offer hiệu lực.
    /// </summary>
    Task<OfferOperationResult> SaveOfferAsync(JobOfferInputModel input, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rút lại / Hủy bỏ một đề xuất Offer đang hiệu lực để mở đường cho offer mới.
    /// </summary>
    Task<OfferOperationResult> WithdrawOfferAsync(Guid offerId, Guid currentUserId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy dữ liệu xem trước thư mời nhận việc từ bản ghi JobOffer đã lưu theo Id.
    /// </summary>
    Task<JobOfferPreviewViewModel?> GetOfferPreviewByIdAsync(Guid offerId, CancellationToken cancellationToken = default);
}
