using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Jobs;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
        [FromQuery] string? level,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 9)
    {
        var model = await _jobService.GetJobListAsync(search, department, location, level, page, pageSize);
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

    // GET: /Jobs/Create
    [HttpGet("Create")]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.HiringManager}")]
    public IActionResult Create()
    {
        var model = new CreateJobViewModel();
        return View(model);
    }

    // POST: /Jobs/Create
    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.HiringManager}")]
    public async Task<IActionResult> Create([FromForm] CreateJobViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            ModelState.AddModelError(nameof(model.Title), "Vui lòng nhập tiêu đề vị trí tuyển dụng.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            // 1. Tìm hoặc tạo Department
            var deptName = string.IsNullOrWhiteSpace(model.DepartmentName) ? "Công nghệ Thông tin (IT)" : model.DepartmentName.Trim();
            var dept = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Name.ToLower() == deptName.ToLower())
                ?? await _dbContext.Departments.FirstOrDefaultAsync();
            if (dept == null)
            {
                dept = new Department
                {
                    Id = Guid.NewGuid(),
                    Name = deptName,
                    Code = deptName.Length >= 3 ? deptName.Substring(0, 3).ToUpper() : "DEP",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.Departments.AddAsync(dept);
                await _dbContext.SaveChangesAsync();
            }

            // 2. Tìm hoặc tạo JobPosition
            var jobPos = await _dbContext.JobPositions.FirstOrDefaultAsync(p => p.Title.ToLower() == model.Title.Trim().ToLower() && p.DepartmentId == dept.Id);
            if (jobPos == null)
            {
                jobPos = new JobPosition
                {
                    Id = Guid.NewGuid(),
                    DepartmentId = dept.Id,
                    Title = model.Title.Trim(),
                    Code = "POS-" + Guid.NewGuid().ToString("N")[..6].ToUpper(),
                    JobLevel = model.ExperienceLevel,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.JobPositions.AddAsync(jobPos);
                await _dbContext.SaveChangesAsync();
            }

            // 3. Tạo JobRequisition
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var currentUserId);
            if (currentUserId == Guid.Empty)
            {
                currentUserId = await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync();
            }

            var req = new JobRequisition
            {
                Id = Guid.NewGuid(),
                DepartmentId = dept.Id,
                JobPositionId = jobPos.Id,
                Code = "REQ-" + DateTime.UtcNow.ToString("yyMM") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpper(),
                Quantity = model.Quantity > 0 ? model.Quantity : 1,
                HeadcountType = HeadcountType.NEW_HEADCOUNT,
                Status = RequisitionStatus.APPROVED,
                HiringManagerId = currentUserId,
                TargetHireDate = DateOnly.FromDateTime(model.ExpiredDate),
                Reason = "Mở rộng quy mô và phát triển sản phẩm công nghệ NoveraTech",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.JobRequisitions.AddAsync(req);

            // 4. Định dạng Requirements kèm [TECHSTACK] và [RESPONSIBILITIES]
            var techTags = string.IsNullOrWhiteSpace(model.TechStack) ? "" : $"[TECHSTACK]{model.TechStack.Trim()}[/TECHSTACK]\n";
            var respBlock = string.IsNullOrWhiteSpace(model.Responsibilities) ? "" : $"[RESPONSIBILITIES]{model.Responsibilities.Trim()}[/RESPONSIBILITIES]\n";
            var combinedReqs = $"{techTags}{respBlock}{model.Requirements ?? ""}".Trim();

            // 5. Sinh Slug thân thiện
            var cleanTitle = System.Text.RegularExpressions.Regex.Replace(model.Title.ToLower(), @"[^a-z0-9\s-]", "");
            var baseSlug = cleanTitle.Trim().Replace(" ", "-");
            if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = "job";
            var slug = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..6]}";

            // 6. Tạo JobPosting với Status PUBLISHED
            var posting = new JobPosting
            {
                Id = Guid.NewGuid(),
                RequisitionId = req.Id,
                Title = model.Title.Trim(),
                Slug = slug,
                WorkLocation = string.IsNullOrWhiteSpace(model.WorkLocation) ? "Hà Nội (Hybrid 2 ngày WFH)" : model.WorkLocation.Trim(),
                EmploymentType = EmploymentType.FULL_TIME,
                SalaryDisplay = string.IsNullOrWhiteSpace(model.SalaryDisplay) ? "Thương lượng theo năng lực" : model.SalaryDisplay.Trim(),
                JobDescription = model.Overview?.Trim(),
                Requirements = combinedReqs,
                Benefits = model.Benefits?.Trim(),
                PublishedAt = DateTimeOffset.UtcNow,
                ExpiredAt = new DateTimeOffset(model.ExpiredDate, TimeSpan.Zero),
                Status = JobPostingStatus.PUBLISHED,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.JobPostings.AddAsync(posting);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Đã xuất bản tin tuyển dụng mới: {Title} (Slug: {Slug}) bởi User {UserId}",
                posting.Title, posting.Slug, currentUserId);

            TempData["SuccessMessage"] = $"🎉 Vị trí tuyển dụng '{model.Title}' đã được xuất bản trực tiếp lên hệ thống thành công!";
            return RedirectToAction(nameof(Detail), new { id = posting.Slug });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tạo tin tuyển dụng mới");
            ModelState.AddModelError("", "Đã có lỗi xảy ra khi tạo tin tuyển dụng: " + ex.Message);
            return View(model);
        }
    }
}

