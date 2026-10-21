using System.Diagnostics;
using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Workspace;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IJobService _jobService;
    private readonly ICandidateReferralService _referralService;
    private readonly ICandidateMergeService _mergeService;
    private readonly IRequisitionAssignmentService _assignmentService;
    private readonly ICandidateInterviewService? _candidateInterviewService;
    private readonly IPositionProgressService? _positionProgressService;
    private readonly ILogger<HomeController> _logger;

    [ActivatorUtilitiesConstructor]
    public HomeController(
        ApplicationDbContext dbContext,
        IJobService jobService,
        ICandidateReferralService referralService,
        ICandidateMergeService mergeService,
        IRequisitionAssignmentService assignmentService,
        ILogger<HomeController> logger,
        ICandidateInterviewService? candidateInterviewService = null,
        IPositionProgressService? positionProgressService = null)
    {
        _dbContext = dbContext;
        _jobService = jobService;
        _referralService = referralService;
        _mergeService = mergeService;
        _assignmentService = assignmentService;
        _logger = logger;
        _candidateInterviewService = candidateInterviewService;
        _positionProgressService = positionProgressService;
    }

    public HomeController(
        ApplicationDbContext dbContext,
        IJobService jobService,
        ICandidateReferralService referralService,
        ICandidateMergeService mergeService,
        ILogger<HomeController> logger)
        : this(
            dbContext,
            jobService,
            referralService,
            mergeService,
            new Ats.Web.Services.RequisitionAssignmentService(dbContext, Microsoft.Extensions.Logging.Abstractions.NullLogger<Ats.Web.Services.RequisitionAssignmentService>.Instance),
            logger,
            null)
    {
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
    public async Task<IActionResult> Dashboard(string? feature = null, [FromQuery] string? interviewScope = null)
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
        if (normalizedFeature == "referrals" || normalizedFeature == "referral" || normalizedFeature == "ho-so-gioi-thieu")
        {
            normalizedFeature = "gioi-thieu";
        }
        if (normalizedFeature == "trung-lap" || normalizedFeature == "duplicates" || normalizedFeature == "ho-so-trung-lap")
        {
            return RedirectToAction("CandidateDuplicates", "Workspace");
        }
        if (normalizedFeature == "tien-do-vi-tri" || normalizedFeature == "tien-do-tuyen-dung")
        {
            normalizedFeature = "tien-do-vi-tri";
        }
        ViewBag.ActiveFeature = normalizedFeature;

        var model = await BuildWorkspaceDashboardViewModelAsync(interviewScope);
        return View("Index", model);
    }

    private async Task<WorkspaceDashboardViewModel> BuildWorkspaceDashboardViewModelAsync(string? interviewScope = null)
    {
        var model = new WorkspaceDashboardViewModel
        {
            IsAuthenticated = true
        };

        var allUserRoles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var primaryRoleCode = UserRoles.GetPrimaryRole(allUserRoles);

        var hasStaffRole = User.IsInRole(UserRoles.Admin) || 
                           User.IsInRole(UserRoles.HRManager) || 
                           User.IsInRole(UserRoles.Recruiter) || 
                           User.IsInRole(UserRoles.HiringManager) || 
                           User.IsInRole(UserRoles.Interviewer) || 
                           User.IsInRole(UserRoles.Approver);
        var isCandidate = !hasStaffRole && User.IsInRole(UserRoles.Candidate);
        var isStaff = hasStaffRole || !isCandidate;
        var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(userIdStr, out var userId);

        model.IsCandidate = isCandidate;
        model.IsStaff = isStaff;
        model.UserFullName = User.Identity?.Name ?? "Người dùng";
        model.UserEmail = userEmail;
        model.UserRolesList = allUserRoles
            .Select(c => UserRoles.GetDisplayName(c))
            .Distinct()
            .ToList();
        model.PrimaryRoleDisplayName = UserRoles.GetDisplayName(primaryRoleCode);

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
                    FullName = string.IsNullOrWhiteSpace(candidate.LastName) 
                        ? candidate.FirstName 
                        : (candidate.FirstName.Trim().EndsWith(candidate.LastName.Trim(), StringComparison.OrdinalIgnoreCase) 
                            ? candidate.FirstName.Trim() 
                            : $"{candidate.FirstName.Trim()} {candidate.LastName.Trim()}"),
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
                            CandidateNotes = interview.CandidateNotes,
                            StatusDisplay = interview.CandidateConfirmed == CandidateConfirmStatus.ACCEPTED ? "Đã xác nhận tham gia" : (interview.CandidateConfirmed == CandidateConfirmStatus.DECLINED ? "Đã gửi yêu cầu đổi lịch" : "Chờ xác nhận"),
                            InvitationToken = _candidateInterviewService != null && !string.IsNullOrWhiteSpace(activeApp.Candidate?.Email)
                                ? _candidateInterviewService.GenerateInvitationToken(interview.Id, activeApp.Candidate.Email)
                                : null,
                            ConfirmationUrl = _candidateInterviewService != null && !string.IsNullOrWhiteSpace(activeApp.Candidate?.Email)
                                ? $"{Request.Scheme}://{Request.Host}/xac-nhan-phong-van/{_candidateInterviewService.GenerateInvitationToken(interview.Id, activeApp.Candidate.Email)}"
                                : null
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
            // Scrum 26: Recruiter chỉ nhìn thấy ứng viên của vị trí được giao
            var isRecruiterOnly = User?.Identity?.IsAuthenticated == true &&
                                  User.IsInRole(UserRoles.Recruiter) &&
                                  !User.IsInRole(UserRoles.Admin) &&
                                  !User.IsInRole(UserRoles.HRManager);

            List<Guid> assignedReqIds = new();
            if (isRecruiterOnly && userId != Guid.Empty)
            {
                assignedReqIds = await _assignmentService.GetAssignedRequisitionIdsForRecruiterAsync(userId);
                model.IsRecruiterScoped = true;
                model.AssignedPositionsCount = assignedReqIds.Count;
            }
            else
            {
                model.RecruitersFilterList = await _assignmentService.GetAvailableRecruitersAsync();
            }

            if (isRecruiterOnly)
            {
                model.TotalOpenJobs = await _dbContext.JobPostings.CountAsync(j => j.Status == JobPostingStatus.PUBLISHED && assignedReqIds.Contains(j.RequisitionId));
                model.TotalApplications = await _dbContext.Applications.CountAsync(a => !a.IsDeleted && a.Status != ApplicationStatus.WITHDRAWN && assignedReqIds.Contains(a.JobPosting.RequisitionId));
            }
            else
            {
                model.TotalOpenJobs = await _dbContext.JobPostings.CountAsync(j => j.Status == JobPostingStatus.PUBLISHED);
                model.TotalApplications = await _dbContext.Applications.CountAsync(a => !a.IsDeleted && a.Status != ApplicationStatus.WITHDRAWN);
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var todayUtc = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, TimeSpan.Zero);
            var startOfWeek = todayUtc.AddDays(-(int)nowUtc.DayOfWeek + 1);
            var endOfWeek = startOfWeek.AddDays(7);

            var isInterviewerOnly = User?.Identity?.IsAuthenticated == true &&
                                    User.IsInRole(UserRoles.Interviewer) &&
                                    !User.IsInRole(UserRoles.Admin) &&
                                    !User.IsInRole(UserRoles.HRManager) &&
                                    !User.IsInRole(UserRoles.Recruiter) &&
                                    !User.IsInRole("Quản trị viên") &&
                                    !User.IsInRole("Trưởng phòng nhân sự") &&
                                    !User.IsInRole("Chuyên viên tuyển dụng");

            var isInterviewerRole = User?.Identity?.IsAuthenticated == true &&
                                    (User.IsInRole(UserRoles.Interviewer) || User.IsInRole(UserRoles.HiringManager));
            model.IsInterviewer = isInterviewerRole || isInterviewerOnly;

            // Đếm số lượng buổi phỏng vấn mà người dùng hiện tại là Panelist
            if (userId != Guid.Empty)
            {
                model.MyUpcomingInterviewsCount = await _dbContext.Interviews
                    .CountAsync(i => i.Panelists.Any(p => p.InterviewerId == userId) && i.Status != InterviewStatus.CANCELLED);
            }

            var effectiveScope = (isInterviewerOnly || interviewScope == "my") ? "my" : "all";
            model.InterviewScope = effectiveScope;

            var interviewBaseQuery = _dbContext.Interviews.AsQueryable();
            if (effectiveScope == "my" && userId != Guid.Empty)
            {
                interviewBaseQuery = interviewBaseQuery.Where(i => i.Panelists.Any(p => p.InterviewerId == userId));
            }
            else if (isRecruiterOnly)
            {
                interviewBaseQuery = interviewBaseQuery.Where(i => assignedReqIds.Contains(i.Application.JobPosting.RequisitionId));
            }

            model.TotalInterviewsThisWeek = await interviewBaseQuery.CountAsync(i => i.StartTime >= startOfWeek && i.StartTime <= endOfWeek);
            if (model.TotalInterviewsThisWeek == 0)
            {
                model.TotalInterviewsThisWeek = await interviewBaseQuery.CountAsync(i => i.Status == InterviewStatus.SCHEDULED);
            }

            var offerBaseQuery = _dbContext.JobOffers.AsQueryable();
            if (isRecruiterOnly)
            {
                offerBaseQuery = offerBaseQuery.Where(o => assignedReqIds.Contains(o.Application.JobPosting.RequisitionId));
            }
            model.TotalPendingOffers = await offerBaseQuery.CountAsync(o => o.Status == OfferStatus.PENDING_APPROVAL || o.Status == OfferStatus.DRAFT);

            // Phễu tuyển dụng thật (chỉ tính hồ sơ đang trong pipeline hoạt động)
            var stages = await _dbContext.PipelineStages.OrderBy(s => s.StageOrder).ToListAsync();
            var appCountQuery = _dbContext.Applications.Where(a => !a.IsDeleted && a.Status != ApplicationStatus.WITHDRAWN);
            if (isRecruiterOnly)
            {
                appCountQuery = appCountQuery.Where(a => assignedReqIds.Contains(a.JobPosting.RequisitionId));
            }

            var appCountsByStage = await appCountQuery
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

            // Lịch phỏng vấn tuần này (Scrum - Số 51: Hiển thị trên lịch của người phỏng vấn)
            var interviewListQuery = _dbContext.Interviews
                .Include(i => i.Application)
                    .ThenInclude(a => a.Candidate)
                .Include(i => i.Application)
                    .ThenInclude(a => a.JobPosting)
                .Include(i => i.Panelists)
                    .ThenInclude(p => p.Interviewer)
                .Include(i => i.Evaluations)
                .AsQueryable();

            if (effectiveScope == "my" && userId != Guid.Empty)
            {
                interviewListQuery = interviewListQuery.Where(i => i.Panelists.Any(p => p.InterviewerId == userId));
            }
            else if (isRecruiterOnly)
            {
                interviewListQuery = interviewListQuery.Where(i => assignedReqIds.Contains(i.Application.JobPosting.RequisitionId));
            }

            var dbInterviews = await interviewListQuery
                .OrderBy(i => i.StartTime)
                .Take(25)
                .ToListAsync();

            var isStaffManager = User?.Identity?.IsAuthenticated == true &&
                                 (User.IsInRole(UserRoles.Admin) || 
                                  User.IsInRole(UserRoles.HRManager) || 
                                  User.IsInRole(UserRoles.Recruiter) ||
                                  User.IsInRole("Quản trị viên") ||
                                  User.IsInRole("Trưởng phòng nhân sự") ||
                                  User.IsInRole("Chuyên viên tuyển dụng"));

            model.UpcomingInterviews = dbInterviews.Select(i =>
            {
                var cand = i.Application?.Candidate;
                var candName = cand != null ? $"{cand.FirstName} {cand.LastName}".Trim() : "Ứng viên";
                var firstLetter = candName.Length > 0 ? candName[..1].ToUpper() : "U";

                var panelists = i.Panelists.Any()
                    ? string.Join(", ", i.Panelists.Select(p => p.Interviewer?.FullName ?? "Người phỏng vấn"))
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

                // Scrum - Số 57: Kiểm tra quyền hạn và trạng thái phiếu đánh giá
                var isInvited = i.Panelists.Any(p => p.InterviewerId == userId && !p.IsDeleted);
                var myEval = i.Evaluations.FirstOrDefault(e => e.InterviewerId == userId && !e.IsDeleted);
                var hasSubmitted = myEval != null && myEval.IsSubmitted;
                var isReopened = myEval != null && myEval.IsReopened;
                var canEvaluate = isInvited && (!hasSubmitted || isReopened);

                // Recruiter có thể mở lại phiếu nếu có phiếu đã nộp trong vòng 24 giờ
                var anySubEvalWithin24h = i.Evaluations.FirstOrDefault(e => e.IsSubmitted && !e.IsDeleted && e.SubmittedAt.HasValue && (DateTimeOffset.UtcNow - e.SubmittedAt.Value) <= TimeSpan.FromHours(24));
                var canRecruiterReopen = isStaffManager && anySubEvalWithin24h != null;

                // Scrum - Số 55: Kiểm tra điều kiện dời hoặc huỷ lịch phỏng vấn (Không sửa được buổi đã có phiếu đánh giá nộp)
                var hasSubmittedEvaluation = i.Evaluations.Any(e => e.IsSubmitted && !e.IsDeleted);
                var canRescheduleOrCancel = isStaffManager &&
                                            !hasSubmittedEvaluation &&
                                            i.Status != InterviewStatus.CANCELLED &&
                                            i.Status != InterviewStatus.COMPLETED;

                string? cancellationReason = null;
                if (i.Status == InterviewStatus.CANCELLED && !string.IsNullOrWhiteSpace(i.CandidateNotes))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(i.CandidateNotes, @"\[LÝ DO HUỶ\]:\s*(.*)");
                    cancellationReason = match.Success ? match.Groups[1].Value.Trim() : i.CandidateNotes;
                }

                var interviewerIds = i.Panelists.Where(p => !p.IsDeleted).Select(p => p.InterviewerId).ToList();

                // Quy tắc 4: Không nhìn thấy đánh giá của người khác trước khi nộp phiếu của mình
                string? evalResult;
                if (isInvited)
                {
                    if (hasSubmitted)
                    {
                        var (recDisp, _) = InterviewEvaluationService.FormatRecommendation(myEval!.Recommendation);
                        evalResult = $"Bạn đã chấm: {myEval.OverallScore:0.0}/5 ({recDisp})";
                    }
                    else
                    {
                        evalResult = "Chưa nộp phiếu đánh giá";
                    }
                }
                else if (isStaffManager)
                {
                    var submittedCount = i.Evaluations.Count(e => e.IsSubmitted && !e.IsDeleted);
                    var totalInvited = i.Panelists.Count(p => !p.IsDeleted);
                    evalResult = $"{submittedCount}/{totalInvited} giám khảo đã nộp";
                }
                else
                {
                    evalResult = null;
                }

                string? token = null;
                string? confirmUrl = null;
                if (_candidateInterviewService != null && !string.IsNullOrWhiteSpace(cand?.Email))
                {
                    token = _candidateInterviewService.GenerateInvitationToken(i.Id, cand.Email);
                    var scheme = Request.Scheme ?? "https";
                    var host = Request.Host.Value;
                    confirmUrl = $"{scheme}://{host}/xac-nhan-phong-van/{token}";
                }

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
                    EvaluationResult = evalResult,
                    IsCurrentUserPanelist = isInvited,
                    HasCurrentUserSubmitted = hasSubmitted,
                    CanCurrentUserEvaluate = canEvaluate,
                    CanRecruiterReopen = canRecruiterReopen,
                    CurrentUserEvaluationId = myEval?.Id,
                    FirstEvaluationId = anySubEvalWithin24h?.Id ?? i.Evaluations.FirstOrDefault(e => e.IsSubmitted)?.Id,
                    IsReopened = isReopened,
                    InvitationToken = token,
                    ConfirmationUrl = confirmUrl,
                    HasSubmittedEvaluation = hasSubmittedEvaluation,
                    CanRescheduleOrCancel = canRescheduleOrCancel,
                    CancellationReason = cancellationReason,
                    InterviewTypeString = i.InterviewType.ToString(),
                    InterviewerIds = interviewerIds
                };
            }).ToList();

            // Offers chờ duyệt
            var offerListQuery = _dbContext.JobOffers
                .Include(o => o.Application)
                    .ThenInclude(a => a.Candidate)
                .Include(o => o.Application)
                    .ThenInclude(a => a.JobPosting)
                .Where(o => o.Status == OfferStatus.PENDING_APPROVAL || o.Status == OfferStatus.DRAFT)
                .AsQueryable();

            if (isRecruiterOnly)
            {
                offerListQuery = offerListQuery.Where(o => assignedReqIds.Contains(o.Application.JobPosting.RequisitionId));
            }

            var dbOffers = await offerListQuery
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
            // Quy tắc 1: Chỉ người phỏng vấn được mời mới mở được phiếu của buổi đó
            var evalCandidatesQuery = dbInterviews.AsEnumerable();
            if (User?.IsInRole(UserRoles.Interviewer) == true && !isStaffManager)
            {
                evalCandidatesQuery = evalCandidatesQuery.Where(i => i.Panelists.Any(p => p.InterviewerId == userId && !p.IsDeleted));
            }

            model.EvaluationCandidates = evalCandidatesQuery
                .Select(i =>
                {
                    var myEval = i.Evaluations.FirstOrDefault(e => e.InterviewerId == userId && !e.IsDeleted);
                    var statusNote = myEval != null && myEval.IsSubmitted ? " [Đã nộp]" : (myEval != null && myEval.IsReopened ? " [Đã mở lại]" : "");
                    return new InterviewEvaluationCandidateDto
                    {
                        InterviewId = i.Id,
                        CandidateName = (i.Application?.Candidate != null ? $"{i.Application.Candidate.FirstName} {i.Application.Candidate.LastName}".Trim() : "Ứng viên") + statusNote,
                        JobTitle = i.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng"
                    };
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

            // Danh sách vị trí tuyển dụng cho bộ lọc ứng viên (Filter & Export SCRUM-328)
            var availableJobs = await _dbContext.JobPostings
                .Include(j => j.Requisition)
                .Where(j => j.Status == JobPostingStatus.PUBLISHED || _dbContext.Applications.Any(a => a.JobPostingId == j.Id))
                .OrderBy(j => j.Title)
                .Select(j => new JobFilterOptionDto
                {
                    Id = j.Id,
                    Code = j.Requisition != null ? j.Requisition.Code : "",
                    Title = j.Title
                })
                .ToListAsync();
            model.AvailableJobsForFilter = availableJobs;

            // Toàn bộ ứng viên cho Bảng Quản Lý Ứng Viên & Tiến Trình Tuyển Dụng (Kanban & Table)
            // Mặc định hiển thị pipeline đang hoạt động (loại trừ hồ sơ đã rút)
            var allAppsQuery = _dbContext.Applications
                .Include(a => a.Candidate)
                    .ThenInclude(c => c.Resumes)
                .Include(a => a.Candidate)
                    .ThenInclude(c => c.ReferrerUser)
                .Include(a => a.Candidate)
                    .ThenInclude(c => c.User)
                .Include(a => a.ReferrerUser)
                .Include(a => a.JobPosting)
                    .ThenInclude(j => j.Requisition)
                .Include(a => a.CurrentStage)
                .Where(a => !a.IsDeleted && a.Status != ApplicationStatus.WITHDRAWN)
                .AsQueryable();

            if (isRecruiterOnly)
            {
                allAppsQuery = allAppsQuery.Where(a => assignedReqIds.Contains(a.JobPosting.RequisitionId));
            }

            var allApps = await allAppsQuery
                .OrderByDescending(a => a.AppliedAt)
                .Take(200)
                .ToListAsync();

            // Quét phát hiện ứng viên trùng lặp (Scrum #40)
            var duplicateGroups = await _mergeService.DetectDuplicatesAsync();
            model.DuplicateCandidatesCount = duplicateGroups.Count;

            var dupLookup = new Dictionary<Guid, (string Reason, Guid OtherId, int Count)>();
            foreach (var g in duplicateGroups)
            {
                foreach (var c in g.Candidates)
                {
                    if (!dupLookup.ContainsKey(c.CandidateId))
                    {
                        var otherCand = g.Candidates.FirstOrDefault(x => x.CandidateId != c.CandidateId);
                        dupLookup[c.CandidateId] = (g.DuplicateReason, otherCand?.CandidateId ?? Guid.Empty, g.Candidates.Count);
                    }
                }
            }

            model.CandidatePipelineItems = allApps.Select(a =>
            {
                var candName = $"{a.Candidate.FirstName} {a.Candidate.LastName}".Trim();
                var primaryResume = a.Candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? a.Candidate.Resumes.FirstOrDefault();
                var jobCode = a.JobPosting?.Requisition?.Code ?? "";
                var isRef = a.ReferrerUserId != null || a.Candidate.Source == CandidateSource.REFERRAL;
                var refUser = a.ReferrerUser ?? a.Candidate.ReferrerUser;
                var hasDup = dupLookup.TryGetValue(a.CandidateId, out var dupInfo);

                var enteredAt = a.StageEnteredAt ?? a.AppliedAt ?? a.CreatedAt;
                var daysInStage = (int)(nowUtc - enteredAt).TotalDays;
                var isOverdue = false;
                var overdueDays = 0;
                var slaDays = a.CurrentStage?.SlaDays ?? 3;

                if (a.CurrentStage != null && a.CurrentStage.IsSlaEnabled && slaDays > 0)
                {
                    if (daysInStage >= slaDays)
                    {
                        isOverdue = true;
                        overdueDays = Math.Max(1, daysInStage - slaDays);
                    }
                }
                return new CandidatePipelineItemDto
                {
                    ApplicationId = a.Id,
                    CandidateId = a.CandidateId,
                    JobPostingId = a.JobPostingId,
                    JobCode = jobCode,
                    CandidateName = candName,
                    CandidateEmail = a.Candidate.Email,
                    CandidatePhone = a.Candidate.Phone ?? "Chưa có",
                    AvatarUrl = a.Candidate.User?.AvatarUrl,
                    JobTitle = a.JobPosting?.Title ?? "Vị trí tuyển dụng",
                    CurrentStageId = a.CurrentStageId,
                    CurrentStageName = a.CurrentStage?.Name ?? "Chưa phân loại",
                    CurrentStageOrder = a.CurrentStage?.StageOrder ?? 1,
                    StageColor = a.CurrentStage?.ColorCode ?? "#059669",
                    AppliedDateDisplay = a.AppliedAt.HasValue ? a.AppliedAt.Value.ToString("dd/MM/yyyy") : a.CreatedAt.ToString("dd/MM/yyyy"),
                    Status = a.Status.ToString(),
                    Rating = a.Rating,
                    ResumeFileName = primaryResume?.FileName ?? "CV_UngVien.pdf",
                    ResumeFilePath = primaryResume?.FilePath ?? "#",
                    IsReferral = isRef,
                    ReferrerName = refUser?.FullName,
                    ReferrerEmail = refUser?.Email,
                    ReferralNote = a.ReferralNote,
                    HasDuplicateWarning = hasDup,
                    DuplicateReason = hasDup ? dupInfo.Reason : null,
                    MatchedCandidateId = hasDup ? dupInfo.OtherId : null,
                    DuplicateCount = hasDup ? dupInfo.Count : 0,
                    IsOverdueSla = isOverdue,
                    SlaDays = slaDays,
                    DaysInCurrentStage = daysInStage,
                    OverdueDays = overdueDays
                };
            }).ToList();

            model.OverdueSlaCandidateCount = model.CandidatePipelineItems.Count(x => x.IsOverdueSla);

            // Load thông báo gần nhất cho nhân sự
            if (userId != Guid.Empty)
            {
                // Nạp danh sách hồ sơ do chính nhân viên nội bộ giới thiệu (Scrum #37)
                model.MyReferrals = await _referralService.GetMyReferralsAsync(userId);

                // Load thông báo gần nhất cho nhân sự
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

            // SCRUM-351: Nạp tiến độ tuyển dụng các vị trí sở hữu của Trưởng bộ phận (Hiring Manager)
            if (User?.Identity?.IsAuthenticated == true && 
                (User.IsInRole(UserRoles.HiringManager) || User.IsInRole(UserRoles.Admin) || User.IsInRole(UserRoles.HRManager)))
            {
                var progressService = _positionProgressService ?? new PositionProgressService(_dbContext, Microsoft.Extensions.Logging.Abstractions.NullLogger<PositionProgressService>.Instance);
                model.MyOwnedPositionsProgress = await progressService.GetOwnedPositionsSummaryAsync(userId);
                if (model.MyOwnedPositionsProgress.Any())
                {
                    var primaryReqId = model.MyOwnedPositionsProgress.FirstOrDefault(p => p.Status == RequisitionStatus.APPROVED)?.RequisitionId
                                       ?? model.MyOwnedPositionsProgress.First().RequisitionId;
                    model.MyPrimaryPositionProgress = await progressService.GetPositionProgressDetailAsync(primaryReqId, userId, true);
                }
            }
        }
        return model;
    }

    [HttpGet("/api/candidates/pipeline")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.HRManager},{UserRoles.Recruiter},{UserRoles.HiringManager},{UserRoles.Interviewer},Quản trị viên,Trưởng phòng nhân sự,Chuyên viên tuyển dụng")]
    public async Task<IActionResult> GetCandidatePipeline(
        [FromQuery] string? search,
        [FromQuery] string? stageOrder,
        [FromQuery] Guid? recruiterId = null,
        [FromQuery] Guid? jobPostingId = null,
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
            .Include(a => a.Candidate)
                .ThenInclude(c => c.ReferrerUser)
            .Include(a => a.Candidate)
                .ThenInclude(c => c.User)
            .Include(a => a.ReferrerUser)
            .Include(a => a.JobPosting)
            .Include(a => a.CurrentStage)
            .Where(a => !a.IsDeleted);

        // Scrum 26: Recruiter chỉ nhìn thấy ứng viên của vị trí được giao
        var isRecruiterOnly = User?.Identity?.IsAuthenticated == true &&
                              User.IsInRole(UserRoles.Recruiter) &&
                              !User.IsInRole(UserRoles.Admin) &&
                              !User.IsInRole(UserRoles.HRManager);

        if (isRecruiterOnly && _assignmentService != null)
        {
            var userIdStr = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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
            else if (stageOrder == "REFERRAL")
            {
                query = query.Where(a => a.ReferrerUserId != null || a.Candidate.Source == CandidateSource.REFERRAL);
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
            var isRef = a.ReferrerUserId != null || a.Candidate.Source == CandidateSource.REFERRAL;
            var refUser = a.ReferrerUser ?? a.Candidate.ReferrerUser;

            return new CandidatePipelineItemDto
            {
                ApplicationId = a.Id,
                CandidateId = a.CandidateId,
                JobPostingId = a.JobPostingId,
                CandidateName = candName,
                CandidateEmail = a.Candidate.Email,
                CandidatePhone = a.Candidate.Phone ?? "Chưa có",
                AvatarUrl = a.Candidate.User?.AvatarUrl,
                JobTitle = a.JobPosting.Title,
                CurrentStageId = a.CurrentStageId,
                CurrentStageName = a.CurrentStage.Name,
                CurrentStageOrder = a.CurrentStage.StageOrder,
                StageColor = a.CurrentStage.ColorCode ?? "#059669",
                AppliedDateDisplay = a.AppliedAt.HasValue ? a.AppliedAt.Value.ToString("dd/MM/yyyy") : a.CreatedAt.ToString("dd/MM/yyyy"),
                Status = a.Status.ToString(),
                Rating = a.Rating,
                ResumeFileName = primaryResume?.FileName ?? "CV_UngVien.pdf",
                ResumeFilePath = primaryResume?.FilePath ?? "#",
                IsReferral = isRef,
                ReferrerName = refUser?.FullName,
                ReferrerEmail = refUser?.Email,
                ReferralNote = a.ReferralNote
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
    [Authorize]
    public async Task<IActionResult> MarkNotificationRead(Guid id)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var currentUserId))
        {
            return Unauthorized(new { success = false, message = "Chưa xác thực người dùng." });
        }

        var notif = await _dbContext.Notifications.FirstOrDefaultAsync(n => n.Id == id);
        if (notif == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy thông báo." });
        }

        if (notif.UserId != currentUserId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Bạn không có quyền sửa đổi thông báo của người khác." });
        }

        notif.IsRead = true;
        await _dbContext.SaveChangesAsync();
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
