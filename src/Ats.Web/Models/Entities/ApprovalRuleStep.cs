using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ats.Web.Models.Entities;

public class ApprovalRuleStep : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ApprovalRuleId { get; set; }

    public int StepOrder { get; set; }

    public Guid? ApproverRoleId { get; set; }

    public Guid? ApproverUserId { get; set; }

    // Navigation properties
    public ApprovalRule ApprovalRule { get; set; } = null!;
    
    public Role? ApproverRole { get; set; }
    
    public User? ApproverUser { get; set; }
}
