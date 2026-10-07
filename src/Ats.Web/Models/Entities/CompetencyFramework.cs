namespace Ats.Web.Models.Entities;

public class CompetencyFramework : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<CompetencyCriterion> Criteria { get; set; } = new List<CompetencyCriterion>();
    public ICollection<JobPosition> AssignedPositions { get; set; } = new List<JobPosition>();
}

public class CompetencyCriterion : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompetencyFrameworkId { get; set; }
    public CompetencyFramework? CompetencyFramework { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Weight { get; set; } = 0; // % weight (sum of all criteria in framework = 100%)
    public string? Rubric1 { get; set; }
    public string? Rubric2 { get; set; }
    public string? Rubric3 { get; set; }
    public string? Rubric4 { get; set; }
    public string? Rubric5 { get; set; }
}
