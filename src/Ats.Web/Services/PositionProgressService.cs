using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Requisitions;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

/// <summary>
/// Triển khai nghiệp vụ theo dõi tiến độ tuyển dụng vị trí sở hữu của Trưởng bộ phận (SCRUM-351).
/// </summary>
public class PositionProgressService(
    ApplicationDbContext dbContext,
    ILogger<PositionProgressService> logger) : IPositionProgressService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<PositionProgressService> _logger = logger;

    /// <summary>
    /// Tính toán số ngày còn lại so với ngày cần người và phân loại mức độ khẩn cấp theo chuẩn thiết kế.
    /// </summary>
    public static (int? DaysRemaining, string DaysRemainingDisplay, string UrgencyLevel, string UrgencyBadgeClass, string UrgencyMessage) CalculateDaysRemaining(DateOnly? targetHireDate)
    {
        if (!targetHireDate.HasValue)
        {
            return (null, "Chưa thiết lập", "NOT_SET", "bg-secondary-subtle text-secondary border border-secondary-subtle", "Chưa ấn định ngày cần người theo kế hoạch");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        int days = targetHireDate.Value.DayNumber - today.DayNumber;

        if (days < 0)
        {
            int overdueDays = Math.Abs(days);
            return (days, $"Quá hạn {overdueDays} ngày", "DANGER", "bg-danger-subtle text-danger border border-danger-subtle", $"Đã quá hạn {overdueDays} ngày so với ngày cần người ({targetHireDate.Value:dd/MM/yyyy})");
        }
        else if (days == 0)
        {
            return (0, "Hôm nay là hạn chót", "WARNING", "bg-warning-subtle text-warning-emphasis border border-warning-subtle", $"Hôm nay ({targetHireDate.Value:dd/MM/yyyy}) là ngày cần người theo kế hoạch tuyển dụng");
        }
        else if (days <= 14)
        {
            return (days, $"Còn {days} ngày", "WARNING", "bg-warning-subtle text-warning-emphasis border border-warning-subtle", $"Chỉ còn {days} ngày nữa đến hạn cần người ({targetHireDate.Value:dd/MM/yyyy}) - Cần ưu tiên chốt phỏng vấn");
        }
        else
        {
            return (days, $"Còn {days} ngày", "NORMAL", "bg-success-subtle text-success border border-success-subtle", $"Còn {days} ngày đến hạn cần người ({targetHireDate.Value:dd/MM/yyyy}) - Tiến độ ổn định");
        }
    }

    /// <inheritdoc />
    public async Task<List<PositionProgressSummaryDto>> GetOwnedPositionsSummaryAsync(
        Guid hiringManagerId, 
        CancellationToken cancellationToken = default)
    {
        var requisitions = await _dbContext.JobRequisitions
            .AsNoTracking()
            .Include(r => r.JobPosition)
            .Include(r => r.Department)
            .Where(r => r.HiringManagerId == hiringManagerId && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        if (requisitions.Count == 0)
        {
            return new List<PositionProgressSummaryDto>();
        }

        var reqIds = requisitions.Select(r => r.Id).ToList();

        // Thống kê số lượng hồ sơ đang hoạt động và số lượng đã tuyển dụng (Stage 6)
        var appsData = await _dbContext.Applications
            .AsNoTracking()
            .Include(a => a.CurrentStage)
            .Include(a => a.JobPosting)
            .Where(a => !a.IsDeleted && a.Status != ApplicationStatus.WITHDRAWN && reqIds.Contains(a.JobPosting.RequisitionId))
            .Select(a => new
            {
                RequisitionId = a.JobPosting.RequisitionId,
                StageOrder = a.CurrentStage.StageOrder,
                IsTerminal = a.CurrentStage.StageOrder == 7 // Bị loại / Từ chối
            })
            .ToListAsync(cancellationToken);

        // Đếm số lượng ứng viên chờ ý kiến đánh giá của Trưởng bộ phận
        var pendingEvals = await _dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Application)
                .ThenInclude(a => a.JobPosting)
            .Include(i => i.Panelists)
            .Include(i => i.Evaluations)
            .Where(i => !i.IsDeleted && 
                        i.Status != InterviewStatus.CANCELLED &&
                        reqIds.Contains(i.Application.JobPosting.RequisitionId) &&
                        (i.Panelists.Any(p => p.InterviewerId == hiringManagerId && !p.IsDeleted) || 
                         i.Application.JobPosting.Requisition.HiringManagerId == hiringManagerId) &&
                        !i.Evaluations.Any(e => e.InterviewerId == hiringManagerId && e.IsSubmitted && !e.IsDeleted))
            .Select(i => i.Application.JobPosting.RequisitionId)
            .ToListAsync(cancellationToken);

        var result = new List<PositionProgressSummaryDto>();

        foreach (var r in requisitions)
        {
            var (daysRem, daysDisp, urgLvl, urgClass, _) = CalculateDaysRemaining(r.TargetHireDate);
            var posApps = appsData.Where(a => a.RequisitionId == r.Id).ToList();
            var hiredCount = posApps.Count(a => a.StageOrder == 6);
            var activeCount = posApps.Count(a => !a.IsTerminal);
            var pendingFeedbackCount = pendingEvals.Count(id => id == r.Id);

            var statusDisplay = r.Status switch
            {
                RequisitionStatus.APPROVED => "Đang tuyển dụng",
                RequisitionStatus.PENDING_APPROVAL => "Chờ phê duyệt",
                RequisitionStatus.CHANGES_REQUESTED => "Yêu cầu sửa",
                RequisitionStatus.PAUSED => "Tạm dừng",
                RequisitionStatus.CLOSED => "Đã đóng tuyển",
                RequisitionStatus.CANCELLED => "Đã hủy",
                _ => "Bản nháp"
            };

            result.Add(new PositionProgressSummaryDto
            {
                RequisitionId = r.Id,
                Code = r.Code,
                Title = r.JobPosition?.Title ?? "Chưa đặt chức danh",
                DepartmentName = r.Department?.Name ?? "Chưa phân bổ",
                Quantity = r.Quantity,
                HiredCount = hiredCount,
                TargetHireDate = r.TargetHireDate,
                TargetHireDateDisplay = r.TargetHireDate?.ToString("dd/MM/yyyy") ?? "—",
                DaysRemaining = daysRem,
                UrgencyLevel = urgLvl,
                UrgencyBadgeClass = urgClass,
                ActiveCandidateCount = activeCount,
                PendingFeedbackCount = pendingFeedbackCount,
                Status = r.Status,
                StatusDisplay = statusDisplay,
                IsSelected = false
            });
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<PositionRecruitmentProgressViewModel?> GetPositionProgressDetailAsync(
        Guid? requisitionId, 
        Guid currentUserId, 
        bool isElevatedRole = false, 
        CancellationToken cancellationToken = default)
    {
        // 1. Tìm yêu cầu tuyển dụng mục tiêu
        IQueryable<JobRequisition> reqQuery = _dbContext.JobRequisitions
            .AsNoTracking()
            .Include(r => r.JobPosition)
            .Include(r => r.Department)
            .Include(r => r.AssignedRecruiter)
            .Include(r => r.Approvals)
            .Where(r => !r.IsDeleted);

        JobRequisition? requisition;

        if (requisitionId.HasValue && requisitionId.Value != Guid.Empty)
        {
            reqQuery = reqQuery.Where(r => r.Id == requisitionId.Value);
            if (!isElevatedRole)
            {
                reqQuery = reqQuery.Where(r => r.HiringManagerId == currentUserId);
            }
            requisition = await reqQuery.FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            // Mặc định chọn vị trí tuyển dụng mới nhất do user sở hữu (hoặc vị trí bất kỳ nếu là elevated role)
            if (!isElevatedRole)
            {
                reqQuery = reqQuery.Where(r => r.HiringManagerId == currentUserId);
            }
            requisition = await reqQuery
                .OrderByDescending(r => r.Status == RequisitionStatus.APPROVED)
                .ThenByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Lấy danh sách tóm tắt tất cả các vị trí thuộc quyền xem của người dùng để build dropdown switcher
        var ownedPositionsSummary = await GetOwnedPositionsSummaryAsync(
            isElevatedRole && requisition != null ? requisition.HiringManagerId : currentUserId, 
            cancellationToken);

        if (requisition == null)
        {
            // Nếu không có vị trí nào, trả về ViewModel rỗng có danh sách OwnedPositions
            return new PositionRecruitmentProgressViewModel
            {
                OwnedPositions = ownedPositionsSummary
            };
        }

        // Đánh dấu vị trí đang được chọn
        foreach (var op in ownedPositionsSummary)
        {
            if (op.RequisitionId == requisition.Id)
            {
                op.IsSelected = true;
            }
        }

        // 2. Tính toán ngày cần người và phân cấp độ khẩn cấp
        var (daysRem, daysDisp, urgLvl, urgClass, urgMsg) = CalculateDaysRemaining(requisition.TargetHireDate);

        // 3. Lấy thông tin tin tuyển dụng liên kết (JobPosting)
        var jobPosting = await _dbContext.JobPostings
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.RequisitionId == requisition.Id && !j.IsDeleted, cancellationToken);

        var workLocation = jobPosting?.WorkLocation ?? "Hà Nội";
        var employmentTypeStr = jobPosting != null ? jobPosting.EmploymentType.ToString() : "Toàn thời gian (Full-time)";

        // 4. Lấy danh sách quy trình các giai đoạn tuyển dụng (Pipeline Stages)
        var allStages = await _dbContext.PipelineStages
            .AsNoTracking()
            .OrderBy(s => s.StageOrder)
            .ToListAsync(cancellationToken);

        // 5. Lấy toàn bộ hồ sơ ứng tuyển của riêng vị trí này
        var applications = await _dbContext.Applications
            .AsNoTracking()
            .Include(a => a.Candidate)
            .Include(a => a.Resume)
            .Include(a => a.CurrentStage)
            .Where(a => a.JobPosting.RequisitionId == requisition.Id && !a.IsDeleted && a.Status != ApplicationStatus.WITHDRAWN)
            .OrderByDescending(a => a.AppliedAt ?? a.CreatedAt)
            .ToListAsync(cancellationToken);

        int totalApps = applications.Count;
        int hiredCount = applications.Count(a => a.CurrentStage.StageOrder == 6);
        int activeCandidatesCount = applications.Count(a => a.CurrentStage.StageOrder != 7);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Xây dựng phễu tuyển dụng từng giai đoạn của riêng vị trí
        var stageDtos = new List<PositionStageFunnelDto>();
        foreach (var stg in allStages)
        {
            var stageApps = applications.Where(a => a.CurrentStageId == stg.Id).ToList();
            int stageCount = stageApps.Count;
            double pct = totalApps > 0 ? Math.Round((double)stageCount / totalApps * 100, 1) : 0;

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

            var candidateDtos = stageApps.Select(a =>
            {
                var cand = a.Candidate;
                var candName = cand != null ? $"{cand.FirstName} {cand.LastName}".Trim() : "Ứng viên";
                int daysInStage = a.StageEnteredAt.HasValue 
                    ? Math.Max(0, (int)(DateTimeOffset.UtcNow - a.StageEnteredAt.Value).TotalDays)
                    : (int)(DateTimeOffset.UtcNow - a.CreatedAt).TotalDays;

                return new PositionCandidateItemDto
                {
                    ApplicationId = a.Id,
                    CandidateId = a.CandidateId,
                    CandidateName = candName,
                    Email = cand?.Email ?? string.Empty,
                    Phone = cand?.Phone ?? string.Empty,
                    AppliedDateDisplay = (a.AppliedAt ?? a.CreatedAt).ToString("dd/MM/yyyy"),
                    Rating = a.Rating,
                    DaysInStage = daysInStage,
                    IsSlaOverdue = a.IsSlaOverdue,
                    ResumeFileName = a.Resume?.FileName,
                    ResumeFilePath = a.Resume?.FilePath,
                    Status = a.Status.ToString()
                };
            }).ToList();

            stageDtos.Add(new PositionStageFunnelDto
            {
                StageId = stg.Id,
                StageOrder = stg.StageOrder,
                StageName = stg.Name,
                ShortName = shortName,
                ColorCode = stageColor,
                CandidateCount = stageCount,
                Percentage = pct,
                IsTerminal = isTerminal,
                Candidates = candidateDtos
            });
        }

        // 6. Lấy lịch phỏng vấn sắp tới của vị trí này
        var nowUtc = DateTimeOffset.UtcNow;
        var upcomingInterviews = await _dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Panelists)
                .ThenInclude(p => p.Interviewer)
            .Where(i => i.Application.JobPosting.RequisitionId == requisition.Id && 
                        !i.IsDeleted && 
                        i.Status != InterviewStatus.CANCELLED &&
                        i.StartTime >= nowUtc)
            .OrderBy(i => i.StartTime)
            .Take(15)
            .ToListAsync(cancellationToken);

        var interviewDtos = upcomingInterviews.Select(i =>
        {
            var cand = i.Application?.Candidate;
            var candName = cand != null ? $"{cand.FirstName} {cand.LastName}".Trim() : "Ứng viên";
            var panelists = i.Panelists.Any()
                ? string.Join(", ", i.Panelists.Where(p => !p.IsDeleted).Select(p => p.Interviewer?.FullName ?? "Giám khảo"))
                : "Hội đồng phỏng vấn";

            var badgeClass = i.Status switch
            {
                InterviewStatus.COMPLETED => "bg-success-subtle text-success border border-success-subtle",
                InterviewStatus.CANCELLED => "bg-danger-subtle text-danger border border-danger-subtle",
                _ => i.CandidateConfirmed == CandidateConfirmStatus.ACCEPTED 
                    ? "bg-info-subtle text-info-emphasis border border-info-subtle" 
                    : "bg-warning-subtle text-warning-emphasis border border-warning-subtle"
            };

            var statusDisplay = i.Status switch
            {
                InterviewStatus.COMPLETED => "Đã hoàn thành",
                InterviewStatus.CANCELLED => "Đã hủy",
                InterviewStatus.RESCHEDULED => "Đã đổi lịch",
                _ => i.CandidateConfirmed == CandidateConfirmStatus.ACCEPTED ? "Ứng viên đã nhận lời" : (i.CandidateConfirmed == CandidateConfirmStatus.DECLINED ? "Đề nghị đổi lịch" : "Chờ xác nhận")
            };

            return new PositionInterviewDto
            {
                InterviewId = i.Id,
                ApplicationId = i.ApplicationId,
                CandidateName = candName,
                CandidateEmail = cand?.Email ?? string.Empty,
                RoundNumber = i.RoundNumber,
                RoundTitle = i.Title,
                StartTime = i.StartTime,
                EndTime = i.EndTime,
                TimeDisplay = i.StartTime.HasValue && i.EndTime.HasValue 
                    ? $"{i.StartTime.Value:HH:mm} - {i.EndTime.Value:HH:mm}" 
                    : "Giờ họp linh hoạt",
                DateDisplay = i.StartTime.HasValue 
                    ? $"{i.StartTime.Value:dddd, dd/MM/yyyy}" 
                    : "Trong tuần này",
                PanelistNames = panelists,
                LocationOrLink = string.IsNullOrWhiteSpace(i.LocationOrLink) ? "https://meet.google.com" : i.LocationOrLink,
                CandidateConfirmed = i.CandidateConfirmed,
                Status = i.Status,
                StatusBadgeClass = badgeClass,
                StatusDisplay = statusDisplay
            };
        }).ToList();

        // 7. Lấy danh sách ứng viên đang chờ Trưởng bộ phận cho ý kiến đánh giá
        // Điều kiện: Buổi phỏng vấn đã diễn ra hoặc đang đến, Trưởng bộ phận là Panelist hoặc là Hiring Manager của vị trí, và chưa nộp phiếu đánh giá
        var pendingEvalInterviews = await _dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Panelists)
            .Include(i => i.Evaluations)
            .Where(i => i.Application.JobPosting.RequisitionId == requisition.Id && 
                        !i.IsDeleted && 
                        i.Status != InterviewStatus.CANCELLED &&
                        (i.Panelists.Any(p => p.InterviewerId == currentUserId && !p.IsDeleted) || 
                         requisition.HiringManagerId == currentUserId) &&
                        !i.Evaluations.Any(e => e.InterviewerId == currentUserId && e.IsSubmitted && !e.IsDeleted))
            .OrderByDescending(i => i.StartTime ?? i.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        var pendingFeedbackDtos = pendingEvalInterviews.Select(i =>
        {
            var cand = i.Application?.Candidate;
            var candName = cand != null ? $"{cand.FirstName} {cand.LastName}".Trim() : "Ứng viên";
            var isInvited = i.Panelists.Any(p => p.InterviewerId == currentUserId && !p.IsDeleted);
            var eval = i.Evaluations.FirstOrDefault(e => e.InterviewerId == currentUserId && !e.IsDeleted);
            int waitingDays = i.StartTime.HasValue && i.StartTime.Value < nowUtc 
                ? Math.Max(0, (int)(nowUtc - i.StartTime.Value).TotalDays) 
                : 0;

            return new PositionPendingFeedbackDto
            {
                InterviewId = i.Id,
                ApplicationId = i.ApplicationId,
                CandidateId = i.Application?.CandidateId ?? Guid.Empty,
                CandidateName = candName,
                CandidateEmail = cand?.Email ?? string.Empty,
                JobTitle = requisition.JobPosition?.Title ?? "Vị trí tuyển dụng",
                RoundTitle = i.Title,
                InterviewTime = i.StartTime,
                InterviewTimeDisplay = i.StartTime.HasValue ? $"{i.StartTime.Value:HH:mm}" : "Chưa rõ",
                InterviewDateDisplay = i.StartTime.HasValue ? $"{i.StartTime.Value:dd/MM/yyyy}" : "Chưa rõ",
                DaysWaitingForFeedback = waitingDays,
                EvaluationId = eval?.Id,
                IsInvitedPanelist = isInvited
            };
        }).ToList();

        // 8. Soạn thảo tóm tắt báo cáo cho Sếp (Executive Summary 1-Click Copy)
        var posTitle = requisition.JobPosition?.Title ?? requisition.Code;
        var inInterviewCount = stageDtos.Where(s => s.StageOrder is 3 or 4).Sum(s => s.CandidateCount);
        var inOfferCount = stageDtos.Where(s => s.StageOrder == 5).Sum(s => s.CandidateCount);

        string paceSummary = daysRem.HasValue
            ? (daysRem.Value < 0 
                ? $"Tiến độ đang bị chậm {Math.Abs(daysRem.Value)} ngày so với kế hoạch, đang cùng Recruiter tập trung đẩy nhanh phỏng vấn."
                : (daysRem.Value <= 14 
                    ? $"Thời hạn còn {daysRem.Value} ngày, phễu tuyển dụng đang ở các vòng nước rút cuối cùng."
                    : $"Thời hạn còn {daysRem.Value} ngày, tiến độ phễu diễn ra đúng kế hoạch."))
            : "Chưa ấn định ngày cần người chính thức.";

        var reportText = 
            $"[BÁO CÁO TIẾN ĐỘ TUYỂN DỤNG - {posTitle.ToUpper()}]\n" +
            $"• Mã yêu cầu: {requisition.Code} | Bộ phận: {requisition.Department?.Name ?? "N/A"}\n" +
            $"• Hạn cần người: {requisition.TargetHireDate?.ToString("dd/MM/yyyy") ?? "Chưa đặt"} ({daysDisp})\n" +
            $"• Chỉ tiêu: {requisition.Quantity} nhân sự (Đã tuyển: {hiredCount}/{requisition.Quantity}, Còn thiếu: {Math.Max(0, requisition.Quantity - hiredCount)})\n" +
            $"• Phễu ứng viên: Tổng {totalApps} hồ sơ (Trong đó: {inInterviewCount} hồ sơ đang phỏng vấn, {inOfferCount} hồ sơ đang thương thảo Offer)\n" +
            $"• Phụ trách Nhân sự: {requisition.AssignedRecruiter?.FullName ?? "Chưa phân công"} ({requisition.AssignedRecruiter?.Email ?? ""})\n" +
            $"• Đánh giá: {paceSummary}";

        var reqStatusDisplay = requisition.Status switch
        {
            RequisitionStatus.APPROVED => "Đang mở tuyển dụng",
            RequisitionStatus.PENDING_APPROVAL => "Đang chờ phê duyệt",
            RequisitionStatus.CHANGES_REQUESTED => "Yêu cầu chỉnh sửa",
            RequisitionStatus.PAUSED => "Tạm dừng tuyển",
            RequisitionStatus.CLOSED => "Đã đóng tuyển dụng",
            RequisitionStatus.CANCELLED => "Đã hủy yêu cầu",
            _ => "Bản nháp"
        };

        return new PositionRecruitmentProgressViewModel
        {
            SelectedRequisitionId = requisition.Id,
            RequisitionCode = requisition.Code,
            PositionTitle = posTitle,
            DepartmentName = requisition.Department?.Name ?? "Chưa phân bổ",
            WorkLocation = workLocation,
            EmploymentType = employmentTypeStr,
            RequisitionStatus = requisition.Status,
            RequisitionStatusDisplay = reqStatusDisplay,
            Quantity = requisition.Quantity,
            HiredCount = hiredCount,
            TargetHireDate = requisition.TargetHireDate,
            TargetHireDateDisplay = requisition.TargetHireDate?.ToString("dd/MM/yyyy") ?? "Chưa thiết lập",
            DaysRemaining = daysRem,
            DaysRemainingDisplay = daysDisp,
            UrgencyLevel = urgLvl,
            UrgencyBadgeClass = urgClass,
            UrgencyMessage = urgMsg,
            AssignedRecruiterName = requisition.AssignedRecruiter?.FullName ?? "Chưa phân công",
            AssignedRecruiterEmail = requisition.AssignedRecruiter?.Email ?? string.Empty,
            AssignedRecruiterPhone = requisition.AssignedRecruiter?.PhoneNumber ?? string.Empty,
            ExecutiveBossReportText = reportText,
            TotalApplications = totalApps,
            ActiveCandidatesCount = activeCandidatesCount,
            Stages = stageDtos,
            UpcomingInterviews = interviewDtos,
            PendingFeedbackCandidates = pendingFeedbackDtos,
            OwnedPositions = ownedPositionsSummary
        };
    }
}
