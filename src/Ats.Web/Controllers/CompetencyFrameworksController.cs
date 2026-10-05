using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
[Route("competency-frameworks")]
public class CompetencyFrameworksController(
    ICompetencyFrameworkService frameworkService,
    ILogger<CompetencyFrameworksController> logger) : Controller
{
    private readonly ICompetencyFrameworkService _frameworkService = frameworkService;
    private readonly ILogger<CompetencyFrameworksController> _logger = logger;

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? keyword,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15,
        CancellationToken cancellationToken = default)
    {
        var model = await _frameworkService.GetFrameworksAsync(keyword, status, page, pageSize, cancellationToken);
        return View(model);
    }

    [HttpGet("{id:guid}/positions")]
    public async Task<IActionResult> GetPositions(Guid id, CancellationToken cancellationToken = default)
    {
        var positions = await _frameworkService.GetAssociatedPositionsAsync(id, cancellationToken);
        return Json(positions);
    }

    [HttpGet("details/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken = default)
    {
        var framework = await _frameworkService.GetFrameworkByIdAsync(id, cancellationToken);
        if (framework == null)
        {
            return NotFound(new { message = "Không tìm thấy khung năng lực yêu cầu." });
        }
        return Json(framework);
    }
}
