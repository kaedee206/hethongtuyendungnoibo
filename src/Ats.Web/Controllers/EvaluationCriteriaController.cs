using Ats.Web.Models.ViewModels.EvaluationCriteria;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
[Route("evaluation-criteria")]
public class EvaluationCriteriaController(
    IEvaluationCriteriaService criteriaService,
    ILogger<EvaluationCriteriaController> logger) : Controller
{
    private readonly IEvaluationCriteriaService _criteriaService = criteriaService;
    private readonly ILogger<EvaluationCriteriaController> _logger = logger;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var model = await _criteriaService.GetAllCriteriaAsync(cancellationToken);
        return View(model);
    }

    [HttpGet("{id:guid}/rubric")]
    [HttpGet("api/{id:guid}/rubric")]
    [HttpGet("/api/evaluation-criteria/{id:guid}/rubric")]
    public async Task<IActionResult> GetRubric(Guid id, CancellationToken cancellationToken = default)
    {
        var rubric = await _criteriaService.GetRubricAsync(id, cancellationToken);
        if (rubric == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy tiêu chí đánh giá yêu cầu." });
        }

        return Ok(new
        {
            success = true,
            data = rubric
        });
    }

    [HttpGet("api/interview-sheet")]
    [HttpGet("/api/evaluation-criteria/interview-sheet")]
    public async Task<IActionResult> GetInterviewSheetApi(CancellationToken cancellationToken = default)
    {
        var sheet = await _criteriaService.GetInterviewSheetRubricsAsync(cancellationToken);
        return Ok(new
        {
            success = true,
            data = sheet
        });
    }

    [HttpPost("save-rubric")]
    [HttpPost("/api/evaluation-criteria/save-rubric")]
    public async Task<IActionResult> SaveRubric(
        [FromBody] CriteriaRubricSaveInputModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ.", errors });
        }

        var (success, message) = await _criteriaService.SaveRubricAsync(model, cancellationToken);
        if (!success)
        {
            return BadRequest(new { success = false, message });
        }

        return Ok(new { success = true, message });
    }
}
