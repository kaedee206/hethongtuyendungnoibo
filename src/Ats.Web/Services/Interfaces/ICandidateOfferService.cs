using Ats.Web.Models.ViewModels.Offers;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Service quản lý thư mời nhận việc, tạo liên kết phản hồi bảo mật và xử lý phản hồi từ Ứng viên (SCRUM-342).
/// </summary>
public interface ICandidateOfferService
{
    /// <summary>
    /// Sinh mã token bảo mật HMAC SHA256 gắn với Offer và Email của ứng viên.
    /// Định dạng: {offerId:D}_{16_hex_hmac_hash}
    /// </summary>
    string GenerateOfferResponseToken(Guid offerId, string candidateEmail);

    /// <summary>
    /// Kiểm tra tính hợp lệ của mã token và trích xuất OfferId.
    /// Sử dụng so sánh thời gian hằng số để chống timing attacks.
    /// </summary>
    bool ValidateOfferResponseToken(string token, out Guid offerId);

    /// <summary>
    /// Lấy chi tiết thư mời nhận việc theo mã token để ứng viên xem xét và phản hồi.
    /// Tự động kiểm tra thời hạn: Nếu quá thời hạn mà chưa phản hồi thì tự chuyển sang Hết hạn (EXPIRED).
    /// </summary>
    Task<CandidateOfferDetailsViewModel?> GetOfferDetailsByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xử lý quyết định phản hồi của Ứng viên (Chấp nhận hoặc Từ chối kèm ý kiến/phản hồi).
    /// Cập nhật ngay lập tức trạng thái trên pipeline và lịch sử ứng tuyển.
    /// </summary>
    Task<CandidateOfferResponseResult> SubmitOfferResponseAsync(CandidateOfferResponseInputModel model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chuyên viên tuyển dụng gửi chính thức thư mời nhận việc cho ứng viên.
    /// Sinh liên kết phản hồi bảo mật và gửi email thông báo có thời hạn.
    /// </summary>
    Task<(bool Success, string Message, string? ResponseUrl)> SendOfferToCandidateAsync(
        Guid offerId,
        Guid recruiterUserId,
        string? customMessage = null,
        string? baseUrl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Quét và tự động chuyển đổi tất cả các offer quá hạn phản hồi sang trạng thái Hết hạn (EXPIRED).
    /// </summary>
    Task<int> CheckAndExpireOverdueOffersAsync(CancellationToken cancellationToken = default);
}
