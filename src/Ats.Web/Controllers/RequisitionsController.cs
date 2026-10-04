using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Models.ViewModels.Requisitions;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Quản lý phân hệ Yêu cầu Tuyển dụng & Luồng phê duyệt (EP-03_Requisition).
/// Tuân thủ Clean Controller, Primary Constructor, ValidateAntiForgeryToken và kebab-case routing.
/// </summary>
[Authorize(Roles = $"{UserRoles.HiringManager},{UserRoles.Admin},{UserRoles.HRManager},{UserRoles.Approver}")]
[Route("yeu-cau-tuyen-dung")]
[Route("requisitions")]
public class RequisitionsController(
    IRequisitionService requisitionService,
    ILogger<RequisitionsController> logger) : Controller
{
    private readonly IRequisitionService _requisitionService = requisitionService;
    private readonly ILogger<RequisitionsController> _logger = logger;

    private Guid GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// GET: /yeu-cau-tuyen-dung/tao-moi hoặc /requisitions/create
    /// Hiển thị giao diện Form khai báo thông tin cơ bản yêu cầu tuyển dụng.
    /// </summary>
    [HttpGet("tao-moi")]
    [HttpGet("create")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var model = await _requisitionService.PrepareCreateViewModelAsync(userId, cancellationToken);
        return View(model);
    }

    /// <summary>
    /// POST: /yeu-cau-tuyen-dung/tao-moi hoặc /requisitions/create
    /// Xử lý lưu yêu cầu tuyển dụng từ form submit truyền thống.
    /// </summary>
    [HttpPost("tao-moi")]
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        RequisitionCreateViewModel model,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();

        if (!ModelState.IsValid)
        {
            await _requisitionService.PopulateOptionsAsync(model, userId, cancellationToken);
            return View(model);
        }

        var (success, message, requisitionId) = await _requisitionService.CreateRequisitionAsync(model, userId, cancellationToken);

        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            await _requisitionService.PopulateOptionsAsync(model, userId, cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Create));
    }

    /// <summary>
    /// POST: /yeu-cau-tuyen-dung/api/quick-create
    /// Endpoint phục vụ lưu yêu cầu tuyển dụng từ Modal hoặc AJAX Request mà không reload trang.
    /// </summary>
    [HttpPost("api/quick-create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickCreate(
        [FromForm] RequisitionCreateViewModel model,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();

            return BadRequest(new
            {
                success = false,
                message = "Dữ liệu khai báo yêu cầu tuyển dụng không hợp lệ.",
                errors
            });
        }

        var (success, message, requisitionId) = await _requisitionService.CreateRequisitionAsync(model, userId, cancellationToken);

        if (!success)
        {
            return BadRequest(new
            {
                success = false,
                message
            });
        }

        return Ok(new
        {
            success = true,
            message,
            requisitionId,
            isDraft = model.IsDraft
        });
    }

    /// <summary>
    /// GET: /yeu-cau-tuyen-dung/api/form-options
    /// Cung cấp dữ liệu khởi tạo dropdown chức danh và phòng ban cho Modal phía client.
    /// </summary>
    [HttpGet("api/form-options")]
    public async Task<IActionResult> GetFormOptionsApi(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var model = await _requisitionService.PrepareCreateViewModelAsync(userId, cancellationToken);

        return Ok(new
        {
            success = true,
            data = new
            {
                positions = model.JobPositionOptions,
                departments = model.DepartmentOptions,
                defaultDepartmentId = model.DepartmentId,
                canChangeDepartment = model.CanChangeDepartment,
                currentUserDepartmentName = model.CurrentUserDepartmentName
            }
        });
    }
}
