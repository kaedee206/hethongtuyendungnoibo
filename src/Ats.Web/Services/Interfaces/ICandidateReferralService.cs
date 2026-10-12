using Ats.Web.Models.ViewModels.Referral;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Interface dịch vụ giới thiệu ứng viên nội bộ (Scrum #37)
/// </summary>
public interface ICandidateReferralService
{
    /// <summary>
    /// Xử lý giới thiệu ứng viên mới từ nhân viên nội bộ
    /// </summary>
    Task<(bool Success, string Message, Guid? ApplicationId, string? TrackingCode)> ReferCandidateAsync(ReferCandidateInputModel model, Guid referrerUserId);

    /// <summary>
    /// Lấy danh sách hồ sơ do chính nhân viên nội bộ giới thiệu kèm tiến độ tổng quát an toàn
    /// </summary>
    Task<MyReferralsViewModel> GetMyReferralsAsync(Guid referrerUserId);

    /// <summary>
    /// Lấy danh sách các vị trí tuyển dụng đang mở để chọn khi giới thiệu
    /// </summary>
    Task<List<JobPostingSelectDto>> GetOpenJobPostingsAsync();
}
