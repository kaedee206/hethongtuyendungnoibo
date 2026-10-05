using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class JobPosting : BaseEntity
{
    public Guid Id { get; set; }
    public Guid RequisitionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string WorkLocation { get; set; } = string.Empty;
    
    public EmploymentType EmploymentType { get; set; }
    public string? SalaryDisplay { get; set; }
    
    public string? JobDescription { get; set; }
    public string? Requirements { get; set; }
    public string? Benefits { get; set; }
    
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ExpiredAt { get; set; }
    public JobPostingStatus Status { get; set; } = JobPostingStatus.DRAFT;

    public JobRequisition Requisition { get; set; } = null!;
}
