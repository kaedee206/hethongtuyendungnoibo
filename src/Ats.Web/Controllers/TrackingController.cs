using Ats.Web.Models.ViewModels.Tracking;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ats.Web.Controllers;

/// <summary>
/// Cổng tra cứu trạng thái hồ sơ ứng viên (Scrum #35)
/// Cho phép ứng viên tự tra cứu tiến độ xét duyệt bằng Mã tra cứu và Email mà không cần gọi điện hỏi.
/// </summary>
[Route("tra-cuu")]
[Route("tra-cuu-ho-so")]
public class TrackingController(
    IApplicationTrackingService trackingService,
    ILogger<TrackingController> logger) : Controller
{
    private readonly IApplicationTrackingService _trackingService = trackingService;
    private readonly ILogger<TrackingController> _logger = logger;

    // GET: /tra-cuu or /tra-cuu-ho-so
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? code, 
        [FromQuery] string? email, 
        CancellationToken cancellationToken)
    {
        var model = new ApplicationTrackingIndexViewModel();

        // Hỗ trợ tự động tra cứu nếu có tham số từ email/link điều hướng
        if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(email))
        {
            model.Input.TrackingCode = code.Trim();
            model.Input.Email = email.Trim();
            model.Result = await _trackingService.TrackApplicationAsync(model.Input, cancellationToken);
            model.HasSearched = true;
        }

        return View(model);
    }

    // POST: /tra-cuu or /tra-cuu-ho-so
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> Index(
        [FromForm] ApplicationTrackingInputModel input, 
        CancellationToken cancellationToken)
    {
        var model = new ApplicationTrackingIndexViewModel
        {
            Input = input
        };

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            model.Result = await _trackingService.TrackApplicationAsync(input, cancellationToken);
            model.HasSearched = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xử lý tra cứu hồ sơ với mã {TrackingCode}", input.TrackingCode);
            model.Result = new ApplicationTrackingResultViewModel
            {
                IsFound = false,
                SearchTrackingCode = input.TrackingCode,
                SearchEmail = input.Email,
                ErrorMessage = "Hệ thống đang bảo trì tạm thời, vui lòng thử lại sau ít phút hoặc liên hệ phòng nhân sự NoveraTech."
            };
            model.HasSearched = true;
        }

        return View(model);
    }
}
