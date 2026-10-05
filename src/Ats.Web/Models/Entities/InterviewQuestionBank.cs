namespace Ats.Web.Models.Entities;

public class InterviewQuestionBank : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Content { get; set; } = string.Empty;
    public string Competency { get; set; } = string.Empty; // Category or Framework Name
    public Guid? CompetencyFrameworkId { get; set; }
    public CompetencyFramework? CompetencyFramework { get; set; }
    public Guid? CompetencyCriterionId { get; set; }
    public CompetencyCriterion? CompetencyCriterion { get; set; }

    public string Difficulty { get; set; } = "Cơ bản"; // Cơ bản, Trung bình, Nâng cao
    public string? SuggestedAnswer { get; set; }
    public bool IsActive { get; set; } = true;
}
