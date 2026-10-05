using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

[Authorize(Roles = $"{UserRoles.Admin},{UserRoles.HRManager}")]
[Route("company-profile")]
[Route("thong-tin-cong-ty")]
public class CompanyProfileController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CompanyProfileController> _logger;

    public CompanyProfileController(ApplicationDbContext dbContext, ILogger<CompanyProfileController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet("")]
    [HttpGet("edit")]
    [HttpGet("chinh-sua")]
    public async Task<IActionResult> Index()
    {
        var profile = await _dbContext.CompanyProfiles.FirstOrDefaultAsync();
        if (profile == null)
        {
            profile = new CompanyProfile
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.CompanyProfiles.Add(profile);
            await _dbContext.SaveChangesAsync();
        }

        return View(profile);
    }

    [HttpPost("")]
    [HttpPost("edit")]
    [HttpPost("chinh-sua")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(CompanyProfile model)
    {
        if (string.IsNullOrWhiteSpace(model.CompanyName) || string.IsNullOrWhiteSpace(model.Headline))
        {
            TempData["ErrorMessage"] = "Tên công ty và tiêu đề giới thiệu là bắt buộc.";
            return View("Index", model);
        }

        var profile = await _dbContext.CompanyProfiles.FirstOrDefaultAsync();
        if (profile == null)
        {
            profile = new CompanyProfile { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
            _dbContext.CompanyProfiles.Add(profile);
        }

        profile.CompanyName = model.CompanyName.Trim();
        profile.Headline = model.Headline.Trim();
        profile.AboutText = model.AboutText?.Trim() ?? string.Empty;
        profile.EngineeringCulture = model.EngineeringCulture?.Trim() ?? string.Empty;
        profile.TechStackJson = model.TechStackJson?.Trim() ?? "[]";
        profile.ProofMetricsJson = model.ProofMetricsJson?.Trim() ?? "[]";
        profile.PerksJson = model.PerksJson?.Trim() ?? "[]";
        profile.HeadquartersAddress = model.HeadquartersAddress?.Trim() ?? string.Empty;
        profile.ContactEmail = model.ContactEmail?.Trim() ?? string.Empty;
        profile.PhoneContact = model.PhoneContact?.Trim() ?? string.Empty;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync();

        TempData["SuccessMessage"] = "Cập nhật hồ sơ công ty và trang giới thiệu thành công!";
        return RedirectToAction(nameof(Index));
    }
}
