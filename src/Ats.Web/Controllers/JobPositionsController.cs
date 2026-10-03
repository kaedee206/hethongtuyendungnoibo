using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.ViewModels.JobPositions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Ats.Web.Controllers;

[Authorize]
[Route("job-positions")]
public class JobPositionsController(
    ApplicationDbContext dbContext,
    ILogger<JobPositionsController> logger) : Controller
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<JobPositionsController> _logger = logger;

    public static bool CanUserViewSalary(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (user.IsInRole(UserRoles.HRManager) ||
            user.IsInRole("Trưởng phòng nhân sự") ||
            user.IsInRole("Trưởng phòng Nhân sự") ||
            user.IsInRole("HRManager"))
        {
            return true;
        }

        var roles = user.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        return Permissions.HasPermission(roles, Permissions.SalaryView);
    }

    public static bool CanUserManage(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (user.IsInRole(UserRoles.Admin) ||
            user.IsInRole(UserRoles.HRManager) ||
            user.IsInRole("Trưởng phòng nhân sự") ||
            user.IsInRole("Trưởng phòng Nhân sự"))
        {
            return true;
        }

        var roles = user.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        var normalizedRoles = roles.Select(UserRoles.NormalizeRole).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return normalizedRoles.Contains(UserRoles.Admin) || normalizedRoles.Contains(UserRoles.HRManager);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? keyword,
        [FromQuery] string? level,
        [FromQuery] Guid? departmentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0 || pageSize > 100)
        {
            pageSize = 10;
        }

        var canViewSalary = CanUserViewSalary(User);
        var canManage = CanUserManage(User);

        var query = _dbContext.JobPositions
            .Include(p => p.Department)
            .AsNoTracking()
            .Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(p => p.Code.ToLower().Contains(kw) || p.Title.ToLower().Contains(kw));
        }

        if (!string.IsNullOrWhiteSpace(level))
        {
            var levelNormalized = level.Trim().ToUpperInvariant();
            query = query.Where(p => p.JobLevel.ToUpper() == levelNormalized);
        }

        if (departmentId.HasValue && departmentId.Value != Guid.Empty)
        {
            query = query.Where(p => p.DepartmentId == departmentId.Value);
        }

        var totalRecords = await query.CountAsync(cancellationToken);

        var positions = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new JobPositionItemViewModel
            {
                Id = p.Id,
                Code = p.Code,
                Title = p.Title,
                DepartmentName = p.Department.Name,
                JobLevel = p.JobLevel,
                MinSalary = canViewSalary ? p.MinSalary : null,
                MaxSalary = canViewSalary ? p.MaxSalary : null,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(cancellationToken);

        ViewBag.Departments = await _dbContext.Departments
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new SelectListItem { Value = d.Id.ToString(), Text = d.Name, Selected = d.Id == departmentId })
            .ToListAsync(cancellationToken);

        ViewBag.JobLevels = JobPositionFormViewModel.GetDefaultJobLevels(level);

        var model = new JobPositionListViewModel
        {
            Positions = positions,
            Keyword = keyword,
            LevelFilter = level,
            DepartmentFilter = departmentId,
            TotalRecords = totalRecords,
            CurrentPage = page,
            PageSize = pageSize,
            CanViewSalary = canViewSalary,
            CanManage = canManage
        };

        return View(model);
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        if (!CanUserManage(User))
        {
            return Forbid();
        }

        var model = new JobPositionFormViewModel
        {
            IsActive = true
        };

        await PopulateSelectListsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(JobPositionFormViewModel model, CancellationToken cancellationToken = default)
    {
        if (!CanUserManage(User))
        {
            return Forbid();
        }
        if (!string.IsNullOrWhiteSpace(model.Code))
        {
            var codeUpper = model.Code.Trim().ToUpperInvariant();
            model.Code = codeUpper;
            var isDuplicate = await _dbContext.JobPositions
                .AnyAsync(p => p.Code.ToUpper() == codeUpper && !p.IsDeleted, cancellationToken);

            if (isDuplicate)
            {
                ModelState.AddModelError(nameof(model.Code), $"Mã chức danh '{codeUpper}' đã tồn tại trong hệ thống. Vui lòng chọn mã khác.");
            }
        }

        if (model.DepartmentId != Guid.Empty)
        {
            var deptExists = await _dbContext.Departments.AnyAsync(d => d.Id == model.DepartmentId && d.IsActive, cancellationToken);
            if (!deptExists)
            {
                ModelState.AddModelError(nameof(model.DepartmentId), "Phòng ban được chọn không tồn tại hoặc đã bị vô hiệu hóa.");
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateSelectListsAsync(model, cancellationToken);
            return View(model);
        }

        try
        {
            var newPosition = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = model.Code.Trim().ToUpperInvariant(),
                Title = model.Title.Trim(),
                DepartmentId = model.DepartmentId,
                JobLevel = model.JobLevel.Trim().ToUpperInvariant(),
                Description = model.Description?.Trim(),
                IsActive = model.IsActive,
                MinSalary = model.MinSalary,
                MaxSalary = model.MaxSalary,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _dbContext.JobPositions.AddAsync(newPosition, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Đã tạo mới chức danh thành công: {Code} - {Title}", newPosition.Code, newPosition.Title);
            TempData["SuccessMessage"] = $"Đã khởi tạo thành công chức danh [{newPosition.Code}] {newPosition.Title}.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi lưu chức danh mới.");
            ModelState.AddModelError(string.Empty, "Có lỗi xảy ra trong quá trình lưu dữ liệu. Vui lòng thử lại.");
            await PopulateSelectListsAsync(model, cancellationToken);
            return View(model);
        }
    }

    [HttpGet("edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken = default)
    {
        if (!CanUserManage(User))
        {
            return Forbid();
        }

        var position = await _dbContext.JobPositions
            .Include(p => p.Department)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);

        if (position == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy chức danh yêu cầu hoặc chức danh đã bị xóa.";
            return RedirectToAction(nameof(Index));
        }

        var model = new JobPositionFormViewModel
        {
            Id = position.Id,
            Code = position.Code,
            Title = position.Title,
            DepartmentId = position.DepartmentId,
            JobLevel = position.JobLevel,
            MinSalary = position.MinSalary,
            MaxSalary = position.MaxSalary,
            Description = position.Description,
            IsActive = position.IsActive
        };

        await PopulateSelectListsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost("edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, JobPositionFormViewModel model, CancellationToken cancellationToken = default)
    {
        if (!CanUserManage(User))
        {
            return Forbid();
        }

        if (id != model.Id)
        {
            return BadRequest("Dữ liệu định danh chức danh không hợp lệ.");
        }

        var position = await _dbContext.JobPositions
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);

        if (position == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy chức danh để cập nhật.";
            return RedirectToAction(nameof(Index));
        }

        if (!string.IsNullOrWhiteSpace(model.Code))
        {
            var codeUpper = model.Code.Trim().ToUpperInvariant();
            model.Code = codeUpper;
            var isDuplicate = await _dbContext.JobPositions
                .AnyAsync(p => p.Code.ToUpper() == codeUpper && p.Id != id && !p.IsDeleted, cancellationToken);

            if (isDuplicate)
            {
                ModelState.AddModelError(nameof(model.Code), $"Mã chức danh '{codeUpper}' đã được sử dụng bởi một vị trí khác.");
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateSelectListsAsync(model, cancellationToken);
            return View(model);
        }

        try
        {
            position.Code = model.Code.Trim().ToUpperInvariant();
            position.Title = model.Title.Trim();
            position.DepartmentId = model.DepartmentId;
            position.JobLevel = model.JobLevel.Trim().ToUpperInvariant();
            position.Description = model.Description?.Trim();
            position.IsActive = model.IsActive;
            position.MinSalary = model.MinSalary;
            position.MaxSalary = model.MaxSalary;
            position.UpdatedAt = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Đã cập nhật chức danh thành công: {Code} - {Title}", position.Code, position.Title);
            TempData["SuccessMessage"] = $"Đã cập nhật thành công chức danh [{position.Code}] {position.Title}.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi cập nhật chức danh id {Id}.", id);
            ModelState.AddModelError(string.Empty, "Không thể lưu cập nhật. Vui lòng kiểm tra lại kết nối và thử lại.");
            await PopulateSelectListsAsync(model, cancellationToken);
            return View(model);
        }
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        if (!CanUserManage(User))
        {
            return Forbid();
        }

        var position = await _dbContext.JobPositions
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);

        if (position == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy chức danh yêu cầu.";
            return RedirectToAction(nameof(Index));
        }

        position.IsDeleted = true;
        position.DeletedAt = DateTimeOffset.UtcNow;
        position.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = $"Đã xóa chức danh [{position.Code}] {position.Title}.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateSelectListsAsync(JobPositionFormViewModel model, CancellationToken cancellationToken)
    {
        model.AvailableDepartments = await _dbContext.Departments
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new SelectListItem
            {
                Value = d.Id.ToString(),
                Text = d.Name,
                Selected = d.Id == model.DepartmentId
            })
            .ToListAsync(cancellationToken);

        model.AvailableJobLevels = JobPositionFormViewModel.GetDefaultJobLevels(model.JobLevel);
    }
}
