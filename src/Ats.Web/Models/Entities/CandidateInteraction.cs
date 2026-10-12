using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class CandidateInteraction : BaseEntity
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Guid? ApplicationId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public CandidateInteractionType InteractionType { get; set; } = CandidateInteractionType.NOTE;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Outcome { get; set; }
    public DateTimeOffset InteractionDate { get; set; } = DateTimeOffset.UtcNow;

    public Candidate Candidate { get; set; } = null!;
    public Application? Application { get; set; }
    public User CreatedByUser { get; set; } = null!;
}
