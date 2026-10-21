using System.Security.Claims;
using Ats.Web.Common;
using Ats.Web.Constants;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Offers;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Cổng tiếp nhận và phản hồi Thư mời nhận việc (Job Offer) dành cho Ứng viên (SCRUM-342).
/// Cho phép ứng viên xem lại các điều khoản, thời hạn hiệu lực, và gửi quyết định Chấp nhận/Từ chối chính thức kèm ý kiến phản hồi.
/// </summary>
[Route("xac-nhan-offer")]
[Route("ung-vien/offer")]
[Route("candidate-offers")]
public class CandidateOffersController(
    ICandidateOfferService offerService,
    ILogger<CandidateOffersController> logger) : Controller
{
    private readonly ICandidateOfferService _offerService = offerService;
    private readonly ILogger<CandidateOffersController> _logger = logger;

    /// <summary>
    /// GET: /ung-vien/offer/{token} hoặc /xac-nhan-offer/{token}
    /// Mở giao diện xem Thư mời nhận việc, bảng điều khoản, thời hạn hiệu lực và form phản hồi.
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
            return View("InvalidToken", "Liên kết thư mời nhận việc không hợp lệ hoặc thiếu mã xác thực. Vui lòng kiểm tra lại email nhận từ NoveraTech.");
        }

        var model = await _offerService.GetOfferDetailsByTokenAsync(effectiveToken, cancellationToken);
        if (model == null)
        {
            return View("InvalidToken", "Liên kết này không hợp lệ, không tồn tại hoặc đã bị can thiệp.");
        }

        // Kiểm tra phân quyền danh tính nếu ứng viên đã đăng nhập (chống IDOR / nhầm lẫn tài khoản)
        if (User.Identity?.IsAuthenticated == true)
        {
            var (isOwner, isStaff, userEmail, userId) = VerifyCandidateIdentity(model);
            if (isStaff)
            {
                // Nhân sự nội bộ đang mở liên kết ứng viên: gắn cờ để hiển thị banner thông báo chế độ xem trước
                ViewBag.IsStaffPreview = true;
                ViewBag.StaffEmail = userEmail;
            }
            else if (!isOwner)
            {
                return ForbiddenView(model, userEmail, userId, Request.Path + Request.QueryString);
            }
        }

        // Nếu offer đã hết hạn và ứng viên chưa phản hồi
        if (model.IsExpired && !model.HasResponded)
        {
            return View("ExpiredOffer", model);
        }

        // Hỗ trợ tham số thao tác nhanh từ email (?action=accept hoặc ?action=decline)
        if (string.Equals(action, "accept", StringComparison.OrdinalIgnoreCase) && model.CanRespond)
        {
            ViewBag.PreSelectAccept = true;
        }
        else if (string.Equals(action, "decline", StringComparison.OrdinalIgnoreCase) && model.CanRespond)
        {
            ViewBag.PreSelectDecline = true;
        }

        return View(model);
    }

    /// <summary>
    /// POST: /ung-vien/offer/phan-hoi hoặc /xac-nhan-offer/phan-hoi
    /// Xử lý quyết định Chấp nhận hoặc Từ chối của ứng viên kèm ý kiến phản hồi.
    /// </summary>
    [HttpPost("phan-hoi")]
    [HttpPost("{token}/phan-hoi")]
    [HttpPost("respond")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Respond(
        string? token,
        [FromForm] CandidateOfferResponseInputModel model,
        CancellationToken cancellationToken = default)
    {
        var targetToken = !string.IsNullOrWhiteSpace(model.Token) ? model.Token : token;
        if (string.IsNullOrWhiteSpace(targetToken))
        {
            if (IsAjaxRequest())
            {
                return BadRequest(new { success = false, message = "Mã xác thực thư mời không hợp lệ." });
            }
            TempData["ErrorMessage"] = "Mã xác thực thư mời không hợp lệ.";
            return RedirectToAction(nameof(Index), new { token = "" });
        }

        model.Token = targetToken;

        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu phản hồi không hợp lệ.";
            if (IsAjaxRequest())
            {
                return BadRequest(new { success = false, message = firstError });
            }
            TempData["ErrorMessage"] = firstError;
            return RedirectToAction(nameof(Index), new { token = targetToken });
        }

        // Lấy thông tin offer để kiểm tra bảo mật
        var offerDetails = await _offerService.GetOfferDetailsByTokenAsync(targetToken, cancellationToken);
        if (offerDetails == null)
        {
            if (IsAjaxRequest())
            {
                return BadRequest(new { success = false, message = "Thư mời nhận việc không tồn tại hoặc mã token không hợp lệ." });
            }
            return View("InvalidToken", "Liên kết này không hợp lệ hoặc đã bị thay đổi.");
        }

        // Kiểm tra tài khoản nếu đã đăng nhập
        if (User.Identity?.IsAuthenticated == true)
        {
            var (isOwner, isStaff, userEmail, userId) = VerifyCandidateIdentity(offerDetails);
            if (isStaff)
            {
                if (IsAjaxRequest())
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Tài khoản nhân sự nội bộ không được phép gửi phản hồi thay cho ứng viên." });
                }
                TempData["ErrorMessage"] = "Tài khoản nhân sự nội bộ không được phép gửi phản hồi thay cho ứng viên.";
                return RedirectToAction(nameof(Index), new { token = targetToken });
            }
            else if (!isOwner)
            {
                if (IsAjaxRequest())
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Bạn không có quyền thực hiện thao tác trên thư mời của ứng viên khác." });
                }
                return ForbiddenView(offerDetails, userEmail, userId, Request.Path + Request.QueryString);
            }
        }

        var result = await _offerService.SubmitOfferResponseAsync(model, cancellationToken);

        if (IsAjaxRequest())
        {
            return Json(new
            {
                success = result.Success,
                message = result.Message,
                newStatus = result.NewStatus?.ToString(),
                respondedAt = result.RespondedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            });
        }

        if (result.Success)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Index), new { token = targetToken });
    }

    private (bool isOwner, bool isStaff, string userEmail, Guid userId) VerifyCandidateIdentity(CandidateOfferDetailsViewModel model)
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

    private IActionResult ForbiddenView(CandidateOfferDetailsViewModel model, string userEmail, Guid userId, string returnUrl)
    {
        var displayCandidateEmail = model.CandidateEmail ?? "Ứng viên";
        _logger.LogWarning("BẢO MẬT: Tài khoản {UserEmail} (ID: {UserId}) bị từ chối truy cập thư mời nhận việc của ứng viên {CandidateEmail} (OfferId: {OfferId}). IP: {Ip}",
            PiiMaskingHelper.MaskEmail(userEmail), userId, PiiMaskingHelper.MaskEmail(displayCandidateEmail), model.OfferId, HttpContext.Connection.RemoteIpAddress);

        var statusCodeFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>();
        if (statusCodeFeature != null)
        {
            statusCodeFeature.Enabled = false;
        }

        Response.StatusCode = StatusCodes.Status403Forbidden;
        ViewBag.LoggedInUserEmail = userEmail;
        ViewBag.CandidateMaskedEmail = PiiMaskingHelper.MaskEmail(displayCandidateEmail);
        ViewBag.ErrorMessage = $"Truy cập bị từ chối: Bạn đang đăng nhập bằng tài khoản ({userEmail}), không phải là chủ sở hữu nhận thư mời nhận việc này ({PiiMaskingHelper.MaskEmail(displayCandidateEmail)}).";
        ViewBag.ReturnUrl = returnUrl;
        return View("UnauthorizedOfferAccess");
    }

    private bool IsAjaxRequest()
    {
        return Request.Headers.XRequestedWith == "XMLHttpRequest" ||
               Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }
}
