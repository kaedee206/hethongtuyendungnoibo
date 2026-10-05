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

    public Application Application { get; set; } = null!;
    public User CreatedByRecruiter { get; set; } = null!;
    public ICollection<OfferApproval> Approvals { get; set; } = new List<OfferApproval>();
}
