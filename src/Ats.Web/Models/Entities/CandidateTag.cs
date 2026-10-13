namespace Ats.Web.Models.Entities;

public class CandidateTag : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CandidateId { get; set; }
    public string TagName { get; set; } = string.Empty;
    public string? ColorCode { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public Candidate Candidate { get; set; } = null!;
    public User? CreatedByUser { get; set; }
}
