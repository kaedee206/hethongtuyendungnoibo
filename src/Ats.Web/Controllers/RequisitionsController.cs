using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Models.ViewModels.Requisitions;
using Ats.Web.Services;
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
public class RequisitionsController : Controller
{
    private readonly IRequisitionService _requisitionService;
    private readonly IDepartmentBudgetService _departmentBudgetService;
    private readonly ILogger<RequisitionsController> _logger;

    [ActivatorUtilitiesConstructor]
    public RequisitionsController(
        IRequisitionService requisitionService,
        IDepartmentBudgetService departmentBudgetService,
        ILogger<RequisitionsController> logger)
    {
        _requisitionService = requisitionService;
        _departmentBudgetService = departmentBudgetService;
        _logger = logger;
    }

    public RequisitionsController(
        IRequisitionService requisitionService,
        ILogger<RequisitionsController> logger)
        : this(
            requisitionService,
            new DepartmentBudgetService(null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<DepartmentBudgetService>.Instance),
            logger)
    {
    }

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

        var (success, message, requisitionId, code) = await _requisitionService.SaveOrUpdateRequisitionAsync(model, userId, cancellationToken);

        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            await _requisitionService.PopulateOptionsAsync(model, userId, cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = message;

        if (model.IsDraft && requisitionId.HasValue)
        {
            return RedirectToAction(nameof(Edit), new { id = requisitionId.Value });
        }

        return RedirectToAction(nameof(Create));
    }

    /// <summary>
    /// GET: /yeu-cau-tuyen-dung/ban-nhap hoặc /requisitions/drafts
    /// Hiển thị danh sách "Yêu cầu tuyển dụng nháp" của Trưởng bộ phận để xem lại và tiếp tục chỉnh sửa.
    /// </summary>
    [HttpGet("ban-nhap")]
    [HttpGet("drafts")]
    public async Task<IActionResult> Drafts(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var drafts = await _requisitionService.GetDraftsByManagerAsync(userId, cancellationToken);
        return View(drafts);
    }

    /// <summary>
    /// GET: /yeu-cau-tuyen-dung/chinh-sua/{id} hoặc /requisitions/edit/{id}
    /// Mở lại bản nháp yêu cầu tuyển dụng để tiếp tục chỉnh sửa hoặc hoàn thiện gửi duyệt.
    /// </summary>
    [HttpGet("chinh-sua/{id:guid}")]
    [HttpGet("edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var model = await _requisitionService.GetDraftByIdAsync(id, userId, cancellationToken);

        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy bản nháp yêu cầu tuyển dụng hoặc bạn không có quyền truy cập.";
            return RedirectToAction(nameof(Drafts));
        }

        return View(model);
    }

    /// <summary>
    /// POST: /yeu-cau-tuyen-dung/chinh-sua/{id} hoặc /requisitions/edit/{id}
    /// Cập nhật bản nháp hoặc hoàn tất gửi duyệt từ trang chỉnh sửa.
    /// </summary>
    [HttpPost("chinh-sua/{id:guid}")]
    [HttpPost("edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        RequisitionCreateViewModel model,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        model.Id = id;

        if (!ModelState.IsValid)
        {
            await _requisitionService.PopulateOptionsAsync(model, userId, cancellationToken);
            return View(model);
        }

        var (success, message, requisitionId, code) = await _requisitionService.SaveOrUpdateRequisitionAsync(model, userId, cancellationToken);

        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            await _requisitionService.PopulateOptionsAsync(model, userId, cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = message;

        if (model.IsDraft)
        {
            return RedirectToAction(nameof(Edit), new { id });
        }

        return RedirectToAction(nameof(Drafts));
    }

    /// <summary>
    /// POST: /yeu-cau-tuyen-dung/xoa-nhap/{id} hoặc /requisitions/delete-draft/{id}
    /// Xóa một bản nháp yêu cầu tuyển dụng không còn nhu cầu sử dụng.
    /// </summary>
    [HttpPost("xoa-nhap/{id:guid}")]
    [HttpPost("delete-draft/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDraft(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var (success, message) = await _requisitionService.DeleteDraftAsync(id, userId, cancellationToken);

        if (success)
        {
            TempData["SuccessMessage"] = message;
        }
        else
        {
            TempData["ErrorMessage"] = message;
        }

        return RedirectToAction(nameof(Drafts));
    }

    /// <summary>
    /// POST: /yeu-cau-tuyen-dung/api/auto-save
    /// Endpoint AJAX chuyên trách Auto-Save định kỳ 30s ngầm từ Client.
    /// </summary>
    [HttpPost("api/auto-save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AutoSaveDraft(
        [FromForm] RequisitionCreateViewModel model,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        model.IsDraft = true;

        var (success, message, requisitionId, code) = await _requisitionService.SaveOrUpdateRequisitionAsync(model, userId, cancellationToken);

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
            code,
            savedAt = DateTime.Now.ToString("HH:mm:ss")
        });
    }

    /// <summary>
    /// GET: /yeu-cau-tuyen-dung/api/drafts/count
    /// Lấy tổng số lượng bản nháp hiện tại của người dùng.
    /// </summary>
    [HttpGet("api/drafts/count")]
    public async Task<IActionResult> GetDraftCountApi(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var count = await _requisitionService.GetDraftCountAsync(userId, cancellationToken);

        return Ok(new
        {
            success = true,
            count
        });
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

        var (success, message, requisitionId, code) = await _requisitionService.SaveOrUpdateRequisitionAsync(model, userId, cancellationToken);

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
            code,
            isDraft = model.IsDraft
        });
    }

    /// <summary>
    /// POST: /yeu-cau-tuyen-dung/api/form-options
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

    /// <summary>
    /// POST: /yeu-cau-tuyen-dung/sao-chep/{id} hoặc /requisitions/duplicate/{id}
    /// Nhân bản một yêu cầu tuyển dụng thành bản nháp mới (SCRUM-274).
    /// Hỗ trợ cả form POST thông thường và AJAX (header Accept: application/json).
    /// </summary>
    [HttpPost("sao-chep/{id:guid}")]
    [HttpPost("duplicate/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duplicate(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var (success, message, newRequisitionId, newCode) =
            await _requisitionService.DuplicateRequisitionAsync(id, userId, cancellationToken);

        var isAjax = Request.Headers.Accept.Any(h => h?.Contains("application/json") == true);

        if (!success)
        {
            if (isAjax)
            {
                return BadRequest(new { success = false, message });
            }

            TempData["ErrorMessage"] = message;
            return RedirectToAction(nameof(Drafts));
        }

        if (isAjax)
        {
            return Ok(new { success = true, message, newRequisitionId, newCode });
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Edit), new { id = newRequisitionId!.Value });
    }

    /// <summary>
    /// GET: /yeu-cau-tuyen-dung/api/department-headcount hoặc /requisitions/api/department-headcount
    /// Kiểm tra thời gian thực chỉ tiêu headcount, số lượng đã sử dụng và còn lại của phòng ban (Scrum 24).
    /// </summary>
    [HttpGet("api/department-headcount")]
    public async Task<IActionResult> CheckDepartmentHeadcount(
        [FromQuery] Guid departmentId,
        [FromQuery] int quantity = 1,
        [FromQuery] Guid? requisitionId = null,
        CancellationToken cancellationToken = default)
    {
        if (departmentId == Guid.Empty)
        {
            return BadRequest(new { success = false, message = "Phòng ban không hợp lệ." });
        }

        var result = await _departmentBudgetService.CheckHeadcountQuotaAsync(
            departmentId,
            quantity,
            excludeRequisitionId: requisitionId,
            cancellationToken: cancellationToken);

        return Ok(new
        {
            success = true,
            hasPlan = result.HasPlan,
            isOverQuota = result.IsOverQuota,
            targetHeadcount = result.TargetHeadcount,
            usedHeadcount = result.UsedHeadcount,
            remainingHeadcount = result.RemainingHeadcount,
            salaryBudget = result.SalaryBudget,
            currency = result.Currency,
            year = result.Year,
            message = result.Message
        });
    }
}

