using Ats.Web.Constants;
using Ats.Web.Models.DTOs.Sla;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller quản lý cấu hình và kiểm tra quá hạn SLA giai đoạn tuyển dụng (SCRUM-327).
/// </summary>
[Authorize(Roles = $"{UserRoles.Admin},{UserRoles.HRManager},{UserRoles.Recruiter}")]
public class SlaController : Controller
{
    private readonly ISlaService _slaService;
    private readonly ILogger<SlaController> _logger;

    public SlaController(ISlaService slaService, ILogger<SlaController> logger)
    {
        _slaService = slaService;
        _logger = logger;
    }

    /// <summary>
    /// Giao diện Cấu hình Ngưỡng SLA Tuyển dụng (GET /cai-dat-sla).
    /// </summary>
    [HttpGet("/cai-dat-sla")]
    [HttpGet("/sla-settings")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Cấu hình Ngưỡng SLA Tuyển dụng";
        var configs = await _slaService.GetStageSlaConfigsAsync(cancellationToken);
        return View(configs);
    }

    /// <summary>
    /// API lấy danh sách cấu hình SLA của các giai đoạn (GET /api/sla/stages).
    /// </summary>
    [HttpGet("/api/sla/stages")]
    [Produces("application/json")]
    public async Task<IActionResult> GetStageConfigs(CancellationToken cancellationToken)
    {
        var configs = await _slaService.GetStageSlaConfigsAsync(cancellationToken);
        return Ok(new { success = true, data = configs });
    }

    /// <summary>
    /// API cập nhật cấu hình SLA cho các giai đoạn (PUT /api/sla/stages).
    /// </summary>
    [HttpPut("/api/sla/stages")]
    [ValidateAntiForgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> UpdateStageConfigs([FromBody] List<UpdateStageSlaDto> configs, CancellationToken cancellationToken)
    {
        if (configs == null || configs.Count == 0)
        {
            return BadRequest(new { success = false, message = "Danh sách cấu hình không được để trống." });
        }

        var result = await _slaService.UpdateStageSlaConfigsAsync(configs, cancellationToken);
        if (!result)
        {
            return BadRequest(new { success = false, message = "Không thể cập nhật cấu hình SLA." });
        }

        return Ok(new { success = true, message = "Đã lưu cấu hình ngưỡng SLA thành công." });
    }

    /// <summary>
    /// API kích hoạt tác vụ quét kiểm tra quá hạn SLA thủ công ngay lập tức (POST /api/sla/scan-now).
    /// </summary>
    [HttpPost("/api/sla/scan-now")]
    [ValidateAntiForgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> TriggerScanNow(CancellationToken cancellationToken)
    {
        var scanResult = await _slaService.ScanAndAlertOverdueApplicationsAsync(cancellationToken);
        return Ok(new
        {
            success = true,
            message = $"Đã hoàn tất quét SLA: Phát hiện {scanResult.OverdueCount} hồ sơ quá hạn, tạo {scanResult.NotificationsCreated} thông báo nhắc việc.",
            data = scanResult
        });
    }
}
