using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class EvaluationForm : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid InterviewId { get; set; }
    public Guid EvaluatorId { get; set; }
    public EvaluationFormStatus Status { get; set; } = EvaluationFormStatus.DRAFT;
    public decimal TotalScore { get; set; } = 0m;
    public DateTimeOffset? SubmittedAt { get; set; }
    public string SnapshotVersion { get; set; } = "1.0";
    public string? OverallRecommendation { get; set; }
    public string? GeneralNotes { get; set; }

    // Navigation properties
    public Application Application { get; set; } = null!;
    public Interview Interview { get; set; } = null!;
    public User Evaluator { get; set; } = null!;
    public ICollection<EvaluationItem> Items { get; set; } = new List<EvaluationItem>();
}
