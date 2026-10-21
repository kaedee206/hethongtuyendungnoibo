using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller điều phối các báo cáo phân tích tuyển dụng đa chiều (SP-8).
/// Phân quyền: Chỉ cho phép HR Manager, Recruiter và Admin.
/// </summary>
[Authorize(Roles = $"{UserRoles.Admin},{UserRoles.HRManager},{UserRoles.Recruiter},Quản trị viên,Trưởng phòng nhân sự,Chuyên viên tuyển dụng,Quản trị viên hệ thống")]
public class ReportsController(
    ICandidateSourceReportService reportService,
    ApplicationDbContext dbContext,
    ILogger<ReportsController> logger) : Controller
{
    private readonly ICandidateSourceReportService _reportService = reportService;
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<ReportsController> _logger = logger;

    /// <summary>
    /// Giao diện Báo cáo Hiệu quả Nguồn ứng viên & Đánh giá Nguồn chất lượng cao (SP-8).
    /// Hỗ trợ cả 2 định dạng route: /reports/candidate-sources và /bao-cao/nguon-ung-vien
    /// </summary>
    [HttpGet("/reports/candidate-sources")]
    [HttpGet("/bao-cao/nguon-ung-vien")]
    public async Task<IActionResult> CandidateSources([FromQuery] CandidateSourceFilterDto filter, CancellationToken cancellationToken)
    {
        filter ??= new CandidateSourceFilterDto();

        // Mặc định nếu chưa chọn thời gian: Lọc 90 ngày gần nhất để có dữ liệu tổng quan
        if (!filter.FromDate.HasValue && !filter.ToDate.HasValue)
        {
            filter.FromDate = DateTime.Today.AddDays(-90);
            filter.ToDate = DateTime.Today;
        }

        var reportSummary = await _reportService.GetSourceEffectivenessReportAsync(filter, cancellationToken);

        // Nạp danh sách vị trí để hiển thị bộ lọc Dropdown
        await PopulateFilterDropdownsAsync(cancellationToken);

        ViewData["Title"] = "Báo cáo Hiệu quả Nguồn Ứng viên";
        return View(reportSummary);
    }

    /// <summary>
    /// API trả về dữ liệu JSON báo cáo nguồn ứng viên phục vụ AJAX / Biểu đồ trực quan (SP-8).
    /// </summary>
    [HttpGet("/api/reports/candidate-sources/data")]
    [Produces("application/json")]
    public async Task<IActionResult> GetCandidateSourcesData([FromQuery] CandidateSourceFilterDto filter, CancellationToken cancellationToken)
    {
        try
        {
            var reportSummary = await _reportService.GetSourceEffectivenessReportAsync(filter, cancellationToken);
            return Ok(new
            {
                success = true,
                data = reportSummary
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy dữ liệu báo cáo nguồn ứng viên");
            return StatusCode(500, new
            {
                success = false,
                message = "Đã xảy ra lỗi khi tạo dữ liệu báo cáo. Vui lòng thử lại sau."
            });
        }
    }

    /// <summary>
    /// Xuất file Excel (.xlsx) báo cáo nguồn ứng viên kèm định dạng chuyên nghiệp (SP-8).
    /// </summary>
    [HttpGet("/reports/candidate-sources/export")]
    [HttpGet("/bao-cao/nguon-ung-vien/xuat-excel")]
    public async Task<IActionResult> ExportCandidateSources([FromQuery] CandidateSourceFilterDto filter, CancellationToken cancellationToken)
    {
        try
        {
            var fileBytes = await _reportService.ExportSourceEffectivenessToExcelAsync(filter, cancellationToken);
            var fileName = $"Bao_Cao_Nguon_Ung_Vien_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            const string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return File(fileBytes, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xuất Excel báo cáo nguồn ứng viên");
            TempData["ErrorMessage"] = "Không thể xuất file Excel. Vui lòng kiểm tra lại bộ lọc và thử lại.";
            return RedirectToAction(nameof(CandidateSources), filter);
        }
    }

    /// <summary>
    /// Nạp dữ liệu các danh mục phục vụ bộ lọc tìm kiếm
    /// </summary>
    private async Task PopulateFilterDropdownsAsync(CancellationToken cancellationToken)
    {
        ViewBag.Positions = await _dbContext.JobPositions
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Title)
            .Select(p => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = p.Id.ToString(),
                Text = $"{p.Title} ({p.Code})"
            })
            .ToListAsync(cancellationToken);

        ViewBag.Requisitions = await _dbContext.JobRequisitions
            .AsNoTracking()
            .Where(r => !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = r.Id.ToString(),
                Text = $"{r.Code} - {(r.JobPosition != null ? r.JobPosition.Title : "Yêu cầu tuyển dụng")}"
            })
            .ToListAsync(cancellationToken);
    }
}
