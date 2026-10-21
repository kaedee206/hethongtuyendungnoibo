using Ats.Web.Models.DTOs.InterviewQuestions;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao diện dịch vụ gợi ý bộ câu hỏi phỏng vấn theo tiêu chí và chức danh (User Story SP-7).
/// </summary>
public interface IInterviewQuestionSuggestionService
{
    /// <summary>
    /// Lấy danh sách câu hỏi gợi ý cho ứng viên trong buổi phỏng vấn (AC1, AC2, AC3).
    /// Tự động lọc theo chức danh ứng viên, tiêu chí đánh giá của vòng hiện tại và đánh dấu câu hỏi đã hỏi ở vòng trước.
    /// </summary>
    Task<InterviewQuestionSuggestionResponseDto> GetSuggestedQuestionsAsync(
        Guid candidateId,
        Guid interviewId,
        Guid? positionId = null,
        Guid? criteriaId = null,
        string? criteriaName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đánh dấu hoặc bỏ đánh dấu câu hỏi đã hỏi trong phiên phỏng vấn hiện tại (AC2).
    /// </summary>
    Task<ToggleQuestionAskedResultDto> ToggleQuestionAskedAsync(
        ToggleQuestionAskedInputModel input,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Khởi tạo dữ liệu câu hỏi mẫu phong phú theo tiêu chí và chức danh nếu hệ thống còn trống.
    /// </summary>
    Task SeedInitialQuestionBankAsync(CancellationToken cancellationToken = default);
}
