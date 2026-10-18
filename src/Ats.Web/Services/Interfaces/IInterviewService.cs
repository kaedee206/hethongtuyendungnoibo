using Ats.Web.Models.DTOs;
using Ats.Web.Models.ViewModels.Interviews;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Interface định nghĩa nghiệp vụ Đặt lịch phỏng vấn và Phân công hội đồng phỏng vấn (Scrum - Số 51 & SCRUM-53 / EP-06).
/// Tuân thủ nguyên tắc Clean Service Layer, C# 12 and async patterns.
/// </summary>
public interface IInterviewService
{
    /// <summary>
    /// Kiểm tra xung đột lịch phỏng vấn đối với Người phỏng vấn và Phòng họp (SCRUM-53).
    /// Công thức xung đột: A.StartTime &lt; B.EndTime &amp;&amp; A.EndTime &gt; B.StartTime.
    /// Bỏ qua các buổi phỏng vấn đã hủy (CANCELLED) và loại trừ chính ID buổi phỏng vấn đang sửa nếu có.
    /// </summary>
    Task<InterviewConflictCheckResultViewModel> CheckScheduleConflictAsync(
        InterviewConflictCheckInputModel input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đặt lịch phỏng vấn cho một ứng viên trong một lần thao tác.
    /// Tự động chạy kiểm tra xung đột thời gian. Nếu có xung đột mà không ghi đè sẽ ném BusinessRuleValidationException.
    /// Mọi thao tác ghi dữ liệu thực thi trong Database Transaction và ghi AuditLog chi tiết khi ghi đè.
    /// </summary>
    Task<InterviewScheduleResultDto> ScheduleInterviewAsync(
        ScheduleInterviewInputModel model,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật / Đổi lịch phỏng vấn đã có (SCRUM-53).
    /// Tự động kiểm tra xung đột (loại trừ chính interviewId này khỏi tập kiểm tra).
    /// </summary>
    Task<InterviewScheduleResultDto> RescheduleInterviewAsync(
        Guid interviewId,
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
    /// Lấy danh sách các phòng họp khả dụng phục vụ phỏng vấn trực tiếp (SCRUM-53).
    /// </summary>
    Task<List<MeetingRoomLookupDto>> GetAvailableMeetingRoomsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách tất cả các vòng phỏng vấn của một đơn ứng tuyển.
    /// </summary>
    Task<List<InterviewScheduleResultDto>> GetApplicationInterviewsAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chuẩn bị toàn bộ dữ liệu khởi tạo cho Trang Wizard Lên lịch phỏng vấn chuyên biệt (Option 2).
    /// </summary>
    Task<Ats.Web.Models.ViewModels.Interviews.ScheduleInterviewWizardViewModel> GetScheduleWizardDataAsync(
        Guid? applicationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy thông tin chi tiết hồ sơ ứng viên (Dossier) phục vụ hiển thị thẻ tóm tắt và cập nhật động qua AJAX ở Bước 1.
    /// </summary>
    Task<Ats.Web.Models.ViewModels.Interviews.CandidateWizardDetailDto?> GetCandidateWizardDetailAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra lịch trống/trùng lịch phỏng vấn của danh sách giám khảo trong khung giờ chỉ định ở Bước 2.
    /// </summary>
    Task<List<Ats.Web.Models.ViewModels.Interviews.InterviewerBusyCheckDto>> CheckInterviewersAvailabilityAsync(
        List<Guid> interviewerIds,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default);
}

