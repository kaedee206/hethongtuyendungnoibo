using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Ats.Web.Services.Interfaces;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.ViewModels.Referral;
using Ats.Web.Models.ViewModels.CandidateMerge;
using Ats.Web.Models.ViewModels.CandidateTimeline;

namespace Ats.Web.Controllers;

[Authorize]
[Route("[controller]")]
public class WorkspaceController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _env;
    private readonly IEmailService _emailService;
    private readonly IFileStorageService _fileStorageService;
    private readonly ITalentPoolService _talentPoolService;
    private readonly ICandidateExportService _candidateExportService;
    private readonly ILogger<WorkspaceController> _logger;
    private readonly IApplicationTrackingService _trackingService;
    private readonly ICandidateReferralService _referralService;
    private readonly ICandidateMergeService _mergeService;
    private readonly ICandidateTimelineService _timelineService;
    private readonly IRequisitionAssignmentService? _assignmentService;
    private readonly IInterviewEvaluationService? _interviewEvaluationService;
    private readonly IInterviewService? _interviewService;
    private readonly ICandidateInterviewService? _candidateInterviewService;

    [ActivatorUtilitiesConstructor]
    public WorkspaceController(
        ApplicationDbContext dbContext,
        IWebHostEnvironment env,
        IEmailService emailService,
        IFileStorageService fileStorageService,
        ITalentPoolService talentPoolService,
        ICandidateExportService candidateExportService,
        ILogger<WorkspaceController> logger,
        IApplicationTrackingService trackingService,
        ICandidateReferralService referralService,
        ICandidateMergeService mergeService,
        ICandidateTimelineService timelineService,
        IRequisitionAssignmentService? assignmentService = null,
        IInterviewEvaluationService? interviewEvaluationService = null,
        IInterviewService? interviewService = null,
        ICandidateInterviewService? candidateInterviewService = null)
    {
        _dbContext = dbContext;
        _env = env;
        _emailService = emailService;
        _fileStorageService = fileStorageService;
        _talentPoolService = talentPoolService;
        _candidateExportService = candidateExportService;
        _logger = logger;
        _trackingService = trackingService;
        _referralService = referralService;
        _mergeService = mergeService;
        _timelineService = timelineService;
        _assignmentService = assignmentService;
        _interviewEvaluationService = interviewEvaluationService;
        _interviewService = interviewService;
        _candidateInterviewService = candidateInterviewService;
    }

    public WorkspaceController(
        ApplicationDbContext dbContext,
        IWebHostEnvironment env,
        IEmailService emailService,
        ILogger<WorkspaceController> logger)
        : this(dbContext, env, emailService, null!, null!, null!, logger, null!, null!, null!, null!, null!, null!, null!, null!)
    {
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
                var savedFilePath = await _fileStorageService.SaveCvAsync(cvFile, $"cv_{candidate.Id:N}");

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
                    FilePath = savedFilePath,
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

            var trackingCode = await _trackingService.GenerateUniqueTrackingCodeAsync();
            var withdrawToken = Guid.NewGuid().ToString("N");
            var application = new Application
            {
                Id = Guid.NewGuid(),
                JobPostingId = jobPosting.Id,
                CandidateId = candidate.Id,
                ResumeId = resume.Id,
                CurrentStageId = firstStage.Id,
                Status = ApplicationStatus.IN_PROCESS,
                TrackingCode = trackingCode,
                StageEnteredAt = DateTimeOffset.UtcNow,
                IsSlaOverdue = false,
                AppliedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                WithdrawToken = withdrawToken,
                CoverLetter = coverLetter?.Trim()
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

            // SCRUM-291: Gửi email xác nhận tiếp nhận hồ sơ kèm liên kết bảo mật rút hồ sơ
            var appCode = $"#NOV-2026-{application.Id.ToString("N")[..4].ToUpper()}";
            var withdrawUrl = Url.Action("Withdraw", "Applications", new { token = withdrawToken }, Request.Scheme)
                           ?? $"/Applications/Withdraw?token={withdrawToken}";

            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendApplicationReceivedAsync(
                        candidate.Email,
                        candidate.FirstName,
                        jobPosting.Title,
                        appCode,
                        withdrawUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Không thể gửi email xác nhận ứng tuyển: {Msg}", ex.Message);
                }
            });

            return Json(new
            {
                success = true,
                trackingCode = trackingCode,
                message = $"🎉 Chúc mừng bạn! Hồ sơ ứng tuyển vị trí '{jobPosting.Title}' đã được lưu vào hệ thống thành công. Mã tra cứu hồ sơ của bạn là: {trackingCode}. Đội ngũ tuyển dụng sẽ phản hồi trong 48 giờ."
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
                offer.Application.StageEnteredAt = DateTimeOffset.UtcNow;
                offer.Application.IsSlaOverdue = false;
                offer.Application.SlaOverdueAlertedAt = null;
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
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin}")]
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

            var descContent = string.IsNullOrWhiteSpace(description) ? "Chịu trách nhiệm thực hiện các mục tiêu công nghệ và sản phẩm trọng điểm của công ty." : description.Trim();

            // Tìm hoặc tạo JobPosition
            var posCode = "JOB-" + Guid.NewGuid().ToString("N")[..6].ToUpper();
            var jobPos = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = posCode,
                Title = title.Trim(),
                DepartmentId = dept.Id,
                JobLevel = "MIDDLE-SENIOR",
                Description = descContent,
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
                JobDescription = descContent,
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
        [FromForm] string? feedback,
        [FromForm] string? strengths,
        [FromForm] string? weaknesses,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var currentUserId);

            if (!interviewId.HasValue || interviewId.Value == Guid.Empty)
            {
                return Json(new { success = false, message = "Vui lòng chọn buổi phỏng vấn cần đánh giá." });
            }

            if (_interviewEvaluationService != null)
            {
                var input = new Ats.Web.Models.ViewModels.Interviews.InterviewEvaluationSubmitInputModel
                {
                    InterviewId = interviewId.Value,
                    Recommendation = recommendation ?? "",
                    OverallScore = score > 0 ? score : 4.0m,
                    DetailedFeedback = feedback,
                    Strengths = strengths,
                    Weaknesses = weaknesses
                };

                var (success, message, evalId) = await _interviewEvaluationService.SubmitEvaluationAsync(
                    input, currentUserId, cancellationToken);

                return Json(new
                {
                    success,
                    message,
                    interviewId = interviewId.Value,
                    evaluationId = evalId
                });
            }

            return Json(new { success = false, message = "Dịch vụ đánh giá chưa sẵn sàng." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gửi phiếu đánh giá phỏng vấn");
            return Json(new { success = false, message = "Lỗi khi lưu đánh giá: " + ex.Message });
        }
    }

    /// <summary>
    /// Lọc danh sách hồ sơ ứng viên theo giai đoạn (Stage) và phân công Recruiter.
    /// </summary>
    [HttpGet("FilterCandidates")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.HRManager},{UserRoles.HiringManager},{UserRoles.Recruiter},{UserRoles.Interviewer},Quản trị viên,Trưởng phòng nhân sự,Chuyên viên tuyển dụng,Người phỏng vấn")]
    public async Task<IActionResult> FilterCandidates([FromQuery] string? stage, [FromQuery] Guid? recruiterId)
    {
        try
        {
            var query = _dbContext.Applications
                .Include(a => a.Candidate)
                .Include(a => a.JobPosting)
                .Include(a => a.CurrentStage)
                .Where(a => !a.IsDeleted);

            var isRecruiterOnly = User.IsInRole(UserRoles.Recruiter) &&
                                  !User.IsInRole(UserRoles.Admin) &&
                                  !User.IsInRole(UserRoles.HRManager);

            if (isRecruiterOnly && _assignmentService != null)
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                _ = Guid.TryParse(userIdStr, out var currentUserId);
                if (currentUserId != Guid.Empty)
                {
                    var assignedReqIds = await _assignmentService.GetAssignedRequisitionIdsForRecruiterAsync(currentUserId);
                    query = query.Where(a => assignedReqIds.Contains(a.JobPosting.RequisitionId));
                }
            }
            else if (recruiterId.HasValue && recruiterId.Value != Guid.Empty && _assignmentService != null)
            {
                var filterReqIds = await _assignmentService.GetAssignedRequisitionIdsForRecruiterAsync(recruiterId.Value);
                query = query.Where(a => filterReqIds.Contains(a.JobPosting.RequisitionId));
            }

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
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.HiringManager},Quản trị viên,Trưởng phòng nhân sự,Chuyên viên tuyển dụng")]
    public async Task<IActionResult> AdvanceStage(
        [FromForm] Guid applicationId,
        [FromForm] Guid? toStageId,
        [FromForm] int? toStageOrder,
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

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var currentUserId);

            // Scrum 26: Recruiter chỉ được thao tác trên ứng viên của vị trí được giao
            var isRecruiterOnly = User?.Identity?.IsAuthenticated == true &&
                                  User.IsInRole(UserRoles.Recruiter) &&
                                  !User.IsInRole(UserRoles.Admin) &&
                                  !User.IsInRole(UserRoles.HRManager);
            if (isRecruiterOnly && _assignmentService != null && currentUserId != Guid.Empty)
            {
                var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, applicationId);
                if (!canAccess)
                {
                    return Json(new { success = false, message = "Bạn không có quyền chuyển giai đoạn hồ sơ này do không phụ trách vị trí tuyển dụng tương ứng." });
                }
            }

            var currentStage = application.CurrentStage;
            PipelineStage? targetStage = null;

            if (toStageId.HasValue && toStageId.Value != Guid.Empty)
            {
                targetStage = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.Id == toStageId.Value);
            }
            else if (toStageOrder.HasValue && toStageOrder.Value > 0)
            {
                targetStage = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == toStageOrder.Value);
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

            var effectiveUserId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync());

            var prevStageId = application.CurrentStageId;
            application.CurrentStageId = targetStage.Id;
            application.StageEnteredAt = DateTimeOffset.UtcNow;
            application.IsSlaOverdue = false;
            application.SlaOverdueAlertedAt = null;
            application.UpdatedAt = DateTimeOffset.UtcNow;

            if (targetStage.StageOrder == 6)
            {
                application.Status = ApplicationStatus.HIRED;
            }
            else if (targetStage.StageOrder == 7 || targetStage.Name.Contains("Từ chối", StringComparison.OrdinalIgnoreCase))
            {
                application.Status = ApplicationStatus.REJECTED;
            }
            else
            {
                application.Status = ApplicationStatus.IN_PROCESS;
            }

            var history = new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                FromStageId = prevStageId,
                ToStageId = targetStage.Id,
                ChangedByUserId = effectiveUserId,
                Comment = string.IsNullOrWhiteSpace(comment)
                    ? $"Chuyển giai đoạn từ '{currentStage?.Name ?? "ban đầu"}' sang '{targetStage.Name}' bởi {User?.Identity?.Name}."
                    : comment.Trim(),
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.ApplicationStageHistories.AddAsync(history);
            await _dbContext.SaveChangesAsync();

            // Nếu vượt qua sơ loại (Screening Passed), gửi email chúc mừng cho ứng viên
            if (targetStage!.StageOrder == 2 || targetStage.Name.Contains("Phỏng vấn", StringComparison.OrdinalIgnoreCase) || targetStage.Name.Contains("Sơ loại", StringComparison.OrdinalIgnoreCase))
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
                stageColor = targetStage.ColorCode,
                status = application.Status.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi chuyển giai đoạn ứng tuyển");
            return Json(new { success = false, message = "Lỗi khi chuyển giai đoạn: " + ex.Message });
        }
    }

    /// <summary>
    /// Lên lịch phỏng vấn, phân công hội đồng phỏng vấn và gửi thư mời email cho ứng viên (Scrum - Số 51).
    /// Hỗ trợ cả Form submit và JSON payload từ các modal tác nghiệp.
    /// </summary>
    [HttpPost("ScheduleInterview")]
    [HttpPost("/ung-vien/dat-lich-phong-van")]
    [HttpPost("/interviews/schedule")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.HiringManager},Quản trị viên,Trưởng phòng nhân sự,Chuyên viên tuyển dụng")]
    public async Task<IActionResult> ScheduleInterview(
        [FromForm] ScheduleInterviewInputModel? formInput,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ScheduleInterviewInputModel? model = formInput;
            if (Request.HasJsonContentType() || (Request.ContentType != null && Request.ContentType.Contains("application/json")))
            {
                try
                {
                    Request.EnableBuffering();
                    using var reader = new System.IO.StreamReader(Request.Body, System.Text.Encoding.UTF8, leaveOpen: true);
                    var bodyText = await reader.ReadToEndAsync(cancellationToken);
                    Request.Body.Position = 0;
                    if (!string.IsNullOrWhiteSpace(bodyText))
                    {
                        model = System.Text.Json.JsonSerializer.Deserialize<ScheduleInterviewInputModel>(
                            bodyText,
                            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                }
                catch (Exception jsonEx)
                {
                    _logger.LogWarning(jsonEx, "Lỗi khi đọc JSON payload ScheduleInterview");
                }
            }

            if (model == null || model.ApplicationId == Guid.Empty)
            {
                return Json(new { success = false, message = "Dữ liệu đặt lịch phỏng vấn không hợp lệ." });
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var currentUserId);

            var isRecruiterOnly = User?.Identity?.IsAuthenticated == true &&
                                  User.IsInRole(UserRoles.Recruiter) &&
                                  !User.IsInRole(UserRoles.Admin) &&
                                  !User.IsInRole(UserRoles.HRManager) &&
                                  !User.IsInRole("Quản trị viên") &&
                                  !User.IsInRole("Trưởng phòng nhân sự");

            if (_interviewService != null)
            {
                var result = await _interviewService.ScheduleInterviewAsync(model, currentUserId, isRecruiterOnly, cancellationToken);
                string? confirmUrl = null;
                if (result.Success && _candidateInterviewService != null && result.InterviewId.HasValue)
                {
                    var candEmail = await _dbContext.Applications
                        .Where(a => a.Id == model.ApplicationId)
                        .Select(a => a.Candidate.Email)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(candEmail))
                    {
                        confirmUrl = _candidateInterviewService.BuildConfirmationUrl(Request, result.InterviewId.Value, candEmail);
                    }
                }

                return Json(new
                {
                    success = result.Success,
                    message = result.Message,
                    interviewId = result.InterviewId,
                    confirmationUrl = confirmUrl,
                    roundNumber = result.RoundNumber,
                    title = result.Title,
                    startTime = result.StartTimeDisplay,
                    endTime = result.EndTimeDisplay,
                    interviewType = result.InterviewTypeDisplay,
                    locationOrLink = result.LocationOrLink,
                    panelists = result.Panelists,
                    errors = result.Errors
                });
            }

            var application = await _dbContext.Applications
                .Include(a => a.Candidate)
                .Include(a => a.JobPosting)
                .Include(a => a.CurrentStage)
                .FirstOrDefaultAsync(a => a.Id == model.ApplicationId, cancellationToken);

            if (application == null)
            {
                return Json(new { success = false, message = "Không tìm thấy hồ sơ ứng viên." });
            }

            if (isRecruiterOnly && _assignmentService != null && currentUserId != Guid.Empty)
            {
                var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, model.ApplicationId);
                if (!canAccess)
                {
                    return Json(new { success = false, message = "Bạn không có quyền lên lịch phỏng vấn cho hồ sơ này do không phụ trách vị trí tuyển dụng tương ứng." });
                }
            }

            var normalizedStart = model.GetNormalizedStartTime();
            var startDto = normalizedStart.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(normalizedStart.Value, DateTimeKind.Utc))
                : DateTimeOffset.UtcNow.AddDays(2).Date.AddHours(9);
            var endDto = model.EndTime.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(model.EndTime.Value, DateTimeKind.Utc))
                : startDto.AddMinutes(model.DurationMinutes > 0 ? model.DurationMinutes : 60);

            var title = string.IsNullOrWhiteSpace(model.RoundTitle) ? "Phỏng vấn Chuyên môn & Văn hóa NoveraTech" : model.RoundTitle.Trim();
            var meetLocation = string.IsNullOrWhiteSpace(model.LocationOrLink) ? "Google Meet (Link phòng họp gửi qua lịch hẹn)" : model.LocationOrLink.Trim();

            var interview = new Interview
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                RoundNumber = model.RoundNumber >= 1 ? model.RoundNumber : 1,
                Title = title,
                InterviewType = meetLocation.Contains("http", StringComparison.OrdinalIgnoreCase) ? InterviewType.ONLINE_MEET : InterviewType.OFFLINE_OFFICE,
                LocationOrLink = meetLocation,
                StartTime = startDto,
                EndTime = endDto,
                Status = InterviewStatus.SCHEDULED,
                CandidateConfirmed = CandidateConfirmStatus.PENDING,
                CandidateNotes = model.Note ?? model.Notes,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.Interviews.AddAsync(interview, cancellationToken);

            var interviewerIds = model.GetNormalizedInterviewerIds();
            var panelistNames = new List<string>();
            if (interviewerIds.Any())
            {
                var interviewers = await _dbContext.Users.Where(u => interviewerIds.Contains(u.Id)).ToListAsync(cancellationToken);
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
                    }, cancellationToken);
                    panelistNames.Add($"{inv.FullName} ({inv.Role})");
                }
            }
            if (!panelistNames.Any())
            {
                panelistNames.Add("Ban Tuyển Dụng NoveraTech");
            }

            var interviewStage = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 3 || s.Name.Contains("Phỏng vấn"), cancellationToken)
                ?? await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 2, cancellationToken);

            if (interviewStage != null && application.CurrentStageId != interviewStage.Id)
            {
                var prev = application.CurrentStageId;
                application.CurrentStageId = interviewStage.Id;
                application.StageEnteredAt = DateTimeOffset.UtcNow;
                application.IsSlaOverdue = false;
                application.SlaOverdueAlertedAt = null;
                application.UpdatedAt = DateTimeOffset.UtcNow;

                var effectiveUserId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync(cancellationToken));

                await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = application.Id,
                    FromStageId = prev,
                    ToStageId = interviewStage.Id,
                    ChangedByUserId = effectiveUserId,
                    Comment = $"Đã lên lịch phỏng vấn: {title} ({startDto:dd/MM/yyyy HH:mm}).",
                    CreatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            var candFullName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();
            var panelistStr = string.Join(", ", panelistNames);
            string? confirmUrlFallback = null;
            if (_candidateInterviewService != null)
            {
                confirmUrlFallback = _candidateInterviewService.BuildConfirmationUrl(Request, interview.Id, application.Candidate.Email);
            }
            else
            {
                var scheme = Request.Scheme ?? "https";
                var host = Request.Host.Value;
                confirmUrlFallback = $"{scheme}://{host}/xac-nhan-phong-van";
            }

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
                        panelistStr,
                        model.Note ?? model.Notes,
                        confirmUrlFallback);
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
                confirmationUrl = confirmUrlFallback,
                startTime = startDto.ToString("dd/MM/yyyy HH:mm"),
                panelists = panelistStr
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xếp lịch phỏng vấn");
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

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _ = Guid.TryParse(userIdStr, out var currentUserId);

            // Scrum 26: Recruiter chỉ được tạo Offer cho ứng viên của vị trí được giao
            var isRecruiterOnly = User?.Identity?.IsAuthenticated == true &&
                                  User.IsInRole(UserRoles.Recruiter) &&
                                  !User.IsInRole(UserRoles.Admin) &&
                                  !User.IsInRole(UserRoles.HRManager);
            if (isRecruiterOnly && _assignmentService != null && currentUserId != Guid.Empty)
            {
                var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, applicationId);
                if (!canAccess)
                {
                    return Json(new { success = false, message = "Bạn không có quyền tạo đề xuất Offer cho hồ sơ này do không phụ trách vị trí tuyển dụng tương ứng." });
                }
            }

            if (baseSalary <= 0)
            {
                return Json(new { success = false, message = "Mức lương cơ bản phải lớn hơn 0." });
            }

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
                application.StageEnteredAt = DateTimeOffset.UtcNow;
                application.IsSlaOverdue = false;
                application.SlaOverdueAlertedAt = null;
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
        [FromForm] Guid? rejectionReasonId,
        [FromForm] string? reason,
        [FromForm] string? feedbackNote,
        [FromForm] bool sendEmail = true,
        [FromForm] bool addToTalentPool = false,
        [FromForm] string? talentPoolReason = null,
        [FromForm] string? targetJobTitle = null,
        [FromForm] string? talentPoolSkills = null,
        [FromForm] int? talentPoolRating = null)
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

            // Scrum 26: Recruiter chỉ được từ chối ứng viên của vị trí được giao
            var isRecruiterOnly = User?.Identity?.IsAuthenticated == true &&
                                  User.IsInRole(UserRoles.Recruiter) &&
                                  !User.IsInRole(UserRoles.Admin) &&
                                  !User.IsInRole(UserRoles.HRManager);
            if (isRecruiterOnly && _assignmentService != null && currentUserId != Guid.Empty)
            {
                var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, applicationId);
                if (!canAccess)
                {
                    return Json(new { success = false, message = "Bạn không có quyền từ chối hồ sơ này do không phụ trách vị trí tuyển dụng tương ứng." });
                }
            }

            var prev = application.CurrentStageId;
            var prevStageName = application.CurrentStage?.Name ?? "Vòng phỏng vấn cuối";
            application.Status = ApplicationStatus.REJECTED;
            application.UpdatedAt = DateTimeOffset.UtcNow;

            var rejectStage = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 7 || s.Name.Contains("Từ chối"));
            if (rejectStage != null)
            {
                application.CurrentStageId = rejectStage.Id;
                application.StageEnteredAt = DateTimeOffset.UtcNow;
                application.IsSlaOverdue = false;
            }

            // Gắn mã lý do từ danh mục dùng chung (RecruitmentCatalogs REJECTION_REASON)
            string rejectReasonTitle = reason?.Trim() ?? string.Empty;
            if (rejectionReasonId.HasValue && rejectionReasonId.Value != Guid.Empty)
            {
                var catReason = await _dbContext.RecruitmentCatalogs.FirstOrDefaultAsync(c => c.Id == rejectionReasonId.Value);
                if (catReason != null)
                {
                    application.RejectionReasonId = catReason.Id;
                    if (string.IsNullOrWhiteSpace(rejectReasonTitle))
                    {
                        rejectReasonTitle = catReason.Name;
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(rejectReasonTitle))
            {
                var catReason = await _dbContext.RecruitmentCatalogs
                    .FirstOrDefaultAsync(c => c.CatalogType == "REJECTION_REASON" && c.Name.ToLower() == rejectReasonTitle.ToLower());
                if (catReason != null)
                {
                    application.RejectionReasonId = catReason.Id;
                }
            }

            if (string.IsNullOrWhiteSpace(rejectReasonTitle))
            {
                rejectReasonTitle = "Hồ sơ chưa phù hợp với tiêu chuẩn yêu cầu của vị trí ở thời điểm hiện tại.";
            }

            var cleanFeedback = !string.IsNullOrWhiteSpace(feedbackNote) ? feedbackNote.Trim() : null;
            application.RejectionNote = cleanFeedback ?? rejectReasonTitle;

            var commentBuilder = $"Từ chối hồ sơ: {rejectReasonTitle}";
            if (!string.IsNullOrWhiteSpace(cleanFeedback))
            {
                commentBuilder += $" (Ghi chú: {cleanFeedback})";
            }

            await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                FromStageId = prev,
                ToStageId = rejectStage?.Id ?? prev,
                ChangedByUserId = effectiveUserId,
                Comment = commentBuilder,
                CreatedAt = DateTimeOffset.UtcNow
            });

            await _dbContext.SaveChangesAsync();

            // Scrum 45: Đưa ứng viên trượt vòng cuối vào Kho tiềm năng kèm lý do
            string talentPoolMessage = "";
            if (addToTalentPool)
            {
                try
                {
                    var finalPoolReason = !string.IsNullOrWhiteSpace(talentPoolReason)
                        ? talentPoolReason.Trim()
                        : (!string.IsNullOrWhiteSpace(cleanFeedback) ? cleanFeedback : rejectReasonTitle);

                    var finalTargetTitle = !string.IsNullOrWhiteSpace(targetJobTitle)
                        ? targetJobTitle.Trim()
                        : (application.JobPosting?.Title ?? "Chuyên viên");

                    await _talentPoolService.AddCandidateToPoolAsync(new Ats.Web.Models.ViewModels.TalentPool.AddTalentPoolCandidateInputModel
                    {
                        CandidateId = application.CandidateId,
                        SourceApplicationId = application.Id,
                        LastStageReached = prevStageName,
                        Reason = finalPoolReason,
                        TargetJobTitle = finalTargetTitle,
                        Skills = talentPoolSkills,
                        Rating = talentPoolRating ?? 4,
                        Notes = $"Tự động lưu từ kết quả phỏng vấn vị trí {application.JobPosting?.Title}. Lý do dừng: {rejectReasonTitle}"
                    }, effectiveUserId);

                    talentPoolMessage = " và đã lưu vào Kho ứng viên tiềm năng";
                }
                catch (Exception poolEx)
                {
                    _logger.LogWarning("Không thể lưu ứng viên vào kho tiềm năng: {Msg}", poolEx.Message);
                }
            }

            var candFullName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();
            var jobPostingTitle = application.JobPosting?.Title ?? "Vị trí tuyển dụng";
            if (sendEmail)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.SendRejectionLetterAsync(
                            application.Candidate.Email,
                            candFullName,
                            jobPostingTitle,
                            rejectReasonTitle);
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
                message = $"Đã cập nhật trạng thái Từ chối cho hồ sơ ứng viên {candFullName}{talentPoolMessage}{(sendEmail ? " và gửi email thư cảm ơn lịch sự." : ".")}"
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
    [HttpGet("/ung-vien/danh-sach-nguoi-phong-van")]
    public async Task<IActionResult> GetInterviewers(CancellationToken cancellationToken = default)
    {
        try
        {
            var interviewers = _interviewService != null
                ? await _interviewService.GetAvailableInterviewersAsync(cancellationToken)
                : await _dbContext.Users
                    .Where(u => u.Status == "ACTIVE")
                    .OrderBy(u => u.FullName)
                    .Select(u => new InterviewerLookupDto
                    {
                        Id = u.Id,
                        FullName = u.FullName,
                        Email = u.Email,
                        Role = u.Role
                    })
                    .ToListAsync(cancellationToken);

            var list = interviewers.Select(u => new
            {
                id = u.Id,
                name = u.FullName,
                fullName = u.FullName,
                email = u.Email,
                role = u.Role,
                displayText = u.DisplayText
            }).ToList();

            return Json(new { success = true, data = list, interviewers = list });
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

            var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(currentUserIdStr, out var currentUserId);
            var currentUserEmail = (User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "").ToLower();

            var isStaff = User.IsInRole(UserRoles.Admin) ||
                          User.IsInRole(UserRoles.HRManager) ||
                          User.IsInRole(UserRoles.HiringManager) ||
                          User.IsInRole(UserRoles.Recruiter) ||
                          User.IsInRole(UserRoles.Interviewer) ||
                          User.IsInRole("Quản trị viên") ||
                          User.IsInRole("Trưởng phòng nhân sự") ||
                          User.IsInRole("Chuyên viên tuyển dụng") ||
                          User.IsInRole("Người phỏng vấn");

            var isOwner = (currentUserId != Guid.Empty && app.Candidate.UserId == currentUserId) ||
                          (!string.IsNullOrEmpty(currentUserEmail) && app.Candidate.Email.ToLower() == currentUserEmail);

            if (!isStaff && !isOwner)
            {
                return Json(new { success = false, message = "Truy cập bị từ chối: Bạn không có quyền xem thông tin hồ sơ này." });
            }

            // Scrum 26: Recruiter chỉ nhìn thấy ứng viên của vị trí được giao
            var isRecruiterOnly = User?.Identity?.IsAuthenticated == true &&
                                  User.IsInRole(UserRoles.Recruiter) &&
                                  !User.IsInRole(UserRoles.Admin) &&
                                  !User.IsInRole(UserRoles.HRManager);
            if (isRecruiterOnly && _assignmentService != null && currentUserId != Guid.Empty)
            {
                var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, appId);
                if (!canAccess)
                {
                    return Json(new { success = false, message = "Bạn không có quyền xem chi tiết hồ sơ ứng viên này (chỉ phụ trách các vị trí được phân công)." });
                }
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

    /// <summary>
    /// SCRUM-328: Xuất danh sách ứng viên rút gọn ra file Excel cho Trưởng bộ phận theo các bộ lọc đang áp dụng
    /// </summary>
    [HttpGet("ExportCandidates")]
    public async Task<IActionResult> ExportCandidates(
        [FromQuery] Guid? jobId,
        [FromQuery] string? stage,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        try
        {
            var exporterName = User.FindFirstValue(ClaimTypes.Name)
                ?? User.FindFirstValue("name")
                ?? User.FindFirstValue(ClaimTypes.Email)
                ?? "Nhân viên tuyển dụng";

            var (fileBytes, fileName, contentType) = await _candidateExportService.ExportCandidatesShortlistAsync(
                jobId,
                stage,
                search,
                exporterName,
                cancellationToken);

            return File(fileBytes, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xuất danh sách ứng viên ra Excel (SCRUM-328)");
            TempData["ErrorMessage"] = "Không thể xuất danh sách ứng viên ra Excel: " + ex.Message;
            return RedirectToAction("Index", "Home", new { tab = "ung-vien" });
        }
    }

    #endregion

    #region Internal Candidate Referral (Scrum #37)

    /// <summary>
    /// GET: /gioi-thieu-ung-vien hoặc /Workspace/ReferCandidate
    /// Mở form giới thiệu ứng viên nội bộ
    /// </summary>
    [HttpGet("/gioi-thieu-ung-vien")]
    [HttpGet("ReferCandidate")]
    public async Task<IActionResult> ReferCandidate([FromQuery] Guid? jobId)
    {
        var openJobs = await _referralService.GetOpenJobPostingsAsync();
        ViewBag.OpenJobs = openJobs;

        var model = new ReferCandidateInputModel();
        if (jobId.HasValue && jobId.Value != Guid.Empty)
        {
            model.JobPostingId = jobId.Value;
        }
        else if (openJobs.Any())
        {
            model.JobPostingId = openJobs.First().Id;
        }

        return View(model);
    }

    /// <summary>
    /// POST: /gioi-thieu-ung-vien hoặc /Workspace/ReferCandidate
    /// Tiếp nhận và xử lý hồ sơ người quen do nhân viên nội bộ gửi
    /// </summary>
    [HttpPost("/gioi-thieu-ung-vien")]
    [HttpPost("ReferCandidate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReferCandidate([FromForm] ReferCandidateInputModel model)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(userIdStr, out var currentUserId);

        if (currentUserId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Không thể xác thực thông tin nhân viên đăng nhập.";
            return RedirectToAction(nameof(ReferCandidate));
        }

        if (!ModelState.IsValid)
        {
            ViewBag.OpenJobs = await _referralService.GetOpenJobPostingsAsync();
            return View(model);
        }

        var (success, message, appId, trackingCode) = await _referralService.ReferCandidateAsync(model, currentUserId);
        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            ViewBag.OpenJobs = await _referralService.GetOpenJobPostingsAsync();
            return View(model);
        }

        TempData["SuccessMessage"] = message;
        TempData["TrackingCode"] = trackingCode;
        return RedirectToAction(nameof(MyReferrals));
    }

    /// <summary>
    /// GET: /ho-so-gioi-thieu hoặc /Workspace/MyReferrals
    /// Xem danh sách hồ sơ do chính nhân viên nội bộ giới thiệu và theo dõi trạng thái tổng quát
    /// </summary>
    [HttpGet("/ho-so-gioi-thieu")]
    [HttpGet("MyReferrals")]
    public async Task<IActionResult> MyReferrals()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(userIdStr, out var currentUserId);

        if (currentUserId == Guid.Empty)
        {
            return Redirect("/Account/StaffLogin");
        }

        var model = await _referralService.GetMyReferralsAsync(currentUserId);
        return View(model);
    }

    #endregion

    #region Candidate Merge & Duplicate Detection (Scrum #40)

    /// <summary>
    /// <summary>
    /// GET: /dashboard/trung-lap hoặc /Workspace/HoSoTrungLap hoặc /Workspace/CandidateDuplicates
    /// Màn hình quản lý và cảnh báo hồ sơ ứng viên trùng lặp (Scrum #40)
    /// </summary>
    [HttpGet("/dashboard/trung-lap")]
    [HttpGet("HoSoTrungLap")]
    [HttpGet("CandidateDuplicates")]
    public async Task<IActionResult> CandidateDuplicates([FromQuery] string tab = "duplicates")
    {
        var model = await _mergeService.GetDuplicateOverviewAsync();
        model.ActiveTab = tab == "history" ? "history" : "duplicates";
        return View(model);
    }

    /// <summary>
    /// GET: /Workspace/SoSanhHoSo hoặc /Workspace/CompareCandidates?primaryId={guid}&secondaryId={guid}
    /// Màn hình so sánh cạnh nhau 2 hồ sơ ứng viên trước khi gộp (Scrum #40)
    /// </summary>
    [HttpGet("SoSanhHoSo")]
    [HttpGet("CompareCandidates")]
    [HttpGet("/dashboard/so-sanh-ung-vien")]
    public async Task<IActionResult> CompareCandidates([FromQuery] Guid primaryId, [FromQuery] Guid? secondaryId)
    {
        if (primaryId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Vui lòng chọn hồ sơ hợp lệ để thực hiện so sánh.";
            return RedirectToAction(nameof(CandidateDuplicates));
        }

        var actualSecondaryId = secondaryId.GetValueOrDefault();
        if (actualSecondaryId == Guid.Empty)
        {
            // Tự động tìm ứng viên có nguy cơ trùng lặp nếu chưa chỉ định secondaryId
            var duplicates = await _mergeService.FindDuplicatesForCandidateAsync(primaryId);
            if (duplicates != null && duplicates.Count > 0)
            {
                actualSecondaryId = duplicates[0].CandidateId;
            }
        }

        if (actualSecondaryId == Guid.Empty || primaryId == actualSecondaryId)
        {
            TempData["ErrorMessage"] = "Vui lòng chọn 2 hồ sơ hợp lệ để thực hiện so sánh.";
            return RedirectToAction(nameof(CandidateDuplicates));
        }

        var model = await _mergeService.GetComparisonAsync(primaryId, actualSecondaryId);
        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy hồ sơ ứng viên cần so sánh.";
            return RedirectToAction(nameof(CandidateDuplicates));
        }

        return View("CompareCandidates", model);
    }

    /// <summary>
    /// POST: /Workspace/GopHoSo hoặc /Workspace/MergeCandidates
    /// Thực hiện gộp hồ sơ: chuyển toàn bộ lịch sử ứng tuyển và CV sang hồ sơ chính
    /// </summary>
    [HttpPost("GopHoSo")]
    [HttpPost("MergeCandidates")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MergeCandidates([FromForm] MergeCandidatesRequestModel model)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(userIdStr, out var currentUserId);

        if (currentUserId == Guid.Empty)
        {
            return Json(new { success = false, message = "Phiên làm việc hết hạn. Vui lòng đăng nhập lại." });
        }

        if (!ModelState.IsValid)
        {
            return Json(new { success = false, message = "Dữ liệu yêu cầu không hợp lệ." });
        }

        var (success, message, historyId) = await _mergeService.MergeCandidatesAsync(model, currentUserId);

        if (!success)
        {
            return Json(new { success = false, message });
        }

        return Json(new
        {
            success = true,
            message,
            historyId,
            redirectUrl = Url.Action(nameof(CandidateDuplicates), new { tab = "history" })
        });
    }

    /// <summary>
    /// POST: /Workspace/HoanTacGop hoặc /Workspace/UndoMerge
    /// Hoàn tác gộp hồ sơ nếu còn trong thời hạn 24 giờ
    /// </summary>
    [HttpPost("HoanTacGop")]
    [HttpPost("UndoMerge")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoMerge([FromForm] Guid historyId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(userIdStr, out var currentUserId);

        if (currentUserId == Guid.Empty)
        {
            return Json(new { success = false, message = "Phiên làm việc hết hạn. Vui lòng đăng nhập lại." });
        }

        if (historyId == Guid.Empty)
        {
            return Json(new { success = false, message = "Mã phiên gộp không hợp lệ." });
        }

        var (success, message) = await _mergeService.UndoMergeAsync(historyId, currentUserId);

        return Json(new
        {
            success,
            message,
            redirectUrl = Url.Action(nameof(CandidateDuplicates), new { tab = "history" })
        });
    }

    #endregion

    #region Candidate Interaction Timeline (Scrum #46)

    /// <summary>
    /// GET: /Workspace/NhatKyTuongTac hoặc /Workspace/CandidateTimeline
    /// Màn hình dòng thời gian toàn bộ tương tác của ứng viên
    /// </summary>
    [HttpGet("NhatKyTuongTac")]
    [HttpGet("CandidateTimeline")]
    public async Task<IActionResult> CandidateTimeline(
        [FromQuery] Guid? candidateId,
        [FromQuery] Guid? applicationId,
        [FromQuery] string? filter,
        [FromQuery] string? q)
    {
        if (!candidateId.HasValue && applicationId.HasValue)
        {
            var app = await _dbContext.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId.Value);
            if (app != null)
            {
                candidateId = app.CandidateId;
            }
        }

        if (!candidateId.HasValue)
        {
            TempData["ErrorMessage"] = "Vui lòng chọn ứng viên để xem nhật ký tương tác.";
            return RedirectToAction("Index", "Home", new { tab = "pipeline" });
        }

        var model = await _timelineService.GetCandidateTimelineAsync(candidateId.Value, applicationId, filter, q);
        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy dữ liệu ứng viên hoặc hồ sơ tương ứng.";
            return RedirectToAction("Index", "Home", new { tab = "pipeline" });
        }

        return View("CandidateTimeline", model);
    }

    /// <summary>
    /// GET: /Workspace/GetCandidateTimelineJson
    /// API AJAX nạp dòng thời gian tương tác phục vụ Modal/Drawer
    /// </summary>
    [HttpGet("GetCandidateTimelineJson")]
    public async Task<IActionResult> GetCandidateTimelineJson(
        [FromQuery] Guid? candidateId,
        [FromQuery] Guid? applicationId,
        [FromQuery] string? filter,
        [FromQuery] string? q)
    {
        if (!candidateId.HasValue && applicationId.HasValue)
        {
            var app = await _dbContext.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId.Value);
            if (app != null)
            {
                candidateId = app.CandidateId;
            }
        }

        if (!candidateId.HasValue)
        {
            return Json(new { success = false, message = "Chưa cung cấp thông tin ứng viên." });
        }

        var model = await _timelineService.GetCandidateTimelineAsync(candidateId.Value, applicationId, filter, q);
        if (model == null)
        {
            return Json(new { success = false, message = "Không tìm thấy thông tin ứng viên." });
        }

        return Json(new { success = true, data = model });
    }

    /// <summary>
    /// POST: /Workspace/AddCandidateInteraction
    /// API ghi nhận cuộc gọi / trao đổi trực tiếp với ứng viên
    /// </summary>
    [HttpPost("AddCandidateInteraction")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCandidateInteraction([FromForm] AddCandidateInteractionInputModel model)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(userIdStr, out var currentUserId);

        if (currentUserId == Guid.Empty)
        {
            return Json(new { success = false, message = "Phiên làm việc hết hạn. Vui lòng đăng nhập lại." });
        }

        if (!ModelState.IsValid)
        {
            var errors = string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return Json(new { success = false, message = errors.Length > 0 ? errors : "Dữ liệu nhập không hợp lệ." });
        }

        var (success, message, interactionId) = await _timelineService.AddInteractionAsync(model, currentUserId);

        return Json(new
        {
            success,
            message,
            interactionId
        });
    }

    #endregion
}
