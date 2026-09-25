using System.ComponentModel.DataAnnotations.Schema;

namespace Ats.Web.Models.Entities;

public class JobPosition : BaseEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public string JobLevel { get; set; } = string.Empty; // e.g. JUNIOR, SENIOR, LEAD
    public string? Description { get; set; }
    
    [Column(TypeName = "jsonb")]
    public string? StandardCompetencies { get; set; }
    public bool IsActive { get; set; } = true;
    
    public Department Department { get; set; } = null!;
}
