namespace Ats.Web.Models.Entities;

public class EvaluationItem : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EvaluationFormId { get; set; }
    public Guid? CriteriaId { get; set; }
    public string CriteriaName { get; set; } = string.Empty;
    public string? CriteriaDescription { get; set; }
    public decimal Weight { get; set; } = 0m;
    public int Score { get; set; }
    public string? Comment { get; set; }

    // Navigation properties
    public EvaluationForm EvaluationForm { get; set; } = null!;
}
