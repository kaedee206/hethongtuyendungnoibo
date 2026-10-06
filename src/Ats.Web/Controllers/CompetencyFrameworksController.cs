using Ats.Web.Constants;
using Ats.Web.Models.ViewModels.CompetencyFrameworks;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
[Route("competency-frameworks")]
public class CompetencyFrameworksController(
    ICompetencyFrameworkService frameworkService,
    ILogger<CompetencyFrameworksController> logger) : Controller
{
    private readonly ICompetencyFrameworkService _frameworkService = frameworkService;
    private readonly ILogger<CompetencyFrameworksController> _logger = logger;

    private bool CanManageFrameworks =>
        User?.Identity?.IsAuthenticated == true && (
            User.IsInRole(UserRoles.Admin) ||
            User.IsInRole(UserRoles.HRManager) ||
            User.IsInRole("Quản trị viên") ||
            User.IsInRole("Trưởng phòng nhân sự") ||
            User.IsInRole("Trưởng phòng Nhân sự"));

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? keyword,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15,
        CancellationToken cancellationToken = default)
    {
        ViewBag.CanManage = CanManageFrameworks;
        var model = await _frameworkService.GetFrameworksAsync(keyword, status, page, pageSize, cancellationToken);
        return View(model);
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        if (!CanManageFrameworks) return Forbid();

        var model = await _frameworkService.PrepareFormAsync(null, cancellationToken);
        return View(model);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromForm] CompetencyFrameworkFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!CanManageFrameworks) return Forbid();

        if (!ModelState.IsValid)
        {
            var formModel = await _frameworkService.PrepareFormAsync(null, cancellationToken);
            formModel.Code = model.Code;
            formModel.Name = model.Name;
            formModel.Description = model.Description;
            formModel.IsActive = model.IsActive;
            formModel.AssignedPositionIds = model.AssignedPositionIds;
            formModel.Criteria = model.Criteria;
            return View(formModel);
        }

        var (success, message, newId) = await _frameworkService.CreateAsync(model, cancellationToken);
        if (!success)
        {
            TempData["ErrorMessage"] = message;
            var formModel = await _frameworkService.PrepareFormAsync(null, cancellationToken);
            formModel.Code = model.Code;
            formModel.Name = model.Name;
            formModel.Description = model.Description;
            formModel.IsActive = model.IsActive;
            formModel.AssignedPositionIds = model.AssignedPositionIds;
            formModel.Criteria = model.Criteria;
            return View(formModel);
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Details), new { id = newId });
    }

    [HttpGet("edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken = default)
    {
        if (!CanManageFrameworks) return Forbid();

        var model = await _frameworkService.PrepareFormAsync(id, cancellationToken);
        if (!model.Id.HasValue)
        {
            TempData["ErrorMessage"] = "Không tìm thấy khung năng lực cần chỉnh sửa.";
            return RedirectToAction(nameof(Index));
        }

        return View(model);
    }

    [HttpPost("edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        [FromForm] CompetencyFrameworkFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!CanManageFrameworks) return Forbid();

        model.Id = id;
        if (!ModelState.IsValid)
        {
            var formModel = await _frameworkService.PrepareFormAsync(id, cancellationToken);
            formModel.Code = model.Code;
            formModel.Name = model.Name;
            formModel.Description = model.Description;
            formModel.IsActive = model.IsActive;
            formModel.AssignedPositionIds = model.AssignedPositionIds;
            formModel.Criteria = model.Criteria;
            return View(formModel);
        }

        var (success, message) = await _frameworkService.UpdateAsync(model, cancellationToken);
        if (!success)
        {
            TempData["ErrorMessage"] = message;
            var formModel = await _frameworkService.PrepareFormAsync(id, cancellationToken);
            formModel.Code = model.Code;
            formModel.Name = model.Name;
            formModel.Description = model.Description;
            formModel.IsActive = model.IsActive;
            formModel.AssignedPositionIds = model.AssignedPositionIds;
            formModel.Criteria = model.Criteria;
            return View(formModel);
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("details/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken = default)
    {
        var isAjax = Request.Headers.Accept.Any(h => h?.Contains("application/json") == true);

        if (isAjax)
        {
            var item = await _frameworkService.GetFrameworkByIdAsync(id, cancellationToken);
            if (item == null) return NotFound(new { message = "Không tìm thấy khung năng lực yêu cầu." });
            return Json(item);
        }

        var detail = await _frameworkService.GetDetailAsync(id, cancellationToken);
        if (detail == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy khung năng lực yêu cầu.";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.CanManage = CanManageFrameworks;
        return View(detail);
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        if (!CanManageFrameworks) return Forbid();

        var (success, message) = await _frameworkService.DeleteAsync(id, cancellationToken);
        if (success)
        {
            TempData["SuccessMessage"] = message;
        }
        else
        {
            TempData["ErrorMessage"] = message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("toggle/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(Guid id, CancellationToken cancellationToken = default)
    {
        if (!CanManageFrameworks) return Forbid();

        var (success, message) = await _frameworkService.ToggleActiveAsync(id, cancellationToken);
        if (success)
        {
            TempData["SuccessMessage"] = message;
        }
        else
        {
            TempData["ErrorMessage"] = message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}/positions")]
    public async Task<IActionResult> GetPositions(Guid id, CancellationToken cancellationToken = default)
    {
        var positions = await _frameworkService.GetAssociatedPositionsAsync(id, cancellationToken);
        return Json(positions);
    }

    /// <summary>
    /// SCRUM-225: Nhân bản (clone) khung năng lực sang một bản sao mới độc lập.
    /// </summary>
    [HttpPost("clone/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clone(Guid id, CancellationToken cancellationToken = default)
    {
        if (!CanManageFrameworks) return Forbid();

        var (success, message, newId) = await _frameworkService.CloneAsync(id, cancellationToken);
        if (success && newId.HasValue)
        {
            TempData["SuccessMessage"] = message;
            return RedirectToAction(nameof(Edit), new { id = newId.Value });
        }

        TempData["ErrorMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// SCRUM-226: API truy vấn khung năng lực, tiêu chí, trọng số & rubric theo chức danh phục vụ phiếu phỏng vấn.
    /// </summary>
    [HttpGet("api/by-position/{jobPositionId:guid}")]
    public async Task<IActionResult> GetByPosition(Guid jobPositionId, CancellationToken cancellationToken = default)
    {
        var framework = await _frameworkService.GetByPositionAsync(jobPositionId, cancellationToken);
        if (framework == null)
        {
            return NotFound(new { success = false, message = "Chức danh này chưa được liên kết với khung năng lực nào hoặc không tồn tại." });
        }

        return Ok(new { success = true, data = framework });
    }
}
