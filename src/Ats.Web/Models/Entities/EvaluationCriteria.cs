namespace Ats.Web.Models.Entities;

public class EvaluationCriteria : BaseEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Weight { get; set; } = 1.0m;
    public string CriteriaType { get; set; } = string.Empty; // e.g. HARD_SKILL, SOFT_SKILL, CULTURE
}
