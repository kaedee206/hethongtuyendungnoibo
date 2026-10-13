namespace Ats.Web.Models.Entities;

public class CandidateNote : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CandidateId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string NoteType { get; set; } = "GENERAL"; // GENERAL, INTERVIEW, SALARY, EVALUATION, RECRUITER_CALL

    public Candidate Candidate { get; set; } = null!;
    public User AuthorUser { get; set; } = null!;
}
