namespace Ats.Web.Models.Entities;

public class InterviewPanelist : BaseEntity
{
    public Guid Id { get; set; }
    public Guid InterviewId { get; set; }
    public Guid InterviewerId { get; set; }
    public bool IsLead { get; set; } = false;
    public bool Attended { get; set; } = false;

    public Interview Interview { get; set; } = null!;
    public User Interviewer { get; set; } = null!;
}
