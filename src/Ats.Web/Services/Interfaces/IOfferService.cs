using Ats.Web.Models.DTOs.Offers;
using Ats.Web.Models.Entities;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao diện dịch vụ quản lý Thư mời nhận việc, Thương lượng lại & Xử lý từ chối Offer (User Story SP-7).
/// </summary>
public interface IOfferService
{
    /// <summary>
    /// Ghi nhận ứng viên từ chối Offer theo danh mục chuẩn, cập nhật trạng thái REJECTED,
    /// đồng thời tự động đưa vào Kho tiềm năng (Talent Pool) nếu được chọn.
    /// </summary>
    Task<(bool Success, string Message, JobOffer? Offer)> RejectOfferAsync(
        RejectOfferDto dto,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo Offer sửa đổi khi thương lượng lại (Offer Revision Flow):
    /// Chuyển offer cũ sang trạng thái REPLACED / SUPERSEDED, tạo offer mới tăng Version và liên kết ParentOfferId.
    /// </summary>
    Task<(bool Success, string Message, JobOffer? NewOffer)> ReviseOfferAsync(
        ReviseOfferDto dto,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách danh mục lý do từ chối chuẩn hóa để hiển thị lên form/modal.
    /// </summary>
    Task<List<OfferRejectionReasonCategoryDto>> GetRejectionReasonsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy thực thể Offer theo ID.
    /// </summary>
    Task<JobOffer?> GetOfferByIdAsync(
        Guid offerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy thông tin chi tiết đầy đủ của Offer phục vụ hiển thị UI / Modal.
    /// </summary>
    Task<OfferItemDto?> GetOfferDetailsAsync(
        Guid offerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy lịch sử các phiên bản Offer của một hồ sơ ứng tuyển (v1, v2, ...).
    /// </summary>
    Task<List<OfferItemDto>> GetOfferHistoryByApplicationAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default);
}
