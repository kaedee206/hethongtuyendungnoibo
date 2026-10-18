using System.Security.Claims;
using Ats.Web.Common;
using Ats.Web.Constants;
using Ats.Web.Models.ViewModels.CandidateInterviews;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller điều phối xác nhận lịch phỏng vấn và phản hồi dành cho ứng viên (Scrum - Số 54).
/// YÊU CẦU BẢO MẬT: Bắt buộc đăng nhập tài khoản Ứng viên và người dùng phải là chính chủ ứng viên nhận thư mời.
/// Ngăn chặn IDOR và chặn hoàn toàn nhân viên nội bộ thao tác thay ứng viên.
/// </summary>
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
        // 1. Kiểm tra xác thực (Authentication) - Bắt buộc đăng nhập tài khoản Ứng viên
        if (User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = Request.Path + Request.QueryString;
            TempData["WarningMessage"] = "Vui lòng đăng nhập tài khoản Ứng viên để xác nhận lịch phỏng vấn và quản lý thông tin phỏng vấn.";
            return RedirectToAction("CandidateAuth", "Account", new { returnUrl });
        }

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

        // 2. Kiểm tra phân quyền & danh tính ứng viên (Authorization / Anti-IDOR check)
        var (isOwner, isStaff, userEmail, userId) = VerifyCandidateOwnership(model);
        if (!isOwner)
        {
            return ForbiddenView(model, userEmail, userId, isStaff, Request.Path + Request.QueryString);
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
        var targetToken = !string.IsNullOrWhiteSpace(model.Token) ? model.Token : token;
        var returnUrl = $"/xac-nhan-phong-van/{Uri.EscapeDataString(targetToken ?? "")}";

        // 1. Kiểm tra xác thực (Authentication)
        if (User.Identity?.IsAuthenticated != true)
        {
            if (IsAjaxRequest())
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new { success = false, message = "Vui lòng đăng nhập tài khoản Ứng viên để xác nhận tham gia phỏng vấn." });
            }
            TempData["WarningMessage"] = "Vui lòng đăng nhập tài khoản Ứng viên để xác nhận tham gia phỏng vấn.";
            return RedirectToAction("CandidateAuth", "Account", new { returnUrl });
        }

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

        // Lấy thông tin lịch phỏng vấn tương ứng token
        var confirmationModel = await _interviewService.GetInterviewConfirmationAsync(model.Token, cancellationToken);
        if (confirmationModel == null)
        {
            if (IsAjaxRequest())
            {
                return BadRequest(new { success = false, message = "Liên kết hoặc mã xác nhận không hợp lệ hoặc đã hết hạn." });
            }
            return View("InvalidToken", "Liên kết này không hợp lệ, đã hết hạn hoặc chỉ được dùng cho đúng buổi phỏng vấn được mời.");
        }

        // 2. Kiểm tra phân quyền & danh tính ứng viên (Authorization / Anti-IDOR check)
        var (isOwner, isStaff, userEmail, userId) = VerifyCandidateOwnership(confirmationModel);
        if (!isOwner)
        {
            if (IsAjaxRequest())
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    success = false,
                    message = isStaff
                        ? "Nhân sự nội bộ không được phép thực hiện xác nhận thay cho ứng viên."
                        : "Bạn không có quyền thực hiện thao tác trên lịch phỏng vấn của ứng viên này."
                });
            }
            return ForbiddenView(confirmationModel, userEmail, userId, isStaff, returnUrl);
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
        var targetToken = !string.IsNullOrWhiteSpace(model.Token) ? model.Token : token;
        var returnUrl = $"/xac-nhan-phong-van/{Uri.EscapeDataString(targetToken ?? "")}";

        // 1. Kiểm tra xác thực (Authentication)
        if (User.Identity?.IsAuthenticated != true)
        {
            if (IsAjaxRequest())
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new { success = false, message = "Vui lòng đăng nhập tài khoản Ứng viên để gửi đề nghị đổi lịch." });
            }
            TempData["WarningMessage"] = "Vui lòng đăng nhập tài khoản Ứng viên để gửi đề nghị đổi lịch.";
            return RedirectToAction("CandidateAuth", "Account", new { returnUrl });
        }

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

        // Lấy thông tin lịch phỏng vấn tương ứng token
        var confirmationModel = await _interviewService.GetInterviewConfirmationAsync(model.Token, cancellationToken);
        if (confirmationModel == null)
        {
            if (IsAjaxRequest())
            {
                return BadRequest(new { success = false, message = "Liên kết hoặc mã xác nhận không hợp lệ hoặc đã hết hạn." });
            }
            return View("InvalidToken", "Liên kết này không hợp lệ, đã hết hạn hoặc chỉ được dùng cho đúng buổi phỏng vấn được mời.");
        }

        // 2. Kiểm tra phân quyền & danh tính ứng viên (Authorization / Anti-IDOR check)
        var (isOwner, isStaff, userEmail, userId) = VerifyCandidateOwnership(confirmationModel);
        if (!isOwner)
        {
            if (IsAjaxRequest())
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    success = false,
                    message = isStaff
                        ? "Nhân sự nội bộ không được phép gửi đề nghị đổi lịch thay cho ứng viên."
                        : "Bạn không có quyền thực hiện thao tác trên lịch phỏng vấn của ứng viên này."
                });
            }
            return ForbiddenView(confirmationModel, userEmail, userId, isStaff, returnUrl);
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

    private (bool isOwner, bool isStaff, string userEmail, Guid userId) VerifyCandidateOwnership(CandidateInterviewConfirmationViewModel model)
    {
        var userEmail = User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant() ?? "";
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(userIdStr, out var userId);

        var userRoles = User.FindAll(ClaimTypes.Role)
            .Select(c => UserRoles.NormalizeRole(c.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var isStaffRole = userRoles.Any(r => r != UserRoles.Candidate);
        var isStaffEmail = userEmail.EndsWith("@noveratech.digital", StringComparison.OrdinalIgnoreCase) ||
                           userEmail.EndsWith("@noveratech.vn", StringComparison.OrdinalIgnoreCase);
        var isStaff = isStaffRole || isStaffEmail;

        var candidateEmail = model.CandidateEmail?.Trim().ToLowerInvariant() ?? "";
        var isOwner = !isStaff && (
            (userId != Guid.Empty && model.CandidateUserId.HasValue && model.CandidateUserId.Value == userId) ||
            (!string.IsNullOrEmpty(userEmail) && !string.IsNullOrEmpty(candidateEmail) && string.Equals(userEmail, candidateEmail, StringComparison.OrdinalIgnoreCase))
        );

        return (isOwner, isStaff, userEmail, userId);
    }

    private IActionResult ForbiddenView(CandidateInterviewConfirmationViewModel model, string userEmail, Guid userId, bool isStaff, string returnUrl)
    {
        var displayCandidateEmail = model.CandidateEmail ?? "Ứng viên";
        _logger.LogWarning("BẢO MẬT: Tài khoản {UserEmail} (ID: {UserId}, IsStaff: {IsStaff}) bị từ chối truy cập thư mời phỏng vấn của ứng viên {CandidateEmail} (InterviewId: {InterviewId}). IP: {Ip}",
            PiiMaskingHelper.MaskEmail(userEmail), userId, isStaff, PiiMaskingHelper.MaskEmail(displayCandidateEmail), model.InterviewId, HttpContext.Connection.RemoteIpAddress);

        var statusCodeFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>();
        if (statusCodeFeature != null)
        {
            statusCodeFeature.Enabled = false;
        }

        Response.StatusCode = StatusCodes.Status403Forbidden;
        ViewBag.IsUnauthorizedUser = true;
        ViewBag.IsStaff = isStaff;
        ViewBag.LoggedInUserEmail = userEmail;
        ViewBag.CandidateMaskedEmail = PiiMaskingHelper.MaskEmail(displayCandidateEmail);
        ViewBag.ErrorMessage = isStaff
            ? $"Truy cập bị từ chối: Bạn đang đăng nhập bằng tài khoản nhân sự nội bộ ({userEmail}). Thư mời phỏng vấn này dành riêng cho ứng viên ({PiiMaskingHelper.MaskEmail(displayCandidateEmail)}). Nhân sự nội bộ vui lòng thao tác trên Cổng quản trị phỏng vấn nội bộ hoặc đăng xuất để đăng nhập bằng tài khoản Ứng viên."
            : $"Truy cập bị từ chối: Bạn đang đăng nhập bằng tài khoản ({userEmail}), không phải là chủ sở hữu nhận thư mời phỏng vấn này ({PiiMaskingHelper.MaskEmail(displayCandidateEmail)}).";
        ViewBag.ReturnUrl = returnUrl;
        return View("UnauthorizedInterviewAccess");
    }

    private bool IsAjaxRequest()
    {
        return Request.Headers.XRequestedWith == "XMLHttpRequest" ||
               (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase));
    }
}
