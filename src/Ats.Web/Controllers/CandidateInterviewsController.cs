using Ats.Web.Models.ViewModels.CandidateInterviews;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller điều phối xác nhận lịch phỏng vấn và phản hồi dành cho ứng viên (Scrum - Số 54).
/// Cho phép ứng viên truy cập công khai qua liên kết bảo mật có token riêng cho từng buổi phỏng vấn.
/// Tuân thủ quy chuẩn: Primary Constructor, kebab-case routing, AntiForgeryToken, Clean Controller.
/// </summary>
[AllowAnonymous]
[Route("xac-nhan-phong-van")]
[Route("ung-vien/xac-nhan-lich")]
[Route("candidate-interviews")]
public class CandidateInterviewsController(
    ICandidateInterviewService interviewService,
    ILogger<CandidateInterviewsController> logger) : Controller
{
    private readonly ICandidateInterviewService _interviewService = interviewService;
    private readonly ILogger<CandidateInterviewsController> _logger = logger;

    /// <summary>
    /// GET: /xac-nhan-phong-van/{token} hoặc /xac-nhan-phong-van?token=...
    /// Mở giao diện xem chi tiết lịch hẹn, thông tin chuẩn bị và nút xác nhận / đề nghị đổi lịch.
    /// </summary>
    [HttpGet("")]
    [HttpGet("{token}")]
    public async Task<IActionResult> Index(
        string? token,
        [FromQuery] string? action,
        CancellationToken cancellationToken = default)
    {
        var effectiveToken = token ?? Request.Query["token"].ToString();
        if (string.IsNullOrWhiteSpace(effectiveToken))
        {
            return View("InvalidToken", "Liên kết không hợp lệ. Vui lòng kiểm tra lại email thư mời từ NoveraTech.");
        }

        var model = await _interviewService.GetInterviewConfirmationAsync(effectiveToken, cancellationToken);
        if (model == null)
        {
            return View("InvalidToken", "Liên kết này không hợp lệ, đã hết hạn hoặc chỉ được dùng cho đúng buổi phỏng vấn được mời.");
        }

        // Hỗ trợ tham số thao tác nhanh từ email (?action=confirm hoặc ?action=reschedule)
        if (string.Equals(action, "confirm", StringComparison.OrdinalIgnoreCase) && model.CanRespond && model.CandidateConfirmed != Ats.Web.Models.Enums.CandidateConfirmStatus.ACCEPTED)
        {
            var (success, msg) = await _interviewService.ConfirmAttendanceAsync(new CandidateInterviewConfirmInputModel
            {
                Token = effectiveToken
            }, cancellationToken);

            model.FeedbackIsSuccess = success;
            model.FeedbackMessage = msg;
            if (success)
            {
                model.CandidateConfirmed = Ats.Web.Models.Enums.CandidateConfirmStatus.ACCEPTED;
            }
        }
        else if (string.Equals(action, "reschedule", StringComparison.OrdinalIgnoreCase))
        {
            ViewBag.OpenReschedule = true;
        }

        return View(model);
    }

    /// <summary>
    /// POST: /xac-nhan-phong-van/{token}/xac-nhan hoặc /xac-nhan-phong-van/xac-nhan
    /// Ứng viên bấm xác nhận tham gia phỏng vấn.
    /// </summary>
    [HttpPost("xac-nhan")]
    [HttpPost("{token}/xac-nhan")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(
        string? token,
        [FromForm] CandidateInterviewConfirmInputModel model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.Token) && !string.IsNullOrWhiteSpace(token))
        {
            model.Token = token;
        }

        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu không hợp lệ.";
            if (IsAjaxRequest())
            {
                return BadRequest(new { success = false, message = firstError });
            }
            TempData["ErrorMessage"] = firstError;
            return RedirectToAction(nameof(Index), new { token = model.Token });
        }

        var (success, message) = await _interviewService.ConfirmAttendanceAsync(model, cancellationToken);

        if (IsAjaxRequest())
        {
            return Json(new { success, message });
        }

        if (success)
        {
            TempData["SuccessMessage"] = message;
        }
        else
        {
            TempData["ErrorMessage"] = message;
        }

        return RedirectToAction(nameof(Index), new { token = model.Token });
    }

    /// <summary>
    /// POST: /xac-nhan-phong-van/{token}/doi-lich hoặc /xac-nhan-phong-van/doi-lich
    /// Ứng viên gửi đề nghị đổi lịch phỏng vấn.
    /// </summary>
    [HttpPost("doi-lich")]
    [HttpPost("{token}/doi-lich")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reschedule(
        string? token,
        [FromForm] CandidateInterviewRescheduleInputModel model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.Token) && !string.IsNullOrWhiteSpace(token))
        {
            model.Token = token;
        }

        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Vui lòng nhập đầy đủ lý do đổi lịch.";
            if (IsAjaxRequest())
            {
                return BadRequest(new { success = false, message = firstError });
            }
            TempData["ErrorMessage"] = firstError;
            return RedirectToAction(nameof(Index), new { token = model.Token });
        }

        var (success, message) = await _interviewService.RequestRescheduleAsync(model, cancellationToken);

        if (IsAjaxRequest())
        {
            return Json(new { success, message });
        }

        if (success)
        {
            TempData["SuccessMessage"] = message;
        }
        else
        {
            TempData["ErrorMessage"] = message;
        }

        return RedirectToAction(nameof(Index), new { token = model.Token });
    }

    private bool IsAjaxRequest()
    {
        return Request.Headers.XRequestedWith == "XMLHttpRequest" ||
               (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase));
    }
}
