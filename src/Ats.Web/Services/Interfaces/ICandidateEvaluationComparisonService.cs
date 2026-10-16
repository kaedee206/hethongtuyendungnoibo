using Ats.Web.Models.ViewModels.CandidateComparison;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao diện dịch vụ So sánh ứng viên theo điểm đánh giá (SCRUM-336 / Scrum - Số 58).
/// Phục vụ Trưởng bộ phận (Hiring Manager) và HR đánh giá khách quan dựa trên tiêu chí thay vì cảm tính cá nhân.
/// </summary>
public interface ICandidateEvaluationComparisonService
{
    /// <summary>
    /// Lấy dữ liệu bảng so sánh ứng viên cho một vị trí tuyển dụng cụ thể.
    /// Chỉ hiện các ứng viên đã có tối thiểu 1 phiếu đánh giá đã nộp (IsSubmitted == true).
    /// Sắp xếp mặc định theo điểm tổng giảm dần.
    /// </summary>
    Task<CandidateEvaluationComparisonViewModel> GetEvaluationComparisonAsync(
        Guid? jobPostingId,
        string? stageFilter = null,
        string? sortBy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách các vị trí tuyển dụng có ứng viên đã được nộp phiếu đánh giá.
    /// </summary>
    Task<List<JobComparisonOptionDto>> GetAvailableJobsForComparisonAsync(
        CancellationToken cancellationToken = default);
}
