namespace Ats.Web.Models.ViewModels.CandidateTimeline;

public class CandidateTimelineEventDto
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty; // APPLIED, STAGE_CHANGE, EMAIL_SENT, INTERVIEW_SCHEDULED, INTERVIEW_EVALUATION, CALL_NOTE
    public string EventCategory { get; set; } = string.Empty; // Ho so, Giai doan, Email, Phong van, Danh gia, Cuoc goi
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string OccurredAtDisplay { get; set; } = string.Empty;
    public string TimeAgoDisplay { get; set; } = string.Empty;

    // Ai da lien he / nguoi thuc hien
    public string ActorName { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string? ActorEmail { get; set; }
    public string ActorAvatarLetter => string.IsNullOrWhiteSpace(ActorName) ? "A" : ActorName.Trim()[0].ToString().ToUpperInvariant();

    // Noi dung noi gi / nhan xet / trao doi
    public string Summary { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? Outcome { get; set; }

    public string? JobTitle { get; set; }
    public string? StageName { get; set; }
    public decimal? Score { get; set; }
    public string? Recommendation { get; set; }

    // Phong cach hien thi theo RULE.md
    public string BadgeClass { get; set; } = "bg-secondary";
    public string IconClass { get; set; } = "bi-clock";
    public Dictionary<string, string> Metadata { get; set; } = new();
}
