namespace Ats.Web.Models.Entities;

public class EvaluationScore : BaseEntity
{
    public Guid Id { get; set; }
    public Guid EvaluationId { get; set; }
    public Guid CriteriaId { get; set; }
    public int Score { get; set; } // 1-5
    public string? Comment { get; set; }

    public InterviewEvaluation Evaluation { get; set; } = null!;
    public EvaluationCriteria Criteria { get; set; } = null!;
}
