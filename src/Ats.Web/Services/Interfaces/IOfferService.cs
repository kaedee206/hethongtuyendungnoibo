using Ats.Web.Models.Entities;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Quản lý vòng đời và phản hồi Thư mời nhận việc (Job Offer) tích hợp thông báo nội bộ (SP-8).
/// </summary>
public interface IOfferService
{
    /// <summary>
    /// Ứng viên chấp thuận Thư mời nhận việc.
    /// Kích hoạt thông báo tới Recruiter phụ trách và người tạo Offer.
    /// </summary>
    Task<(bool Success, string Message)> AcceptOfferAsync(
        Guid offerId,
        string? candidateComment = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ứng viên từ chối Thư mời nhận việc.
    /// Kích hoạt thông báo tới Recruiter phụ trách và người tạo Offer.
    /// </summary>
    Task<(bool Success, string Message)> RejectOfferAsync(
        Guid offerId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ứng viên yêu cầu thương lượng lại các điều khoản Offer.
    /// Kích hoạt thông báo tới Recruiter phụ trách và người tạo Offer.
    /// </summary>
    Task<(bool Success, string Message)> ReviseOfferAsync(
        Guid offerId,
        string? feedback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bắn thông báo chờ duyệt khi có Đề xuất Offer mới được tạo.
    /// Gửi tới các Approver / HR Manager có thẩm quyền phê duyệt.
    /// </summary>
    Task NotifyOfferPendingApprovalAsync(
        JobOffer offer,
        Guid candidateId,
        string candidateName,
        string jobTitle,
        decimal baseSalary,
        CancellationToken cancellationToken = default);
}
