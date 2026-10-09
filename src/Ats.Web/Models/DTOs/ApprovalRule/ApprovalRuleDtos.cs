using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.DTOs.ApprovalRule;

public class CreateApprovalRuleDto
{
    [Required(ErrorMessage = "Tên quy trình duyệt là bắt buộc")]
    public string Name { get; set; } = null!;

    public Guid? DepartmentId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Mức lương tối thiểu không hợp lệ")]
    public decimal MinSalary { get; set; }

    public decimal? MaxSalary { get; set; }

    public bool IsActive { get; set; } = true;

    [Required]
    [MinLength(1, ErrorMessage = "Cần ít nhất 1 cấp duyệt")]
    public List<CreateApprovalRuleStepDto> Steps { get; set; } = new();
}

public class CreateApprovalRuleStepDto
{
    [Required]
    public int StepOrder { get; set; }

    public Guid? ApproverRoleId { get; set; }
    public Guid? ApproverUserId { get; set; }
}

public class UpdateApprovalRuleDto : CreateApprovalRuleDto
{
}

public class ApprovalRuleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public decimal MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
    public bool IsActive { get; set; }
    public List<ApprovalRuleStepDto> Steps { get; set; } = new();
}

public class ApprovalRuleStepDto
{
    public Guid Id { get; set; }
    public int StepOrder { get; set; }
    public Guid? ApproverRoleId { get; set; }
    public string? ApproverRoleName { get; set; }
    public Guid? ApproverUserId { get; set; }
    public string? ApproverUserName { get; set; }
}
