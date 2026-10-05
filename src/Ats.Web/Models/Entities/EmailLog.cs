namespace Ats.Web.Models.Entities;

public class EmailLog : BaseEntity
{
    public Guid Id { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // SENT, FAILED
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
