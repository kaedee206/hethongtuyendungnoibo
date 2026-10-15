using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Models.ViewModels.Interviews;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller điều phối nghiệp vụ Phỏng vấn & Đánh giá ứng viên (EP-06_InterviewEvaluation / Scrum - Số 57)
/// Tuân thủ quy chuẩn: Primary Constructor, kebab-case routing, ValidateAntiForgeryToken, phân tách Service layer.
/// </summary>
[Authorize]
[Route("interviews")]
[Route("phong-van")]
public class InterviewsController(
    IInterviewEvaluationService evaluationService,
    ILogger<InterviewsController> logger) : Controller
{
    private readonly IInterviewEvaluationService _evaluationService = evaluationService;
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
