using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class RequisitionApproval : BaseEntity
{
    public Guid Id { get; set; }
    public Guid RequisitionId { get; set; }
    public Guid ApproverId { get; set; }
    
    public int StepOrder { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.PENDING;
    public string? Comment { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    public JobRequisition Requisition { get; set; } = null!;
    public User Approver { get; set; } = null!;
}
