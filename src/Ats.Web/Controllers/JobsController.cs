using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Jobs;
using Ats.Web.Services.Interfaces;
using Ats.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

[Route("[controller]")]
public class JobsController : Controller
{
    private readonly IJobService _jobService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _env;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<JobsController> _logger;
    private readonly ISecurityAuditService? _auditService;

    public JobsController(
        IJobService jobService,
        ApplicationDbContext dbContext,
        IWebHostEnvironment env,
        IFileStorageService fileStorageService,
        ILogger<JobsController> logger,
        ISecurityAuditService? auditService = null)
    {
        _jobService = jobService;
        _dbContext = dbContext;
        _env = env;
        _fileStorageService = fileStorageService;
        _logger = logger;
        _auditService = auditService;
    }

    // GET: /Jobs or /jobs
    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(
        [FromQuery] string? search,
        [FromQuery] string? department,
        [FromQuery] string? location,
        [FromQuery] string? employmentType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 9)
    {
        var model = await _jobService.GetJobListAsync(search, department, location, employmentType, page, pageSize);
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
    [EnableRateLimiting("ApplyRateLimit")]
    public async Task<IActionResult> QuickApply(
        [FromForm] string jobId,
        [FromForm] string candidateName,
        [FromForm] string candidateEmail,
        [FromForm] string candidatePhone,
        [FromForm] string? coverLetter,
        [FromForm] bool agreeToPrivacyConsent,
        IFormFile? cvFile)
    {
        var clientIp = ClientIpHelper.GetClientIp(HttpContext);

        if (!agreeToPrivacyConsent)
        {
            _auditService?.LogSecurityEvent(new SecurityAuditEvent
            {
                EventType = SecurityAuditEventType.PrivacyConsentRejected,
                ClientIp = clientIp,
                UserNameOrEmail = candidateEmail,
                ResourcePath = $"/Jobs/Detail/{jobId}",
                Action = "APPLY_JOB_REJECTED_CONSENT",
                IsSuccess = false,
                Details = "Từ chối nộp hồ sơ do chưa đồng ý điều khoản dữ liệu cá nhân theo Nghị định 13/2023/NĐ-CP"
            });

            TempData["ErrorMessage"] = "Bạn cần đồng ý với điều khoản thu thập và xử lý dữ liệu cá nhân theo Nghị định 13/2023/NĐ-CP để hoàn tất nộp hồ sơ.";
            return RedirectToAction(nameof(Detail), new { id = jobId });
        }

        _auditService?.LogSecurityEvent(new SecurityAuditEvent
        {
            EventType = SecurityAuditEventType.PrivacyConsentAccepted,
            ClientIp = clientIp,
            UserNameOrEmail = candidateEmail,
            ResourcePath = $"/Jobs/Detail/{jobId}",
            Action = "APPLY_JOB_CONSENT_RECORDED",
            IsSuccess = true,
            Details = "Đã xác nhận sự đồng ý thu thập và xử lý dữ liệu cá nhân theo Nghị định 13/2023/NĐ-CP"
        });

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
                var savedFilePath = await _fileStorageService.SaveCvAsync(cvFile, $"cv_{candidate.Id:N}");

                foreach (var r in candidate.Resumes)
                {
                    r.IsPrimary = false;
                }

                resume = new Resume
                {
                    Id = Guid.NewGuid(),
                    CandidateId = candidate.Id,
                    FileName = originalName,
                    FilePath = savedFilePath,
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
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin}")]
    public IActionResult Create()
    {
        var model = new CreateJobViewModel();
        return View(model);
    }

    // POST: /Jobs/Create
    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin}")]
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

            var descContent = !string.IsNullOrWhiteSpace(model.PositionDescription) 
                ? model.PositionDescription.Trim() 
                : (!string.IsNullOrWhiteSpace(model.Overview) ? model.Overview.Trim() : "Chịu trách nhiệm thực hiện các mục tiêu công nghệ và sản phẩm trọng điểm của công ty.");

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
                    Description = descContent,
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
                JobDescription = descContent,
                Requirements = model.Requirements?.Trim(),
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
                JobDescription = model.Overview?.Trim() ?? descContent,
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

    // POST: /Jobs/UpdatePosting
    [HttpPost("UpdatePosting")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin}")]
    public async Task<IActionResult> UpdatePosting([FromForm] UpdateJobPostingRequest model)
    {
        if (string.IsNullOrWhiteSpace(model.JobId) || string.IsNullOrWhiteSpace(model.Title))
        {
            return Json(new { success = false, message = "Vui lòng nhập đầy đủ tiêu đề và thông tin bài đăng." });
        }

        try
        {
            JobPosting? posting = null;
            if (Guid.TryParse(model.JobId, out var jobGuid))
            {
                posting = await _dbContext.JobPostings
                    .Include(j => j.Requisition)
                        .ThenInclude(r => r.Department)
                    .Include(j => j.Requisition)
                        .ThenInclude(r => r.JobPosition)
                    .FirstOrDefaultAsync(j => j.Id == jobGuid);
            }

            if (posting == null)
            {
                posting = await _dbContext.JobPostings
                    .Include(j => j.Requisition)
                        .ThenInclude(r => r.Department)
                    .Include(j => j.Requisition)
                        .ThenInclude(r => r.JobPosition)
                    .FirstOrDefaultAsync(j => j.Slug == model.JobId);
            }

            var techTags = string.IsNullOrWhiteSpace(model.TechStack) ? "" : $"[TECHSTACK]{model.TechStack.Trim()}[/TECHSTACK]\n";
            var respBlock = string.IsNullOrWhiteSpace(model.Responsibilities) ? "" : $"[RESPONSIBILITIES]{model.Responsibilities.Trim()}[/RESPONSIBILITIES]\n";
            var combinedReqs = $"{techTags}{respBlock}{model.Requirements ?? ""}".Trim();

            if (posting != null)
            {
                posting.Title = model.Title.Trim();
                posting.SalaryDisplay = string.IsNullOrWhiteSpace(model.SalaryDisplay) ? "Thương lượng theo năng lực" : model.SalaryDisplay.Trim();
                posting.WorkLocation = string.IsNullOrWhiteSpace(model.WorkLocation) ? "Hà Nội (Hybrid 2 ngày WFH)" : model.WorkLocation.Trim();
                posting.JobDescription = model.Overview?.Trim();
                posting.Requirements = combinedReqs;
                posting.Benefits = model.Benefits?.Trim();
                posting.ExpiredAt = new DateTimeOffset(model.Deadline, TimeSpan.Zero);
                posting.UpdatedAt = DateTimeOffset.UtcNow;

                if (posting.Requisition != null)
                {
                    posting.Requisition.JobDescription = !string.IsNullOrWhiteSpace(model.PositionDescription) ? model.PositionDescription.Trim() : model.Overview?.Trim();
                    posting.Requisition.Requirements = model.Requirements?.Trim();
                    posting.Requisition.TargetHireDate = DateOnly.FromDateTime(model.Deadline);
                    posting.Requisition.UpdatedAt = DateTimeOffset.UtcNow;

                    if (posting.Requisition.JobPosition != null)
                    {
                        posting.Requisition.JobPosition.Title = model.Title.Trim();
                        if (!string.IsNullOrWhiteSpace(model.ExperienceLevel))
                        {
                            posting.Requisition.JobPosition.JobLevel = model.ExperienceLevel.Trim();
                        }
                        posting.Requisition.JobPosition.Description = !string.IsNullOrWhiteSpace(model.PositionDescription) ? model.PositionDescription.Trim() : model.Overview?.Trim();
                        posting.Requisition.JobPosition.UpdatedAt = DateTimeOffset.UtcNow;
                    }
                }

                await _dbContext.SaveChangesAsync();
                return Json(new { success = true, message = "Cập nhật bài đăng tuyển dụng thành công!", slug = posting.Slug });
            }
            else
            {
                // Vị trí mẫu chuẩn (mock) -> Khởi tạo thật vào DB để lưu các sửa đổi trực tiếp
                var deptName = string.IsNullOrWhiteSpace(model.Department) ? "Công nghệ Thông tin (IT)" : model.Department.Trim();
                var dept = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Name.ToLower() == deptName.ToLower())
                    ?? await _dbContext.Departments.FirstOrDefaultAsync()
                    ?? new Department { Id = Guid.NewGuid(), Name = deptName, Code = "IT" };

                var descContent = !string.IsNullOrWhiteSpace(model.PositionDescription) ? model.PositionDescription.Trim() : model.Overview?.Trim();

                var jobPos = new JobPosition
                {
                    Id = Guid.NewGuid(),
                    DepartmentId = dept.Id,
                    Title = model.Title.Trim(),
                    Code = "POS-" + Guid.NewGuid().ToString("N")[..6].ToUpper(),
                    JobLevel = string.IsNullOrWhiteSpace(model.ExperienceLevel) ? "SENIOR" : model.ExperienceLevel.Trim(),
                    Description = descContent,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.JobPositions.AddAsync(jobPos);

                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                _ = Guid.TryParse(userIdStr, out var currentUserId);
                if (currentUserId == Guid.Empty) currentUserId = await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync();

                var req = new JobRequisition
                {
                    Id = Guid.NewGuid(),
                    DepartmentId = dept.Id,
                    JobPositionId = jobPos.Id,
                    Code = "REQ-" + DateTime.UtcNow.ToString("yyMM") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpper(),
                    Quantity = 1,
                    HeadcountType = HeadcountType.NEW_HEADCOUNT,
                    Status = RequisitionStatus.APPROVED,
                    HiringManagerId = currentUserId,
                    JobDescription = descContent,
                    Requirements = model.Requirements?.Trim(),
                    TargetHireDate = DateOnly.FromDateTime(model.Deadline),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.JobRequisitions.AddAsync(req);

                var cleanTitle = System.Text.RegularExpressions.Regex.Replace(model.Title.ToLower(), @"[^a-z0-9\s-]", "");
                var baseSlug = cleanTitle.Trim().Replace(" ", "-");
                if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = "job";
                var slug = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..5]}";

                var newPosting = new JobPosting
                {
                    Id = Guid.NewGuid(),
                    RequisitionId = req.Id,
                    Title = model.Title.Trim(),
                    Slug = slug,
                    WorkLocation = string.IsNullOrWhiteSpace(model.WorkLocation) ? "Hà Nội (Hybrid 2 ngày WFH)" : model.WorkLocation.Trim(),
                    EmploymentType = EmploymentType.FULL_TIME,
                    SalaryDisplay = string.IsNullOrWhiteSpace(model.SalaryDisplay) ? "Thương lượng theo năng lực" : model.SalaryDisplay.Trim(),
                    JobDescription = model.Overview?.Trim() ?? descContent,
                    Requirements = combinedReqs,
                    Benefits = model.Benefits?.Trim(),
                    PublishedAt = DateTimeOffset.UtcNow,
                    ExpiredAt = new DateTimeOffset(model.Deadline, TimeSpan.Zero),
                    Status = JobPostingStatus.PUBLISHED,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.JobPostings.AddAsync(newPosting);
                await _dbContext.SaveChangesAsync();

                return Json(new { success = true, message = "Đã lưu chỉnh sửa và đồng bộ bài đăng tuyển dụng vào cơ sở dữ liệu thành công!", slug = newPosting.Slug });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi cập nhật bài đăng tuyển dụng");
            return Json(new { success = false, message = "Lỗi khi lưu bài đăng: " + ex.Message });
        }
    }
}

