using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class Interview : BaseEntity
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public int RoundNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public InterviewType InterviewType { get; set; }
    public string? LocationOrLink { get; set; }
    
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    
    public CandidateConfirmStatus CandidateConfirmed { get; set; } = CandidateConfirmStatus.PENDING;
    public string? CandidateNotes { get; set; }
    public InterviewStatus Status { get; set; } = InterviewStatus.SCHEDULED;

    public Application Application { get; set; } = null!;
    public ICollection<InterviewPanelist> Panelists { get; set; } = new List<InterviewPanelist>();
    public ICollection<InterviewEvaluation> Evaluations { get; set; } = new List<InterviewEvaluation>();
}
