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
