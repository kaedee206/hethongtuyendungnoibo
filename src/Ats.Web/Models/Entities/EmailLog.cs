namespace Ats.Web.Models.Entities;

public class EmailLog : BaseEntity
{
    public Guid Id { get; set; }
    public Guid? CandidateId { get; set; }
    public Candidate? Candidate { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string EmailType { get; set; } = string.Empty; // Vd: Thư mời phỏng vấn, Thư mời nhận việc, Tiếp nhận hồ sơ...
    public string? HtmlBody { get; set; }
    public string Status { get; set; } = string.Empty; // QUEUED, SENT, FAILED
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; } = 0;
    public int MaxRetries { get; set; } = 3;
    public Guid? TriggeredByUserId { get; set; }
    public User? TriggeredByUser { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
