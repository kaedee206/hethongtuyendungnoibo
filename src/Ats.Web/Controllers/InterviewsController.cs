using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.ViewModels.CandidateComparison;
using Ats.Web.Models.ViewModels.Interviews;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller điều phối nghiệp vụ Phỏng vấn, Đặt lịch Wizard & Đánh giá ứng viên (EP-06)
/// Tuân thủ quy chuẩn: Primary Constructor, kebab-case routing, ValidateAntiForgeryToken, phân tách Service layer.
/// </summary>
[Authorize]
[Route("interviews")]
[Route("phong-van")]
public class InterviewsController(
    IInterviewEvaluationService evaluationService,
    ICandidateEvaluationComparisonService comparisonService,
    IInterviewService interviewService,
    ILogger<InterviewsController> logger) : Controller
{
    private readonly IInterviewEvaluationService _evaluationService = evaluationService;
    private readonly ICandidateEvaluationComparisonService _comparisonService = comparisonService;
    private readonly IInterviewService _interviewService = interviewService;
    private readonly ILogger<InterviewsController> _logger = logger;

    /// <summary>
    /// GET: /interviews/{id}/evaluation hoặc /phong-van/{id}/phieu-danh-gia
    /// Mở dữ liệu phiếu đánh giá phỏng vấn.
    /// Quy tắc 1: Chỉ người phỏng vấn được mời mới mở được phiếu của buổi đó.
    /// Quy tắc 4: Không nhìn thấy đánh giá của người khác trước khi nộp phiếu của mình.
    /// </summary>
    [HttpGet("{id:guid}/evaluation")]
    [HttpGet("{id:guid}/phieu-danh-gia")]
    public async Task<IActionResult> GetEvaluation(Guid id, CancellationToken cancellationToken = default)
    {
        var currentUserId = GetCurrentUserId();
        var isStaffManager = IsStaffManager();

        var (canOpen, errorMessage, model) = await _evaluationService.GetEvaluationSheetAsync(
            id, currentUserId, isStaffManager, cancellationToken);

        if (!canOpen)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                success = false,
                message = errorMessage ?? "Bạn không có quyền mở phiếu đánh giá của buổi phỏng vấn này."
            });
        }

        return Ok(new
        {
            success = true,
            data = model
        });
    }

    /// <summary>
    /// POST: /interviews/evaluation/submit hoặc /phong-van/nop-phieu-danh-gia
    /// Nộp phiếu đánh giá sau buổi phỏng vấn.
    /// Quy tắc 1: Chỉ người phỏng vấn được mời mới nộp được phiếu.
    /// Quy tắc 2: Bắt buộc chọn kết luận: Nên tuyển, Cân nhắc, Không nên tuyển.
    /// Quy tắc 3: Đã nộp thì không sửa được, trừ khi recruiter mở lại trong 24 giờ.
    /// </summary>
    [HttpPost("evaluation/submit")]
    [HttpPost("nop-phieu-danh-gia")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitEvaluation(
        [FromForm] InterviewEvaluationSubmitInputModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            return BadRequest(new
            {
                success = false,
                message = errors.FirstOrDefault() ?? "Dữ liệu đánh giá không hợp lệ.",
                errors
            });
        }

        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty)
        {
            return Unauthorized(new { success = false, message = "Vui lòng đăng nhập để nộp phiếu đánh giá." });
        }

        var (success, message, evalId) = await _evaluationService.SubmitEvaluationAsync(
            model, currentUserId, cancellationToken);

        if (!success)
        {
            return BadRequest(new
            {
                success = false,
                message,
                evaluationId = evalId
            });
        }

        return Ok(new
        {
            success = true,
            message,
            evaluationId = evalId
        });
    }

    /// <summary>
    /// POST: /interviews/evaluations/{evaluationId}/reopen hoặc /phong-van/mo-lai-phieu
    /// Recruiter mở lại phiếu đánh giá trong vòng 24 giờ.
    /// Quy tắc 3: Đã nộp thì không sửa được, trừ khi recruiter mở lại trong 24 giờ.
    /// </summary>
    [HttpPost("evaluations/{evaluationId:guid}/reopen")]
    [HttpPost("mo-lai-phieu")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin},Quản trị viên,Trưởng phòng nhân sự,Chuyên viên tuyển dụng")]
    public async Task<IActionResult> ReopenEvaluation(
        Guid evaluationId,
        [FromForm] string? reason,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty)
        {
            return Unauthorized(new { success = false, message = "Vui lòng đăng nhập với tư cách Recruiter để mở lại phiếu." });
        }

        var (success, message) = await _evaluationService.ReopenEvaluationAsync(
            evaluationId, currentUserId, reason, cancellationToken);

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
            message
        });
    }

    /// <summary>
    /// GET: /interviews/{id}/panel-evaluations hoặc /phong-van/{id}/danh-gia-hoi-dong
    /// Xem đánh giá của các giám khảo trong hội đồng.
    /// Quy tắc 4: Không nhìn thấy đánh giá của người khác trước khi nộp phiếu của mình.
    /// </summary>
    [HttpGet("{id:guid}/panel-evaluations")]
    [HttpGet("{id:guid}/danh-gia-hoi-dong")]
    public async Task<IActionResult> GetPanelEvaluations(Guid id, CancellationToken cancellationToken = default)
    {
        var currentUserId = GetCurrentUserId();
        var isStaffManager = IsStaffManager();

        var (success, message, evaluations) = await _evaluationService.GetPanelEvaluationsAsync(
            id, currentUserId, isStaffManager, cancellationToken);

        if (!success)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                success = false,
                message = message ?? "Không thể truy cập đánh giá của hội đồng phỏng vấn."
            });
        }

        return Ok(new
        {
            success = true,
            data = evaluations
        });
    }

    /// <summary>
    /// GET: /interviews/so-sanh-ung-vien hoặc /phong-van/so-sanh-ung-vien hoặc /interviews/comparison
    /// SCRUM-336 / Scrum - Số 58: Là Trưởng bộ phận, tôi muốn so sánh các ứng viên của cùng một vị trí theo điểm đánh giá, để chọn người dựa trên tiêu chí thay vì ấn tượng cá nhân.
    /// </summary>
    [HttpGet("so-sanh-ung-vien")]
    [HttpGet("comparison")]
    [Authorize(Roles = $"{UserRoles.HiringManager},{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.Recruiter},Quản lý tuyển dụng,Quản trị viên,Trưởng phòng nhân sự,Chuyên viên tuyển dụng")]
    public async Task<IActionResult> CandidateComparison(
        [FromQuery] Guid? jobId,
        [FromQuery] string? stage,
        [FromQuery] string? sortBy,
        [FromQuery] bool? thresholdOnly,
        [FromQuery] decimal? minScore,
        CancellationToken cancellationToken = default)
    {
        var model = await _comparisonService.GetEvaluationComparisonAsync(
            jobId,
            stage,
            sortBy,
            thresholdOnly ?? false,
            minScore,
            cancellationToken);
        return View(model);
    }

    /// <summary>
    /// GET: /interviews/api/comparison-data
    /// API trả về dữ liệu so sánh ứng viên dạng JSON phục vụ lọc động qua AJAX
    /// </summary>
    [HttpGet("api/comparison-data")]
    public async Task<IActionResult> GetComparisonDataJson(
        [FromQuery] Guid? jobId,
        [FromQuery] string? stage,
        [FromQuery] string? sortBy,
        [FromQuery] bool? thresholdOnly,
        [FromQuery] decimal? minScore,
        CancellationToken cancellationToken = default)
    {
        var model = await _comparisonService.GetEvaluationComparisonAsync(
            jobId,
            stage,
            sortBy,
            thresholdOnly ?? false,
            minScore,
            cancellationToken);
        return Json(new { success = true, data = model });
    }

    #region Option 2: Trang Chuyên Biệt Lên Lịch Phỏng Vấn (Wizard 4 Bước)

    /// <summary>
    /// GET: /phong-van/dat-lich hoặc /interviews/dat-lich hoặc /interviews/schedule-wizard
    /// Mở trang Wizard toàn màn hình 4 bước để lên lịch phỏng vấn chuyên biệt (Option 2).
    /// </summary>
    [HttpGet("dat-lich")]
    [HttpGet("schedule-wizard")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.HRManager},{UserRoles.Recruiter},{UserRoles.HiringManager},Quản trị viên,Trưởng phòng nhân sự,Chuyên viên tuyển dụng,Quản lý tuyển dụng")]
    public async Task<IActionResult> ScheduleWizard(
        [FromQuery] Guid? applicationId,
        CancellationToken cancellationToken = default)
    {
        var model = await _interviewService.GetScheduleWizardDataAsync(applicationId, cancellationToken);
        return View(model);
    }

    /// <summary>
    /// GET: /phong-van/api/candidate-dossier/{applicationId}
    /// API trả về thông tin chi tiết hồ sơ ứng viên (Dossier) khi chuyển đổi ứng viên ở Bước 1 của Wizard.
    /// </summary>
    [HttpGet("api/candidate-dossier/{applicationId:guid}")]
    public async Task<IActionResult> GetCandidateDossierJson(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var dossier = await _interviewService.GetCandidateWizardDetailAsync(applicationId, cancellationToken);
        if (dossier == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy thông tin hồ sơ ứng viên." });
        }
        return Json(new { success = true, data = dossier });
    }

    /// <summary>
    /// GET: /phong-van/api/check-interviewer-busy
    /// API kiểm tra tình trạng trống lịch / trùng lịch của các giám khảo ở Bước 2 của Wizard.
    /// </summary>
    [HttpGet("api/check-interviewer-busy")]
    public async Task<IActionResult> CheckInterviewerBusyJson(
        [FromQuery] string? interviewerIds,
        [FromQuery] DateTime? startTime,
        [FromQuery] int durationMinutes = 60,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(interviewerIds))
        {
            return Json(new { success = true, data = new List<object>() });
        }

        var ids = interviewerIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Guid.TryParse(s.Trim(), out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (!ids.Any())
        {
            return Json(new { success = true, data = new List<object>() });
        }

        var effectiveStart = startTime ?? DateTime.UtcNow.AddDays(1);
        var startUtc = new DateTimeOffset(DateTime.SpecifyKind(effectiveStart, DateTimeKind.Utc));
        var endUtc = startUtc.AddMinutes(durationMinutes > 0 ? durationMinutes : 60);

        var busyList = await _interviewService.CheckInterviewersAvailabilityAsync(ids, startUtc, endUtc, cancellationToken);
        return Json(new { success = true, data = busyList });
    }

    /// <summary>
    /// POST: /phong-van/dat-lich hoặc /interviews/dat-lich hoặc /interviews/schedule-wizard
    /// Tiếp nhận lệnh chốt lịch từ Bước 4 của Wizard, tự động tạo lịch, gán hội đồng và gửi thư mời email.
    /// </summary>
    [HttpPost("dat-lich")]
    [HttpPost("schedule-wizard")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.HRManager},{UserRoles.Recruiter},{UserRoles.HiringManager},Quản trị viên,Trưởng phòng nhân sự,Chuyên viên tuyển dụng,Quản lý tuyển dụng")]
    public async Task<IActionResult> SubmitScheduleWizard(
        [FromForm] ScheduleInterviewInputModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();

            var firstErr = errors.FirstOrDefault() ?? "Dữ liệu đặt lịch phỏng vấn không hợp lệ.";

            if (IsAjaxRequest())
            {
                return BadRequest(new { success = false, message = firstErr, errors });
            }

            TempData["ErrorMessage"] = firstErr;
            return RedirectToAction(nameof(ScheduleWizard), new { applicationId = model.ApplicationId });
        }

        var currentUserId = GetCurrentUserId();
        var isRecruiterOnly = User.IsInRole(UserRoles.Recruiter) &&
                              !User.IsInRole(UserRoles.Admin) &&
                              !User.IsInRole(UserRoles.HRManager) &&
                              !User.IsInRole("Quản trị viên") &&
                              !User.IsInRole("Trưởng phòng nhân sự");

        var result = await _interviewService.ScheduleInterviewAsync(model, currentUserId, isRecruiterOnly, cancellationToken);

        if (!result.Success)
        {
            if (IsAjaxRequest())
            {
                return BadRequest(new
                {
                    success = false,
                    message = result.Message,
                    errors = result.Errors
                });
            }

            TempData["ErrorMessage"] = result.Message;
            return RedirectToAction(nameof(ScheduleWizard), new { applicationId = model.ApplicationId });
        }

        if (IsAjaxRequest())
        {
            return Ok(new
            {
                success = true,
                message = result.Message,
                interviewId = result.InterviewId,
                roundNumber = result.RoundNumber,
                title = result.Title,
                startTime = result.StartTimeDisplay,
                endTime = result.EndTimeDisplay,
                redirectUrl = $"/dashboard/ung-vien?scheduledAppId={model.ApplicationId}"
            });
        }

        TempData["SuccessMessage"] = result.Message;
        return RedirectToAction("Index", "Home", new { tab = "pipeline" });
    }

    private bool IsAjaxRequest()
    {
        return Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
               Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    private Guid GetCurrentUserId()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdStr, out var id) ? id : Guid.Empty;
    }

    private bool IsStaffManager()
    {
        return User.IsInRole(UserRoles.Admin) ||
               User.IsInRole(UserRoles.HRManager) ||
               User.IsInRole(UserRoles.Recruiter) ||
               User.IsInRole("Quản trị viên") ||
               User.IsInRole("Trưởng phòng nhân sự") ||
               User.IsInRole("Chuyên viên tuyển dụng");
    }
}
