using Ats.Web.Models.ViewModels.DepartmentBudgets;

namespace Ats.Web.Services.Interfaces;

public interface IDepartmentBudgetService
{
    Task<DepartmentBudgetListViewModel> GetBudgetsAsync(int? year, CancellationToken cancellationToken = default);

    Task<DepartmentBudgetViewModel?> GetBudgetByDepartmentAndYearAsync(Guid departmentId, int year, CancellationToken cancellationToken = default);

    Task<(bool Success, string Message, Guid? BudgetId)> SaveBudgetAsync(DepartmentBudgetViewModel model, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<DepartmentHeadcountQuotaCheckResult> CheckHeadcountQuotaAsync(Guid departmentId, int quantity, Guid? excludeRequisitionId = null, int? year = null, CancellationToken cancellationToken = default);
}
