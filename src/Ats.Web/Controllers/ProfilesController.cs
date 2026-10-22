using System.Security.Claims;
using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class ProfilesController(
    IProfileService profileService,
    INotificationPreferenceService preferenceService,
    IWebHostEnvironment environment) : Controller
{
    private readonly IProfileService _profileService = profileService;
    private readonly INotificationPreferenceService _preferenceService = preferenceService;
    private readonly IWebHostEnvironment _environment = environment;

    private Guid GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(idClaim, out var id) && id != Guid.Empty)
            return id;

        var sessionUserId = HttpContext.Session.GetString("UserId");
        if (Guid.TryParse(sessionUserId, out var parsedSessionId) && parsedSessionId != Guid.Empty)
            return parsedSessionId;

        return Guid.Empty;
    }

    [HttpGet("/ho-so")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return RedirectToAction("StaffLogin", "Account");
        }

        var profile = await _profileService.GetProfileAsync(userId, cancellationToken);
        if (profile == null)
        {
            return NotFound("Không tìm thấy thông tin hồ sơ.");
        }

        // SCRUM-349: Nạp tùy chọn thông báo để hiển thị trực tiếp trong Hồ sơ cá nhân
        ViewBag.NotificationPreferences = await _preferenceService.GetUserPreferencesAsync(userId, cancellationToken);

        return View(profile);
    }

    [HttpGet("/ho-so/chinh-sua")]
    public async Task<IActionResult> Edit(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return RedirectToAction("StaffLogin", "Account");
        }

        var profile = await _profileService.GetProfileAsync(userId, cancellationToken);
        if (profile == null)
        {
            return NotFound("Không tìm thấy thông tin hồ sơ.");
        }

        return View(profile);
    }

    [HttpPost("/ho-so/chinh-sua")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost([FromBody] UpdateProfileRequestDto request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { isSuccess = false, message = "Phiên làm việc đã hết hạn. Vui lòng đăng nhập lại." });
        }

        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu không hợp lệ.";
            return BadRequest(new { isSuccess = false, message = firstError });
        }

        var (success, message) = await _profileService.UpdateProfileAsync(userId, request, cancellationToken);
        if (!success)
        {
            return BadRequest(new { isSuccess = false, message });
        }

        return Ok(new { isSuccess = true, message });
    }

    [HttpPost("/ho-so/avatar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAvatar(IFormFile avatarFile, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { isSuccess = false, message = "Phiên làm việc đã hết hạn. Vui lòng đăng nhập lại." });
        }

        if (avatarFile == null || avatarFile.Length == 0)
        {
            return BadRequest(new { isSuccess = false, message = "Vui lòng chọn file hình ảnh đại diện." });
        }

        var (success, message, avatarUrl) = await _profileService.UploadAvatarAsync(userId, avatarFile, _environment.WebRootPath, cancellationToken);
        if (!success)
        {
            return BadRequest(new { isSuccess = false, message });
        }

        return Ok(new { isSuccess = true, message, avatarUrl });
    }

    // ==========================================
    // SCRUM-349: CẤU HÌNH THÔNG BÁO CÁ NHÂN (NOTIFICATION PREFERENCES)
    // ==========================================

    /// <summary>
    /// Giao diện chuyên biệt quản lý tùy chọn nhận thông báo cá nhân cho nhân sự (SCRUM-349).
    /// </summary>
    [HttpGet("/ho-so/thong-bao")]
    public async Task<IActionResult> NotificationPreferences(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return RedirectToAction("StaffLogin", "Account");
        }

        var profile = await _profileService.GetProfileAsync(userId, cancellationToken);
        var preferences = await _preferenceService.GetUserPreferencesAsync(userId, cancellationToken);

        ViewBag.UserProfile = profile;
        return View(preferences);
    }

    /// <summary>
    /// API trả về danh sách tùy chọn thông báo hiện tại của người dùng.
    /// </summary>
    [HttpGet("/ho-so/api/thong-bao")]
    public async Task<IActionResult> GetNotificationPreferencesApi(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { success = false, message = "Phiên làm việc đã hết hạn." });
        }

        var preferences = await _preferenceService.GetUserPreferencesAsync(userId, cancellationToken);
        return Json(new NotificationPreferenceResponseDto
        {
            Success = true,
            Message = "Thành công",
            Preferences = preferences
        });
    }

    /// <summary>
    /// API cập nhật trạng thái bật/tắt của một kênh thông báo đơn lẻ qua AJAX tức thì.
    /// </summary>
    [HttpPost("/ho-so/api/thong-bao/cap-nhat-kenh")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSinglePreferenceApi(
        [FromBody] UpdateSinglePreferenceRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { success = false, message = "Phiên làm việc đã hết hạn." });
        }

        var (success, message) = await _preferenceService.UpdateSinglePreferenceAsync(userId, request, cancellationToken);
        if (!success)
        {
            return BadRequest(new { success = false, message });
        }

        return Json(new { success = true, message });
    }

    /// <summary>
    /// API lưu toàn bộ danh sách tùy chọn thông báo hàng loạt.
    /// </summary>
    [HttpPost("/ho-so/api/thong-bao/luu-tat-ca")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAllPreferencesApi(
        [FromBody] BatchUpdatePreferencesRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { success = false, message = "Phiên làm việc đã hết hạn." });
        }

        var (success, message) = await _preferenceService.UpdateBatchPreferencesAsync(userId, request, cancellationToken);
        if (!success)
        {
            return BadRequest(new { success = false, message });
        }

        return Json(new { success = true, message });
    }
}
