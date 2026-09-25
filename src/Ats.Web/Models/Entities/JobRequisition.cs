using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class JobRequisition : BaseEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid JobPositionId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid HiringManagerId { get; set; }
    public Guid? AssignedRecruiterId { get; set; }
    
    public int Quantity { get; set; }
    public HeadcountType HeadcountType { get; set; }
    public string? Reason { get; set; }
    
    public decimal? MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
    public string Currency { get; set; } = "VND";
    
    public DateOnly? TargetHireDate { get; set; }
    public RequisitionStatus Status { get; set; } = RequisitionStatus.DRAFT;

    public JobPosition JobPosition { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public User HiringManager { get; set; } = null!;
    public User? AssignedRecruiter { get; set; }
    public ICollection<RequisitionApproval> Approvals { get; set; } = new List<RequisitionApproval>();
}
