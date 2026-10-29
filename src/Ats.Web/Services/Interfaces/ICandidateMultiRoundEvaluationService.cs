using Ats.Web.Models.ViewModels.Candidates;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao diện dịch vụ Tổng hợp kết quả đánh giá nhiều vòng và Ra quyết định tuyển (SP - 7).
/// Dành cho Trưởng bộ phận (Hiring Manager) và các cấp quản lý tuyển dụng.
/// </summary>
public interface ICandidateMultiRoundEvaluationService
{
    /// <summary>
    /// Lấy toàn bộ phiếu đánh giá của mọi vòng phỏng vấn cho một ứng viên kèm điểm trung bình theo tiêu chí và nhật ký quyết định.
    /// </summary>
    Task<CandidateMultiRoundEvaluationSummaryViewModel> GetMultiRoundSummaryAsync(
        Guid applicationId,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Thực hiện lưu quyết định tuyển của Trưởng bộ phận (Đề xuất offer hoặc Loại kèm lý do) và ghi nhật ký kèm người quyết định.
    /// </summary>
    Task<HiringDecisionLogDto> MakeHiringDecisionAsync(
        CandidateHiringDecisionInputModel input,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default);
}
