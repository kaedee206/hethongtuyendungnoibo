using Ats.Web.Models.DTOs;
using Ats.Web.Models.DTOs.ApprovalRule;
using Ats.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Ats.Web.Controllers;

[ApiController]
[Route("api/approval-rules")]
[Authorize] // Should ideally be [Authorize(Roles = "HR Manager,Admin")] depending on the exact RBAC
public class ApprovalRuleController : Controller
{
    private readonly IApprovalRuleService _approvalRuleService;
    private readonly Ats.Web.Data.ApplicationDbContext _dbContext;

    public ApprovalRuleController(IApprovalRuleService approvalRuleService, Ats.Web.Data.ApplicationDbContext dbContext)
    {
        _approvalRuleService = approvalRuleService;
        _dbContext = dbContext;
    }

    [HttpGet("/approval-rules")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Cấu hình luồng phê duyệt";
        
        var departments = await _dbContext.Departments
            .OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Name })
            .ToListAsync();
            
        var roles = await _dbContext.Roles
            .OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Name })
            .ToListAsync();
            
        var users = await _dbContext.Users
            .Where(u => u.Status == "ACTIVE")
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Email })
            .ToListAsync();

        ViewBag.Departments = departments;
        ViewBag.Roles = roles;
        ViewBag.Users = users;

        return View();
    }


    [HttpGet]
    public async Task<IActionResult> GetAllRules([FromQuery] Guid? departmentId, CancellationToken cancellationToken)
    {
        var result = await _approvalRuleService.GetAllRulesAsync(departmentId, cancellationToken);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetRuleById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _approvalRuleService.GetRuleByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result) : NotFound(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateRule([FromBody] CreateApprovalRuleDto request, CancellationToken cancellationToken)
    {
        var currentUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());
        var result = await _approvalRuleService.CreateRuleAsync(request, currentUserId, cancellationToken);
        return result.IsSuccess ? CreatedAtAction(nameof(GetRuleById), new { id = result.Data?.Id }, result) : BadRequest(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateRule(Guid id, [FromBody] UpdateApprovalRuleDto request, CancellationToken cancellationToken)
    {
        var currentUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());
        var result = await _approvalRuleService.UpdateRuleAsync(id, request, currentUserId, cancellationToken);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken cancellationToken)
    {
        var result = await _approvalRuleService.DeleteRuleAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpGet("applicable")]
    public async Task<IActionResult> GetApplicableSteps([FromQuery] Guid? departmentId, [FromQuery] decimal proposedSalary, CancellationToken cancellationToken)
    {
        var result = await _approvalRuleService.GetApplicableStepsAsync(departmentId, proposedSalary, cancellationToken);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }
}
