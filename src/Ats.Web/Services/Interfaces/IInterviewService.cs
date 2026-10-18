using Ats.Web.Models.DTOs;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Interface định nghĩa nghiệp vụ Đặt lịch phỏng vấn và Phân công hội đồng phỏng vấn (Scrum - Số 51 / EP-06).
/// Tuân thủ nguyên tắc Clean Service Layer, C# 12 and async patterns.
/// </summary>
public interface IInterviewService
{
    /// <summary>
    /// Đặt lịch phỏng vấn cho một ứng viên trong một lần thao tác.
    /// Kiểm tra nghiêm ngặt: không đặt lịch ở quá khứ, bắt buộc chọn người phỏng vấn, thời lượng hợp lệ.
    /// Tự động cập nhật giai đoạn tuyển dụng, phân công hội đồng và gửi thư mời email.
    /// </summary>
    Task<InterviewScheduleResultDto> ScheduleInterviewAsync(
        ScheduleInterviewInputModel model,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách nhân sự nội bộ đang hoạt động có thể phân công phỏng vấn.
    /// </summary>
    Task<List<InterviewerLookupDto>> GetAvailableInterviewersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách tất cả các vòng phỏng vấn của một đơn ứng tuyển.
    /// </summary>
    Task<List<InterviewScheduleResultDto>> GetApplicationInterviewsAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tính toán và xem trước các khe thời gian liên tiếp cho danh sách ứng viên (Scrum - Số 52).
    /// Tự động chia slot dựa trên mốc bắt đầu, thời lượng và thời gian giải lao giữa các ca.
    /// </summary>
    Task<BatchSchedulePreviewResultDto> PreviewBatchScheduleAsync(
        BatchSchedulePreviewInputModel model,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xác nhận đặt lịch phỏng vấn hàng loạt và gửi thư mời email cho từng ứng viên với đúng khung giờ (Scrum - Số 52).
    /// </summary>
    Task<BatchScheduleResultDto> ConfirmBatchScheduleAsync(
        BatchScheduleConfirmInputModel model,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách ứng viên hợp lệ để nhân viên tuyển dụng chọn đặt lịch phỏng vấn tập trung (Scrum - Số 52).
    /// </summary>
    Task<List<Ats.Web.Models.ViewModels.Workspace.CandidatePipelineItemDto>> GetCandidatesForBatchSchedulingAsync(
        Guid? jobPostingId,
        int? stageOrder,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy thông tin chi tiết của một buổi phỏng vấn để mở form dời lịch hoặc xem lại (Scrum - Số 55).
    /// </summary>
    Task<InterviewDetailForEditDto?> GetInterviewDetailForEditAsync(
        Guid interviewId,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dời lịch phỏng vấn: đổi ngày giờ, hình thức, liên kết và danh sách người phỏng vấn (Scrum - Số 55).
    /// Chặn nếu đã có phiếu đánh giá nộp. Gửi thông báo đến ứng viên và toàn bộ người phỏng vấn.
    /// </summary>
    Task<InterviewScheduleResultDto> RescheduleInterviewAsync(
        RescheduleInterviewInputModel model,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Huỷ buổi phỏng vấn với lý do bắt buộc (Scrum - Số 55).
    /// Chặn nếu đã có phiếu đánh giá nộp. Gửi thông báo đến ứng viên và toàn bộ người phỏng vấn.
    /// </summary>
    Task<InterviewCancelResultDto> CancelInterviewAsync(
        CancelInterviewInputModel model,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default);
}
