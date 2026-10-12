using System.Diagnostics;
using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Workspace;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IJobService _jobService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        ApplicationDbContext dbContext,
        IJobService jobService,
        ILogger<HomeController> logger)
    {
        _dbContext = dbContext;
        _jobService = jobService;
        _logger = logger;
    }

    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(Dashboard));
        }

        return View("Landing");
    }

    [HttpGet("/dashboard")]
    [HttpGet("/dashboard/{feature}")]
    public async Task<IActionResult> Dashboard(string? feature = null)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = string.IsNullOrWhiteSpace(feature) ? "/dashboard" : $"/dashboard/{feature}";
            return Redirect($"/Account/StaffLogin?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var normalizedFeature = string.IsNullOrWhiteSpace(feature) ? "tong-quan" : feature.Trim().ToLowerInvariant();
        if (normalizedFeature == "tuyen-dung" || normalizedFeature == "pipeline")
        {
            normalizedFeature = "ung-vien";
        }
        ViewBag.ActiveFeature = normalizedFeature;

        var model = await BuildWorkspaceDashboardViewModelAsync();
        return View("Index", model);
    }

    private async Task<WorkspaceDashboardViewModel> BuildWorkspaceDashboardViewModelAsync()
    {
        var model = new WorkspaceDashboardViewModel
        {
            IsAuthenticated = true
        };

        var isCandidate = User.IsInRole(UserRoles.Candidate);
        var isStaff = !isCandidate;
        var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(userIdStr, out var userId);

        model.IsCandidate = isCandidate;
        model.IsStaff = isStaff;
        model.UserFullName = User.Identity?.Name ?? "Người dùng";
        model.UserEmail = userEmail;
        model.UserRolesList = User.FindAll(ClaimTypes.Role)
            .Select(c => UserRoles.GetDisplayName(c.Value))
            .Distinct()
            .ToList();

        // Đồng bộ tổng số vị trí đang mở trên toàn hệ thống cho mọi vai trò
        model.TotalOpenJobs = await _dbContext.JobPostings.CountAsync(j => j.Status == JobPostingStatus.PUBLISHED);

        if (isCandidate)
        {
            // Load hồ sơ của ứng viên hiện tại
            var candidate = await _dbContext.Candidates
                .Include(c => c.Resumes)
                .FirstOrDefaultAsync(c => (userId != Guid.Empty && c.UserId == userId) || c.Email.ToLower() == userEmail.ToLower());

            if (candidate != null)
            {
                var primaryResume = candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? candidate.Resumes.FirstOrDefault();
                model.CandidateProfile = new CandidateProfileDto
                {
                    Id = candidate.Id,
                    FullName = candidate.FirstName + (string.IsNullOrWhiteSpace(candidate.LastName) ? "" : " " + candidate.LastName),
                    Email = candidate.Email,
                    Phone = candidate.Phone,
                    ResumeFileName = primaryResume?.FileName ?? "Chưa có CV đính kèm",
                    ResumeUpdatedAt = primaryResume?.UpdatedAt.ToString("dd/MM/yyyy") ?? DateTime.UtcNow.ToString("dd/MM/yyyy"),
                    LinkedinUrl = candidate.LinkedinUrl,
                    HasResume = primaryResume != null
                };

                // Lấy đơn ứng tuyển mới nhất còn trong quy trình
                var activeApp = await _dbContext.Applications
                    .Include(a => a.JobPosting)
                    .Include(a => a.CurrentStage)
                    .Include(a => a.StageHistories)
                    .Include(a => a.Interviews)
                    .Where(a => a.CandidateId == candidate.Id && !a.IsDeleted)
                    .OrderByDescending(a => a.AppliedAt)
                    .FirstOrDefaultAsync();

                if (activeApp != null)
                {
                    var stageOrder = activeApp.CurrentStage?.StageOrder ?? 1;
                    int progress = stageOrder switch
                    {
                        1 => 20,
                        2 => 40,
                        3 or 4 => 66,
                        5 => 85,
                        6 => 100,
                        _ => 50
                    };

                    var isWithdrawn = activeApp.Status == ApplicationStatus.WITHDRAWN;
                    model.ActiveApplication = new ApplicationTrackerDto
                    {
                        ApplicationId = activeApp.Id,
                        ApplicationCode = $"#NOV-2026-{activeApp.Id.ToString("N")[..4].ToUpper()}",
                        JobTitle = activeApp.JobPosting?.Title ?? "Vị trí tại NoveraTech",
                        JobId = activeApp.JobPostingId.ToString(),
                        CurrentStageName = isWithdrawn ? "Ứng viên đã rút hồ sơ" : (activeApp.CurrentStage?.Name ?? "Đang xử lý"),
                        CurrentStageOrder = stageOrder,
                        ProgressPercentage = isWithdrawn ? 0 : progress,
                        StatusDisplay = isWithdrawn ? "Đã rút hồ sơ" : activeApp.Status.ToString(),
                        AppliedDateDisplay = activeApp.AppliedAt?.ToString("dd/MM/yyyy") ?? activeApp.CreatedAt.ToString("dd/MM/yyyy"),
                        WithdrawToken = activeApp.WithdrawToken,
                        IsWithdrawn = isWithdrawn,
                        WithdrawnAt = activeApp.WithdrawnAt,
                        WithdrawReason = activeApp.WithdrawReason,
                        Steps = new List<StageStepDto>
                        {
                            new() { StepNumber = 1, StepName = "1. Nộp hồ sơ", Status = stageOrder >= 1 ? "completed" : "pending", Subtitle = activeApp.AppliedAt?.ToString("dd/MM/yyyy") ?? "Hoàn tất" },
                            new() { StepNumber = 2, StepName = "2. Sơ loại CV", Status = isWithdrawn ? "pending" : (stageOrder > 2 ? "completed" : (stageOrder == 2 ? "active" : "pending")), Subtitle = isWithdrawn ? "Đã dừng quy trình" : (stageOrder >= 2 ? "Đã đạt" : "Chờ xử lý") },
                            new() { StepNumber = 3, StepName = "3. Phỏng vấn", Status = isWithdrawn ? "pending" : (stageOrder > 4 ? "completed" : (stageOrder is 3 or 4 ? "active" : "pending")), Subtitle = isWithdrawn ? "Đã dừng quy trình" : (stageOrder is 3 or 4 ? "Vòng chuyên môn" : (stageOrder > 4 ? "Đã đạt" : "Chờ lịch")) },
                            new() { StepNumber = 4, StepName = "4. Đề nghị Offer", Status = isWithdrawn ? "pending" : (stageOrder == 6 ? "completed" : (stageOrder == 5 ? "active" : "pending")), Subtitle = isWithdrawn ? "Đã dừng quy trình" : (stageOrder == 6 ? "Tuyển dụng thành công" : (stageOrder == 5 ? "Chờ duyệt" : "Chưa mở")) }
                        }
                    };

                    // Tìm lịch phỏng vấn của ứng viên
                    var interview = activeApp.Interviews
                        .Where(i => i.Status == InterviewStatus.SCHEDULED)
                        .OrderBy(i => i.StartTime)
                        .FirstOrDefault();

                    if (interview != null)
                    {
                        model.CandidateInterview = new UpcomingInterviewDto
                        {
                            InterviewId = interview.Id,
                            ApplicationId = activeApp.Id,
                            JobTitle = activeApp.JobPosting?.Title ?? "",
                            RoundTitle = interview.Title,
                            StartTime = interview.StartTime,
                            EndTime = interview.EndTime,
                            TimeDisplay = interview.StartTime.HasValue && interview.EndTime.HasValue
                                ? $"{interview.StartTime.Value:HH:mm} - {interview.EndTime.Value:HH:mm}"
                                : "14:00 - 15:30",
                            DateDisplay = interview.StartTime.HasValue
                                ? $"{interview.StartTime.Value:dddd, dd/MM/yyyy}"
                                : "Thứ Sáu, tuần này",
                            LocationOrLink = string.IsNullOrWhiteSpace(interview.LocationOrLink) ? "https://meet.google.com/nov-interview-089" : interview.LocationOrLink,
                            PanelistNames = "Lường Minh Hiếu (CEO), Ngô Quang Huy (Tech Lead)",
                            CandidateConfirmed = interview.CandidateConfirmed,
                            StatusDisplay = interview.CandidateConfirmed == CandidateConfirmStatus.ACCEPTED ? "Đã xác nhận tham gia" : (interview.CandidateConfirmed == CandidateConfirmStatus.DECLINED ? "Đã gửi yêu cầu đổi lịch" : "Chờ xác nhận")
                        };
                    }
                }
            }

            // Lấy danh sách việc làm mở để ứng viên có thể ứng tuyển nhanh
            model.CandidateOpenJobs = await _jobService.GetHotJobsAsync(6);
        }
        else
        {
            // Dành cho nhân sự (Staff / HR / Recruiter / Manager / Approver / Admin)
            model.TotalOpenJobs = await _dbContext.JobPostings.CountAsync(j => j.Status == JobPostingStatus.PUBLISHED);
            // Pipeline đang hoạt động: Loại trừ hồ sơ mà ứng viên đã rút
            model.TotalApplications = await _dbContext.Applications.CountAsync(a => !a.IsDeleted && a.Status != ApplicationStatus.WITHDRAWN);

            var nowUtc = DateTimeOffset.UtcNow;
            var todayUtc = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, TimeSpan.Zero);
            var startOfWeek = todayUtc.AddDays(-(int)nowUtc.DayOfWeek + 1);
            var endOfWeek = startOfWeek.AddDays(7);
            model.TotalInterviewsThisWeek = await _dbContext.Interviews.CountAsync(i => i.StartTime >= startOfWeek && i.StartTime <= endOfWeek);
            if (model.TotalInterviewsThisWeek == 0)
            {
                model.TotalInterviewsThisWeek = await _dbContext.Interviews.CountAsync(i => i.Status == InterviewStatus.SCHEDULED);
            }

            model.TotalPendingOffers = await _dbContext.JobOffers.CountAsync(o => o.Status == OfferStatus.PENDING_APPROVAL || o.Status == OfferStatus.DRAFT);

            // Phễu tuyển dụng thật (chỉ tính hồ sơ đang trong pipeline hoạt động)
            var stages = await _dbContext.PipelineStages.OrderBy(s => s.StageOrder).ToListAsync();
            var appCountsByStage = await _dbContext.Applications
                .Where(a => !a.IsDeleted && a.Status != ApplicationStatus.WITHDRAWN)
                .GroupBy(a => a.CurrentStageId)
                .Select(g => new { StageId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.StageId, x => x.Count);

            int totalApps = Math.Max(model.TotalApplications, 1);
            var funnelList = new List<PipelineFunnelStageDto>();
            foreach (var stg in stages)
            {
                appCountsByStage.TryGetValue(stg.Id, out var count);
                var pct = Math.Round((double)count / totalApps * 100, 1);
                var (shortName, stageColor, isTerminal) = stg.StageOrder switch
                {
                    1 => ("Ứng tuyển", "#2563EB", false),
                    2 => ("Sàng lọc CV", "#4F46E5", false),
                    3 => ("PV Sơ loại", "#0891B2", false),
                    4 => ("PV Chuyên môn", "#D97706", false),
                    5 => ("Đề xuất Offer", "#7C3AED", false),
                    6 => ("Đã tuyển dụng", "#059669", false),
                    7 => ("Đã từ chối", "#E11D48", true),
                    _ => (stg.Name, "#64748B", false)
                };

                var subtitle = stg.StageOrder switch
                {
                    1 => "Cổng ứng tuyển",
                    2 => "Đánh giá hồ sơ",
                    3 => "Phỏng vấn sơ bộ",
                    4 => "Đánh giá nghiệp vụ",
                    5 => "Thương thảo Offer",
                    6 => "Đã ký HĐLĐ",
                    7 => "Hồ sơ lưu trữ",
                    _ => "Tiến trình"
                };

                funnelList.Add(new PipelineFunnelStageDto
                {
                    StageOrder = stg.StageOrder,
                    StageName = stg.Name,
                    ShortName = shortName,
                    ColorCode = stageColor,
                    Count = count,
                    Percentage = pct,
                    Subtitle = subtitle,
                    IsTerminal = isTerminal
                });
            }
            model.PipelineFunnel = funnelList;

            // Lịch phỏng vấn tuần này
            var dbInterviews = await _dbContext.Interviews
                .Include(i => i.Application)
                    .ThenInclude(a => a.Candidate)
                .Include(i => i.Application)
                    .ThenInclude(a => a.JobPosting)
                .Include(i => i.Panelists)
                    .ThenInclude(p => p.Interviewer)
                .Include(i => i.Evaluations)
                .OrderBy(i => i.StartTime)
                .Take(10)
                .ToListAsync();

            model.UpcomingInterviews = dbInterviews.Select(i =>
            {
                var cand = i.Application?.Candidate;
                var candName = cand != null ? $"{cand.FirstName} {cand.LastName}".Trim() : "Ứng viên";
                var firstLetter = candName.Length > 0 ? candName[..1].ToUpper() : "U";

                var panelists = i.Panelists.Any()
                    ? string.Join(", ", i.Panelists.Select(p => p.Interviewer.FullName))
                    : "Lường Minh Hiếu, Ngô Quang Huy";

                var statusDisplay = i.Status switch
                {
                    InterviewStatus.COMPLETED => "Đã hoàn thành",
                    InterviewStatus.RESCHEDULED => "Đã đổi lịch",
                    InterviewStatus.CANCELLED => "Đã hủy",
                    _ => i.CandidateConfirmed == CandidateConfirmStatus.ACCEPTED ? "Đã xác nhận" : (i.CandidateConfirmed == CandidateConfirmStatus.DECLINED ? "Yêu cầu đổi lịch" : "Chờ phỏng vấn")
                };

                var badgeClass = i.Status switch
                {
                    InterviewStatus.COMPLETED => "bg-success-subtle text-success",
                    InterviewStatus.CANCELLED => "bg-danger-subtle text-danger",
                    _ => i.CandidateConfirmed == CandidateConfirmStatus.ACCEPTED ? "bg-info-subtle text-info-emphasis" : "bg-warning-subtle text-warning-emphasis"
                };

                var eval = i.Evaluations.FirstOrDefault();
                string? evalResult = eval != null ? $"Đã chấm: {eval.OverallScore}/5 sao ({eval.Recommendation})." : null;

                return new UpcomingInterviewDto
                {
                    InterviewId = i.Id,
                    ApplicationId = i.ApplicationId,
                    CandidateName = candName,
                    CandidateEmail = cand?.Email ?? "",
                    CandidateAvatarLetter = firstLetter,
                    JobTitle = i.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng",
                    RoundTitle = i.Title,
                    StartTime = i.StartTime,
                    EndTime = i.EndTime,
                    TimeDisplay = i.StartTime.HasValue && i.EndTime.HasValue
                        ? $"{i.StartTime.Value:HH:mm} - {i.EndTime.Value:HH:mm}"
                        : "14:00 - 15:30",
                    DateDisplay = i.StartTime.HasValue ? $"{i.StartTime.Value:dddd, dd/MM/yyyy}" : "Trong tuần",
                    LocationOrLink = string.IsNullOrWhiteSpace(i.LocationOrLink) ? "https://meet.google.com" : i.LocationOrLink,
                    PanelistNames = panelists,
                    StatusBadgeClass = badgeClass,
                    StatusDisplay = statusDisplay,
                    CandidateConfirmed = i.CandidateConfirmed,
                    CandidateNotes = i.CandidateNotes,
                    Status = i.Status,
                    EvaluationResult = evalResult
                };
            }).ToList();

            // Offers chờ duyệt
            var dbOffers = await _dbContext.JobOffers
                .Include(o => o.Application)
                    .ThenInclude(a => a.Candidate)
                .Include(o => o.Application)
                    .ThenInclude(a => a.JobPosting)
                .Where(o => o.Status == OfferStatus.PENDING_APPROVAL || o.Status == OfferStatus.DRAFT)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            model.PendingOffers = dbOffers.Select(o =>
            {
                var cand = o.Application?.Candidate;
                var candName = cand != null ? $"{cand.FirstName} {cand.LastName}".Trim() : "Ứng viên";
                return new PendingOfferDto
                {
                    OfferId = o.Id,
                    ApplicationId = o.ApplicationId,
                    CandidateName = candName,
                    CandidateEmail = cand?.Email ?? "",
                    JobTitle = o.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng",
                    BaseSalary = o.BaseSalary,
                    TotalPackage = o.TotalPackage,
                    SalaryDisplay = $"{o.BaseSalary:N0} VNĐ",
                    Status = o.Status,
                    CreatedDateDisplay = o.CreatedAt.ToString("dd/MM/yyyy")
                };
            }).ToList();

            // Danh sách ứng viên cho modal chấm điểm phỏng vấn
            model.EvaluationCandidates = dbInterviews
                .Where(i => i.Status != InterviewStatus.COMPLETED)
                .Select(i => new InterviewEvaluationCandidateDto
                {
                    InterviewId = i.Id,
                    CandidateName = i.Application?.Candidate != null ? $"{i.Application.Candidate.FirstName} {i.Application.Candidate.LastName}".Trim() : "Ứng viên",
                    JobTitle = i.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng"
                })
                .ToList();

            // SCRUM-319: Lấy danh sách vị trí tuyển dụng kèm thông tin Recruiter phụ trách
            var jobPostings = await _dbContext.JobPostings
                .Include(j => j.Requisition)
                    .ThenInclude(r => r.Department)
                .Where(j => !j.IsDeleted)
                .OrderBy(j => j.Title)
                .ToListAsync();

            var jobCandidateCounts = await _dbContext.Applications
                .Where(a => !a.IsDeleted && a.Status != ApplicationStatus.WITHDRAWN)
                .GroupBy(a => a.JobPostingId)
                .Select(g => new { JobPostingId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.JobPostingId, x => x.Count);

            var filterOptions = new List<JobPostingFilterOptionDto>();
            Guid? defaultJobId = null;

            foreach (var job in jobPostings)
            {
                var isAssigned = (userId != Guid.Empty && job.Requisition?.AssignedRecruiterId == userId);
                jobCandidateCounts.TryGetValue(job.Id, out var count);

                filterOptions.Add(new JobPostingFilterOptionDto
                {
                    Id = job.Id,
                    Title = job.Title,
                    DepartmentName = job.Requisition?.Department?.Name ?? "Chung",
                    IsAssignedToCurrentUser = isAssigned,
                    ActiveCandidateCount = count
                });

                if (isAssigned && defaultJobId == null)
                {
                    defaultJobId = job.Id;
                }
            }

            model.JobPostingFilterOptions = filterOptions;
            model.DefaultJobPostingId = defaultJobId;

            // Toàn bộ ứng viên cho Bảng Quản Lý Ứng Viên & Tiến Trình Tuyển Dụng (Kanban & Table)
            // Mặc định hiển thị pipeline đang hoạt động (loại trừ hồ sơ đã rút)
            var allApps = await _dbContext.Applications
                .Include(a => a.Candidate)
                    .ThenInclude(c => c.Resumes)
                .Include(a => a.JobPosting)
                .Include(a => a.CurrentStage)
                .Where(a => !a.IsDeleted && a.Status != ApplicationStatus.WITHDRAWN)
                .OrderByDescending(a => a.AppliedAt)
                .Take(200)
                .ToListAsync();

            model.CandidatePipelineItems = allApps.Select(a =>
            {
                var candName = $"{a.Candidate.FirstName} {a.Candidate.LastName}".Trim();
                var primaryResume = a.Candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? a.Candidate.Resumes.FirstOrDefault();
                return new CandidatePipelineItemDto
                {
                    ApplicationId = a.Id,
                    CandidateId = a.CandidateId,
                    JobPostingId = a.JobPostingId,
                    CandidateName = candName,
                    CandidateEmail = a.Candidate.Email,
                    CandidatePhone = a.Candidate.Phone ?? "Chưa có",
                    JobTitle = a.JobPosting.Title,
                    CurrentStageId = a.CurrentStageId,
                    CurrentStageName = a.CurrentStage.Name,
                    CurrentStageOrder = a.CurrentStage.StageOrder,
                    StageColor = a.CurrentStage.ColorCode ?? "#059669",
                    AppliedDateDisplay = a.AppliedAt.HasValue ? a.AppliedAt.Value.ToString("dd/MM/yyyy") : a.CreatedAt.ToString("dd/MM/yyyy"),
                    Status = a.Status.ToString(),
                    Rating = a.Rating,
                    ResumeFileName = primaryResume?.FileName ?? "CV_UngVien.pdf",
                    ResumeFilePath = primaryResume?.FilePath ?? "#"
                };
            }).ToList();

            // Load thông báo gần nhất cho nhân sự
            if (userId != Guid.Empty)
            {
                var notifs = await _dbContext.Notifications
                    .Where(n => n.UserId == userId)
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(10)
                    .ToListAsync();

                model.UnreadNotificationsCount = notifs.Count(n => !n.IsRead);
                model.RecentNotifications = notifs.Select(n => new NotificationDto
                {
                    Id = n.Id,
                    Title = n.Title,
                    Content = n.Content,
                    Link = n.Link,
                    IsRead = n.IsRead,
                    CreatedAtDisplay = n.CreatedAt.ToString("HH:mm dd/MM/yyyy")
                }).ToList();
            }
        }
        return model;
    }

    [HttpGet("/api/candidates/pipeline")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.HRManager},{UserRoles.Recruiter},{UserRoles.HiringManager},{UserRoles.Interviewer},Quản trị viên,Trưởng phòng nhân sự,Chuyên viên tuyển dụng")]
    public async Task<IActionResult> GetCandidatePipeline(
        [FromQuery] string? search,
        [FromQuery] string? stageOrder,
        [FromQuery] Guid? jobPostingId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize <= 0 || pageSize > 100) pageSize = 10;

        var query = _dbContext.Applications
            .AsNoTracking()
            .Include(a => a.Candidate)
                .ThenInclude(c => c.Resumes)
            .Include(a => a.JobPosting)
            .Include(a => a.CurrentStage)
            .Where(a => !a.IsDeleted);

        if (jobPostingId.HasValue && jobPostingId.Value != Guid.Empty)
        {
            query = query.Where(a => a.JobPostingId == jobPostingId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search) && search.Trim().Length >= 3)
        {
            var s = search.Trim().ToLower();
            query = query.Where(a => 
                (a.Candidate.FirstName + " " + a.Candidate.LastName).ToLower().Contains(s) ||
                a.Candidate.Email.ToLower().Contains(s) ||
                (a.Candidate.Phone != null && a.Candidate.Phone.Contains(s)) ||
                a.JobPosting.Title.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(stageOrder) && stageOrder != "ALL")
        {
            if (stageOrder == "WITHDRAWN")
            {
                query = query.Where(a => a.Status == ApplicationStatus.WITHDRAWN);
            }
            else if (stageOrder == "REJECTED")
            {
                query = query.Where(a => a.Status == ApplicationStatus.REJECTED);
            }
            else if (int.TryParse(stageOrder, out var order))
            {
                query = query.Where(a => a.CurrentStage.StageOrder == order && a.Status != ApplicationStatus.WITHDRAWN);
            }
        }
        else
        {
            // Pipeline đang hoạt động mặc định ẩn ứng viên đã rút
            query = query.Where(a => a.Status != ApplicationStatus.WITHDRAWN);
        }

        var totalRecords = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);

        var apps = await query
            .OrderByDescending(a => a.AppliedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = apps.Select(a =>
        {
            var candName = $"{a.Candidate.FirstName} {a.Candidate.LastName}".Trim();
            var primaryResume = a.Candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? a.Candidate.Resumes.FirstOrDefault();
            return new CandidatePipelineItemDto
            {
                ApplicationId = a.Id,
                CandidateId = a.CandidateId,
                JobPostingId = a.JobPostingId,
                CandidateName = candName,
                CandidateEmail = a.Candidate.Email,
                CandidatePhone = a.Candidate.Phone ?? "Chưa có",
                JobTitle = a.JobPosting.Title,
                CurrentStageId = a.CurrentStageId,
                CurrentStageName = a.CurrentStage.Name,
                CurrentStageOrder = a.CurrentStage.StageOrder,
                StageColor = a.CurrentStage.ColorCode ?? "#059669",
                AppliedDateDisplay = a.AppliedAt.HasValue ? a.AppliedAt.Value.ToString("dd/MM/yyyy") : a.CreatedAt.ToString("dd/MM/yyyy"),
                Status = a.Status.ToString(),
                Rating = a.Rating,
                ResumeFileName = primaryResume?.FileName ?? "CV_UngVien.pdf",
                ResumeFilePath = primaryResume?.FilePath ?? "#"
            };
        }).ToList();

        return Ok(new
        {
            isSuccess = true,
            data = items,
            totalRecords,
            currentPage = page,
            pageSize,
            totalPages
        });
    }

    [HttpGet("/gioi-thieu")]
    [HttpHead("/gioi-thieu")]
    public async Task<IActionResult> GioiThieu()
    {
        ViewBag.TotalOpenJobs = await _dbContext.JobPostings.CountAsync(j => j.Status == JobPostingStatus.PUBLISHED);
        return View("Landing");
    }

    [HttpGet("/landing")]
    [HttpHead("/landing")]
    public IActionResult LegacyLanding()
    {
        return RedirectPermanent("/gioi-thieu");
    }

    [HttpGet("/api/notifications/recent")]
    public async Task<IActionResult> GetRecentNotifications()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return Ok(new { success = true, unreadCount = 0, items = Array.Empty<object>() });
        }

        var notifs = await _dbContext.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(10)
            .ToListAsync();

        return Ok(new
        {
            success = true,
            unreadCount = notifs.Count(n => !n.IsRead),
            items = notifs.Select(n => new
            {
                id = n.Id,
                title = n.Title,
                content = n.Content,
                link = n.Link ?? "/dashboard/ung-vien",
                isRead = n.IsRead,
                timeDisplay = n.CreatedAt.ToString("HH:mm dd/MM")
            })
        });
    }

    [HttpPost("/api/notifications/mark-read/{id}")]
    public async Task<IActionResult> MarkNotificationRead(Guid id)
    {
        var notif = await _dbContext.Notifications.FirstOrDefaultAsync(n => n.Id == id);
        if (notif != null)
        {
            notif.IsRead = true;
            await _dbContext.SaveChangesAsync();
        }
        return Ok(new { success = true });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
