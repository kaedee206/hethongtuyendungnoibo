using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.DepartmentBudgets;

public class DepartmentBudgetViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn phòng ban.")]
    public Guid DepartmentId { get; set; }

    public string DepartmentName { get; set; } = string.Empty;
    public string DepartmentCode { get; set; } = string.Empty;
    public string? ManagerName { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn năm áp dụng.")]
    [Range(2020, 2100, ErrorMessage = "Năm áp dụng không hợp lệ (2020 - 2100).")]
    public int Year { get; set; } = DateTime.UtcNow.Year;

    [Required(ErrorMessage = "Vui lòng nhập chỉ tiêu headcount.")]
    [Range(0, 10000, ErrorMessage = "Chỉ tiêu headcount phải là số nguyên dương từ 0 đến 10.000 người.")]
    public int TargetHeadcount { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập ngân sách quỹ lương.")]
    [Range(0, 10000000000000, ErrorMessage = "Ngân sách quỹ lương phải từ 0 đến 10.000 tỷ VNĐ.")]
    public decimal SalaryBudget { get; set; }

    public string Currency { get; set; } = "VND";

    [MaxLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự.")]
    public string? Note { get; set; }

    public bool IsActive { get; set; } = true;
    public bool HasBudgetPlan => Id.HasValue && TargetHeadcount > 0;

    // Chỉ số tính toán thực tế
    public int CurrentStaffCount { get; set; }
    public int RecruitingHeadcount { get; set; }
    public int InFlightRequisitionQuantity => RecruitingHeadcount;
    public int UsedHeadcount => CurrentStaffCount + RecruitingHeadcount;
    public int RemainingHeadcount => TargetHeadcount - UsedHeadcount;
    public double UsagePercentage => TargetHeadcount > 0 
        ? Math.Min(100.0, Math.Round((double)UsedHeadcount / TargetHeadcount * 100.0, 1)) 
        : 0.0;
}

public class DepartmentBudgetListViewModel
{
    public int SelectedYear { get; set; } = DateTime.UtcNow.Year;
    public List<int> AvailableYears { get; set; } = [];

    public int TotalTargetHeadcount { get; set; }
    public int TotalCurrentStaff { get; set; }
    public int TotalRecruiting { get; set; }
    public int TotalUsedHeadcount => TotalCurrentStaff + TotalRecruiting;
    public int TotalRemainingHeadcount => TotalTargetHeadcount - TotalUsedHeadcount;
    public decimal TotalSalaryBudget { get; set; }
    public double OverallUsagePercentage => TotalTargetHeadcount > 0 
        ? Math.Min(100.0, Math.Round((double)TotalUsedHeadcount / TotalTargetHeadcount * 100.0, 1)) 
        : 0.0;

    public List<DepartmentBudgetViewModel> DepartmentBudgets { get; set; } = [];
    public List<DepartmentBudgetViewModel> Items => DepartmentBudgets;
    public bool CanManageBudget { get; set; } = true;
}

public class DepartmentHeadcountQuotaCheckResult
{
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int TargetHeadcount { get; set; }
    public int CurrentStaffCount { get; set; }
    public int RecruitingHeadcount { get; set; }
    public int UsedHeadcount { get; set; }
    public int RemainingHeadcount { get; set; }
    public decimal SalaryBudget { get; set; }
    public string Currency { get; set; } = "VND";
    public bool HasBudgetPlan { get; set; }
    public bool HasPlan => HasBudgetPlan;
    public bool IsOverQuota { get; set; }
    public string? WarningMessage { get; set; }
    public string Message => WarningMessage ?? string.Empty;
}
