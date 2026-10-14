using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Models.ViewModels.RequisitionAssignments;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Quản lý phân công Recruiter phụ trách yêu cầu tuyển dụng và lịch sử chuyển giao (Scrum 26).
/// Chỉ dành cho Trưởng phòng Nhân sự và Admin hệ thống.
/// </summary>
[Authorize(Roles = $"{UserRoles.Admin},{UserRoles.HRManager}")]
[Route("phan-cong-recruiter")]
[Route("yeu-cau-tuyen-dung/phan-cong")]
public class RequisitionAssignmentsController : Controller
{
    private readonly IRequisitionAssignmentService _assignmentService;
    private readonly ILogger<RequisitionAssignmentsController> _logger;

    public RequisitionAssignmentsController(
        IRequisitionAssignmentService assignmentService,
        ILogger<RequisitionAssignmentsController> logger)
    {
        _assignmentService = assignmentService;
        _logger = logger;
    }

    private Guid GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// GET: /phan-cong-recruiter hoặc /yeu-cau-tuyen-dung/phan-cong
    /// Màn hình danh sách phân công Recruiter chính & hỗ trợ, kèm KPI và bộ lọc.
    /// </summary>
    [HttpGet("")]
    [HttpGet("index")]
    public async Task<IActionResult> Index(
        [FromQuery] string? search,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? recruiterId,
        [FromQuery] string? filter,
        CancellationToken cancellationToken = default)
    {
        var model = await _assignmentService.GetAssignmentOverviewAsync(search, departmentId, recruiterId, filter, cancellationToken);
        return View(model);
    }

    /// <summary>
    /// GET: /phan-cong-recruiter/api/detail/{id}
    /// Lấy thông tin chi tiết phân công phục vụ nạp dữ liệu vào Modal.
    /// </summary>
    [HttpGet("api/detail/{id:guid}")]
    [HttpGet("detail/{id:guid}")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await _assignmentService.GetAssignmentDetailAsync(id, cancellationToken);
        if (detail == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy yêu cầu tuyển dụng." });
        }

        var availableRecruiters = await _assignmentService.GetAvailableRecruitersAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            data = detail,
            recruiters = availableRecruiters
        });
    }

    /// <summary>
    /// GET: /phan-cong-recruiter/assign hoặc /phan-cong-recruiter/save
    /// Chuyển hướng an toàn về trang danh sách nếu truy cập trực tiếp bằng phương thức GET.
    /// </summary>
    [HttpGet("assign")]
    [HttpGet("save")]
    public IActionResult AssignRedirect() => RedirectToAction(nameof(Index));

    /// <summary>
    /// POST: /phan-cong-recruiter/save hoặc /phan-cong-recruiter/assign
    /// Xử lý phân công 1 Recruiter chính và nhiều Recruiter hỗ trợ, ghi lịch sử chuyển giao nếu đổi người phụ trách.
    /// </summary>
    [HttpPost("save")]
    [HttpPost("assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        [FromForm] RequisitionAssignRequestModel model,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = GetCurrentUserId();

        if (model.RequisitionId == Guid.Empty || model.LeadRecruiterId == Guid.Empty)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return BadRequest(new { success = false, message = "Vui lòng chọn yêu cầu tuyển dụng và chỉ định 1 Recruiter chính." });
            }

            TempData["ErrorMessage"] = "Vui lòng chỉ định 1 Recruiter chính chịu trách nhiệm chạy tới cùng.";
            return RedirectToAction(nameof(Index));
        }

        var (success, message) = await _assignmentService.AssignRecruitersAsync(model, currentUserId, cancellationToken);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            if (!success)
            {
                return BadRequest(new { success = false, message });
            }
            return Ok(new { success = true, message });
        }

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

    /// <summary>
    /// GET: /phan-cong-recruiter/api/history/{id}
    /// Lấy toàn bộ timeline lịch sử chuyển giao người phụ trách của một yêu cầu tuyển dụng.
    /// </summary>
    [HttpGet("api/history/{id:guid}")]
    [HttpGet("history/{id:guid}")]
    [HttpGet("lich-su/{id:guid}")]
    public async Task<IActionResult> GetHandoverHistory(Guid id, CancellationToken cancellationToken = default)
    {
        var histories = await _assignmentService.GetHandoverHistoriesAsync(id, cancellationToken);
        return Ok(new
        {
            success = true,
            total = histories.Count,
            data = histories
        });
    }
}
