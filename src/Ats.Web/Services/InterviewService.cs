using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Dịch vụ điều phối nghiệp vụ Đặt lịch phỏng vấn và Phân công hội đồng phỏng vấn (Scrum - Số 51 / EP-06).
/// Tuân thủ quy chuẩn C# 12 Primary Constructors, xử lý thời gian UTC và kiểm tra tính hợp lệ chặt chẽ.
/// </summary>
public class InterviewService(
    ApplicationDbContext dbContext,
    IEmailService emailService,
    IRequisitionAssignmentService? assignmentService,
    ILogger<InterviewService> logger) : IInterviewService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IEmailService _emailService = emailService;
    private readonly IRequisitionAssignmentService? _assignmentService = assignmentService;
    private readonly ILogger<InterviewService> _logger = logger;

    /// <inheritdoc/>
    public async Task<InterviewScheduleResultDto> ScheduleInterviewAsync(
        ScheduleInterviewInputModel model,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default)
    {
        if (model == null)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Dữ liệu yêu cầu đặt lịch phỏng vấn không hợp lệ.",
                Errors = new List<string> { "Model không được null." }
            };
        }

        // 1. Kiểm tra ngày giờ bắt đầu
        var rawStartTime = model.GetNormalizedStartTime();
        if (!rawStartTime.HasValue)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Vui lòng chọn ngày giờ bắt đầu phỏng vấn.",
                Errors = new List<string> { "Ngày giờ bắt đầu phỏng vấn không được để trống." }
            };
        }

        var startUtc = new DateTimeOffset(DateTime.SpecifyKind(rawStartTime.Value, DateTimeKind.Utc));
        var nowUtc = DateTimeOffset.UtcNow;

        // Quy tắc cốt lõi: Không đặt được lịch ở quá khứ
        if (startUtc <= nowUtc)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Không thể đặt lịch phỏng vấn trong quá khứ. Vui lòng chọn ngày giờ trong tương lai.",
                Errors = new List<string> { "Thời điểm phỏng vấn phải lớn hơn thời gian hiện tại." }
            };
        }

        // 2. Kiểm tra thời lượng phỏng vấn
        if (model.DurationMinutes <= 0)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Thời lượng phỏng vấn không hợp lệ. Thời lượng tối thiểu là 15 phút.",
                Errors = new List<string> { "Thời lượng phỏng vấn phải lớn hơn 0." }
            };
        }

        var endUtc = model.EndTime.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(model.EndTime.Value, DateTimeKind.Utc))
            : startUtc.AddMinutes(model.DurationMinutes);

        if (endUtc <= startUtc)
        {
            endUtc = startUtc.AddMinutes(model.DurationMinutes);
        }

        // 3. Kiểm tra danh sách người phỏng vấn
        var interviewerIds = model.GetNormalizedInterviewerIds();
        if (!interviewerIds.Any())
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Vui lòng chọn ít nhất một người phỏng vấn cho hội đồng.",
                Errors = new List<string> { "Hội đồng phỏng vấn bắt buộc có ít nhất 1 thành viên." }
            };
        }

        // 4. Kiểm tra phòng hoặc liên kết họp
        if (string.IsNullOrWhiteSpace(model.LocationOrLink))
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Vui lòng cung cấp phòng họp hoặc liên kết họp trực tuyến.",
                Errors = new List<string> { "Địa điểm hoặc liên kết họp không được để trống." }
            };
        }

        // 5. Kiểm tra hồ sơ ứng tuyển
        var application = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
            .Include(a => a.CurrentStage)
            .FirstOrDefaultAsync(a => a.Id == model.ApplicationId && !a.IsDeleted, cancellationToken);

        if (application == null)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Không tìm thấy hồ sơ ứng tuyển của ứng viên trong hệ thống.",
                Errors = new List<string> { "Hồ sơ ứng viên không tồn tại hoặc đã bị xóa." }
            };
        }

        // 6. Kiểm tra quyền của Recruiter phụ trách (Scrum 26)
        if (isRecruiterOnly && _assignmentService != null && currentUserId != Guid.Empty)
        {
            var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, model.ApplicationId);
            if (!canAccess)
            {
                return new InterviewScheduleResultDto
                {
                    Success = false,
                    Message = "Bạn không có quyền lên lịch phỏng vấn cho hồ sơ này do không phụ trách vị trí tuyển dụng tương ứng.",
                    Errors = new List<string> { "Không có quyền phụ trách vị trí tuyển dụng tương ứng." }
                };
            }
        }

        // 7. Xác định thông tin buổi phỏng vấn
        var roundNum = model.RoundNumber >= 1 ? model.RoundNumber : 1;
        var roundTitle = !string.IsNullOrWhiteSpace(model.RoundTitle)
            ? model.RoundTitle.Trim()
            : $"Phỏng vấn Vòng {roundNum} - {application.JobPosting?.Title ?? "Chuyên môn"}";

        var meetLocation = model.LocationOrLink.Trim();
        var interviewType = model.InterviewType?.Equals("OFFLINE_OFFICE", StringComparison.OrdinalIgnoreCase) == true
            ? InterviewType.OFFLINE_OFFICE
            : (model.InterviewType?.Equals("ONLINE_MEET", StringComparison.OrdinalIgnoreCase) == true
                ? InterviewType.ONLINE_MEET
                : (meetLocation.Contains("http", StringComparison.OrdinalIgnoreCase) ? InterviewType.ONLINE_MEET : InterviewType.OFFLINE_OFFICE));

        var interview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            RoundNumber = roundNum,
            Title = roundTitle,
            InterviewType = interviewType,
            LocationOrLink = meetLocation,
            StartTime = startUtc,
            EndTime = endUtc,
            Status = InterviewStatus.SCHEDULED,
            CandidateConfirmed = CandidateConfirmStatus.PENDING,
            CandidateNotes = model.GetNormalizedNotes(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _dbContext.Interviews.AddAsync(interview, cancellationToken);

        // 8. Gán danh sách Hội đồng phỏng vấn (Panelists)
        var interviewers = await _dbContext.Users
            .Where(u => interviewerIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

        var panelistNames = new List<string>();
        var isLead = true;
        foreach (var inv in interviewers)
        {
            await _dbContext.InterviewPanelists.AddAsync(new InterviewPanelist
            {
                Id = Guid.NewGuid(),
                InterviewId = interview.Id,
                InterviewerId = inv.Id,
                IsLead = isLead,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);

            panelistNames.Add($"{inv.FullName} ({inv.Role})");
            isLead = false;
        }

        if (!panelistNames.Any())
        {
            panelistNames.Add("Ban Tuyển Dụng NoveraTech");
        }

        // 9. Cập nhật giai đoạn tuyển dụng nếu hồ sơ chưa tới stage Phỏng vấn
        var interviewStage = await _dbContext.PipelineStages
            .FirstOrDefaultAsync(s => s.StageOrder == 3 || s.Name.Contains("Phỏng vấn"), cancellationToken);

        if (interviewStage != null && (application.CurrentStage == null || application.CurrentStage.StageOrder < interviewStage.StageOrder))
        {
            var prevStageId = application.CurrentStageId;
            application.CurrentStageId = interviewStage.Id;
            application.StageEnteredAt = DateTimeOffset.UtcNow;
            application.IsSlaOverdue = false;
            application.SlaOverdueAlertedAt = null;
            application.UpdatedAt = DateTimeOffset.UtcNow;

            var effectiveUserId = currentUserId != Guid.Empty
                ? currentUserId
                : (interviewers.FirstOrDefault()?.Id ?? Guid.Empty);

            await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                FromStageId = prevStageId,
                ToStageId = interviewStage.Id,
                ChangedByUserId = effectiveUserId,
                Comment = $"Đã lên lịch {roundTitle} ({startUtc:dd/MM/yyyy HH:mm}).",
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // 10. Gửi thư mời phỏng vấn tự động cho ứng viên
        var candFullName = $"{application.Candidate?.FirstName} {application.Candidate?.LastName}".Trim();
        var panelistStr = string.Join(", ", panelistNames);
        var candEmail = application.Candidate?.Email;
        var jobTitle = application.JobPosting?.Title ?? "Vị trí tuyển dụng";

        if (!string.IsNullOrWhiteSpace(candEmail))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendInterviewInvitationAsync(
                        candEmail,
                        candFullName,
                        jobTitle,
                        roundTitle,
                        startUtc,
                        endUtc,
                        meetLocation,
                        panelistStr);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Không thể gửi email thư mời phỏng vấn cho {Email}: {Msg}", candEmail, ex.Message);
                }
            });
        }

        var interviewTypeDisplay = interviewType == InterviewType.ONLINE_MEET
            ? "Trực tuyến (Google Meet)"
            : "Trực tiếp tại văn phòng";

        return new InterviewScheduleResultDto
        {
            Success = true,
            Message = $"Đã lên lịch phỏng vấn {roundTitle} cho ứng viên {candFullName} thành công!",
            InterviewId = interview.Id,
            RoundNumber = roundNum,
            Title = roundTitle,
            StartTimeDisplay = startUtc.ToString("dd/MM/yyyy HH:mm"),
            EndTimeDisplay = endUtc.ToString("dd/MM/yyyy HH:mm"),
            InterviewTypeDisplay = interviewTypeDisplay,
            LocationOrLink = meetLocation,
            Panelists = panelistStr
        };
    }

    /// <inheritdoc/>
    public async Task<List<InterviewerLookupDto>> GetAvailableInterviewersAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users
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
    }

    /// <inheritdoc/>
    public async Task<List<InterviewScheduleResultDto>> GetApplicationInterviewsAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var interviews = await _dbContext.Interviews
            .Include(i => i.Panelists)
                .ThenInclude(p => p.Interviewer)
            .Where(i => i.ApplicationId == applicationId && !i.IsDeleted)
            .OrderBy(i => i.RoundNumber)
            .ThenBy(i => i.StartTime)
            .ToListAsync(cancellationToken);

        return interviews.Select(i => new InterviewScheduleResultDto
        {
            Success = true,
            InterviewId = i.Id,
            RoundNumber = i.RoundNumber,
            Title = i.Title,
            StartTimeDisplay = i.StartTime.HasValue ? i.StartTime.Value.ToString("dd/MM/yyyy HH:mm") : string.Empty,
            EndTimeDisplay = i.EndTime.HasValue ? i.EndTime.Value.ToString("dd/MM/yyyy HH:mm") : string.Empty,
            InterviewTypeDisplay = i.InterviewType == InterviewType.ONLINE_MEET ? "Trực tuyến" : "Trực tiếp",
            LocationOrLink = i.LocationOrLink,
            Panelists = string.Join(", ", i.Panelists.Select(p => p.Interviewer?.FullName ?? "Hội đồng phỏng vấn")),
            Message = i.Status.ToString()
        }).ToList();
    }
}
