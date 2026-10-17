using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Workspace;
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

    /// <inheritdoc/>
    public async Task<BatchSchedulePreviewResultDto> PreviewBatchScheduleAsync(
        BatchSchedulePreviewInputModel model,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default)
    {
        if (model == null)
        {
            return new BatchSchedulePreviewResultDto
            {
                Success = false,
                Message = "Dữ liệu yêu cầu xem trước không hợp lệ.",
                Errors = new List<string> { "Model không được null." }
            };
        }

        if (model.ApplicationIds == null || !model.ApplicationIds.Any())
        {
            return new BatchSchedulePreviewResultDto
            {
                Success = false,
                Message = "Vui lòng chọn ít nhất một ứng viên để đặt lịch hàng loạt.",
                Errors = new List<string> { "Danh sách mã hồ sơ ứng tuyển rỗng." }
            };
        }

        if (!model.StartTime.HasValue)
        {
            return new BatchSchedulePreviewResultDto
            {
                Success = false,
                Message = "Vui lòng chọn ngày giờ bắt đầu ca phỏng vấn đầu tiên.",
                Errors = new List<string> { "Thời điểm bắt đầu không được để trống." }
            };
        }

        var startUtc = new DateTimeOffset(DateTime.SpecifyKind(model.StartTime.Value, DateTimeKind.Utc));
        var nowUtc = DateTimeOffset.UtcNow;

        if (startUtc <= nowUtc)
        {
            return new BatchSchedulePreviewResultDto
            {
                Success = false,
                Message = "Không thể đặt lịch phỏng vấn trong quá khứ. Vui lòng chọn ngày giờ trong tương lai.",
                Errors = new List<string> { "Thời điểm phỏng vấn phải lớn hơn thời gian hiện tại." }
            };
        }

        var slotDuration = model.SlotDurationMinutes >= 15 ? model.SlotDurationMinutes : 30;
        var breakDuration = model.BreakDurationMinutes >= 0 ? model.BreakDurationMinutes : 5;

        var applications = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
            .Where(a => model.ApplicationIds.Contains(a.Id) && !a.IsDeleted)
            .ToListAsync(cancellationToken);

        var appsMap = applications.ToDictionary(a => a.Id);

        var slots = new List<BatchScheduleSlotPreviewItemDto>();
        var currentSlotStart = startUtc;
        var order = 1;

        foreach (var appId in model.ApplicationIds)
        {
            if (!appsMap.TryGetValue(appId, out var app))
            {
                continue;
            }

            if (isRecruiterOnly && _assignmentService != null && currentUserId != Guid.Empty)
            {
                var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, appId);
                if (!canAccess)
                {
                    continue;
                }
            }

            var currentSlotEnd = currentSlotStart.AddMinutes(slotDuration);
            var candName = $"{app.Candidate?.FirstName} {app.Candidate?.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(candName)) candName = "Ứng viên";

            slots.Add(new BatchScheduleSlotPreviewItemDto
            {
                SlotOrder = order++,
                ApplicationId = app.Id,
                CandidateId = app.CandidateId,
                CandidateName = candName,
                CandidateEmail = app.Candidate?.Email ?? string.Empty,
                CandidatePhone = app.Candidate?.Phone ?? string.Empty,
                JobTitle = app.JobPosting?.Title ?? "Chuyên môn",
                StartTime = currentSlotStart.UtcDateTime,
                EndTime = currentSlotEnd.UtcDateTime,
                StartTimeDisplay = currentSlotStart.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm"),
                EndTimeDisplay = currentSlotEnd.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm"),
                DurationMinutes = slotDuration,
                LocationOrLink = model.LocationOrLink
            });

            currentSlotStart = currentSlotEnd.AddMinutes(breakDuration);
        }

        if (!slots.Any())
        {
            return new BatchSchedulePreviewResultDto
            {
                Success = false,
                Message = "Không tìm thấy hồ sơ ứng viên hợp lệ nào trong danh sách được chọn.",
                Errors = new List<string> { "Không có ứng viên nào thỏa mãn điều kiện." }
            };
        }

        var overallStart = slots.First().StartTime;
        var overallEnd = slots.Last().EndTime;
        var totalMinutes = (int)(overallEnd - overallStart).TotalMinutes;
        var hours = totalMinutes / 60;
        var mins = totalMinutes % 60;
        var durationText = hours > 0
            ? (mins > 0 ? $"{hours} giờ {mins} phút" : $"{hours} giờ")
            : $"{mins} phút";

        var overallDisplay = $"{slots.First().StartTimeDisplay} - {slots.Last().EndTimeDisplay} ({durationText})";

        return new BatchSchedulePreviewResultDto
        {
            Success = true,
            Message = $"Đã tính toán thành công {slots.Count} khe thời gian phỏng vấn liên tiếp.",
            TotalCandidates = slots.Count,
            OverallStartTime = overallStart,
            OverallEndTime = overallEnd,
            OverallTimeDisplay = overallDisplay,
            TotalDurationMinutes = totalMinutes,
            Slots = slots
        };
    }

    /// <inheritdoc/>
    public async Task<BatchScheduleResultDto> ConfirmBatchScheduleAsync(
        BatchScheduleConfirmInputModel model,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default)
    {
        if (model == null || model.Slots == null || !model.Slots.Any())
        {
            return new BatchScheduleResultDto
            {
                Success = false,
                Message = "Danh sách các khe giờ xác nhận không được để trống.",
                Errors = new List<string> { "Dữ liệu model hoặc danh sách Slots rỗng." }
            };
        }

        var interviewerIds = model.GetNormalizedInterviewerIds();
        if (!interviewerIds.Any())
        {
            return new BatchScheduleResultDto
            {
                Success = false,
                Message = "Vui lòng chọn ít nhất một người phỏng vấn cho hội đồng.",
                Errors = new List<string> { "Hội đồng phỏng vấn bắt buộc có ít nhất 1 thành viên." }
            };
        }

        if (string.IsNullOrWhiteSpace(model.LocationOrLink))
        {
            return new BatchScheduleResultDto
            {
                Success = false,
                Message = "Vui lòng cung cấp phòng họp hoặc liên kết họp trực tuyến.",
                Errors = new List<string> { "Địa điểm hoặc liên kết họp không được để trống." }
            };
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var appIds = model.Slots.Select(s => s.ApplicationId).Distinct().ToList();

        var applications = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
            .Include(a => a.CurrentStage)
            .Where(a => appIds.Contains(a.Id) && !a.IsDeleted)
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var interviewers = await _dbContext.Users
            .Where(u => interviewerIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

        var panelistNames = new List<string>();
        foreach (var inv in interviewers)
        {
            panelistNames.Add($"{inv.FullName} ({inv.Role})");
        }
        if (!panelistNames.Any())
        {
            panelistNames.Add("Ban Tuyển Dụng NoveraTech");
        }
        var panelistStr = string.Join(", ", panelistNames);

        var interviewStage = await _dbContext.PipelineStages
            .FirstOrDefaultAsync(s => s.StageOrder == 3 || s.Name.Contains("Phỏng vấn"), cancellationToken);

        var roundNum = model.RoundNumber >= 1 ? model.RoundNumber : 1;
        var defaultMeetLocation = model.LocationOrLink.Trim();
        var defaultInterviewType = model.InterviewType?.Equals("OFFLINE_OFFICE", StringComparison.OrdinalIgnoreCase) == true
            ? InterviewType.OFFLINE_OFFICE
            : (model.InterviewType?.Equals("ONLINE_MEET", StringComparison.OrdinalIgnoreCase) == true
                ? InterviewType.ONLINE_MEET
                : (defaultMeetLocation.Contains("http", StringComparison.OrdinalIgnoreCase) ? InterviewType.ONLINE_MEET : InterviewType.OFFLINE_OFFICE));

        var results = new List<InterviewScheduleResultDto>();
        var errors = new List<string>();
        var emailTasks = new List<(string email, string name, string job, string round, DateTimeOffset start, DateTimeOffset end, string loc, string panelists)>();

        foreach (var slot in model.Slots)
        {
            var startUtc = new DateTimeOffset(DateTime.SpecifyKind(slot.StartTime, DateTimeKind.Utc));
            var endUtc = new DateTimeOffset(DateTime.SpecifyKind(slot.EndTime, DateTimeKind.Utc));

            if (startUtc <= nowUtc)
            {
                return new BatchScheduleResultDto
                {
                    Success = false,
                    Message = "Không thể đặt lịch phỏng vấn trong quá khứ. Có khe giờ bắt đầu trước hoặc bằng thời gian hiện tại.",
                    Errors = new List<string> { $"Khe giờ {startUtc.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm} không hợp lệ (nằm trong quá khứ)." }
                };
            }

            if (endUtc <= startUtc)
            {
                return new BatchScheduleResultDto
                {
                    Success = false,
                    Message = "Thời gian kết thúc của khe phỏng vấn phải lớn hơn thời gian bắt đầu.",
                    Errors = new List<string> { "Thời gian kết thúc không hợp lệ." }
                };
            }
        }

        foreach (var slot in model.Slots.OrderBy(s => s.SlotOrder).ThenBy(s => s.StartTime))
        {
            if (!applications.TryGetValue(slot.ApplicationId, out var application))
            {
                errors.Add($"Không tìm thấy hồ sơ ID {slot.ApplicationId}.");
                continue;
            }

            if (isRecruiterOnly && _assignmentService != null && currentUserId != Guid.Empty)
            {
                var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, slot.ApplicationId);
                if (!canAccess)
                {
                    errors.Add($"Hồ sơ {application.Candidate?.FirstName} {application.Candidate?.LastName} không thuộc quyền phụ trách.");
                    continue;
                }
            }

            var startUtc = new DateTimeOffset(DateTime.SpecifyKind(slot.StartTime, DateTimeKind.Utc));
            var endUtc = new DateTimeOffset(DateTime.SpecifyKind(slot.EndTime, DateTimeKind.Utc));

            var slotLocation = !string.IsNullOrWhiteSpace(slot.CustomLocationOrLink)
                ? slot.CustomLocationOrLink.Trim()
                : defaultMeetLocation;

            var roundTitle = !string.IsNullOrWhiteSpace(model.RoundTitle)
                ? model.RoundTitle.Trim()
                : $"Phỏng vấn Vòng {roundNum} - {application.JobPosting?.Title ?? "Chuyên môn"}";

            var interview = new Interview
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                RoundNumber = roundNum,
                Title = roundTitle,
                InterviewType = defaultInterviewType,
                LocationOrLink = slotLocation,
                StartTime = startUtc,
                EndTime = endUtc,
                Status = InterviewStatus.SCHEDULED,
                CandidateConfirmed = CandidateConfirmStatus.PENDING,
                CandidateNotes = model.Notes?.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _dbContext.Interviews.AddAsync(interview, cancellationToken);

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
                isLead = false;
            }

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
                    Comment = $"Đã lên lịch {roundTitle} ({startUtc.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm} - {endUtc.ToOffset(TimeSpan.FromHours(7)):HH:mm}).",
                    CreatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
            }

            var candFullName = $"{application.Candidate?.FirstName} {application.Candidate?.LastName}".Trim();
            var candEmail = application.Candidate?.Email;
            var jobTitle = application.JobPosting?.Title ?? "Vị trí tuyển dụng";

            if (!string.IsNullOrWhiteSpace(candEmail))
            {
                emailTasks.Add((candEmail, candFullName, jobTitle, roundTitle, startUtc, endUtc, slotLocation, panelistStr));
            }

            results.Add(new InterviewScheduleResultDto
            {
                Success = true,
                Message = $"Đã xếp lịch cho ứng viên {candFullName}",
                InterviewId = interview.Id,
                RoundNumber = roundNum,
                Title = roundTitle,
                StartTimeDisplay = startUtc.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm"),
                EndTimeDisplay = endUtc.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm"),
                InterviewTypeDisplay = defaultInterviewType == InterviewType.ONLINE_MEET ? "Trực tuyến" : "Trực tiếp",
                LocationOrLink = slotLocation,
                Panelists = panelistStr
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (emailTasks.Any())
        {
            _ = Task.Run(async () =>
            {
                foreach (var item in emailTasks)
                {
                    try
                    {
                        await _emailService.SendInterviewInvitationAsync(
                            item.email,
                            item.name,
                            item.job,
                            item.round,
                            item.start,
                            item.end,
                            item.loc,
                            item.panelists);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Không thể gửi email thư mời phỏng vấn cho {Email}: {Msg}", item.email, ex.Message);
                    }
                }
            });
        }

        return new BatchScheduleResultDto
        {
            Success = results.Any(),
            Message = $"Đã lên lịch phỏng vấn tập trung cho {results.Count} ứng viên thành công!",
            SuccessCount = results.Count,
            FailureCount = errors.Count,
            ScheduledInterviews = results,
            Errors = errors
        };
    }

    /// <inheritdoc/>
    public async Task<List<CandidatePipelineItemDto>> GetCandidatesForBatchSchedulingAsync(
        Guid? jobPostingId,
        int? stageOrder,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Applications
            .Include(a => a.Candidate)
                .ThenInclude(c => c.Resumes)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
            .Include(a => a.CurrentStage)
            .Where(a => !a.IsDeleted && a.Status != ApplicationStatus.REJECTED && a.Status != ApplicationStatus.WITHDRAWN);

        if (jobPostingId.HasValue && jobPostingId.Value != Guid.Empty)
        {
            query = query.Where(a => a.JobPostingId == jobPostingId.Value);
        }

        if (stageOrder.HasValue && stageOrder.Value > 0)
        {
            query = query.Where(a => a.CurrentStage != null && a.CurrentStage.StageOrder == stageOrder.Value);
        }

        var list = await query
            .OrderByDescending(a => a.StageEnteredAt ?? a.CreatedAt)
            .ToListAsync(cancellationToken);

        if (isRecruiterOnly && _assignmentService != null && currentUserId != Guid.Empty)
        {
            var accessible = new List<Application>();
            foreach (var app in list)
            {
                if (await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, app.Id))
                {
                    accessible.Add(app);
                }
            }
            list = accessible;
        }

        return list.Select(a =>
        {
            var candName = $"{a.Candidate?.FirstName} {a.Candidate?.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(candName)) candName = "Ứng viên";

            var stage = a.CurrentStage;
            var primaryResume = a.Candidate?.Resumes?.FirstOrDefault(r => r.IsPrimary) ?? a.Candidate?.Resumes?.FirstOrDefault();

            return new CandidatePipelineItemDto
            {
                ApplicationId = a.Id,
                CandidateId = a.CandidateId,
                JobPostingId = a.JobPostingId,
                CandidateName = candName,
                CandidateEmail = a.Candidate?.Email ?? string.Empty,
                CandidatePhone = a.Candidate?.Phone ?? string.Empty,
                JobTitle = a.JobPosting?.Title ?? "Chuyên môn",
                CurrentStageId = a.CurrentStageId,
                CurrentStageName = stage?.Name ?? "Sàng lọc CV",
                CurrentStageOrder = stage?.StageOrder ?? 2,
                StageColor = stage?.ColorCode ?? "#059669",
                AppliedDateDisplay = a.CreatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy"),
                Status = a.Status.ToString(),
                ResumeFilePath = primaryResume?.FilePath ?? string.Empty,
                ResumeFileName = primaryResume?.FileName ?? string.Empty
            };
        }).ToList();
    }
}
