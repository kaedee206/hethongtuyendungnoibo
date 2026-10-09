using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ats.Web.Models.Entities;

public class ApprovalRule : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Name { get; set; } = null!;

    public Guid? DepartmentId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MinSalary { get; set; } = 0;

    [Column(TypeName = "decimal(18,2)")]
    public decimal? MaxSalary { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Department? Department { get; set; }
    
    public ICollection<ApprovalRuleStep> Steps { get; set; } = new List<ApprovalRuleStep>();
}
