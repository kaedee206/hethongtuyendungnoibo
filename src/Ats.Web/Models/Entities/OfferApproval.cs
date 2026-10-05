using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class OfferApproval : BaseEntity
{
    public Guid Id { get; set; }
    public Guid OfferId { get; set; }
    public Guid ApproverId { get; set; }
    
    public int Level { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.PENDING;
    public string? Comment { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    public JobOffer Offer { get; set; } = null!;
    public User Approver { get; set; } = null!;
}
