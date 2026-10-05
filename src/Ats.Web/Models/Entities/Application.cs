using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class Application : BaseEntity
{
    public Guid Id { get; set; }
    public Guid JobPostingId { get; set; }
    public Guid CandidateId { get; set; }
    public Guid ResumeId { get; set; }
    public Guid CurrentStageId { get; set; }
    
    public ApplicationStatus Status { get; set; } = ApplicationStatus.IN_PROCESS;
    public Guid? RejectionReasonId { get; set; }
    public string? RejectionNote { get; set; }
    public int? Rating { get; set; } // 1-5
    
    public DateTimeOffset? AppliedAt { get; set; }

    public JobPosting JobPosting { get; set; } = null!;
    public Candidate Candidate { get; set; } = null!;
    public Resume Resume { get; set; } = null!;
    public PipelineStage CurrentStage { get; set; } = null!;
    public ICollection<ApplicationStageHistory> StageHistories { get; set; } = new List<ApplicationStageHistory>();
    public ICollection<Interview> Interviews { get; set; } = new List<Interview>();
    public ICollection<JobOffer> JobOffers { get; set; } = new List<JobOffer>();
}
