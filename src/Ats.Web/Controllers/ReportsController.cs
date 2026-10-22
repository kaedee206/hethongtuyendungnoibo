using Ats.Web.Constants;
using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller điều phối các báo cáo phân tích tuyển dụng cho cấp quản lý (SCRUM-352).
/// Thiết kế tuân thủ Clean Controller, C# 12 Primary Constructor và Kebab-case routing.
/// </summary>
[Authorize(Roles = $"{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.Recruiter},{UserRoles.HiringManager}")]
[Route("bao-cao")]
[Route("reports")]
public class ReportsController(
    IRecruitmentReportService reportService,
    ILogger<ReportsController> logger) : Controller
{
    private readonly IRecruitmentReportService _reportService = reportService;
    private readonly ILogger<ReportsController> _logger = logger;

    /// <summary>
    /// Giao diện báo cáo Phễu tuyển dụng & Tỷ lệ chuyển đổi giai đoạn (SCRUM-352).
    /// </summary>
    [HttpGet("pheu-tuyen-dung")]
    [HttpGet("recruitment-funnel")]
    public async Task<IActionResult> RecruitmentFunnel(
        [FromQuery] FunnelFilterCriteriaDto criteria,
        CancellationToken cancellationToken)
    {
        var model = await _reportService.GetRecruitmentFunnelReportAsync(criteria, cancellationToken);
        return View(model);
    }

    /// <summary>
    /// API trả về dữ liệu JSON phễu tuyển dụng phục vụ biểu đồ động hoặc xuất dữ liệu.
    /// </summary>
    [HttpGet("api/pheu-tuyen-dung")]
    [HttpGet("api/recruitment-funnel")]
    public async Task<IActionResult> GetRecruitmentFunnelApi(
        [FromQuery] FunnelFilterCriteriaDto criteria,
        CancellationToken cancellationToken)
    {
        try
        {
            var data = await _reportService.GetRecruitmentFunnelReportAsync(criteria, cancellationToken);
            return Json(new { success = true, data });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy dữ liệu báo cáo phễu tuyển dụng");
            return Json(new { success = false, message = "Không thể kết xuất dữ liệu báo cáo phễu tuyển dụng." });
        }
    }
}
