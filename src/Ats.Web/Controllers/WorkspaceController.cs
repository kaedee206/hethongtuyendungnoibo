using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Ats.Web.Services.Interfaces;

namespace Ats.Web.Controllers;

[Authorize]
[Route("[controller]")]
public class WorkspaceController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _env;
    private readonly IEmailService _emailService;
    private readonly ILogger<WorkspaceController> _logger;

    public WorkspaceController(
        ApplicationDbContext dbContext,
        IWebHostEnvironment env,
        IEmailService emailService,
        ILogger<WorkspaceController> logger)
    {
        _dbContext = dbContext;
        _env = env;
        _emailService = emailService;
        _logger = logger;
    }

    #region Role Redirects (Tránh 404 cho các vai trò nội bộ)

    [HttpGet("/recruiter/pipeline")]
    public IActionResult RecruiterPipeline() => RedirectToAction("Index", "Home", new { tab = "pipeline" });

    [HttpGet("/manager/yeu-cau-tuyen-dung")]
    public IActionResult ManagerRequisitions() => RedirectToAction("Index", "Home", new { tab = "requisitions" });

    [HttpGet("/interviewer/lich-phong-van")]
    public IActionResult InterviewerSchedules() => RedirectToAction("Index", "Home", new { tab = "interviews" });

    [HttpGet("/hrm/dashboard")]
    public IActionResult HrmDashboard() => RedirectToAction("Index", "Home", new { tab = "hrm" });

    [HttpGet("/approver/danh-sach-duyet")]
    public IActionResult ApproverList() => RedirectToAction("Index", "Home", new { tab = "approvals" });

    [HttpGet("/candidate/ho-so-cua-toi")]
    public IActionResult CandidateProfile() => RedirectToAction("Index", "Home", new { tab = "profile" });

    #endregion

    #region Candidate Actions (Ứng viên tương tác thực thi)

    /// <summary>
    /// Cập nhật CV và thông tin cá nhân của ứng viên vào cơ sở dữ liệu thật.
    /// </summary>
    [HttpPost("UpdateCv")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCv(
        [FromForm] string? fullName,
        [FromForm] string? phone,
        [FromForm] string? linkedinUrl,
        IFormFile? cvFile)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var userId);

            // Tìm hoặc tạo Candidate theo Email hoặc UserId
            var candidate = await _dbContext.Candidates
                .Include(c => c.Resumes)
                .FirstOrDefaultAsync(c => (userId != Guid.Empty && c.UserId == userId) || c.Email == userEmail);

            if (candidate == null)
            {
                candidate = new Candidate
                {
                    Id = Guid.NewGuid(),
                    UserId = userId != Guid.Empty ? userId : null,
                    Email = userEmail ?? "candidate@noveratech.digital",
                    FirstName = fullName ?? User.Identity?.Name ?? "Ứng viên",
                    LastName = "",
                    Phone = phone,
                    LinkedinUrl = linkedinUrl,
                    Source = CandidateSource.PORTAL,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.Candidates.AddAsync(candidate);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    candidate.FirstName = fullName.Trim();
                }
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    candidate.Phone = phone.Trim();
                }
                if (!string.IsNullOrWhiteSpace(linkedinUrl))
                {
                    candidate.LinkedinUrl = linkedinUrl.Trim();
                }
                candidate.UpdatedAt = DateTimeOffset.UtcNow;
            }

            string savedFileName = "CV_NoveraTech_Candidate.pdf";

            if (cvFile != null && cvFile.Length > 0)
            {
                savedFileName = Path.GetFileName(cvFile.FileName);
                var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "resumes");
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }

                var uniqueFileName = $"{Guid.NewGuid()}_{savedFileName}";
                var filePath = Path.Combine(uploadsDir, uniqueFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await cvFile.CopyToAsync(stream);
                }

                // Đặt các CV cũ thành không phải primary
                foreach (var r in candidate.Resumes)
                {
                    r.IsPrimary = false;
                    r.UpdatedAt = DateTimeOffset.UtcNow;
                }

                var newResume = new Resume
                {
                    Id = Guid.NewGuid(),
                    CandidateId = candidate.Id,
                    FileName = savedFileName,
                    FilePath = $"/uploads/resumes/{uniqueFileName}",
                    FileSize = cvFile.Length,
                    MimeType = cvFile.ContentType,
                    IsPrimary = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                await _dbContext.Resumes.AddAsync(newResume);
            }
            else
            {
                // Nếu chưa có resume nào, tạo một bản ghi đại diện
                if (!candidate.Resumes.Any())
                {
                    var defaultResume = new Resume
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
                    await _dbContext.Resumes.AddAsync(defaultResume);
                    savedFileName = defaultResume.FileName;
                }
                else
                {
                    var primary = candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? candidate.Resumes.First();
                    savedFileName = primary.FileName;
                }
            }

            await _dbContext.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = "Cập nhật hồ sơ và file CV thành công! Hồ sơ đã được đồng bộ với hệ thống ATS.",
                fileName = savedFileName,
                phone = candidate.Phone,
                fullName = candidate.FirstName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi cập nhật CV ứng viên");
            return Json(new { success = false, message = "Có lỗi xảy ra khi lưu hồ sơ: " + ex.Message });
        }
    }

    /// <summary>
    /// Ứng tuyển nhanh vào vị trí tuyển dụng từ giao diện Workspace.
    /// </summary>
    [HttpPost("ApplyJob")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyJob(
        [FromForm] string jobId,
        [FromForm] string? coverLetter)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var userId);

            // Tìm JobPosting
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

            if (jobPosting == null)
            {
                return Json(new { success = false, message = "Không tìm thấy vị trí tuyển dụng yêu cầu." });
            }

            // Tìm hoặc tạo Candidate
            var candidate = await _dbContext.Candidates
                .Include(c => c.Resumes)
                .FirstOrDefaultAsync(c => (userId != Guid.Empty && c.UserId == userId) || c.Email == userEmail);

            if (candidate == null)
            {
                candidate = new Candidate
                {
                    Id = Guid.NewGuid(),
                    UserId = userId != Guid.Empty ? userId : null,
                    Email = userEmail ?? "candidate@noveratech.digital",
                    FirstName = User.Identity?.Name ?? "Ứng viên",
                    LastName = "",
                    Source = CandidateSource.PORTAL,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.Candidates.AddAsync(candidate);
                await _dbContext.SaveChangesAsync();
            }

            // Đảm bảo Candidate có ít nhất 1 Resume
            var resume = candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? candidate.Resumes.FirstOrDefault();
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

            // Tìm Stage 1: Ứng tuyển mới
            var firstStage = await _dbContext.PipelineStages.OrderBy(s => s.StageOrder).FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("Chưa khởi tạo PipelineStages trong CSDL.");

            // Kiểm tra xem đã ứng tuyển chưa
            var existingApp = await _dbContext.Applications
                .FirstOrDefaultAsync(a => a.CandidateId == candidate.Id && a.JobPostingId == jobPosting.Id && !a.IsDeleted);

            if (existingApp != null)
            {
                return Json(new { success = false, message = "Bạn đã nộp hồ sơ ứng tuyển vị trí này rồi. Vui lòng theo dõi tiến độ trên hệ thống." });
            }

            var application = new Application
            {
                Id = Guid.NewGuid(),
                JobPostingId = jobPosting.Id,
                CandidateId = candidate.Id,
                ResumeId = resume.Id,
                CurrentStageId = firstStage.Id,
                Status = ApplicationStatus.IN_PROCESS,
                AppliedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _dbContext.Applications.AddAsync(application);

            // Ghi nhận lịch sử stage
            var effectiveUserId = userId != Guid.Empty ? userId : (await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync());
            var stageHistory = new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                FromStageId = null,
                ToStageId = firstStage.Id,
                ChangedByUserId = effectiveUserId,
                Comment = string.IsNullOrWhiteSpace(coverLetter) ? "Ứng viên nộp hồ sơ trực tuyến qua cổng ATS." : $"Lời nhắn ứng viên: {coverLetter}",
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _dbContext.ApplicationStageHistories.AddAsync(stageHistory);
            await _dbContext.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"🎉 Chúc mừng bạn! Hồ sơ ứng tuyển vị trí '{jobPosting.Title}' đã được lưu vào hệ thống thành công. Đội ngũ tuyển dụng sẽ phản hồi trong 48 giờ."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi ứng tuyển vị trí");
            return Json(new { success = false, message = "Lỗi khi nộp hồ sơ: " + ex.Message });
        }
    }

    /// <summary>
    /// Ứng viên yêu cầu đổi lịch phỏng vấn.
    /// </summary>
    [HttpPost("RequestReschedule")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestReschedule(
        [FromForm] Guid? interviewId,
        [FromForm] string? reason)
    {
        try
        {
            Interview? interview = null;
            if (interviewId.HasValue && interviewId.Value != Guid.Empty)
            {
                interview = await _dbContext.Interviews.FirstOrDefaultAsync(i => i.Id == interviewId.Value);
            }

            if (interview == null)
            {
                var userEmail = User.FindFirstValue(ClaimTypes.Email);
                interview = await _dbContext.Interviews
                    .Include(i => i.Application)
                        .ThenInclude(a => a.Candidate)
                    .Where(i => i.Application.Candidate.Email == userEmail && i.Status == InterviewStatus.SCHEDULED)
                    .OrderBy(i => i.StartTime)
                    .FirstOrDefaultAsync();
            }

            if (interview == null)
            {
                return Json(new { success = false, message = "Không tìm thấy phiên phỏng vấn cần đổi lịch." });
            }

            interview.CandidateConfirmed = CandidateConfirmStatus.DECLINED;
            interview.CandidateNotes = string.IsNullOrWhiteSpace(reason)
                ? "Ứng viên yêu cầu đổi lịch phỏng vấn."
                : $"Yêu cầu đổi lịch từ ứng viên: {reason.Trim()}";
            interview.UpdatedAt = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = "Yêu cầu đổi lịch phỏng vấn đã được ghi nhận vào hệ thống ATS. Chuyên viên nhân sự sẽ liên hệ lại với bạn trong 24 giờ làm việc."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi yêu cầu đổi lịch phỏng vấn");
            return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
        }
    }

    /// <summary>
    /// Ứng viên xác nhận tham gia buổi phỏng vấn.
    /// </summary>
    [HttpPost("ConfirmInterview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmInterview([FromForm] Guid interviewId)
    {
        try
        {
            var interview = await _dbContext.Interviews.FirstOrDefaultAsync(i => i.Id == interviewId);
            if (interview == null)
            {
                return Json(new { success = false, message = "Không tìm thấy lịch phỏng vấn yêu cầu." });
            }

            interview.CandidateConfirmed = CandidateConfirmStatus.ACCEPTED;
            interview.UpdatedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync();

            return Json(new { success = true, message = "Đã xác nhận tham gia buổi phỏng vấn thành công!" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xác nhận tham gia phỏng vấn");
            return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
        }
    }

    #endregion

    #region Staff Actions (Nhân sự & Quản trị tương tác thực thi)

    /// <summary>
    /// Duyệt Offer cho ứng viên: Cập nhật JobOffer thành APPROVED và Application thành HIRED.
    /// </summary>
    [HttpPost("ApproveOffer")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Approver},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> ApproveOffer([FromForm] Guid? offerId, [FromForm] string? candidateName)
    {
        try
        {
            JobOffer? offer = null;
            if (offerId.HasValue && offerId.Value != Guid.Empty)
            {
                offer = await _dbContext.JobOffers
                    .Include(o => o.Application)
                        .ThenInclude(a => a.Candidate)
                    .Include(o => o.Application)
                        .ThenInclude(a => a.JobPosting)
                    .FirstOrDefaultAsync(o => o.Id == offerId.Value);
            }

            if (offer == null)
            {
                // Fallback: Tìm offer đang PENDING_APPROVAL
                offer = await _dbContext.JobOffers
                    .Include(o => o.Application)
                        .ThenInclude(a => a.Candidate)
                    .Include(o => o.Application)
                        .ThenInclude(a => a.JobPosting)
                    .Where(o => o.Status == OfferStatus.PENDING_APPROVAL || o.Status == OfferStatus.DRAFT)
                    .OrderByDescending(o => o.CreatedAt)
                    .FirstOrDefaultAsync();
            }

            if (offer == null)
            {
                return Json(new { success = false, message = "Không tìm thấy Thư mời Offer đang chờ duyệt." });
            }

            // Cập nhật trạng thái Offer
            offer.Status = OfferStatus.APPROVED;
            offer.UpdatedAt = DateTimeOffset.UtcNow;

            // Chuyển Application sang Stage 6: Tuyển dụng thành công
            var stageSuccess = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 6)
                ?? await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.Name.Contains("thành công"));

            if (stageSuccess != null && offer.Application != null)
            {
                offer.Application.CurrentStageId = stageSuccess.Id;
                offer.Application.Status = ApplicationStatus.HIRED;
                offer.Application.UpdatedAt = DateTimeOffset.UtcNow;

                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                _ = Guid.TryParse(userIdStr, out var currentUserId);
                var effectiveUserId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync());

                await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = offer.Application.Id,
                    FromStageId = offer.Application.CurrentStageId,
                    ToStageId = stageSuccess.Id,
                    ChangedByUserId = effectiveUserId,
                    Comment = $"Thư mời nhận việc đã được phê duyệt bởi {User.Identity?.Name} (Mức lương: {offer.BaseSalary:N0} VNĐ).",
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();

            var candName = offer.Application?.Candidate?.FirstName ?? candidateName ?? "Ứng viên";
            return Json(new
            {
                success = true,
                message = $"✅ Đã phê duyệt Thư mời Offer cho ứng viên {candName} thành công! Hồ sơ đã được chuyển sang giai đoạn Tuyển dụng thành công.",
                offerId = offer.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi phê duyệt Offer");
            return Json(new { success = false, message = "Lỗi khi duyệt Offer: " + ex.Message });
        }
    }

    /// <summary>
    /// Tạo tin tuyển dụng mới: Lưu thật vào JobRequisition và JobPosting.
    /// </summary>
    [HttpPost("CreateJobPosting")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.Approver}")]
    public async Task<IActionResult> CreateJobPosting(
        [FromForm] string title,
        [FromForm] string? deptName,
        [FromForm] int quantity,
        [FromForm] string? salaryDisplay,
        [FromForm] DateTime? expiredDate,
        [FromForm] string? description)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return Json(new { success = false, message = "Vui lòng nhập tiêu đề vị trí tuyển dụng." });
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var currentUserId);

            // Tìm hoặc gán Department
            var dept = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Name.Contains(deptName ?? "Công nghệ") || d.Code == "IT")
                ?? await _dbContext.Departments.FirstAsync();

            // Tìm hoặc tạo JobPosition
            var posCode = "JOB-" + Guid.NewGuid().ToString("N")[..6].ToUpper();
            var jobPos = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = posCode,
                Title = title.Trim(),
                DepartmentId = dept.Id,
                JobLevel = "MIDDLE-SENIOR",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.JobPositions.AddAsync(jobPos);

            // Tạo JobRequisition
            var req = new JobRequisition
            {
                Id = Guid.NewGuid(),
                Code = "REQ-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpper(),
                JobPositionId = jobPos.Id,
                DepartmentId = dept.Id,
                HiringManagerId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.FirstAsync()).Id,
                Quantity = quantity > 0 ? quantity : 1,
                HeadcountType = HeadcountType.NEW_HEADCOUNT,
                Status = RequisitionStatus.APPROVED,
                Currency = "VND",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.JobRequisitions.AddAsync(req);

            // Tạo Slug
            var baseSlug = title.ToLower().Trim()
                .Replace(" ", "-")
                .Replace("/", "-")
                .Replace(".", "")
                .Replace("&", "va");
            var slug = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..5]}";

            // Tạo JobPosting
            var posting = new JobPosting
            {
                Id = Guid.NewGuid(),
                RequisitionId = req.Id,
                Title = title.Trim(),
                Slug = slug,
                WorkLocation = "Hà Nội (Hybrid 2 ngày WFH)",
                EmploymentType = EmploymentType.FULL_TIME,
                SalaryDisplay = string.IsNullOrWhiteSpace(salaryDisplay) ? "Thương lượng theo năng lực" : salaryDisplay.Trim(),
                JobDescription = string.IsNullOrWhiteSpace(description) ? "Chịu trách nhiệm thực hiện các mục tiêu công nghệ và sản phẩm trọng điểm của công ty." : description.Trim(),
                Requirements = "Thành thạo công nghệ chuyên môn, tư duy giải quyết vấn đề tốt, trách nhiệm cao.",
                Benefits = "Mức lương thưởng cạnh tranh, BHXH đầy đủ, bảo hiểm NoveraCare VIP, du lịch nghỉ dưỡng 5 sao.",
                PublishedAt = DateTimeOffset.UtcNow,
                ExpiredAt = expiredDate.HasValue ? new DateTimeOffset(expiredDate.Value, TimeSpan.Zero) : DateTimeOffset.UtcNow.AddDays(30),
                Status = JobPostingStatus.PUBLISHED,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.JobPostings.AddAsync(posting);

            await _dbContext.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"🚀 Đã xuất bản tin tuyển dụng mới: '{posting.Title}' thành công! Vị trí đã hiển thị trên Cổng việc làm và Trang chủ.",
                jobId = posting.Id,
                slug = posting.Slug,
                title = posting.Title
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tạo tin tuyển dụng");
            return Json(new { success = false, message = "Lỗi khi tạo tin tuyển dụng: " + ex.Message });
        }
    }

    /// <summary>
    /// Khởi tạo đề xuất nhân sự (Hiring Manager) lưu vào CSDL thật.
    /// </summary>
    [HttpPost("CreateRequisition")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.HiringManager},{UserRoles.Admin},{UserRoles.HRManager},{UserRoles.Approver}")]
    public async Task<IActionResult> CreateRequisition(
        [FromForm] string title,
        [FromForm] int quantity,
        [FromForm] DateTime? targetDate,
        [FromForm] string? reason,
        [FromForm] string? description)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return Json(new { success = false, message = "Vui lòng nhập chức danh cần bổ sung." });
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var currentUserId);

            var user = await _dbContext.Users.Include(u => u.DepartmentEntity).FirstOrDefaultAsync(u => u.Id == currentUserId);
            var deptId = user?.DepartmentId ?? (await _dbContext.Departments.FirstAsync()).Id;

            // Tạo JobPosition nếu cần
            var jobPos = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = "POS-" + Guid.NewGuid().ToString("N")[..6].ToUpper(),
                Title = title.Trim(),
                DepartmentId = deptId,
                JobLevel = "MIDDLE",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.JobPositions.AddAsync(jobPos);

            var req = new JobRequisition
            {
                Id = Guid.NewGuid(),
                Code = "REQ-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpper(),
                JobPositionId = jobPos.Id,
                DepartmentId = deptId,
                HiringManagerId = user?.Id ?? (await _dbContext.Users.FirstAsync()).Id,
                Quantity = quantity > 0 ? quantity : 1,
                HeadcountType = HeadcountType.NEW_HEADCOUNT,
                Reason = string.IsNullOrWhiteSpace(reason) ? "Mở rộng quy mô dự án mới" : reason.Trim(),
                TargetHireDate = targetDate.HasValue ? DateOnly.FromDateTime(targetDate.Value) : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(45)),
                Status = RequisitionStatus.PENDING_APPROVAL,
                Currency = "VND",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.JobRequisitions.AddAsync(req);

            await _dbContext.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"📝 Đề xuất tuyển dụng nhân sự cho vị trí '{title}' (Mã: {req.Code}) đã được tạo và gửi duyệt thành công tới Ban Giám đốc & Phòng Nhân sự!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tạo đề xuất tuyển dụng");
            return Json(new { success = false, message = "Lỗi khi gửi đề xuất: " + ex.Message });
        }
    }

    /// <summary>
    /// Nhập phiếu đánh giá phỏng vấn: Lưu điểm số, nhận xét và đề xuất vào CSDL thật.
    /// </summary>
    [HttpPost("SubmitEvaluation")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Interviewer},{UserRoles.Admin},{UserRoles.HiringManager},{UserRoles.HRManager},{UserRoles.Approver}")]
    public async Task<IActionResult> SubmitEvaluation(
        [FromForm] Guid? interviewId,
        [FromForm] decimal score,
        [FromForm] string? recommendation,
        [FromForm] string? feedback)
    {
        try
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var currentUserId);

            Interview? interview = null;
            if (interviewId.HasValue && interviewId.Value != Guid.Empty)
            {
                interview = await _dbContext.Interviews
                    .Include(i => i.Application)
                        .ThenInclude(a => a.Candidate)
                    .Include(i => i.Application)
                        .ThenInclude(a => a.JobPosting)
                    .Include(i => i.Evaluations)
                    .FirstOrDefaultAsync(i => i.Id == interviewId.Value);
            }

            if (interview == null)
            {
                // Lấy interview có lịch gần nhất để đánh giá
                interview = await _dbContext.Interviews
                    .Include(i => i.Application)
                        .ThenInclude(a => a.Candidate)
                    .Include(i => i.Application)
                        .ThenInclude(a => a.JobPosting)
                    .Include(i => i.Evaluations)
                    .OrderBy(i => i.StartTime)
                    .FirstOrDefaultAsync();
            }

            if (interview == null)
            {
                return Json(new { success = false, message = "Không tìm thấy buổi phỏng vấn cần đánh giá." });
            }

            // Parse recommendation enum
            RecommendationType recType = RecommendationType.HIRE;
            if (!string.IsNullOrWhiteSpace(recommendation))
            {
                if (recommendation.Contains("PASS", StringComparison.OrdinalIgnoreCase) || recommendation.Contains("STRONG_HIRE", StringComparison.OrdinalIgnoreCase))
                    recType = RecommendationType.HIRE;
                else if (recommendation.Contains("CONSIDER", StringComparison.OrdinalIgnoreCase))
                    recType = RecommendationType.CONSIDER;
                else if (recommendation.Contains("REJECT", StringComparison.OrdinalIgnoreCase))
                    recType = RecommendationType.STRONG_NO_HIRE;
            }

            var eval = interview.Evaluations.FirstOrDefault(e => e.InterviewerId == currentUserId)
                ?? interview.Evaluations.FirstOrDefault();

            if (eval == null)
            {
                eval = new InterviewEvaluation
                {
                    Id = Guid.NewGuid(),
                    InterviewId = interview.Id,
                    InterviewerId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.FirstAsync()).Id,
                    OverallScore = score > 0 ? score : 4.0m,
                    Recommendation = recType,
                    DetailedFeedback = feedback ?? "Ứng viên đáp ứng tốt yêu cầu chuyên môn.",
                    IsSubmitted = true,
                    SubmittedAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.InterviewEvaluations.AddAsync(eval);
            }
            else
            {
                eval.OverallScore = score > 0 ? score : 4.0m;
                eval.Recommendation = recType;
                eval.DetailedFeedback = feedback ?? eval.DetailedFeedback;
                eval.IsSubmitted = true;
                eval.SubmittedAt = DateTimeOffset.UtcNow;
                eval.UpdatedAt = DateTimeOffset.UtcNow;
            }

            interview.Status = InterviewStatus.COMPLETED;
            interview.UpdatedAt = DateTimeOffset.UtcNow;

            // Nếu ĐẠT -> chuyển hồ sơ sang giai đoạn Offer (Stage 5)
            if (recType == RecommendationType.HIRE || recType == RecommendationType.STRONG_HIRE)
            {
                var stageOffer = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 5)
                    ?? await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.Name.Contains("Offer"));
                if (stageOffer != null && interview.Application != null)
                {
                    interview.Application.CurrentStageId = stageOffer.Id;
                    interview.Application.UpdatedAt = DateTimeOffset.UtcNow;

                    var effectiveUserId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync());
                    await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
                    {
                        Id = Guid.NewGuid(),
                        ApplicationId = interview.Application.Id,
                        FromStageId = interview.Application.CurrentStageId,
                        ToStageId = stageOffer.Id,
                        ChangedByUserId = effectiveUserId,
                        Comment = $"Đã phỏng vấn ĐẠT (Điểm: {eval.OverallScore}/5). Đề xuất tiến hành gửi Thư mời Offer.",
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }
            }
            else if (recType == RecommendationType.STRONG_NO_HIRE)
            {
                var stageReject = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 7)
                    ?? await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.Name.Contains("Từ chối"));
                if (stageReject != null && interview.Application != null)
                {
                    interview.Application.CurrentStageId = stageReject.Id;
                    interview.Application.Status = ApplicationStatus.REJECTED;
                    interview.Application.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }

            await _dbContext.SaveChangesAsync();

            var candName = interview.Application?.Candidate?.FirstName ?? "ứng viên";
            return Json(new
            {
                success = true,
                message = $"📋 Phiếu đánh giá cho ứng viên {candName} đã được lưu thành công! Kết quả: {(recType == RecommendationType.HIRE ? "ĐẠT (Chuyển sang bước Offer)" : "Đã lưu nhận xét")}.",
                interviewId = interview.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gửi phiếu đánh giá phỏng vấn");
            return Json(new { success = false, message = "Lỗi khi lưu đánh giá: " + ex.Message });
        }
    }

    /// <summary>
    /// Lọc danh sách hồ sơ ứng viên theo giai đoạn (Stage).
    /// </summary>
    [HttpGet("FilterCandidates")]
    public async Task<IActionResult> FilterCandidates([FromQuery] string? stage)
    {
        try
        {
            var query = _dbContext.Applications
                .Include(a => a.Candidate)
                .Include(a => a.JobPosting)
                .Include(a => a.CurrentStage)
                .Where(a => !a.IsDeleted);

            if (!string.IsNullOrWhiteSpace(stage) && stage != "ALL")
            {
                if (int.TryParse(stage, out var stageNum))
                {
                    query = query.Where(a => a.CurrentStage.StageOrder == stageNum);
                }
                else
                {
                    var s = stage.Trim().ToLower();
                    query = query.Where(a => a.CurrentStage.Name.ToLower().Contains(s));
                }
            }

            var results = await query
                .OrderByDescending(a => a.AppliedAt)
                .Select(a => new
                {
                    a.Id,
                    CandidateName = a.Candidate.FirstName + " " + a.Candidate.LastName,
                    CandidateEmail = a.Candidate.Email,
                    CandidatePhone = a.Candidate.Phone ?? "Chưa có",
                    JobTitle = a.JobPosting.Title,
                    StageName = a.CurrentStage.Name,
                    StageColor = a.CurrentStage.ColorCode,
                    AppliedDate = a.AppliedAt.HasValue ? a.AppliedAt.Value.ToString("dd/MM/yyyy") : a.CreatedAt.ToString("dd/MM/yyyy"),
                    Status = a.Status.ToString()
                })
                .ToListAsync();

            return Json(new
            {
                success = true,
                total = results.Count,
                candidates = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lọc hồ sơ");
            return Json(new { success = false, message = "Lỗi: " + ex.Message });
        }
    }

    /// <summary>
    /// Chuyển giai đoạn ứng tuyển (VD: Sơ loại -> Phỏng vấn, Phỏng vấn -> Offer, v.v.).
    /// </summary>
    [HttpPost("AdvanceStage")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.HiringManager}")]
    public async Task<IActionResult> AdvanceStage(
        [FromForm] Guid applicationId,
        [FromForm] Guid? toStageId,
        [FromForm] string? comment)
    {
        try
        {
            var application = await _dbContext.Applications
                .Include(a => a.Candidate)
                .Include(a => a.JobPosting)
                .Include(a => a.CurrentStage)
                .FirstOrDefaultAsync(a => a.Id == applicationId);

            if (application == null)
            {
                return Json(new { success = false, message = "Không tìm thấy hồ sơ ứng viên yêu cầu." });
            }

            var currentStage = application.CurrentStage;
            PipelineStage? targetStage = null;

            if (toStageId.HasValue && toStageId.Value != Guid.Empty)
            {
                targetStage = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.Id == toStageId.Value);
            }
            else
            {
                // Mặc định chuyển sang stage có StageOrder kế tiếp
                targetStage = await _dbContext.PipelineStages
                    .Where(s => s.StageOrder > currentStage.StageOrder)
                    .OrderBy(s => s.StageOrder)
                    .FirstOrDefaultAsync();
            }

            if (targetStage == null)
            {
                return Json(new { success = false, message = "Hồ sơ hiện đã ở giai đoạn cao nhất trong quy trình tuyển dụng." });
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var currentUserId);
            var effectiveUserId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync());

            var prevStageId = application.CurrentStageId;
            application.CurrentStageId = targetStage.Id;
            application.UpdatedAt = DateTimeOffset.UtcNow;

            var history = new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                FromStageId = prevStageId,
                ToStageId = targetStage.Id,
                ChangedByUserId = effectiveUserId,
                Comment = string.IsNullOrWhiteSpace(comment)
                    ? $"Chuyển giai đoạn từ '{currentStage.Name}' sang '{targetStage.Name}' bởi {User.Identity?.Name}."
                    : comment.Trim(),
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.ApplicationStageHistories.AddAsync(history);
            await _dbContext.SaveChangesAsync();

            // Nếu vượt qua sơ loại (Screening Passed), gửi email chúc mừng cho ứng viên
            if (targetStage.StageOrder == 2 || targetStage.Name.Contains("Phỏng vấn", StringComparison.OrdinalIgnoreCase) || targetStage.Name.Contains("Sơ loại", StringComparison.OrdinalIgnoreCase))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var candName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();
                        await _emailService.SendScreeningPassedAsync(
                            application.Candidate.Email,
                            candName,
                            application.JobPosting.Title);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Không thể gửi email thông báo qua sơ loại: {Msg}", ex.Message);
                    }
                });
            }

            var candidateFullName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();
            return Json(new
            {
                success = true,
                message = $"🎉 Đã chuyển hồ sơ ứng viên {candidateFullName} sang giai đoạn: '{targetStage.Name}' thành công!",
                stageId = targetStage.Id,
                stageName = targetStage.Name,
                stageOrder = targetStage.StageOrder,
                stageColor = targetStage.ColorCode
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi chuyển giai đoạn ứng tuyển");
            return Json(new { success = false, message = "Lỗi khi chuyển giai đoạn: " + ex.Message });
        }
    }

    /// <summary>
    /// Lên lịch phỏng vấn, phân công hội đồng phỏng vấn và gửi thư mời email cho ứng viên.
    /// </summary>
    [HttpPost("ScheduleInterview")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.HiringManager}")]
    public async Task<IActionResult> ScheduleInterview(
        [FromForm] Guid applicationId,
        [FromForm] string? roundTitle,
        [FromForm] DateTime? startTime,
        [FromForm] DateTime? endTime,
        [FromForm] string? locationOrLink,
        [FromForm] List<Guid>? interviewerIds,
        [FromForm] string? note)
    {
        try
        {
            var application = await _dbContext.Applications
                .Include(a => a.Candidate)
                .Include(a => a.JobPosting)
                .Include(a => a.CurrentStage)
                .FirstOrDefaultAsync(a => a.Id == applicationId);

            if (application == null)
            {
                return Json(new { success = false, message = "Không tìm thấy hồ sơ ứng viên." });
            }

            var startDto = startTime.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(startTime.Value, DateTimeKind.Utc))
                : DateTimeOffset.UtcNow.AddDays(2).Date.AddHours(9); // Mặc định 9h sáng ngày kia
            var endDto = endTime.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(endTime.Value, DateTimeKind.Utc))
                : startDto.AddHours(1);

            var title = string.IsNullOrWhiteSpace(roundTitle) ? "Phỏng vấn Chuyên môn & Văn hóa NoveraTech" : roundTitle.Trim();
            var meetLocation = string.IsNullOrWhiteSpace(locationOrLink) ? "Google Meet (Link phòng họp gửi qua lịch hẹn)" : locationOrLink.Trim();

            var interview = new Interview
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                RoundNumber = 1,
                Title = title,
                InterviewType = meetLocation.Contains("http", StringComparison.OrdinalIgnoreCase) ? InterviewType.ONLINE_MEET : InterviewType.OFFLINE_OFFICE,
                LocationOrLink = meetLocation,
                StartTime = startDto,
                EndTime = endDto,
                Status = InterviewStatus.SCHEDULED,
                CandidateConfirmed = CandidateConfirmStatus.PENDING,
                CandidateNotes = note,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.Interviews.AddAsync(interview);

            // Gán Panelists (Người phỏng vấn)
            var panelistNames = new List<string>();
            if (interviewerIds != null && interviewerIds.Any())
            {
                var interviewers = await _dbContext.Users.Where(u => interviewerIds.Contains(u.Id)).ToListAsync();
                foreach (var inv in interviewers)
                {
                    await _dbContext.InterviewPanelists.AddAsync(new InterviewPanelist
                    {
                        Id = Guid.NewGuid(),
                        InterviewId = interview.Id,
                        InterviewerId = inv.Id,
                        IsLead = panelistNames.Count == 0,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    });
                    panelistNames.Add($"{inv.FullName} ({inv.Role})");
                }
            }
            if (!panelistNames.Any())
            {
                panelistNames.Add("Ban Tuyển Dụng NoveraTech");
            }

            // Chuyển giai đoạn sang Stage Phỏng vấn nếu chưa ở đó
            var interviewStage = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 3 || s.Name.Contains("Phỏng vấn"))
                ?? await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 2);

            if (interviewStage != null && application.CurrentStageId != interviewStage.Id)
            {
                var prev = application.CurrentStageId;
                application.CurrentStageId = interviewStage.Id;
                application.UpdatedAt = DateTimeOffset.UtcNow;

                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                _ = Guid.TryParse(userIdStr, out var currentUserId);
                var effectiveUserId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync());

                await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = application.Id,
                    FromStageId = prev,
                    ToStageId = interviewStage.Id,
                    ChangedByUserId = effectiveUserId,
                    Comment = $"Đã lên lịch phỏng vấn: {title} ({startDto:dd/MM/yyyy HH:mm}).",
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();

            // Gửi email thư mời phỏng vấn cho ứng viên
            var candFullName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();
            var panelistStr = string.Join(", ", panelistNames);
            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendInterviewInvitationAsync(
                        application.Candidate.Email,
                        candFullName,
                        application.JobPosting.Title,
                        title,
                        startDto,
                        endDto,
                        meetLocation,
                        panelistStr);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Không thể gửi email thư mời phỏng vấn: {Msg}", ex.Message);
                }
            });

            return Json(new
            {
                success = true,
                message = $"📅 Đã lên lịch phỏng vấn và gửi thư mời tới ứng viên {candFullName} thành công!",
                interviewId = interview.Id,
                startTime = startDto.ToString("dd/MM/yyyy HH:mm"),
                panelists = panelistStr
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lên lịch phỏng vấn");
            return Json(new { success = false, message = "Lỗi khi xếp lịch phỏng vấn: " + ex.Message });
        }
    }

    /// <summary>
    /// Đề xuất Thư mời nhận việc (Job Offer) cho ứng viên.
    /// </summary>
    [HttpPost("CreateOffer")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.HiringManager}")]
    public async Task<IActionResult> CreateOffer(
        [FromForm] Guid applicationId,
        [FromForm] decimal baseSalary,
        [FromForm] decimal? bonusAllowance,
        [FromForm] decimal? totalPackage,
        [FromForm] DateTime? proposedJoinDate,
        [FromForm] string? contractType,
        [FromForm] string? note)
    {
        try
        {
            var application = await _dbContext.Applications
                .Include(a => a.Candidate)
                .Include(a => a.JobPosting)
                .Include(a => a.CurrentStage)
                .FirstOrDefaultAsync(a => a.Id == applicationId);

            if (application == null)
            {
                return Json(new { success = false, message = "Không tìm thấy hồ sơ ứng viên." });
            }

            if (baseSalary <= 0)
            {
                return Json(new { success = false, message = "Mức lương cơ bản phải lớn hơn 0." });
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var currentUserId);
            var recruiterId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync());

            var offer = await _dbContext.JobOffers.FirstOrDefaultAsync(o => o.ApplicationId == application.Id);
            var joinDate = proposedJoinDate.HasValue
                ? DateOnly.FromDateTime(proposedJoinDate.Value)
                : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14));

            var total = totalPackage ?? (baseSalary + (bonusAllowance ?? 0));

            if (offer == null)
            {
                offer = new JobOffer
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = application.Id,
                    CreatedByRecruiterId = recruiterId,
                    BaseSalary = baseSalary,
                    BonusAllowance = bonusAllowance,
                    TotalPackage = total,
                    ProposedJoinDate = joinDate,
                    ProbationPeriodMonths = 2,
                    ContractType = contractType ?? "Chính thức (Toàn thời gian)",
                    Status = OfferStatus.PENDING_APPROVAL,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.JobOffers.AddAsync(offer);
            }
            else
            {
                offer.BaseSalary = baseSalary;
                offer.BonusAllowance = bonusAllowance;
                offer.TotalPackage = total;
                offer.ProposedJoinDate = joinDate;
                offer.ContractType = contractType ?? offer.ContractType;
                offer.Status = OfferStatus.PENDING_APPROVAL;
                offer.UpdatedAt = DateTimeOffset.UtcNow;
            }

            // Chuyển giai đoạn sang Stage 5: Offer
            var offerStage = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 5 || s.Name.Contains("Offer"));
            if (offerStage != null)
            {
                var prev = application.CurrentStageId;
                application.CurrentStageId = offerStage.Id;
                application.UpdatedAt = DateTimeOffset.UtcNow;

                await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = application.Id,
                    FromStageId = prev,
                    ToStageId = offerStage.Id,
                    ChangedByUserId = recruiterId,
                    Comment = $"Đề xuất Thư mời nhận việc: Lương cơ bản {baseSalary:N0} VNĐ. Ngày dự kiến nhận việc: {joinDate:dd/MM/yyyy}.",
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();

            // Gửi email thông báo Thư mời nhận việc cho ứng viên
            var candFullName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();
            _ = Task.Run(async () =>
            {
                try
                {
                    var joinDateTime = joinDate.ToDateTime(TimeOnly.MinValue);
                    await _emailService.SendOfferLetterNotificationAsync(
                        application.Candidate.Email,
                        candFullName,
                        application.JobPosting.Title,
                        baseSalary,
                        joinDateTime);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Không thể gửi email thư mời Offer: {Msg}", ex.Message);
                }
            });

            return Json(new
            {
                success = true,
                message = $"💼 Đã tạo Đề xuất Offer (Lương: {baseSalary:N0} VNĐ) và gửi thông báo tới ứng viên {candFullName} thành công!",
                offerId = offer.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tạo đề xuất Offer");
            return Json(new { success = false, message = "Lỗi khi tạo Offer: " + ex.Message });
        }
    }

    /// <summary>
    /// Từ chối hồ sơ ứng viên và gửi thư cảm ơn/từ chối lịch sự.
    /// </summary>
    [HttpPost("RejectApplication")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.HiringManager}")]
    public async Task<IActionResult> RejectApplication(
        [FromForm] Guid applicationId,
        [FromForm] string? reason,
        [FromForm] bool sendEmail = true)
    {
        try
        {
            var application = await _dbContext.Applications
                .Include(a => a.Candidate)
                .Include(a => a.JobPosting)
                .Include(a => a.CurrentStage)
                .FirstOrDefaultAsync(a => a.Id == applicationId);

            if (application == null)
            {
                return Json(new { success = false, message = "Không tìm thấy hồ sơ ứng viên." });
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var currentUserId);
            var effectiveUserId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync());

            var prev = application.CurrentStageId;
            application.Status = ApplicationStatus.REJECTED;
            application.UpdatedAt = DateTimeOffset.UtcNow;

            var rejectStage = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 7 || s.Name.Contains("Từ chối"));
            if (rejectStage != null)
            {
                application.CurrentStageId = rejectStage.Id;
            }

            var rejectReason = string.IsNullOrWhiteSpace(reason)
                ? "Hồ sơ chưa phù hợp với tiêu chuẩn yêu cầu của vị trí ở thời điểm hiện tại."
                : reason.Trim();

            await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                FromStageId = prev,
                ToStageId = rejectStage?.Id ?? prev,
                ChangedByUserId = effectiveUserId,
                Comment = $"Từ chối hồ sơ: {rejectReason}",
                CreatedAt = DateTimeOffset.UtcNow
            });

            await _dbContext.SaveChangesAsync();

            var candFullName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();
            if (sendEmail)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.SendRejectionLetterAsync(
                            application.Candidate.Email,
                            candFullName,
                            application.JobPosting.Title,
                            rejectReason);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Không thể gửi email từ chối: {Msg}", ex.Message);
                    }
                });
            }

            return Json(new
            {
                success = true,
                message = $"Đã cập nhật trạng thái Từ chối cho hồ sơ ứng viên {candFullName}{(sendEmail ? " và gửi email thư cảm ơn lịch sự." : ".")}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi từ chối hồ sơ ứng viên");
            return Json(new { success = false, message = "Lỗi khi xử lý từ chối: " + ex.Message });
        }
    }

    /// <summary>
    /// Lấy danh sách thành viên nội bộ có thể phân công phỏng vấn.
    /// </summary>
    [HttpGet("GetInterviewers")]
    public async Task<IActionResult> GetInterviewers()
    {
        try
        {
            var interviewers = await _dbContext.Users
                .Where(u => u.Status == "ACTIVE")
                .Select(u => new
                {
                    u.Id,
                    u.FullName,
                    u.Email,
                    u.Role
                })
                .ToListAsync();

            return Json(new { success = true, data = interviewers });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Lấy chi tiết hồ sơ ứng viên kèm lịch sử các giai đoạn và CV.
    /// </summary>
    [HttpGet("GetCandidateDetail")]
    public async Task<IActionResult> GetCandidateDetail([FromQuery] Guid id, [FromQuery] Guid? applicationId)
    {
        try
        {
            var appId = id != Guid.Empty ? id : (applicationId ?? Guid.Empty);
            var app = await _dbContext.Applications
                .Include(a => a.Candidate)
                    .ThenInclude(c => c.Resumes)
                .Include(a => a.JobPosting)
                .Include(a => a.CurrentStage)
                .Include(a => a.StageHistories)
                .Include(a => a.Interviews)
                    .ThenInclude(i => i.Evaluations)
                .Include(a => a.JobOffers)
                .FirstOrDefaultAsync(a => a.Id == appId);

            if (app == null)
            {
                return Json(new { success = false, message = "Không tìm thấy hồ sơ." });
            }

            var candName = $"{app.Candidate.FirstName} {app.Candidate.LastName}".Trim();
            var primaryResume = app.Candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? app.Candidate.Resumes.FirstOrDefault();

            return Json(new
            {
                success = true,
                data = new
                {
                    id = app.Id,
                    candidateName = candName,
                    email = app.Candidate.Email,
                    candidateEmail = app.Candidate.Email,
                    phone = app.Candidate.Phone ?? "Chưa cập nhật",
                    candidatePhone = app.Candidate.Phone ?? "Chưa cập nhật",
                    linkedin = app.Candidate.LinkedinUrl ?? "",
                    jobTitle = app.JobPosting.Title,
                    currentStageId = app.CurrentStageId,
                    currentStageName = app.CurrentStage.Name,
                    currentStage = app.CurrentStage.Name,
                    stageColor = app.CurrentStage.ColorCode,
                    stageOrder = app.CurrentStage.StageOrder,
                    status = app.Status.ToString(),
                    appliedDate = app.AppliedAt.HasValue ? app.AppliedAt.Value.ToString("dd/MM/yyyy HH:mm") : app.CreatedAt.ToString("dd/MM/yyyy"),
                    resumeFileName = primaryResume?.FileName ?? "Chưa tải lên",
                    resumeFilePath = primaryResume?.FilePath ?? "#",
                    interviews = app.Interviews.Select(i => new
                    {
                        i.Title,
                        time = i.StartTime.HasValue ? i.StartTime.Value.ToString("dd/MM/yyyy HH:mm") : "Chưa ấn định",
                        link = i.LocationOrLink ?? "Chưa có",
                        status = i.Status.ToString()
                    }),
                    histories = app.StageHistories.OrderByDescending(h => h.CreatedAt).Select(h => new
                    {
                        comment = h.Comment ?? "Cập nhật tiến trình",
                        date = h.CreatedAt.ToString("dd/MM/yyyy HH:mm")
                    })
                }
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    #endregion
}
