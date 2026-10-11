using System.ComponentModel.DataAnnotations.Schema;

namespace Ats.Web.Models.Entities;

public class JobPosition : BaseEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public string JobLevel { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    [Column(TypeName = "jsonb")]
    public string? StandardCompetencies { get; set; }
    public bool IsActive { get; set; } = true;
    
    // FK đến Khung năng lực (SCRUM-222): nullable — một chức danh có thể chưa gán khung
    public Guid? CompetencyFrameworkId { get; set; }
    public CompetencyFramework? CompetencyFramework { get; set; }

    public Department Department { get; set; } = null!;

    [Column("min_salary")]
    public decimal? MinSalary { get; set; }

    [Column("max_salary")]
    public decimal? MaxSalary { get; set; }
}
