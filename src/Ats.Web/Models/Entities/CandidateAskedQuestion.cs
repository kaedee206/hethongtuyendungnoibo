namespace Ats.Web.Models.Entities;

/// <summary>
/// Thực thể theo dõi các câu hỏi phỏng vấn đã được hỏi cho từng ứng viên qua các vòng (User Story SP-7).
/// Giúp tránh trùng lặp câu hỏi giữa các vòng và cho phép người phỏng vấn đánh dấu câu đã hỏi trong phiên hiện tại.
/// </summary>
public class CandidateAskedQuestion : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// ID ứng viên nhận câu hỏi
    /// </summary>
    public Guid CandidateId { get; set; }
    public Candidate Candidate { get; set; } = null!;

    /// <summary>
    /// ID câu hỏi trong ngân hàng câu hỏi
    /// </summary>
    public Guid QuestionId { get; set; }
    public InterviewQuestionBank Question { get; set; } = null!;

    /// <summary>
    /// ID phiên phỏng vấn diễn ra câu hỏi
    /// </summary>
    public Guid InterviewId { get; set; }
    public Interview Interview { get; set; } = null!;

    /// <summary>
    /// Số thứ tự vòng phỏng vấn tại thời điểm hỏi (Vòng 1, 2, 3...)
    /// </summary>
    public int InterviewRound { get; set; } = 1;

    /// <summary>
    /// Người phỏng vấn đã đặt câu hỏi này
    /// </summary>
    public Guid AskedByUserId { get; set; }
    public User AskedByUser { get; set; } = null!;

    /// <summary>
    /// Thời điểm câu hỏi được đánh dấu đã hỏi
    /// </summary>
    public DateTimeOffset AskedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Ghi chú nhanh của người phỏng vấn khi đặt câu hỏi này
    /// </summary>
    public string? Notes { get; set; }
}
