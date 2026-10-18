using System.Security.Claims;
using Ats.Web.Common;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Applications;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller xử lý các tác vụ liên quan đến Đơn ứng tuyển của Ứng viên (SCRUM-291: Rút hồ sơ ứng tuyển).
/// Đã được thắt chặt bảo mật: Bắt buộc đăng nhập và chỉ chính ứng viên nộp hồ sơ mới có quyền thao tác.
/// </summary>
public class ApplicationsController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly ILogger<ApplicationsController> _logger;

    public ApplicationsController(
        ApplicationDbContext dbContext,
        IEmailService emailService,
        ILogger<ApplicationsController> logger)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _logger = logger;
    }

    /// <summary>
    /// GET /Applications/Withdraw?token={token}
    /// Mở trang xác nhận rút hồ sơ ứng tuyển thông qua token bảo mật.
    /// YÊU CẦU BẢO MẬT: Bắt buộc đăng nhập và người dùng phải là chính chủ ứng viên sở hữu hồ sơ này.
    /// </summary>
    [HttpGet("/Applications/Withdraw")]
    [HttpGet("/ung-vien/rut-ho-so")]
    public async Task<IActionResult> Withdraw([FromQuery] string? token)
    {
        // 1. Kiểm tra đăng nhập (Authentication)
        if (User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = Request.Path + Request.QueryString;
            TempData["WarningMessage"] = "Vui lòng đăng nhập tài khoản Ứng viên để xác thực quyền quản lý và thao tác rút hồ sơ ứng tuyển.";
            return RedirectToAction("CandidateAuth", "Account", new { returnUrl });
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            ViewBag.ErrorMessage = "Liên kết rút hồ sơ không hợp lệ hoặc thiếu mã xác thực bảo mật.";
            return View("WithdrawError");
        }

        var cleanToken = token.Trim();
        var application = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
                .ThenInclude(j => j.Requisition)
                    .ThenInclude(r => r.Department)
            .Include(a => a.CurrentStage)
            .FirstOrDefaultAsync(a => a.WithdrawToken == cleanToken && !a.IsDeleted);

        if (application == null)
        {
            ViewBag.ErrorMessage = "Không tìm thấy hồ sơ ứng tuyển tương ứng với liên kết này hoặc liên kết đã hết hiệu lực.";
            return View("WithdrawError");
        }

        // 2. Kiểm tra quyền sở hữu hồ sơ (Authorization / Ownership check)
        var userEmail = User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant() ?? "";
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(userIdStr, out var userId);

        var candidateEmail = application.Candidate?.Email?.Trim().ToLowerInvariant() ?? "";
        var candidateUserId = application.Candidate?.UserId;
        var isOwner = (userId != Guid.Empty && candidateUserId.HasValue && candidateUserId.Value == userId)
                   || (!string.IsNullOrEmpty(userEmail) && !string.IsNullOrEmpty(candidateEmail) && candidateEmail == userEmail);

        if (!isOwner)
        {
            var displayCandidateEmail = application.Candidate?.Email ?? "Ứng viên";
            _logger.LogWarning("BẢO MẬT: Tài khoản {UserEmail} (ID: {UserId}) bị từ chối truy cập hồ sơ rút của ứng viên {CandidateEmail} (AppId: {AppId}). IP: {Ip}",
                PiiMaskingHelper.MaskEmail(userEmail), userId, PiiMaskingHelper.MaskEmail(displayCandidateEmail), application.Id, HttpContext.Connection.RemoteIpAddress);

            var statusCodeFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>();
            if (statusCodeFeature != null)
            {
                statusCodeFeature.Enabled = false;
            }

            Response.StatusCode = StatusCodes.Status403Forbidden;
            ViewBag.IsUnauthorizedUser = true;
            ViewBag.LoggedInUserEmail = userEmail;
            ViewBag.CandidateMaskedEmail = PiiMaskingHelper.MaskEmail(displayCandidateEmail);
            ViewBag.ErrorMessage = $"Truy cập bị từ chối: Bạn đang đăng nhập bằng tài khoản ({userEmail}), không phải là chủ sở hữu của hồ sơ ứng tuyển này. Vui lòng đăng nhập đúng tài khoản ứng viên ({PiiMaskingHelper.MaskEmail(displayCandidateEmail)}) để thực hiện rút hồ sơ.";
            ViewBag.ReturnUrl = Request.Path + Request.QueryString;
            return View("WithdrawError");
        }

        var candidateFullName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();
        var appCode = $"#NOV-2026-{application.Id.ToString("N")[..4].ToUpper()}";

        var viewModel = new WithdrawApplicationViewModel
        {
            Token = cleanToken,
            ApplicationId = application.Id,
            ApplicationCode = appCode,
            CandidateName = candidateFullName,
            CandidateEmail = application.Candidate.Email,
            JobTitle = application.JobPosting.Title,
            DepartmentName = application.JobPosting.Requisition?.Department?.Name ?? "NoveraTech Engineering",
            WorkLocation = application.JobPosting.WorkLocation,
            CurrentStageName = application.CurrentStage?.Name ?? "Đang xử lý",
            AppliedDateDisplay = application.AppliedAt.HasValue
                ? application.AppliedAt.Value.ToString("dd/MM/yyyy HH:mm")
                : application.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
            IsAlreadyWithdrawn = application.Status == ApplicationStatus.WITHDRAWN,
            WithdrawnAt = application.WithdrawnAt,
            ExistingReason = application.WithdrawReason
        };

        return View(viewModel);
    }

    /// <summary>
    /// POST /Applications/ConfirmWithdraw
    /// Tiếp nhận xác nhận rút hồ sơ từ ứng viên, đổi trạng thái sang WITHDRAWN, đưa khỏi pipeline hoạt động và thông báo cho Recruiter.
    /// YÊU CẦU BẢO MẬT: Bắt buộc đăng nhập và chỉ chính chủ ứng viên mới được xác nhận rút hồ sơ.
    /// </summary>
    [HttpPost("/Applications/ConfirmWithdraw")]
    [HttpPost("/ung-vien/xac-nhan-rut-ho-so")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmWithdraw(
        [FromForm] string token,
        [FromForm] string? reasonCategory,
        [FromForm] string? reasonDetail)
    {
        // 1. Kiểm tra đăng nhập (Authentication)
        if (User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = $"/Applications/Withdraw?token={Uri.EscapeDataString(token ?? "")}";
            TempData["WarningMessage"] = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại tài khoản Ứng viên để xác nhận thao tác.";
            return RedirectToAction("CandidateAuth", "Account", new { returnUrl });
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            ViewBag.ErrorMessage = "Yêu cầu không hợp lệ: Mã xác thực bảo mật bị trống.";
            return View("WithdrawError");
        }

        var cleanToken = token.Trim();
        var application = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
                .ThenInclude(j => j.Requisition)
            .Include(a => a.CurrentStage)
            .FirstOrDefaultAsync(a => a.WithdrawToken == cleanToken && !a.IsDeleted);

        if (application == null)
        {
            ViewBag.ErrorMessage = "Không tìm thấy hồ sơ ứng tuyển hợp lệ tương ứng với liên kết này.";
            return View("WithdrawError");
        }

        // 2. Kiểm tra quyền sở hữu hồ sơ (Authorization / Ownership check)
        var userEmail = User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant() ?? "";
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(userIdStr, out var userId);

        var candidateEmail = application.Candidate?.Email?.Trim().ToLowerInvariant() ?? "";
        var candidateUserId = application.Candidate?.UserId;
        var isOwner = (userId != Guid.Empty && candidateUserId.HasValue && candidateUserId.Value == userId)
                   || (!string.IsNullOrEmpty(userEmail) && !string.IsNullOrEmpty(candidateEmail) && candidateEmail == userEmail);

        if (!isOwner)
        {
            var displayCandidateEmail = application.Candidate?.Email ?? "Ứng viên";
            _logger.LogWarning("BẢO MẬT: Tài khoản {UserEmail} (ID: {UserId}) cố tình xác nhận rút hồ sơ của ứng viên {CandidateEmail} (AppId: {AppId}). IP: {Ip}",
                PiiMaskingHelper.MaskEmail(userEmail), userId, PiiMaskingHelper.MaskEmail(displayCandidateEmail), application.Id, HttpContext.Connection.RemoteIpAddress);

            var statusCodeFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>();
            if (statusCodeFeature != null)
            {
                statusCodeFeature.Enabled = false;
            }

            Response.StatusCode = StatusCodes.Status403Forbidden;
            ViewBag.IsUnauthorizedUser = true;
            ViewBag.LoggedInUserEmail = userEmail;
            ViewBag.CandidateMaskedEmail = PiiMaskingHelper.MaskEmail(displayCandidateEmail);
            ViewBag.ErrorMessage = $"Truy cập bị từ chối: Bạn không có quyền thao tác trên hồ sơ của ứng viên khác. Chỉ chính ứng viên sở hữu hồ sơ ({PiiMaskingHelper.MaskEmail(displayCandidateEmail)}) mới có quyền xác nhận rút hồ sơ.";
            ViewBag.ReturnUrl = $"/Applications/Withdraw?token={Uri.EscapeDataString(cleanToken)}";
            return View("WithdrawError");
        }

        // Kiểm tra nếu hồ sơ đã rút trước đó
        if (application.Status == ApplicationStatus.WITHDRAWN)
        {
            var appCodeOld = $"#NOV-2026-{application.Id.ToString("N")[..4].ToUpper()}";
            return View("WithdrawSuccess", new WithdrawApplicationViewModel
            {
                Token = cleanToken,
                ApplicationId = application.Id,
                ApplicationCode = appCodeOld,
                CandidateName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim(),
                CandidateEmail = application.Candidate.Email,
                JobTitle = application.JobPosting.Title,
                DepartmentName = "NoveraTech Engineering",
                IsAlreadyWithdrawn = true,
                WithdrawnAt = application.WithdrawnAt,
                ExistingReason = application.WithdrawReason
            });
        }

        // Tạo chuỗi lý do rút hồ sơ hoàn chỉnh
        var selectedCategory = string.IsNullOrWhiteSpace(reasonCategory) ? "Đã nhận việc nơi khác" : reasonCategory.Trim();
        var fullReason = string.IsNullOrWhiteSpace(reasonDetail)
            ? selectedCategory
            : $"{selectedCategory} (Chi tiết: {reasonDetail.Trim()})";

        var candidateFullName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();
        var jobTitle = application.JobPosting.Title;
        var appCode = $"#NOV-2026-{application.Id.ToString("N")[..4].ToUpper()}";
        var nowUtc = DateTimeOffset.UtcNow;

        // 1. Chuyển trạng thái sang WITHDRAWN (Ứng viên rút) và lưu thông tin
        application.Status = ApplicationStatus.WITHDRAWN;
        application.WithdrawnAt = nowUtc;
        application.WithdrawReason = fullReason;
        application.UpdatedAt = nowUtc;

        // 2. Ghi nhận lịch sử luồng tuyển dụng (ApplicationStageHistory)
        var changedByUserId = (userId != Guid.Empty ? userId : application.Candidate.UserId)
            ?? (await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync());

        await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            FromStageId = application.CurrentStageId,
            ToStageId = application.CurrentStageId,
            ChangedByUserId = changedByUserId,
            Comment = $"Ứng viên chủ động rút hồ sơ ứng tuyển. Lý do: {fullReason}",
            CreatedAt = nowUtc
        });

        // 3. Tìm kiếm Recruiter phụ trách hoặc Hiring Manager để gửi thông báo
        var recruiterId = application.JobPosting.Requisition?.AssignedRecruiterId
                       ?? application.JobPosting.Requisition?.HiringManagerId;

        User? recruiter = null;
        if (recruiterId.HasValue)
        {
            recruiter = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == recruiterId.Value);
        }

        if (recruiter == null)
        {
            recruiter = await _dbContext.Users.FirstOrDefaultAsync(u => 
                u.Role == UserRoles.Recruiter || 
                u.Role == UserRoles.HRManager || 
                u.Role == UserRoles.NameRecruiter || 
                u.Role == UserRoles.NameHRManager || 
                u.UserRoles.Any(ur => ur.Role != null && (ur.Role.Code == RoleCode.RECRUITER || ur.Role.Code == RoleCode.HR_MANAGER)))
                     ?? await _dbContext.Users.FirstOrDefaultAsync();
        }

        // Tạo bản ghi thông báo trong hệ thống (Notifications table)
        if (recruiter != null)
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = recruiter.Id,
                Title = $"Ứng viên rút hồ sơ: {candidateFullName}",
                Content = $"Ứng viên {candidateFullName} ({application.Candidate.Email}) đã rút hồ sơ vị trí '{jobTitle}'. Lý do: {fullReason}",
                Link = "/dashboard/ung-vien",
                IsRead = false,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };
            await _dbContext.Notifications.AddAsync(notification);
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Ứng viên {Name} ({Email}) đã rút hồ sơ vị trí {JobTitle} (AppId: {AppId}). Lý do: {Reason}",
            candidateFullName, application.Candidate.Email, jobTitle, application.Id, fullReason);

        // 4. Gửi email thông báo cho Recruiter và email xác nhận cho Ứng viên (chạy nền)
        _ = Task.Run(async () =>
        {
            try
            {
                if (recruiter != null && !string.IsNullOrWhiteSpace(recruiter.Email))
                {
                    await _emailService.SendRecruiterWithdrawalAlertAsync(
                        recruiter.Email,
                        recruiter.FullName ?? "Chuyên viên Tuyển dụng",
                        candidateFullName,
                        application.Candidate.Email,
                        jobTitle,
                        fullReason,
                        nowUtc);
                }

                await _emailService.SendCandidateWithdrawalConfirmedAsync(
                    application.Candidate.Email,
                    candidateFullName,
                    jobTitle,
                    fullReason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Lỗi khi gửi email thông báo rút hồ sơ: {Msg}", ex.Message);
            }
        });

        var resultModel = new WithdrawApplicationViewModel
        {
            Token = cleanToken,
            ApplicationId = application.Id,
            ApplicationCode = appCode,
            CandidateName = candidateFullName,
            CandidateEmail = application.Candidate.Email,
            JobTitle = jobTitle,
            DepartmentName = application.JobPosting.Requisition?.Department?.Name ?? "NoveraTech Engineering",
            WithdrawnAt = nowUtc,
            ExistingReason = fullReason,
            IsAlreadyWithdrawn = false
        };

        return View("WithdrawSuccess", resultModel);
    }

    /// <summary>
    /// POST /Applications/QuickWithdrawFromPortal
    /// Cho phép ứng viên đã đăng nhập rút hồ sơ trực tiếp từ Bàn làm việc (Dashboard).
    /// </summary>
    [HttpPost("/Applications/QuickWithdrawFromPortal")]
    [Authorize(Roles = $"{UserRoles.Candidate}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickWithdrawFromPortal(
        [FromForm] Guid applicationId,
        [FromForm] string? reasonCategory,
        [FromForm] string? reasonDetail)
    {
        var userEmail = User.FindFirstValue(ClaimTypes.Email)?.ToLower() ?? "";
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(userIdStr, out var userId);

        var application = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
                .ThenInclude(j => j.Requisition)
            .Include(a => a.CurrentStage)
            .FirstOrDefaultAsync(a => a.Id == applicationId && !a.IsDeleted);

        if (application == null)
        {
            return Json(new { success = false, message = "Không tìm thấy hồ sơ ứng tuyển." });
        }

        // Kiểm tra quyền sở hữu hồ sơ
        var isOwner = (userId != Guid.Empty && application.Candidate.UserId == userId)
                   || application.Candidate.Email.ToLower() == userEmail;

        if (!isOwner)
        {
            return Json(new { success = false, message = "Bạn không có quyền thực hiện thao tác trên hồ sơ này." });
        }

        if (application.Status == ApplicationStatus.WITHDRAWN)
        {
            return Json(new { success = true, message = "Hồ sơ của bạn đã được rút trước đó." });
        }

        var selectedCategory = string.IsNullOrWhiteSpace(reasonCategory) ? "Đã nhận việc nơi khác" : reasonCategory.Trim();
        var fullReason = string.IsNullOrWhiteSpace(reasonDetail)
            ? selectedCategory
            : $"{selectedCategory} (Chi tiết: {reasonDetail.Trim()})";

        var candidateFullName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();
        var jobTitle = application.JobPosting.Title;
        var nowUtc = DateTimeOffset.UtcNow;

        application.Status = ApplicationStatus.WITHDRAWN;
        application.WithdrawnAt = nowUtc;
        application.WithdrawReason = fullReason;
        application.UpdatedAt = nowUtc;

        var quickChangedByUserId = (userId != Guid.Empty ? userId : application.Candidate.UserId)
            ?? (await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync());

        await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            FromStageId = application.CurrentStageId,
            ToStageId = application.CurrentStageId,
            ChangedByUserId = quickChangedByUserId,
            Comment = $"Ứng viên chủ động rút hồ sơ từ Cổng ứng viên. Lý do: {fullReason}",
            CreatedAt = nowUtc
        });

        // Tạo thông báo cho Recruiter
        var recruiterId = application.JobPosting.Requisition?.AssignedRecruiterId
                       ?? application.JobPosting.Requisition?.HiringManagerId;

        User? recruiter = null;
        if (recruiterId.HasValue)
        {
            recruiter = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == recruiterId.Value);
        }
        if (recruiter == null)
        {
            recruiter = await _dbContext.Users.FirstOrDefaultAsync(u => 
                u.Role == UserRoles.Recruiter || 
                u.Role == UserRoles.HRManager || 
                u.Role == UserRoles.NameRecruiter || 
                u.Role == UserRoles.NameHRManager || 
                u.UserRoles.Any(ur => ur.Role != null && (ur.Role.Code == RoleCode.RECRUITER || ur.Role.Code == RoleCode.HR_MANAGER)))
                     ?? await _dbContext.Users.FirstOrDefaultAsync();
        }

        if (recruiter != null)
        {
            await _dbContext.Notifications.AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = recruiter.Id,
                Title = $"Ứng viên rút hồ sơ: {candidateFullName}",
                Content = $"Ứng viên {candidateFullName} ({application.Candidate.Email}) đã rút hồ sơ vị trí '{jobTitle}'. Lý do: {fullReason}",
                Link = "/dashboard/ung-vien",
                IsRead = false,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });
        }

        await _dbContext.SaveChangesAsync();

        // Gửi email
        _ = Task.Run(async () =>
        {
            try
            {
                if (recruiter != null && !string.IsNullOrWhiteSpace(recruiter.Email))
                {
                    await _emailService.SendRecruiterWithdrawalAlertAsync(
                        recruiter.Email,
                        recruiter.FullName ?? "Chuyên viên Tuyển dụng",
                        candidateFullName,
                        application.Candidate.Email,
                        jobTitle,
                        fullReason,
                        nowUtc);
                }

                await _emailService.SendCandidateWithdrawalConfirmedAsync(
                    application.Candidate.Email,
                    candidateFullName,
                    jobTitle,
                    fullReason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Lỗi khi gửi email thông báo rút hồ sơ: {Msg}", ex.Message);
            }
        });

        return Json(new { success = true, message = "Đã hoàn tất rút hồ sơ ứng tuyển thành công." });
    }
}
