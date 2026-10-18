using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs.Sla;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

/// <summary>
/// Service triển khai nghiệp vụ cấu hình và quét cảnh báo quá hạn SLA giai đoạn tuyển dụng (SCRUM-327).
/// </summary>
public class SlaService : ISlaService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly ILogger<SlaService> _logger;

    public SlaService(
        ApplicationDbContext dbContext,
        IEmailService emailService,
        ILogger<SlaService> logger)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<PipelineStageSlaDto>> GetStageSlaConfigsAsync(CancellationToken cancellationToken = default)
    {
        var stages = await _dbContext.PipelineStages
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.StageOrder)
            .ToListAsync(cancellationToken);

        var activeApps = await _dbContext.Applications
            .Include(a => a.CurrentStage)
            .Where(a => !a.IsDeleted && 
                        a.Status != ApplicationStatus.HIRED && 
                        a.Status != ApplicationStatus.REJECTED && 
                        a.Status != ApplicationStatus.WITHDRAWN)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var result = new List<PipelineStageSlaDto>();

        foreach (var stage in stages)
        {
            var appsInStage = activeApps.Where(a => a.CurrentStageId == stage.Id).ToList();
            var overdueCount = 0;

            if (stage.IsSlaEnabled && stage.SlaDays > 0)
            {
                overdueCount = appsInStage.Count(a =>
                {
                    var enteredAt = a.StageEnteredAt ?? a.AppliedAt ?? a.CreatedAt;
                    return (now - enteredAt).TotalDays >= stage.SlaDays;
                });
            }

            result.Add(new PipelineStageSlaDto
            {
                Id = stage.Id,
                Name = stage.Name,
                StageOrder = stage.StageOrder,
                ColorCode = stage.ColorCode,
                SlaDays = stage.SlaDays,
                IsSlaEnabled = stage.IsSlaEnabled,
                ActiveCandidateCount = appsInStage.Count,
                OverdueCandidateCount = overdueCount
            });
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateStageSlaConfigsAsync(List<UpdateStageSlaDto> configs, CancellationToken cancellationToken = default)
    {
        if (configs == null || configs.Count == 0)
            return false;

        var stageIds = configs.Select(c => c.Id).ToList();
        var stages = await _dbContext.PipelineStages
            .Where(s => stageIds.Contains(s.Id) && !s.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var item in configs)
        {
            var stage = stages.FirstOrDefault(s => s.Id == item.Id);
            if (stage != null)
            {
                stage.SlaDays = Math.Max(0, item.SlaDays);
                stage.IsSlaEnabled = item.IsSlaEnabled;
                stage.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã cập nhật cấu hình SLA cho {Count} giai đoạn tuyển dụng (SCRUM-327).", configs.Count);
        return true;
    }

    /// <inheritdoc />
    public async Task<SlaScanResultDto> ScanAndAlertOverdueApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var today = now.Date;

        var result = new SlaScanResultDto
        {
            ScannedAt = now
        };

        // Lấy tất cả hồ sơ đang trong tiến trình xét tuyển
        var applications = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
                .ThenInclude(j => j.Requisition)
                    .ThenInclude(r => r.Department)
            .Include(a => a.CurrentStage)
            .Where(a => !a.IsDeleted &&
                        a.Status != ApplicationStatus.HIRED &&
                        a.Status != ApplicationStatus.REJECTED &&
                        a.Status != ApplicationStatus.WITHDRAWN)
            .ToListAsync(cancellationToken);

        // Fallback recruiter mặc định nếu phiếu chưa gán recruiter cụ thể
        var fallbackRecruiters = await _dbContext.Users
            .Where(u => u.Status == "ACTIVE" && (
                u.Role == UserRoles.Recruiter || 
                u.Role == UserRoles.HRManager || 
                u.Role == UserRoles.NameRecruiter || 
                u.Role == UserRoles.NameHRManager || 
                u.UserRoles.Any(ur => ur.Role != null && (ur.Role.Code == RoleCode.RECRUITER || ur.Role.Code == RoleCode.HR_MANAGER))))
            .ToListAsync(cancellationToken);

        foreach (var app in applications)
        {
            result.TotalScanned++;
            var stage = app.CurrentStage;

            // Nếu giai đoạn không áp dụng SLA hoặc số ngày <= 0 -> bỏ qua
            if (stage == null || !stage.IsSlaEnabled || stage.SlaDays <= 0)
            {
                app.IsSlaOverdue = false;
                continue;
            }

            var enteredAt = app.StageEnteredAt ?? app.AppliedAt ?? app.CreatedAt;
            var daysInStage = (int)(now - enteredAt).TotalDays;

            if (daysInStage >= stage.SlaDays)
            {
                // Hồ sơ bị quá hạn SLA
                app.IsSlaOverdue = true;
                result.OverdueCount++;

                var overdueDays = Math.Max(1, daysInStage - stage.SlaDays);
                var candFullName = $"{app.Candidate?.FirstName} {app.Candidate?.LastName}".Trim();
                var candEmail = app.Candidate?.Email ?? "N/A";
                var jobTitle = app.JobPosting?.Title ?? "Vị trí tuyển dụng";

                // Kiểm tra điều kiện chống nhắc trùng trong cùng ngày (SCRUM-327)
                var alreadyAlertedToday = app.SlaOverdueAlertedAt.HasValue && 
                                          app.SlaOverdueAlertedAt.Value.Date == today;

                // Xác định Recruiter nhận nhắc việc
                var recruiterId = app.JobPosting?.Requisition?.AssignedRecruiterId 
                               ?? app.JobPosting?.Requisition?.HiringManagerId;

                User? targetRecruiter = null;
                if (recruiterId.HasValue)
                {
                    targetRecruiter = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == recruiterId.Value, cancellationToken);
                }

                if (targetRecruiter == null && fallbackRecruiters.Count > 0)
                {
                    targetRecruiter = fallbackRecruiters.First();
                }

                var alertDetail = new OverdueApplicationAlertDto
                {
                    ApplicationId = app.Id,
                    CandidateName = candFullName,
                    CandidateEmail = candEmail,
                    JobTitle = jobTitle,
                    StageName = stage.Name,
                    SlaDays = stage.SlaDays,
                    DaysInStage = daysInStage,
                    OverdueDays = overdueDays,
                    RecruiterEmail = targetRecruiter?.Email ?? "N/A",
                    IsAlreadyAlertedToday = alreadyAlertedToday
                };
                result.OverdueItems.Add(alertDetail);

                if (!alreadyAlertedToday && targetRecruiter != null)
                {
                    // 1. Tạo thông báo in-app cho Recruiter
                    var notification = new Notification
                    {
                        Id = Guid.NewGuid(),
                        UserId = targetRecruiter.Id,
                        Title = $"⚠️ Nhắc việc SLA: {candFullName} ({overdueDays} ngày quá hạn)",
                        Content = $"Hồ sơ ứng viên {candFullName} cho vị trí '{jobTitle}' đã nằm ở giai đoạn '{stage.Name}' {daysInStage} ngày (ngưỡng SLA: {stage.SlaDays} ngày). Vui lòng xử lý để tránh trễ hạn!",
                        Link = $"/dashboard/ung-vien?jobId={app.JobPostingId}",
                        IsRead = false,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    await _dbContext.Notifications.AddAsync(notification, cancellationToken);
                    result.NotificationsCreated++;

                    // 2. Gửi email cảnh báo quá hạn cho Recruiter
                    if (!string.IsNullOrWhiteSpace(targetRecruiter.Email))
                    {
                        try
                        {
                            var sent = await _emailService.SendSlaOverdueAlertAsync(
                                targetRecruiter.Email,
                                targetRecruiter.FullName ?? "Chuyên viên Tuyển dụng",
                                candFullName,
                                candEmail,
                                jobTitle,
                                stage.Name,
                                daysInStage,
                                stage.SlaDays,
                                overdueDays,
                                cancellationToken);

                            if (sent)
                            {
                                result.EmailsSent++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Không thể gửi email cảnh báo SLA tới {Email}: {Msg}", targetRecruiter.Email, ex.Message);
                        }
                    }

                    // 3. Cập nhật mốc thời gian nhắc việc để chống nhắc trùng trong ngày hôm nay
                    app.SlaOverdueAlertedAt = now;
                    app.UpdatedAt = now;
                }
            }
            else
            {
                // Hồ sơ nằm trong hạn SLA bình thường
                app.IsSlaOverdue = false;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Hoàn tất quét SLA (SCRUM-327): Tổng quét {Total}, Quá hạn {Overdue}, Tạo {Notif} thông báo, Gửi {Emails} email.",
            result.TotalScanned, result.OverdueCount, result.NotificationsCreated, result.EmailsSent);

        return result;
    }
}
