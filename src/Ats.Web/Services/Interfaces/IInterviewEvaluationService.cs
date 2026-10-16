using Ats.Web.Models.ViewModels.Interviews;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao diện xử lý nghiệp vụ Phiếu đánh giá phỏng vấn (Scrum - Số 57)
/// </summary>
public interface IInterviewEvaluationService
{
    /// <summary>
    /// Mở phiếu đánh giá phỏng vấn.
    /// Quy tắc 1: Chỉ người phỏng vấn được mời mới mở được phiếu của buổi đó (hoặc quản lý cấp cao).
    /// Quy tắc 4: Không nhìn thấy đánh giá của người khác trước khi nộp phiếu của mình.
    /// </summary>
    Task<(bool CanOpen, string? ErrorMessage, InterviewEvaluationSheetViewModel? Model)> GetEvaluationSheetAsync(
        Guid interviewId, Guid currentUserId, bool isStaffManager, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nộp phiếu đánh giá sau buổi phỏng vấn.
    /// Quy tắc 1: Chỉ người phỏng vấn được mời mới nộp được phiếu của buổi đó.
    /// Quy tắc 2: Bắt buộc chọn kết luận: Nên tuyển, Cân nhắc, Không nên tuyển.
    /// Quy tắc 3: Đã nộp thì không sửa được, trừ khi recruiter mở lại trong 24 giờ.
    /// </summary>
    Task<(bool Success, string Message, Guid? EvaluationId)> SubmitEvaluationAsync(
        InterviewEvaluationSubmitInputModel input, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recruiter mở lại phiếu đánh giá cho người phỏng vấn.
    /// Quy tắc 3: Chỉ recruiter (hoặc HR Manager/Admin) mới mở lại được và phải trong vòng 24 giờ kể từ khi nộp.
    /// </summary>
    Task<(bool Success, string Message)> ReopenEvaluationAsync(
        Guid evaluationId, Guid recruiterUserId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách đánh giá của toàn bộ hội đồng phỏng vấn.
    /// Quy tắc 4: Không nhìn thấy đánh giá của người khác trước khi nộp phiếu của mình.
    /// </summary>
    Task<(bool Success, string? Message, List<PanelistEvaluationSummaryDto>? Evaluations)> GetPanelEvaluationsAsync(
        Guid interviewId, Guid currentUserId, bool isStaffManager, CancellationToken cancellationToken = default);
}
