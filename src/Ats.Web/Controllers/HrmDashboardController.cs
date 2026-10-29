using Ats.Web.Constants;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[ApiController]
[Route("api/hrm/dashboard")]
[Authorize(Roles = $"{UserRoles.Admin},{UserRoles.HRManager},Quản trị viên,Quản trị viên hệ thống,Trưởng phòng nhân sự")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class HrmDashboardController(
    IHrmRecruitmentDashboardService dashboardService,
    ILogger<HrmDashboardController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await dashboardService.GetDashboardAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to load the HR recruitment dashboard.");
            return Problem(
                title: "Không thể tải dữ liệu tổng quan tuyển dụng.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
