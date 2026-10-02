using System.Security.Claims;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

[Route("[controller]")]
public class JobsController : Controller
{
    private readonly IJobService _jobService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        IJobService jobService,
        ApplicationDbContext dbContext,
        IWebHostEnvironment env,
        ILogger<JobsController> logger)
    {
        _jobService = jobService;
        _dbContext = dbContext;
        _env = env;
        _logger = logger;
    }

    // GET: /Jobs or /jobs
    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(
        [FromQuery] string? search,
        [FromQuery] string? department,
        [FromQuery] string? location,
        [FromQuery] string? level)
    {
        var model = await _jobService.GetJobListAsync(search, department, location, level);
        return View(model);
    }

    // GET: /Jobs/Detail/{id} or /Jobs/{id}
    [HttpGet("Detail/{id}")]
    [HttpGet("{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return RedirectToAction(nameof(Index));
        }

        var model = await _jobService.GetJobDetailAsync(id);
        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy thông tin vị trí tuyển dụng yêu cầu hoặc vị trí đã hết hạn ứng tuyển.";
            return RedirectToAction(nameof(Index));
        }

        return View(model);
    }

    // POST: /Jobs/QuickApply (Xử lý ứng tuyển nhanh từ form ở trang chi tiết)
    [HttpPost("QuickApply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickApply(
        [FromForm] string jobId,
        [FromForm] string candidateName,
        [FromForm] string candidateEmail,
        [FromForm] string candidatePhone,
        [FromForm] string? coverLetter,
        IFormFile? cvFile)
    {
        var jobDetail = await _jobService.GetJobDetailAsync(jobId);
        var jobTitle = jobDetail?.Job.Title ?? "Vị trí tại NoveraTech";

        try
        {
            // 1. Tìm JobPosting trong DB
            JobPosting? jobPosting = null;
            if (Guid.TryParse(jobId, out var jobGuid))
            {
                jobPosting = await _dbContext.JobPostings.FirstOrDefaultAsync(j => j.Id == jobGuid);
            }
            if (jobPosting == null)
            {
                jobPosting = await _dbContext.JobPostings.FirstOrDefaultAsync(j => j.Slug == jobId)
                    ?? await _dbContext.JobPostings.FirstOrDefaultAsync();
            }

            // 2. Tìm hoặc tạo Candidate
            var emailClean = (candidateEmail ?? "").Trim().ToLower();
            var candidate = await _dbContext.Candidates
                .Include(c => c.Resumes)
                .FirstOrDefaultAsync(c => c.Email.ToLower() == emailClean);

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var userId);

            if (candidate == null)
            {
                candidate = new Candidate
                {
                    Id = Guid.NewGuid(),
                    UserId = userId != Guid.Empty ? userId : null,
                    FirstName = candidateName?.Trim() ?? "Ứng viên",
                    LastName = "",
                    Email = emailClean,
                    Phone = candidatePhone?.Trim(),
                    Source = CandidateSource.PORTAL,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.Candidates.AddAsync(candidate);
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(candidatePhone))
                {
                    candidate.Phone = candidatePhone.Trim();
                    candidate.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }

            // 3. Xử lý CV file
            Resume? resume = null;
            if (cvFile != null && cvFile.Length > 0)
            {
                var originalName = Path.GetFileName(cvFile.FileName);
                var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "resumes");
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }

                var uniqueFileName = $"{Guid.NewGuid()}_{originalName}";
                var filePath = Path.Combine(uploadsDir, uniqueFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await cvFile.CopyToAsync(stream);
                }

                foreach (var r in candidate.Resumes)
                {
                    r.IsPrimary = false;
                }

                resume = new Resume
                {
                    Id = Guid.NewGuid(),
                    CandidateId = candidate.Id,
                    FileName = originalName,
                    FilePath = $"/uploads/resumes/{uniqueFileName}",
                    FileSize = cvFile.Length,
                    MimeType = cvFile.ContentType,
                    IsPrimary = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.Resumes.AddAsync(resume);
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                resume = candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? candidate.Resumes.FirstOrDefault();
                if (resume == null)
                {
                    resume = new Resume
                    {
                        Id = Guid.NewGuid(),
                        CandidateId = candidate.Id,
                        FileName = $"CV_{candidate.FirstName.Replace(" ", "")}.pdf",
                        FilePath = "/uploads/resumes/default_cv.pdf",
                        FileSize = 1024 * 512,
                        MimeType = "application/pdf",
                        IsPrimary = true,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    await _dbContext.Resumes.AddAsync(resume);
                    await _dbContext.SaveChangesAsync();
                }
            }

            // 4. Tìm Stage 1: Ứng tuyển mới
            var stageNew = await _dbContext.PipelineStages.OrderBy(s => s.StageOrder).FirstOrDefaultAsync()
                ?? await _dbContext.PipelineStages.FirstAsync();

            if (jobPosting != null)
            {
                var existingApp = await _dbContext.Applications
                    .FirstOrDefaultAsync(a => a.CandidateId == candidate.Id && a.JobPostingId == jobPosting.Id && !a.IsDeleted);

                if (existingApp == null)
                {
                    var application = new Application
                    {
                        Id = Guid.NewGuid(),
                        JobPostingId = jobPosting.Id,
                        CandidateId = candidate.Id,
                        ResumeId = resume.Id,
                        CurrentStageId = stageNew.Id,
                        Status = ApplicationStatus.IN_PROCESS,
                        AppliedAt = DateTimeOffset.UtcNow,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    await _dbContext.Applications.AddAsync(application);

                    var effectiveUserId = userId != Guid.Empty ? userId : (await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync());
                    await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
                    {
                        Id = Guid.NewGuid(),
                        ApplicationId = application.Id,
                        FromStageId = null,
                        ToStageId = stageNew.Id,
                        ChangedByUserId = effectiveUserId,
                        Comment = string.IsNullOrWhiteSpace(coverLetter) ? "Ứng viên nộp hồ sơ qua trang chi tiết việc làm." : $"Cover letter: {coverLetter}",
                        CreatedAt = DateTimeOffset.UtcNow
                    });

                    await _dbContext.SaveChangesAsync();
                }
            }

            // Ghi log ứng tuyển
            _logger.LogInformation("Ứng viên {Name} ({Email}, {Phone}) đã được lưu hồ sơ ứng tuyển vị trí {JobTitle} (ID: {JobId}) vào CSDL.",
                candidateName, candidateEmail, candidatePhone, jobTitle, jobId);

            TempData["SuccessMessage"] = $"🎉 Chúc mừng bạn {candidateName}! Hồ sơ ứng tuyển vị trí '{jobTitle}' đã được lưu thành công vào hệ thống tuyển dụng NoveraTech. Chúng tôi sẽ phản hồi lại cho bạn trong vòng 48 giờ làm việc.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lưu đơn ứng tuyển");
            TempData["ErrorMessage"] = "Có lỗi xảy ra khi nộp hồ sơ, vui lòng thử lại.";
        }

        return RedirectToAction(nameof(Detail), new { id = jobId });
    }
}
