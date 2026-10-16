using Ats.Web.Models.ViewModels.CandidateInterviews;
using Microsoft.AspNetCore.Http;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Service phụ trách nghiệp vụ Thư mời phỏng vấn và Xác nhận/Đổi lịch dành cho Ứng viên (Scrum - Số 54).
/// </summary>
public interface ICandidateInterviewService
{
    /// <summary>
    /// Sinh mã token bảo mật gắn liền duy nhất với buổi phỏng vấn này.
    /// </summary>
    string GenerateInvitationToken(Guid interviewId, string candidateEmail);

    /// <summary>
    /// Kiểm tra tính hợp lệ của token và trích xuất InterviewId.
    /// </summary>
    bool ValidateInvitationToken(string token, out Guid interviewId);

    /// <summary>
    /// Lấy dữ liệu chi tiết buổi phỏng vấn để hiển thị trang xác nhận của ứng viên.
    /// </summary>
    Task<CandidateInterviewConfirmationViewModel?> GetInterviewConfirmationAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ứng viên xác nhận tham gia buổi phỏng vấn.
    /// </summary>
    Task<(bool Success, string Message)> ConfirmAttendanceAsync(
        CandidateInterviewConfirmInputModel model,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ứng viên đề nghị đổi lịch phỏng vấn kèm lý do và thời gian đề xuất.
    /// </summary>
    Task<(bool Success, string Message)> RequestRescheduleAsync(
        CandidateInterviewRescheduleInputModel model,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xây dựng đường dẫn tuyệt đối cho liên kết thư mời phỏng vấn trong email.
    /// </summary>
    string BuildConfirmationUrl(HttpRequest request, Guid interviewId, string candidateEmail);

    /// <summary>
    /// Xây dựng đường dẫn xác nhận dựa trên baseUrl.
    /// </summary>
    string BuildConfirmationUrl(Guid interviewId, string candidateEmail, string baseUrl);
}
