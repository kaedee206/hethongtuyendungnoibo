using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Referral;
using Ats.Web.Models.ViewModels.Tracking;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Ats.Web.Services;

/// <summary>
/// Cài đặt dịch vụ giới thiệu ứng viên nội bộ (Scrum #37)
/// Đảm bảo bảo mật thông tin nội bộ và cung cấp trạng thái tổng quát an toàn
/// </summary>
public class CandidateReferralService : ICandidateReferralService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly IApplicationTrackingService _trackingService;
    private readonly ILogger<CandidateReferralService> _logger;

    public CandidateReferralService(
        ApplicationDbContext dbContext,
        IFileStorageService fileStorageService,
        IApplicationTrackingService trackingService,
        ILogger<CandidateReferralService> logger)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _trackingService = trackingService;
        _logger = logger;
    }

    public async Task<(bool Success, string Message, Guid? ApplicationId, string? TrackingCode)> ReferCandidateAsync(
        ReferCandidateInputModel model,
        Guid referrerUserId)
    {
        if (model == null)
        {
            return (false, "Dữ liệu giới thiệu không hợp lệ.", null, null);
        }

        if (referrerUserId == Guid.Empty)
        {
            return (false, "Không xác định được danh tính nhân sự nội bộ giới thiệu.", null, null);
        }

        try
        {
            // 1. Kiểm tra vị trí tuyển dụng đang mở
            var jobPosting = await _dbContext.JobPostings
                .Include(j => j.Requisition)
                .FirstOrDefaultAsync(j => j.Id == model.JobPostingId && !j.IsDeleted);

            if (jobPosting == null || jobPosting.Status != JobPostingStatus.PUBLISHED)
            {
                return (false, "Vị trí tuyển dụng không tồn tại hoặc đã ngừng tiếp nhận hồ sơ.", null, null);
            }

            var emailClean = (model.CandidateEmail ?? "").Trim().ToLowerInvariant();

            // 2. Tìm hoặc khởi tạo Candidate
            var candidate = await _dbContext.Candidates
                .Include(c => c.Resumes)
                .FirstOrDefaultAsync(c => c.Email.ToLower() == emailClean);

            if (candidate == null)
            {
                candidate = new Candidate
                {
                    Id = Guid.NewGuid(),
                    FirstName = model.CandidateName.Trim(),
                    LastName = string.Empty,
                    Email = emailClean,
                    Phone = model.CandidatePhone?.Trim(),
                    LinkedinUrl = model.LinkedinUrl?.Trim(),
                    Source = CandidateSource.REFERRAL,
                    ReferrerUserId = referrerUserId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.Candidates.AddAsync(candidate);
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                // Cập nhật thông tin bổ sung và đánh dấu nguồn giới thiệu
                candidate.Source = CandidateSource.REFERRAL;
                candidate.ReferrerUserId = referrerUserId;
                if (!string.IsNullOrWhiteSpace(model.CandidatePhone))
                {
                    candidate.Phone = model.CandidatePhone.Trim();
                }
                if (!string.IsNullOrWhiteSpace(model.LinkedinUrl))
                {
                    candidate.LinkedinUrl = model.LinkedinUrl.Trim();
                }
                candidate.UpdatedAt = DateTimeOffset.UtcNow;
            }

            // 3. Xử lý lưu CV
            Resume? resume = null;
            if (model.CvFile != null && model.CvFile.Length > 0)
            {
                var originalFileName = Path.GetFileName(model.CvFile.FileName);
                var savedFilePath = await _fileStorageService.SaveCvAsync(model.CvFile, $"cv_ref_{candidate.Id:N}");

                foreach (var r in candidate.Resumes)
                {
                    r.IsPrimary = false;
                }

                resume = new Resume
                {
                    Id = Guid.NewGuid(),
                    CandidateId = candidate.Id,
                    FileName = originalFileName,
                    FilePath = savedFilePath,
                    FileSize = model.CvFile.Length,
                    MimeType = model.CvFile.ContentType,
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
                    return (false, "Vui lòng đính kèm file CV của ứng viên.", null, null);
                }
            }

            // 4. Kiểm tra xem đã tồn tại hồ sơ cho công việc này chưa
            var existingApp = await _dbContext.Applications
                .FirstOrDefaultAsync(a => a.CandidateId == candidate.Id && a.JobPostingId == jobPosting.Id && !a.IsDeleted);

            if (existingApp != null)
            {
                // Nếu đã có hồ sơ, cập nhật thông tin người giới thiệu và ghi chú
                existingApp.ReferrerUserId = referrerUserId;
                existingApp.ReferralNote = model.ReferralNote.Trim();
                existingApp.UpdatedAt = DateTimeOffset.UtcNow;

                if (string.IsNullOrWhiteSpace(existingApp.TrackingCode))
                {
                    existingApp.TrackingCode = await _trackingService.GenerateUniqueTrackingCodeAsync();
                }

                await _dbContext.SaveChangesAsync();
                return (true, $"Ứng viên đã có hồ sơ ứng tuyển vị trí này. Đã cập nhật nhãn giới thiệu nội bộ thành công với mã tra cứu: {existingApp.TrackingCode}", existingApp.Id, existingApp.TrackingCode);
            }

            // 5. Tìm Stage 1 (Ứng tuyển mới)
            var stageNew = await _dbContext.PipelineStages.OrderBy(s => s.StageOrder).FirstOrDefaultAsync()
                ?? await _dbContext.PipelineStages.FirstAsync();

            var trackingCode = await _trackingService.GenerateUniqueTrackingCodeAsync();

            var application = new Application
            {
                Id = Guid.NewGuid(),
                JobPostingId = jobPosting.Id,
                CandidateId = candidate.Id,
                ResumeId = resume.Id,
                CurrentStageId = stageNew.Id,
                Status = ApplicationStatus.IN_PROCESS,
                TrackingCode = trackingCode,
                ReferrerUserId = referrerUserId,
                ReferralNote = model.ReferralNote.Trim(),
                AppliedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _dbContext.Applications.AddAsync(application);

            // Ghi nhận lịch sử vòng tuyển dụng
            await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                ToStageId = stageNew.Id,
                ChangedByUserId = referrerUserId,
                Comment = $"Nhân sự nội bộ giới thiệu ứng viên kèm ghi chú: {model.ReferralNote.Trim()}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Nhân viên {ReferrerUserId} đã giới thiệu ứng viên {CandidateEmail} cho vị trí {JobTitle} với mã tra cứu {TrackingCode}",
                referrerUserId, emailClean, jobPosting.Title, trackingCode);

            return (true, $"Giới thiệu ứng viên thành công! Mã tra cứu hồ sơ: {trackingCode}", application.Id, trackingCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xử lý giới thiệu ứng viên nội bộ");
            return (false, "Đã có lỗi xảy ra trong quá trình ghi nhận hồ sơ giới thiệu. Vui lòng thử lại sau.", null, null);
        }
    }

    public async Task<MyReferralsViewModel> GetMyReferralsAsync(Guid referrerUserId)
    {
        var model = new MyReferralsViewModel();

        if (referrerUserId == Guid.Empty)
        {
            return model;
        }

        // Truy vấn các hồ sơ do chính nhân viên này giới thiệu
        var apps = await _dbContext.Applications
            .AsNoTracking()
            .Include(a => a.Candidate)
                .ThenInclude(c => c.Resumes)
            .Include(a => a.JobPosting)
                .ThenInclude(j => j.Requisition)
                    .ThenInclude(r => r.Department)
            .Include(a => a.CurrentStage)
            .Include(a => a.Interviews)
            .Where(a => !a.IsDeleted && (a.ReferrerUserId == referrerUserId || (a.Candidate.ReferrerUserId == referrerUserId && a.Candidate.Source == CandidateSource.REFERRAL)))
            .OrderByDescending(a => a.AppliedAt ?? a.CreatedAt)
            .ToListAsync();

        var items = new List<ReferralApplicationItemDto>();

        foreach (var app in apps)
        {
            var candName = $"{app.Candidate.FirstName} {app.Candidate.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(candName)) candName = "Ứng viên";

            var generalStatus = MapToPublicStatus(app);
            var (statusDisplay, badgeClass, progress) = GetGeneralStatusMeta(generalStatus, app);
            var primaryResume = app.Candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? app.Candidate.Resumes.FirstOrDefault();

            var item = new ReferralApplicationItemDto
            {
                ApplicationId = app.Id,
                CandidateName = candName,
                MaskedEmail = MaskEmail(app.Candidate.Email),
                CandidatePhone = app.Candidate.Phone,
                JobTitle = app.JobPosting?.Title ?? "Vị trí tuyển dụng",
                DepartmentName = app.JobPosting?.Requisition?.Department?.Name ?? "Khối Công nghệ & Sản phẩm",
                TrackingCode = app.TrackingCode,
                AppliedAt = app.AppliedAt ?? app.CreatedAt,
                AppliedDateDisplay = (app.AppliedAt ?? app.CreatedAt).ToString("dd/MM/yyyy"),
                GeneralStatus = generalStatus,
                GeneralStatusDisplay = statusDisplay,
                StatusBadgeClass = badgeClass,
                ProgressPercentage = progress,
                ReferralNote = app.ReferralNote,
                ResumeFileName = primaryResume?.FileName,
                ResumeFilePath = primaryResume?.FilePath,
                StepperSteps = BuildStepperSteps(generalStatus, app)
            };

            items.Add(item);
        }

        model.Referrals = items;
        model.TotalReferrals = items.Count;
        model.ReceivedCount = items.Count(x => x.GeneralStatus == PublicApplicationStatus.Received);
        model.UnderReviewCount = items.Count(x => x.GeneralStatus == PublicApplicationStatus.UnderReview);
        model.InterviewInvitedCount = items.Count(x => x.GeneralStatus == PublicApplicationStatus.InterviewInvited);
        model.ConcludedCount = items.Count(x => x.GeneralStatus == PublicApplicationStatus.Concluded);

        // Nạp danh sách việc làm đang mở cho form giới thiệu
        model.OpenJobs = await GetOpenJobPostingsAsync();

        return model;
    }

    public async Task<List<JobPostingSelectDto>> GetOpenJobPostingsAsync()
    {
        return await _dbContext.JobPostings
            .AsNoTracking()
            .Include(j => j.Requisition)
                .ThenInclude(r => r.Department)
            .Where(j => !j.IsDeleted && j.Status == JobPostingStatus.PUBLISHED)
            .OrderByDescending(j => j.PublishedAt ?? j.CreatedAt)
            .Select(j => new JobPostingSelectDto
            {
                Id = j.Id,
                Title = j.Title,
                DepartmentName = j.Requisition.Department != null ? j.Requisition.Department.Name : "Khối Công nghệ",
                Location = j.WorkLocation ?? "Hà Nội"
            })
            .ToListAsync();
    }

    /// <summary>
    /// Ánh xạ trạng thái nội bộ sang 4 mức tổng quát an toàn (Scrum #35 & #37)
    /// Tuyệt đối không để lộ dữ liệu nội bộ (rejection_note, rating, interviewer name)
    /// </summary>
    private static PublicApplicationStatus MapToPublicStatus(Application app)
    {
        if (app.Status == ApplicationStatus.HIRED ||
            app.Status == ApplicationStatus.REJECTED ||
            app.Status == ApplicationStatus.WITHDRAWN)
        {
            return PublicApplicationStatus.Concluded;
        }

        var stageOrder = app.CurrentStage?.StageOrder ?? 1;
        var stageName = app.CurrentStage?.Name?.ToLowerInvariant() ?? string.Empty;

        if (stageOrder >= 6 || stageName.Contains("thành công") || stageName.Contains("từ chối"))
        {
            return PublicApplicationStatus.Concluded;
        }

        if (stageOrder is 3 or 4 || stageName.Contains("phỏng vấn") || (app.Interviews != null && app.Interviews.Any()))
        {
            return PublicApplicationStatus.InterviewInvited;
        }

        if (stageOrder is 2 or 5 || stageName.Contains("sàng lọc") || stageName.Contains("offer"))
        {
            return PublicApplicationStatus.UnderReview;
        }

        return PublicApplicationStatus.Received;
    }

    private static (string Display, string BadgeClass, int Progress) GetGeneralStatusMeta(PublicApplicationStatus status, Application app)
    {
        return status switch
        {
            PublicApplicationStatus.Received =>
                ("Đã nhận", "bg-secondary-subtle text-secondary border border-secondary-subtle", 25),

            PublicApplicationStatus.UnderReview =>
                ("Đang xem xét", "bg-warning-subtle text-warning-emphasis border border-warning-subtle", 50),

            PublicApplicationStatus.InterviewInvited =>
                ("Mời phỏng vấn", "bg-info-subtle text-info-emphasis border border-info-subtle", 75),

            PublicApplicationStatus.Concluded =>
                app.Status == ApplicationStatus.HIRED || (app.CurrentStage?.StageOrder == 6)
                    ? ("Kết thúc (Trúng tuyển)", "bg-success-subtle text-success border border-success-subtle", 100)
                    : ("Kết thúc", "bg-dark-subtle text-dark border border-dark-subtle", 100),

            _ => ("Đã nhận", "bg-secondary-subtle text-secondary border", 25)
        };
    }

    private static List<TrackingStageStepViewModel> BuildStepperSteps(PublicApplicationStatus status, Application app)
    {
        var appliedDate = (app.AppliedAt ?? app.CreatedAt).ToString("dd/MM/yyyy");
        var updatedDate = app.UpdatedAt.ToString("dd/MM/yyyy");

        return status switch
        {
            PublicApplicationStatus.Received =>
            [
                new() { StepIndex = 1, Title = "Đã nhận", Subtitle = "Hồ sơ đã được tiếp nhận thành công vào hệ thống tuyển dụng", State = "active", DateDisplay = appliedDate },
                new() { StepIndex = 2, Title = "Đang xem xét", Subtitle = "Ban tuyển dụng đang tiến hành sàng lọc và đánh giá hồ sơ", State = "upcoming" },
                new() { StepIndex = 3, Title = "Mời phỏng vấn", Subtitle = "Lên lịch hoặc trao đổi phỏng vấn chuyên môn", State = "upcoming" },
                new() { StepIndex = 4, Title = "Kết thúc", Subtitle = "Hoàn tất toàn bộ quy trình xét tuyển", State = "upcoming" }
            ],

            PublicApplicationStatus.UnderReview =>
            [
                new() { StepIndex = 1, Title = "Đã nhận", Subtitle = "Hồ sơ đã được tiếp nhận thành công vào hệ thống tuyển dụng", State = "completed", DateDisplay = appliedDate },
                new() { StepIndex = 2, Title = "Đang xem xét", Subtitle = "Ban tuyển dụng đang tiến hành sàng lọc và đánh giá hồ sơ", State = "active", DateDisplay = updatedDate },
                new() { StepIndex = 3, Title = "Mời phỏng vấn", Subtitle = "Lên lịch hoặc trao đổi phỏng vấn chuyên môn", State = "upcoming" },
                new() { StepIndex = 4, Title = "Kết thúc", Subtitle = "Hoàn tất toàn bộ quy trình xét tuyển", State = "upcoming" }
            ],

            PublicApplicationStatus.InterviewInvited =>
            [
                new() { StepIndex = 1, Title = "Đã nhận", Subtitle = "Hồ sơ đã được tiếp nhận thành công vào hệ thống tuyển dụng", State = "completed", DateDisplay = appliedDate },
                new() { StepIndex = 2, Title = "Đang xem xét", Subtitle = "Hồ sơ đã vượt qua vòng sơ loại chuyên môn", State = "completed", DateDisplay = updatedDate },
                new() { StepIndex = 3, Title = "Mời phỏng vấn", Subtitle = "Ứng viên đã được mời tham gia các vòng phỏng vấn chuyên môn", State = "active", DateDisplay = updatedDate },
                new() { StepIndex = 4, Title = "Kết thúc", Subtitle = "Hoàn tất toàn bộ quy trình xét tuyển", State = "upcoming" }
            ],

            PublicApplicationStatus.Concluded =>
            [
                new() { StepIndex = 1, Title = "Đã nhận", Subtitle = "Hồ sơ đã được tiếp nhận thành công vào hệ thống tuyển dụng", State = "completed", DateDisplay = appliedDate },
                new() { StepIndex = 2, Title = "Đang xem xét", Subtitle = "Hồ sơ đã hoàn thành đánh giá", State = "completed", DateDisplay = updatedDate },
                new() { StepIndex = 3, Title = "Mời phỏng vấn", Subtitle = "Đã hoàn thành các vòng phỏng vấn", State = "completed", DateDisplay = updatedDate },
                new() { StepIndex = 4, Title = "Kết thúc", Subtitle = "Quy trình tuyển dụng cho vị trí này đã chính thức khép lại", State = "completed", DateDisplay = updatedDate }
            ],

            _ => []
        };
    }

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "Chưa cập nhật";
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return email;

        var namePart = email[..atIndex];
        var domainPart = email[atIndex..];

        if (namePart.Length <= 2)
        {
            return $"{namePart[0]}***{domainPart}";
        }

        return $"{namePart[0]}***{namePart[^1]}{domainPart}";
    }
}
