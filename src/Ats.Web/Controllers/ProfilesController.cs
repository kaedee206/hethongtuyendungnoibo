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
    IWebHostEnvironment environment) : Controller
{
    private readonly IProfileService _profileService = profileService;
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
}
