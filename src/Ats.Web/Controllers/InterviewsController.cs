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
/// Controller điều phối nghiệp vụ Phỏng vấn & Đánh giá ứng viên (EP-06_InterviewEvaluation / Scrum - Số 57 & SCRUM-336 / Scrum - Số 58)
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
        CancellationToken cancellationToken = default)
    {
        var model = await _comparisonService.GetEvaluationComparisonAsync(jobId, stage, sortBy, cancellationToken);
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
        CancellationToken cancellationToken = default)
    {
        var model = await _comparisonService.GetEvaluationComparisonAsync(jobId, stage, sortBy, cancellationToken);
        return Json(new { success = true, data = model });
    }

    /// <summary>
    /// POST: /interviews/kiem-tra-trung-lich hoặc /interviews/check-conflict
    /// Kiểm tra xung đột lịch phỏng vấn đối với Người phỏng vấn hoặc Phòng họp (SCRUM-53).
    /// </summary>
    [HttpPost("kiem-tra-trung-lich")]
    [HttpPost("check-conflict")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.HiringManager},Quản trị viên,Trưởng phòng nhân sự,Chuyên viên tuyển dụng")]
    public async Task<IActionResult> CheckConflict(
        [FromForm] InterviewConflictCheckInputModel? formInput,
        CancellationToken cancellationToken = default)
    {
        InterviewConflictCheckInputModel? input = formInput;
        if (Request.HasJsonContentType())
        {
            try
            {
                Request.EnableBuffering();
                using var reader = new System.IO.StreamReader(Request.Body, System.Text.Encoding.UTF8, leaveOpen: true);
                var bodyText = await reader.ReadToEndAsync(cancellationToken);
                Request.Body.Position = 0;
                if (!string.IsNullOrWhiteSpace(bodyText))
                {
                    input = System.Text.Json.JsonSerializer.Deserialize<InterviewConflictCheckInputModel>(
                        bodyText,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi đọc JSON payload check-conflict");
            }
        }

        if (input == null || !input.StartTime.HasValue)
        {
            return BadRequest(new { success = false, message = "Vui lòng chọn ngày giờ bắt đầu phỏng vấn." });
        }

        var result = await _interviewService.CheckScheduleConflictAsync(input, cancellationToken);
        return Ok(new
        {
            success = true,
            data = result
        });
    }

    /// <summary>
    /// GET: /interviews/danh-sach-phong-hop hoặc /interviews/meeting-rooms
    /// Lấy danh sách các phòng họp phục vụ chọn phòng phỏng vấn trực tiếp (SCRUM-53).
    /// </summary>
    [HttpGet("danh-sach-phong-hop")]
    [HttpGet("meeting-rooms")]
    public async Task<IActionResult> GetMeetingRooms(CancellationToken cancellationToken = default)
    {
        var rooms = await _interviewService.GetAvailableMeetingRoomsAsync(cancellationToken);
        return Json(new { success = true, data = rooms });
    }

    /// <summary>
    /// POST: /interviews/{id}/doi-lich hoặc /interviews/{id}/reschedule
    /// Cập nhật / Đổi lịch phỏng vấn đã có (SCRUM-53).
    /// </summary>
    [HttpPost("{id:guid}/doi-lich")]
    [HttpPost("{id:guid}/reschedule")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.HiringManager},Quản trị viên,Trưởng phòng nhân sự,Chuyên viên tuyển dụng")]
    public async Task<IActionResult> Reschedule(
        Guid id,
        [FromForm] ScheduleInterviewInputModel? formInput,
        CancellationToken cancellationToken = default)
    {
        ScheduleInterviewInputModel? model = formInput;
        if (Request.HasJsonContentType())
        {
            try
            {
                Request.EnableBuffering();
                using var reader = new System.IO.StreamReader(Request.Body, System.Text.Encoding.UTF8, leaveOpen: true);
                var bodyText = await reader.ReadToEndAsync(cancellationToken);
                Request.Body.Position = 0;
                if (!string.IsNullOrWhiteSpace(bodyText))
                {
                    model = System.Text.Json.JsonSerializer.Deserialize<ScheduleInterviewInputModel>(
                        bodyText,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi đọc JSON payload reschedule");
            }
        }

        if (model == null)
        {
            return BadRequest(new { success = false, message = "Dữ liệu đổi lịch không hợp lệ." });
        }

        var currentUserId = GetCurrentUserId();
        var isRecruiterOnly = User?.Identity?.IsAuthenticated == true &&
                              User.IsInRole(UserRoles.Recruiter) &&
                              !User.IsInRole(UserRoles.Admin) &&
                              !User.IsInRole(UserRoles.HRManager) &&
                              !User.IsInRole("Quản trị viên") &&
                              !User.IsInRole("Trưởng phòng nhân sự");

        try
        {
            var result = await _interviewService.RescheduleInterviewAsync(id, model, currentUserId, isRecruiterOnly, cancellationToken);
            return Ok(new
            {
                success = result.Success,
                message = result.Message,
                data = result
            });
        }
        catch (Ats.Web.Exceptions.BusinessRuleValidationException ex)
        {
            return BadRequest(new
            {
                success = false,
                isConflict = true,
                message = ex.Message,
                errors = ex.ValidationErrors,
                data = ex.ErrorData
            });
        }
    }

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
