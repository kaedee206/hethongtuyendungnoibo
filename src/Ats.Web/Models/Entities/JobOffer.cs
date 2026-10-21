using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class JobOffer : BaseEntity
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid CreatedByRecruiterId { get; set; }
    
    public Guid? JobPositionId { get; set; }
    public string? JobTitle { get; set; }
    public string? WorkLocation { get; set; }
    
    public decimal BaseSalary { get; set; }
    public decimal? BonusAllowance { get; set; }
    public decimal TotalPackage { get; set; }
    
    public DateOnly? ProposedJoinDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public int? ProbationPeriodMonths { get; set; }
    public string? ContractType { get; set; }
    public string? OfferLetterPath { get; set; }
    
    public bool IsAboveBudget { get; set; } = false;
    public bool IsOutsideBand { get; set; } = false;
    public string? SalaryJustification { get; set; }
    public string? Notes { get; set; }
    public OfferStatus Status { get; set; } = OfferStatus.DRAFT;

    // SCRUM-342: Phản hồi từ ứng viên và quản lý thời hạn
    public string? ResponseToken { get; set; }
    public DateTimeOffset? CandidateRespondedAt { get; set; }
    public string? CandidateFeedback { get; set; }
    public DateTimeOffset? ExpiredAt { get; set; }

    public bool IsExpired() => Status == OfferStatus.EXPIRED || 
        (ExpirationDate.HasValue && ExpirationDate.Value < DateOnly.FromDateTime(DateTime.UtcNow) && (Status == OfferStatus.SENT_TO_CANDIDATE || Status == OfferStatus.APPROVED));

    public Application Application { get; set; } = null!;
    public User CreatedByRecruiter { get; set; } = null!;
    public JobPosition? JobPosition { get; set; }
    public ICollection<OfferApproval> Approvals { get; set; } = new List<OfferApproval>();
}
