using Ats.Web.Models.ViewModels.Evaluations;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao diện dịch vụ xử lý Phiếu đánh giá tự động sinh theo khung năng lực (SCRUM-56).
/// </summary>
public interface IEvaluationService
{
    /// <summary>
    /// Tự động sinh hoặc tải lại phiếu đánh giá dựa trên khung năng lực của chức danh.
    /// Nếu đã có phiếu nháp hoặc đã nộp thì tải từ bản ghi snapshot bất biến.
    /// </summary>
    Task<EvaluationFormGenerateViewModel> GenerateEvaluationFormAsync(
        Guid applicationId,
        Guid interviewId,
        Guid evaluatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Nộp phiếu đánh giá hoặc lưu nháp trong database transaction tường minh.
    /// Thực hiện snapshot toàn bộ tiêu chí, trọng số, tính điểm bình quân gia quyền và ghi AuditLog.
    /// </summary>
    Task<Guid> SubmitEvaluationAsync(
        EvaluationFormSubmitInputModel input,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy chi tiết phiếu đánh giá đã nộp (Interviewer chỉ xem phiếu của mình, HR Manager / Admin xem được tất cả).
    /// </summary>
    Task<EvaluationDetailViewModel?> GetEvaluationDetailAsync(
        Guid evaluationFormId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bảng tổng hợp so sánh các ứng viên cho vị trí tuyển dụng trên cùng một thước đo khung năng lực (dành cho HR Manager).
    /// </summary>
    Task<EvaluationSummaryComparisonViewModel> GetComparisonByJobAsync(
        Guid jobPostingId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);
}
