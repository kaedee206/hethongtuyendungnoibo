using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Models.ViewModels.DepartmentBudgets;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Quản lý chỉ tiêu headcount và ngân sách lương theo phòng ban theo năm (Scrum 24).
/// Dành cho Trưởng phòng Nhân sự (HR Manager) và Quản trị viên (Admin).
/// </summary>
[Authorize(Roles = $"{UserRoles.Admin},{UserRoles.HRManager}")]
[Route("ngan-sach-headcount")]
[Route("department-budgets")]
public class DepartmentBudgetsController(
    IDepartmentBudgetService budgetService,
    ILogger<DepartmentBudgetsController> logger) : Controller
{
    private readonly IDepartmentBudgetService _budgetService = budgetService;
    private readonly ILogger<DepartmentBudgetsController> _logger = logger;

    private Guid GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// GET: /ngan-sach-headcount?year=2026
    /// Hiển thị danh sách chỉ tiêu định biên, ngân sách lương và tiến độ sử dụng theo phòng ban.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] int? year, CancellationToken cancellationToken = default)
    {
        var selectedYear = year.GetValueOrDefault(DateTime.UtcNow.Year);
        var viewModel = await _budgetService.GetBudgetsAsync(selectedYear, cancellationToken);
        return View(viewModel);
    }

    /// <summary>
    /// GET: /ngan-sach-headcount/api/{departmentId}/{year}
    /// Lấy chi tiết định biên của một phòng ban theo năm.
    /// </summary>
    [HttpGet("api/{departmentId:guid}/{year:int}")]
    public async Task<IActionResult> GetDetail(
        Guid departmentId,
        int year,
        CancellationToken cancellationToken = default)
    {
        var budget = await _budgetService.GetBudgetByDepartmentAndYearAsync(departmentId, year, cancellationToken);
        if (budget == null)
        {
            return NotFound(new { success = false, message = "Chưa có dữ liệu định biên cho phòng ban này." });
        }

        return Ok(new { success = true, data = budget });
    }

    /// <summary>
    /// POST: /ngan-sach-headcount/save
    /// Tạo mới hoặc cập nhật chỉ tiêu headcount và ngân sách lương của phòng ban.
    /// </summary>
    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        DepartmentBudgetViewModel model,
        CancellationToken cancellationToken = default)
    {
        var isAjax = Request.Headers.Accept.Any(h => h?.Contains("application/json") == true) ||
                     Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (model.DepartmentId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(model.DepartmentId), "Vui lòng chọn phòng ban.");
        }

        if (model.Year < 2000 || model.Year > 2100)
        {
            ModelState.AddModelError(nameof(model.Year), "Năm tài chính không hợp lệ.");
        }

        if (model.TargetHeadcount < 0)
        {
            ModelState.AddModelError(nameof(model.TargetHeadcount), "Chỉ tiêu headcount không được âm.");
        }

        if (model.SalaryBudget < 0)
        {
            ModelState.AddModelError(nameof(model.SalaryBudget), "Ngân sách lương không được âm.");
        }

        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu không hợp lệ.";
            if (isAjax)
            {
                return BadRequest(new { success = false, message = firstError });
            }

            TempData["ErrorMessage"] = firstError;
            return RedirectToAction(nameof(Index), new { year = model.Year });
        }

        var userId = GetCurrentUserId();
        var (success, message, savedId) = await _budgetService.SaveBudgetAsync(model, userId, cancellationToken);

        if (!success)
        {
            if (isAjax)
            {
                return BadRequest(new { success = false, message });
            }

            TempData["ErrorMessage"] = message;
            return RedirectToAction(nameof(Index), new { year = model.Year });
        }

        _logger.LogInformation("Người dùng {UserId} đã lưu thành công ngân sách headcount phòng ban {DeptId} năm {Year}",
            userId, model.DepartmentId, model.Year);

        if (isAjax)
        {
            return Ok(new { success = true, message, id = savedId });
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Index), new { year = model.Year });
    }
}
