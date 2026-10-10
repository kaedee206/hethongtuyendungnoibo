namespace Ats.Web.Models.Entities;

public class DepartmentHeadcountBudget : BaseEntity
{
    public Guid Id { get; set; }
    public Guid DepartmentId { get; set; }
    public int Year { get; set; }
    public int TargetHeadcount { get; set; }
    public decimal SalaryBudget { get; set; }
    public string Currency { get; set; } = "VND";
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;

    public Department Department { get; set; } = null!;
}
