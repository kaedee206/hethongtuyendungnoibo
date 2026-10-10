using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.DepartmentBudgets;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

public class DepartmentBudgetService(
    ApplicationDbContext dbContext,
    ILogger<DepartmentBudgetService> logger) : IDepartmentBudgetService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<DepartmentBudgetService> _logger = logger;

    public async Task<DepartmentBudgetListViewModel> GetBudgetsAsync(int? year, CancellationToken cancellationToken = default)
    {
        var targetYear = year ?? DateTime.UtcNow.Year;

        // 1. Tải toàn bộ phòng ban chưa xóa
        var departments = await _dbContext.Departments
            .Include(d => d.Manager)
            .Where(d => !d.IsDeleted)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        // 2. Tải danh sách chỉ tiêu đã khai báo cho năm targetYear
        var budgets = await _dbContext.DepartmentHeadcountBudgets
            .Where(b => b.Year == targetYear && !b.IsDeleted)
            .ToListAsync(cancellationToken);

        var budgetMap = budgets.ToDictionary(b => b.DepartmentId);

        // 3. Đếm số nhân viên chính thức đang hoạt động theo phòng ban
        var staffCounts = await _dbContext.Users
            .Where(u => u.DepartmentId.HasValue && u.Status == "ACTIVE")
            .GroupBy(u => u.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count, cancellationToken);

        // 4. Đếm số chỉ tiêu đang tuyển (Requisitions đang PENDING_APPROVAL, APPROVED, IN_PROGRESS trong năm)
        var recruitingCounts = await _dbContext.JobRequisitions
            .Where(r => r.DepartmentId.HasValue && !r.IsDeleted &&
                        (r.Status == RequisitionStatus.PENDING_APPROVAL ||
                         r.Status == RequisitionStatus.APPROVED ||
                         r.Status == RequisitionStatus.IN_PROGRESS) &&
                        ((r.TargetHireDate.HasValue && r.TargetHireDate.Value.Year == targetYear) ||
                         (!r.TargetHireDate.HasValue && r.CreatedAt.Year == targetYear)))
            .GroupBy(r => r.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, SumQty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.SumQty, cancellationToken);

        // 5. Tổng hợp dữ liệu hiển thị theo từng phòng ban
        var resultList = new List<DepartmentBudgetViewModel>();

        foreach (var dept in departments)
        {
            budgetMap.TryGetValue(dept.Id, out var b);
            staffCounts.TryGetValue(dept.Id, out var staffCount);
            recruitingCounts.TryGetValue(dept.Id, out var recCount);

            resultList.Add(new DepartmentBudgetViewModel
            {
                Id = b?.Id,
                DepartmentId = dept.Id,
                DepartmentName = dept.Name,
                DepartmentCode = dept.Code,
                ManagerName = dept.Manager?.FullName,
                Year = targetYear,
                TargetHeadcount = b?.TargetHeadcount ?? 0,
                SalaryBudget = b?.SalaryBudget ?? 0,
                Currency = b?.Currency ?? "VND",
                Note = b?.Note,
                CurrentStaffCount = staffCount,
                RecruitingHeadcount = recCount
            });
        }

        // 6. Tính danh sách các năm khả dụng
        var dbYears = await _dbContext.DepartmentHeadcountBudgets
            .Where(b => !b.IsDeleted)
            .Select(b => b.Year)
            .Distinct()
            .ToListAsync(cancellationToken);

        var currentYear = DateTime.UtcNow.Year;
        var availableYears = dbYears
            .Union([currentYear - 1, currentYear, currentYear + 1])
            .OrderByDescending(y => y)
            .ToList();

        var viewModel = new DepartmentBudgetListViewModel
        {
            SelectedYear = targetYear,
            AvailableYears = availableYears,
            TotalTargetHeadcount = resultList.Sum(x => x.TargetHeadcount),
            TotalCurrentStaff = resultList.Sum(x => x.CurrentStaffCount),
            TotalRecruiting = resultList.Sum(x => x.RecruitingHeadcount),
            TotalSalaryBudget = resultList.Sum(x => x.SalaryBudget),
            DepartmentBudgets = resultList
        };

        return viewModel;
    }

    public async Task<DepartmentBudgetViewModel?> GetBudgetByDepartmentAndYearAsync(Guid departmentId, int year, CancellationToken cancellationToken = default)
    {
        var dept = await _dbContext.Departments
            .Include(d => d.Manager)
            .FirstOrDefaultAsync(d => d.Id == departmentId && !d.IsDeleted, cancellationToken);

        if (dept == null) return null;

        var budget = await _dbContext.DepartmentHeadcountBudgets
            .FirstOrDefaultAsync(b => b.DepartmentId == departmentId && b.Year == year && !b.IsDeleted, cancellationToken);

        var staffCount = await _dbContext.Users
            .CountAsync(u => u.DepartmentId == departmentId && u.Status == "ACTIVE", cancellationToken);

        var recCount = await _dbContext.JobRequisitions
            .Where(r => r.DepartmentId == departmentId && !r.IsDeleted &&
                        (r.Status == RequisitionStatus.PENDING_APPROVAL ||
                         r.Status == RequisitionStatus.APPROVED ||
                         r.Status == RequisitionStatus.IN_PROGRESS) &&
                        ((r.TargetHireDate.HasValue && r.TargetHireDate.Value.Year == year) ||
                         (!r.TargetHireDate.HasValue && r.CreatedAt.Year == year)))
            .SumAsync(r => r.Quantity, cancellationToken);

        return new DepartmentBudgetViewModel
        {
            Id = budget?.Id,
            DepartmentId = dept.Id,
            DepartmentName = dept.Name,
            DepartmentCode = dept.Code,
            ManagerName = dept.Manager?.FullName,
            Year = year,
            TargetHeadcount = budget?.TargetHeadcount ?? 0,
            SalaryBudget = budget?.SalaryBudget ?? 0,
            Currency = budget?.Currency ?? "VND",
            Note = budget?.Note,
            CurrentStaffCount = staffCount,
            RecruitingHeadcount = recCount
        };
    }

    public async Task<(bool Success, string Message, Guid? BudgetId)> SaveBudgetAsync(
        DepartmentBudgetViewModel model, 
        Guid currentUserId, 
        CancellationToken cancellationToken = default)
    {
        var dept = await _dbContext.Departments
            .FirstOrDefaultAsync(d => d.Id == model.DepartmentId && !d.IsDeleted, cancellationToken);

        if (dept == null)
        {
            return (false, "Phòng ban được chọn không tồn tại hoặc đã bị xóa.", null);
        }

        if (model.Year < 2020 || model.Year > 2100)
        {
            return (false, "Năm áp dụng không hợp lệ.", null);
        }

        if (model.TargetHeadcount < 0)
        {
            return (false, "Chỉ tiêu headcount không thể là số âm.", null);
        }

        if (model.SalaryBudget < 0)
        {
            return (false, "Ngân sách quỹ lương không thể là số âm.", null);
        }

        // Tìm bản ghi ngân sách hiện có theo phòng ban và năm
        var existing = await _dbContext.DepartmentHeadcountBudgets
            .FirstOrDefaultAsync(b => b.DepartmentId == model.DepartmentId && b.Year == model.Year && !b.IsDeleted, cancellationToken);

        if (existing != null)
        {
            existing.TargetHeadcount = model.TargetHeadcount;
            existing.SalaryBudget = model.SalaryBudget;
            existing.Currency = string.IsNullOrWhiteSpace(model.Currency) ? "VND" : model.Currency.Trim();
            existing.Note = model.Note?.Trim();
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedById = currentUserId;

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Người dùng {UserId} đã cập nhật chỉ tiêu Headcount phòng ban {DeptName} năm {Year}: {Headcount} người, quỹ lương {Budget:N0} VNĐ",
                currentUserId, dept.Name, model.Year, model.TargetHeadcount, model.SalaryBudget);

            return (true, $"Đã cập nhật chỉ tiêu Headcount & Quỹ lương năm {model.Year} cho phòng ban '{dept.Name}' thành công.", existing.Id);
        }
        else
        {
            var newBudget = new DepartmentHeadcountBudget
            {
                Id = Guid.NewGuid(),
                DepartmentId = model.DepartmentId,
                Year = model.Year,
                TargetHeadcount = model.TargetHeadcount,
                SalaryBudget = model.SalaryBudget,
                Currency = string.IsNullOrWhiteSpace(model.Currency) ? "VND" : model.Currency.Trim(),
                Note = model.Note?.Trim(),
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                CreatedById = currentUserId,
                UpdatedById = currentUserId
            };

            await _dbContext.DepartmentHeadcountBudgets.AddAsync(newBudget, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Người dùng {UserId} đã tạo mới chỉ tiêu Headcount phòng ban {DeptName} năm {Year}: {Headcount} người, quỹ lương {Budget:N0} VNĐ",
                currentUserId, dept.Name, model.Year, model.TargetHeadcount, model.SalaryBudget);

            return (true, $"Đã khai báo chỉ tiêu Headcount & Quỹ lương năm {model.Year} cho phòng ban '{dept.Name}' thành công.", newBudget.Id);
        }
    }

    public async Task<DepartmentHeadcountQuotaCheckResult> CheckHeadcountQuotaAsync(
        Guid departmentId, 
        int quantity, 
        Guid? excludeRequisitionId = null,
        int? year = null, 
        CancellationToken cancellationToken = default)
    {
        var targetYear = (year.HasValue && year.Value > 0) ? year.Value : DateTime.UtcNow.Year;

        var dept = await _dbContext.Departments
            .FirstOrDefaultAsync(d => d.Id == departmentId && !d.IsDeleted, cancellationToken);

        var deptName = dept?.Name ?? "Phòng ban";

        var budget = await _dbContext.DepartmentHeadcountBudgets
            .FirstOrDefaultAsync(b => b.DepartmentId == departmentId && b.Year == targetYear && !b.IsDeleted, cancellationToken);

        var staffCount = await _dbContext.Users
            .CountAsync(u => u.DepartmentId == departmentId && u.Status == "ACTIVE", cancellationToken);

        var query = _dbContext.JobRequisitions
            .Where(r => r.DepartmentId == departmentId && !r.IsDeleted &&
                        (r.Status == RequisitionStatus.PENDING_APPROVAL ||
                         r.Status == RequisitionStatus.APPROVED ||
                         r.Status == RequisitionStatus.IN_PROGRESS) &&
                        ((r.TargetHireDate.HasValue && r.TargetHireDate.Value.Year == targetYear) ||
                         (!r.TargetHireDate.HasValue && r.CreatedAt.Year == targetYear)));

        if (excludeRequisitionId.HasValue && excludeRequisitionId.Value != Guid.Empty)
        {
            query = query.Where(r => r.Id != excludeRequisitionId.Value);
        }

        var recruitingCount = await query.SumAsync(r => r.Quantity, cancellationToken);
        var usedCount = staffCount + recruitingCount;

        var hasBudgetPlan = budget != null && !budget.IsDeleted && budget.IsActive;
        var targetHeadcount = budget?.TargetHeadcount ?? 0;
        var remainingHeadcount = hasBudgetPlan ? (targetHeadcount - usedCount) : 0;

        // Vượt chỉ tiêu nếu phòng ban đã có kế hoạch định biên và tổng số (đã dùng + đề xuất mới) vượt chỉ tiêu đã duyệt
        var isOverQuota = hasBudgetPlan && (usedCount + quantity > targetHeadcount);

        string warningMessage;
        if (!hasBudgetPlan)
        {
            warningMessage = $"Phòng ban '{deptName}' chưa thiết lập chỉ tiêu headcount cho năm {targetYear}.";
        }
        else if (isOverQuota)
        {
            warningMessage = $"Số lượng đề xuất ({quantity} người) vượt quá chỉ tiêu headcount còn lại ({remainingHeadcount} người) của phòng ban '{deptName}' trong năm {targetYear} (Chỉ tiêu đã duyệt: {targetHeadcount}, Đã dùng & đang tuyển: {usedCount}). Vượt chỉ tiêu là cảnh báo chặn, cần Trưởng phòng Nhân sự xác nhận ghi đè kèm lý do.";
        }
        else
        {
            warningMessage = $"Phòng ban '{deptName}' còn {remainingHeadcount} chỉ tiêu headcount trong năm {targetYear} (Chỉ tiêu đã duyệt: {targetHeadcount}, Đã dùng: {usedCount}).";
        }

        return new DepartmentHeadcountQuotaCheckResult
        {
            DepartmentId = departmentId,
            DepartmentName = deptName,
            Year = targetYear,
            TargetHeadcount = targetHeadcount,
            CurrentStaffCount = staffCount,
            RecruitingHeadcount = recruitingCount,
            UsedHeadcount = usedCount,
            RemainingHeadcount = remainingHeadcount,
            SalaryBudget = budget?.SalaryBudget ?? 0,
            HasBudgetPlan = hasBudgetPlan,
            IsOverQuota = isOverQuota,
            WarningMessage = warningMessage
        };
    }
}
