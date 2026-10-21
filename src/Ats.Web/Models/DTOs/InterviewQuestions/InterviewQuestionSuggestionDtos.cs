namespace Ats.Web.Models.DTOs.InterviewQuestions;

/// <summary>
/// Chi tiết một câu hỏi phỏng vấn gợi ý (SP-7).
/// Bao gồm nội dung, tiêu chí, độ khó, gợi ý câu trả lời tốt (Rubric) và trạng thái hỏi trùng lặp qua các vòng.
/// </summary>
public class InterviewQuestionSuggestionItemDto
{
    public Guid QuestionId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Competency { get; set; } = string.Empty;
    public Guid? CriteriaId { get; set; }
    public string CriteriaName { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "Cơ bản"; // Cơ bản, Trung bình, Nâng cao

    // AC3: Gợi ý câu trả lời tốt (Good Answer Rubric)
    public string? ExpectedAnswer { get; set; }
    public string? KeyPoints { get; set; }
    public string? GoodAnswerRubric { get; set; }

    // AC2: Tránh trùng lặp qua các vòng
    public bool IsAskedInPreviousRounds { get; set; }
    public string? PreviousRoundBadge { get; set; } // Ví dụ: "Đã hỏi ở Vòng 1 (Trần Văn A)"
    public string? PreviousRoundInterviewerName { get; set; }
    public DateTimeOffset? PreviousRoundAskedAt { get; set; }
    public List<PreviousRoundAskedDetailDto> PreviousRoundsHistory { get; set; } = [];

    // Trạng thái phiên hiện tại
    public bool IsAskedInCurrentRound { get; set; }
    public Guid? CurrentRoundAskedByUserId { get; set; }
    public string? CurrentRoundAskedByUserName { get; set; }

    // Chức danh liên kết
    public Guid? JobPositionId { get; set; }
    public string? JobPositionTitle { get; set; }
}

/// <summary>
/// Chi tiết lịch sử hỏi ở vòng trước
/// </summary>
public class PreviousRoundAskedDetailDto
{
    public int RoundNumber { get; set; }
    public string InterviewerName { get; set; } = string.Empty;
    public DateTimeOffset AskedAt { get; set; }
}

/// <summary>
/// DTO phản hồi danh sách câu hỏi gợi ý và bộ lọc
/// </summary>
public class InterviewQuestionSuggestionResponseDto
{
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public Guid InterviewId { get; set; }
    public int CurrentRound { get; set; } = 1;
    public Guid? JobPositionId { get; set; }
    public string JobPositionTitle { get; set; } = string.Empty;

    public int TotalQuestions { get; set; }
    public int UnaskedCount { get; set; }
    public int AskedInPreviousRoundsCount { get; set; }
    public int AskedInCurrentRoundCount { get; set; }

    public List<CriteriaFilterOptionDto> CriteriaOptions { get; set; } = [];
    public List<InterviewQuestionSuggestionItemDto> Questions { get; set; } = [];
}

/// <summary>
/// Tiêu chí lọc câu hỏi
/// </summary>
public class CriteriaFilterOptionDto
{
    public Guid? CriteriaId { get; set; }
    public string CriteriaName { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
}

/// <summary>
/// Model nhận request toggle trạng thái câu hỏi đã hỏi trong phiên hiện tại
/// </summary>
public class ToggleQuestionAskedInputModel
{
    public Guid CandidateId { get; set; }
    public Guid InterviewId { get; set; }
    public Guid QuestionId { get; set; }
    public bool IsAsked { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Kết quả sau khi toggle
/// </summary>
public class ToggleQuestionAskedResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid QuestionId { get; set; }
    public bool IsAskedInCurrentRound { get; set; }
    public int CurrentRoundAskedCount { get; set; }
}
