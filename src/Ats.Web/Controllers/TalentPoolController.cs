using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Models.ViewModels.TalentPool;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Quản lý kho ứng viên tiềm năng và gợi ý hồ sơ phù hợp.
/// </summary>
[Authorize(Roles = $"{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.Recruiter},{UserRoles.HiringManager}")]
[Route("kho-tiem-nang")]
[Route("[controller]")]
public class TalentPoolController(
    ITalentPoolService talentPoolService,
    ISecurityAuditService securityAuditService,
    ILogger<TalentPoolController> logger) : Controller
{
    private readonly ITalentPoolService _talentPoolService = talentPoolService;
    private readonly ISecurityAuditService _securityAuditService = securityAuditService;
    private readonly ILogger<TalentPoolController> _logger = logger;

    // GET: /kho-tiem-nang hoặc /TalentPool
    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? search,
        [FromQuery] string? jobTitle,
        [FromQuery] string? skill,
        [FromQuery] string? status,
        [FromQuery] int? minRating,
        CancellationToken cancellationToken)
    {
        var model = await _talentPoolService.GetTalentPoolListAsync(
            search, jobTitle, skill, status, minRating, cancellationToken);

        return View(model); // Views/TalentPool/Index.cshtml
    }

    // GET: /kho-tiem-nang/goi-y hoặc /kho-tiem-nang/goi-y/{jobId}
    [HttpGet("goi-y")]
    [HttpGet("Suggestions")]
    [HttpGet("goi-y/{jobId:guid}")]
    [HttpGet("Suggestions/{jobId:guid}")]
    public async Task<IActionResult> Suggestions(Guid? jobId, CancellationToken cancellationToken)
    {
        if (!jobId.HasValue || jobId.Value == Guid.Empty)
        {
            var firstJobId = await _talentPoolService.GetFirstActiveJobPostingIdAsync(cancellationToken);
            if (firstJobId.HasValue && firstJobId.Value != Guid.Empty)
            {
                return RedirectToAction(nameof(Suggestions), new { jobId = firstJobId.Value });
            }

            TempData["ErrorMessage"] = "Hiện chưa có tin tuyển dụng nào đang mở để gợi ý ứng viên tiềm năng.";
            return RedirectToAction(nameof(Index));
        }

        var model = await _talentPoolService.GetSuggestedCandidatesForJobAsync(jobId.Value, cancellationToken);
        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy thông tin vị trí tuyển dụng tương ứng.";
            return RedirectToAction(nameof(Index));
        }

        return View(model);
    }

    // GET: /kho-tiem-nang/api/goi-y/{jobId}
    [HttpGet("api/goi-y/{jobId:guid}")]
    public async Task<IActionResult> GetSuggestionsApi(Guid jobId, CancellationToken cancellationToken)
    {
        var model = await _talentPoolService.GetSuggestedCandidatesForJobAsync(jobId, cancellationToken);
        if (model == null)
        {
            return NotFound(new { success = false, message = "Vị trí tuyển dụng không tồn tại." });
        }

        return Json(new { success = true, data = model });
    }

    // POST: /kho-tiem-nang/them-ung-vien hoặc /TalentPool/AddCandidate
    [HttpPost("them-ung-vien")]
    [HttpPost("AddCandidate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCandidate([FromForm] AddTalentPoolCandidateInputModel model, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();

        var result = await _talentPoolService.AddCandidateToPoolAsync(model, currentUserId, cancellationToken);

        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            return Json(new { success = result.Success, message = result.Message, id = result.Id });
        }

        if (result.Success)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    // POST: /kho-tiem-nang/cap-nhat hoặc /TalentPool/UpdateCandidate
    [HttpPost("cap-nhat")]
    [HttpPost("UpdateCandidate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCandidate([FromForm] UpdateTalentPoolCandidateInputModel model, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();

        var result = await _talentPoolService.UpdateCandidateInPoolAsync(model, currentUserId, cancellationToken);

        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            return Json(new { success = result.Success, message = result.Message });
        }

        if (result.Success)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    // POST: /kho-tiem-nang/go-ung-vien hoặc /TalentPool/RemoveCandidate
    [HttpPost("go-ung-vien")]
    [HttpPost("RemoveCandidate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveCandidate([FromForm] RemoveTalentPoolCandidateInputModel model, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();

        var result = await _talentPoolService.RemoveCandidateFromPoolAsync(model.Id, currentUserId, cancellationToken);

        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            return Json(new { success = result.Success, message = result.Message });
        }

        if (result.Success)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    // POST: /kho-tiem-nang/moi-ung-tuyen hoặc /TalentPool/InviteCandidate
    [HttpPost("moi-ung-tuyen")]
    [HttpPost("InviteCandidate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InviteCandidate([FromForm] InviteTalentPoolCandidateInputModel model, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();

        var result = await _talentPoolService.InviteCandidateToJobAsync(model, currentUserId, cancellationToken);

        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            return Json(new { success = result.Success, message = result.Message, applicationId = result.NewApplicationId });
        }

        if (result.Success)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Suggestions), new { jobId = model.JobPostingId });
    }

    private Guid GetCurrentUserId()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdStr, out var id) ? id : Guid.Empty;
    }
}
