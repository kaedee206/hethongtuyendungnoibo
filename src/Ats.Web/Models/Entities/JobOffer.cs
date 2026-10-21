using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class JobOffer : BaseEntity
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid CreatedByRecruiterId { get; set; }
    
    public decimal BaseSalary { get; set; }
    public decimal? BonusAllowance { get; set; }
    public decimal TotalPackage { get; set; }
    
    public DateOnly? ProposedJoinDate { get; set; }
    public int? ProbationPeriodMonths { get; set; }
    public string? ContractType { get; set; }
    public string? OfferLetterPath { get; set; }
    
    public bool IsAboveBudget { get; set; } = false;
    public OfferStatus Status { get; set; } = OfferStatus.DRAFT;

    // SP-7: Phiên bản Offer & Luồng thương lượng lại (Offer Revision Flow)
    public int Version { get; set; } = 1;
    public Guid? ParentOfferId { get; set; }

    // SP-7: Ghi nhận lý do từ chối offer (Rejection Reason Categorization)
    public OfferRejectionReasonCategory? RejectionReasonCategory { get; set; }
    public string? RejectionNotes { get; set; }
    public decimal? ExpectedSalary { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }

    public Application Application { get; set; } = null!;
    public User CreatedByRecruiter { get; set; } = null!;
    public JobOffer? ParentOffer { get; set; }
    public ICollection<JobOffer> RevisedOffers { get; set; } = new List<JobOffer>();
    public ICollection<OfferApproval> Approvals { get; set; } = new List<OfferApproval>();
}
