using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ats.Web.Data;
using Ats.Web.Exceptions;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Interviews;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Dịch vụ điều phối nghiệp vụ Đặt lịch phỏng vấn, Kiểm tra xung đột lịch và Phân công hội đồng phỏng vấn (Scrum - Số 51 & SCRUM-53 / EP-06).
/// Tuân thủ quy chuẩn: Primary Constructors, Service Layer phân tầng sạch, xử lý UTC chuẩn DB và múi giờ Việt Nam khi hiển thị.
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

    private static readonly TimeZoneInfo VietnamTimeZone = GetVietnamTimeZone();

    private static TimeZoneInfo GetVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("Vietnam Time", TimeSpan.FromHours(7), "Vietnam Time", "Vietnam Time");
            }
        }
    }

    private static string FormatVietnamTime(DateTimeOffset? utcTime)
    {
        if (!utcTime.HasValue) return string.Empty;
        var vnTime = TimeZoneInfo.ConvertTime(utcTime.Value, VietnamTimeZone);
        return vnTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("vi-VN"));
    }

    /// <inheritdoc/>
    public async Task<InterviewConflictCheckResultViewModel> CheckScheduleConflictAsync(
        InterviewConflictCheckInputModel input,
        CancellationToken cancellationToken = default)
    {
        if (input == null || !input.StartTime.HasValue)
        {
            return new InterviewConflictCheckResultViewModel
            {
                HasConflict = false,
                Message = "Dữ liệu thời gian kiểm tra xung đột không hợp lệ."
            };
        }

        var startUtc = input.StartTime.Value.ToUniversalTime();
        var durationMinutes = input.DurationMinutes.GetValueOrDefault(60);
        if (durationMinutes <= 0) durationMinutes = 60;

        var endUtc = input.EndTime.HasValue
            ? input.EndTime.Value.ToUniversalTime()
            : startUtc.AddMinutes(durationMinutes);

        if (endUtc <= startUtc)
        {
            endUtc = startUtc.AddMinutes(durationMinutes);
        }

        // Truy vấn tất cả các buổi phỏng vấn có khung giờ giao nhau:
        // A.StartTime < B.EndTime && A.EndTime > B.StartTime
        // Bỏ qua các buổi đã hủy (CANCELLED) và đã bị xóa mềm (IsDeleted)
        var query = _dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Panelists)
                .ThenInclude(p => p.Interviewer)
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Application)
                .ThenInclude(a => a.JobPosting)
            .Where(i => !i.IsDeleted &&
                        i.Status != InterviewStatus.CANCELLED &&
                        i.StartTime.HasValue &&
                        i.EndTime.HasValue &&
                        startUtc < i.EndTime.Value &&
                        endUtc > i.StartTime.Value);

        if (input.ExcludeInterviewId.HasValue && input.ExcludeInterviewId.Value != Guid.Empty)
        {
            query = query.Where(i => i.Id != input.ExcludeInterviewId.Value);
        }

        var overlappingInterviews = await query.ToListAsync(cancellationToken);

        var conflicts = new List<InterviewConflictItemViewModel>();

        // 1. Kiểm tra xung đột Người phỏng vấn (Interviewer Conflict)
        var checkedInterviewerIds = input.InterviewerIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        if (checkedInterviewerIds.Count > 0)
        {
            foreach (var interview in overlappingInterviews)
            {
                var matchedPanelists = interview.Panelists
                    .Where(p => !p.IsDeleted && checkedInterviewerIds.Contains(p.InterviewerId))
                    .ToList();

                foreach (var panelist in matchedPanelists)
                {
                    var interviewerName = panelist.Interviewer?.FullName ?? "Người phỏng vấn";
                    var candName = $"{interview.Application?.Candidate?.FirstName} {interview.Application?.Candidate?.LastName}".Trim();
                    if (string.IsNullOrWhiteSpace(candName)) candName = "Ứng viên";
                    var jobTitle = interview.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng";

                    var startVn = FormatVietnamTime(interview.StartTime);
                    var endVn = FormatVietnamTime(interview.EndTime);

                    conflicts.Add(new InterviewConflictItemViewModel
                    {
                        ConflictType = "INTERVIEWER",
                        ConflictingEntityName = interviewerName,
                        ConflictingEntityId = panelist.InterviewerId,
                        ConflictingCandidateName = candName,
                        JobTitle = jobTitle,
                        ConflictingInterviewId = interview.Id,
                        ConflictingStartTime = startVn,
                        ConflictingEndTime = endVn,
                        StartTimeUtc = interview.StartTime,
                        EndTimeUtc = interview.EndTime,
                        TimeRangeDisplay = $"{startVn} - {endVn}"
                    });
                }
            }
        }

        // 2. Kiểm tra xung đột Phòng họp (Room/Location Conflict)
        string? targetRoomName = null;
        if (input.MeetingRoomId.HasValue && input.MeetingRoomId.Value != Guid.Empty)
        {
            var roomCatalog = await _dbContext.RecruitmentCatalogs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == input.MeetingRoomId.Value, cancellationToken);
            targetRoomName = roomCatalog?.Name;
        }

        if (string.IsNullOrWhiteSpace(targetRoomName) && !string.IsNullOrWhiteSpace(input.LocationOrLink))
        {
            var loc = input.LocationOrLink.Trim();
            // Nếu không phải đường link mạng (http/https) thì xem như địa điểm phòng họp thực tế
            if (!loc.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                targetRoomName = loc;
            }
        }

        if (!string.IsNullOrWhiteSpace(targetRoomName))
        {
            foreach (var interview in overlappingInterviews)
            {
                if (string.IsNullOrWhiteSpace(interview.LocationOrLink)) continue;
                var existingLoc = interview.LocationOrLink.Trim();

                var isRoomConflict = existingLoc.Equals(targetRoomName, StringComparison.OrdinalIgnoreCase) ||
                                     (input.MeetingRoomId.HasValue && existingLoc.Equals(input.MeetingRoomId.Value.ToString(), StringComparison.OrdinalIgnoreCase));

                if (isRoomConflict)
                {
                    var candName = $"{interview.Application?.Candidate?.FirstName} {interview.Application?.Candidate?.LastName}".Trim();
                    if (string.IsNullOrWhiteSpace(candName)) candName = "Ứng viên";
                    var jobTitle = interview.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng";

                    var startVn = FormatVietnamTime(interview.StartTime);
                    var endVn = FormatVietnamTime(interview.EndTime);

                    conflicts.Add(new InterviewConflictItemViewModel
                    {
                        ConflictType = "ROOM",
                        ConflictingEntityName = targetRoomName,
                        ConflictingEntityId = input.MeetingRoomId,
                        ConflictingCandidateName = candName,
                        JobTitle = jobTitle,
                        ConflictingInterviewId = interview.Id,
                        ConflictingStartTime = startVn,
                        ConflictingEndTime = endVn,
                        StartTimeUtc = interview.StartTime,
                        EndTimeUtc = interview.EndTime,
                        TimeRangeDisplay = $"{startVn} - {endVn}"
                    });
                }
            }
        }

        var hasConflict = conflicts.Count > 0;
        string message;
        if (hasConflict)
        {
            var interviewerCount = conflicts.Count(c => c.ConflictType == "INTERVIEWER");
            var roomCount = conflicts.Count(c => c.ConflictType == "ROOM");
            var details = new List<string>();
            if (interviewerCount > 0) details.Add($"{interviewerCount} người phỏng vấn");
            if (roomCount > 0) details.Add($"{roomCount} phòng họp");
            message = $"Phát hiện trùng lịch phỏng vấn ({string.Join(", ", details)}).";
        }
        else
        {
            message = "Không có xung đột lịch phỏng vấn.";
        }

        return new InterviewConflictCheckResultViewModel
        {
            HasConflict = hasConflict,
            Conflicts = conflicts,
            Message = message
        };
    }

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
        if (interviewerIds.Count == 0)
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

        // 7. Kiểm tra xung đột lịch phỏng vấn (SCRUM-53)
        var conflictInput = new InterviewConflictCheckInputModel
        {
            StartTime = startUtc,
            EndTime = endUtc,
            InterviewerIds = interviewerIds,
            MeetingRoomId = model.MeetingRoomId,
            LocationOrLink = model.LocationOrLink,
            ExcludeInterviewId = null
        };

        var conflictCheck = await CheckScheduleConflictAsync(conflictInput, cancellationToken);
        if (conflictCheck.HasConflict)
        {
            if (!model.IsOverridden)
            {
                var errorList = conflictCheck.Conflicts.Select(c =>
                    $"[{c.ConflictType}] {c.ConflictingEntityName} bị trùng lịch với ứng viên {c.ConflictingCandidateName} ({c.JobTitle}) khung giờ {c.TimeRangeDisplay}").ToList();

                throw new BusinessRuleValidationException(
                    conflictCheck.Message,
                    errorList,
                    conflictCheck);
            }

            if (string.IsNullOrWhiteSpace(model.OverrideReason) || model.OverrideReason.Trim().Length < 10)
            {
                throw new BusinessRuleValidationException(
                    "Bắt buộc nhập lý do ghi đè trùng lịch hợp lệ (tối thiểu 10 ký tự).",
                    new List<string> { "Lý do ghi đè phải có độ dài tối thiểu 10 ký tự để phục vụ lưu vết kiểm toán." },
                    conflictCheck);
            }
        }

        // 8. Xác định thông tin buổi phỏng vấn
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

        var panelistNames = new List<string>();

        // Thực thi toàn bộ thao tác ghi dữ liệu trong Transaction tường minh
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.Interviews.AddAsync(interview, cancellationToken);

            // 9. Gán danh sách Hội đồng phỏng vấn (Panelists)
            var interviewers = await _dbContext.Users
                .Where(u => interviewerIds.Contains(u.Id))
                .ToListAsync(cancellationToken);

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

            if (panelistNames.Count == 0)
            {
                panelistNames.Add("Ban Tuyển Dụng NoveraTech");
            }

            // 10. Cập nhật giai đoạn tuyển dụng nếu hồ sơ chưa tới stage Phỏng vấn
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

            // 11. Ghi vết kiểm toán (AuditLog) chi tiết nếu có hành động ghi đè trùng lịch
            if (model.IsOverridden && conflictCheck.HasConflict)
            {
                var auditLog = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    UserId = currentUserId != Guid.Empty ? currentUserId : null,
                    Action = "OVERRIDE_INTERVIEW_CONFLICT",
                    EntityName = nameof(Interview),
                    EntityId = interview.Id.ToString(),
                    NewValues = JsonSerializer.Serialize(new
                    {
                        InterviewId = interview.Id,
                        ApplicationId = application.Id,
                        CandidateName = $"{application.Candidate?.FirstName} {application.Candidate?.LastName}".Trim(),
                        JobTitle = application.JobPosting?.Title ?? "Vị trí tuyển dụng",
                        StartTime = startUtc,
                        EndTime = endUtc,
                        IsOverridden = true,
                        OverrideReason = model.OverrideReason?.Trim(),
                        ConflictsCount = conflictCheck.Conflicts.Count,
                        Conflicts = conflictCheck.Conflicts
                    }, new JsonSerializerOptions
                    {
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    }),
                    CreatedAt = DateTimeOffset.UtcNow
                };

                await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        // 12. Gửi thư mời phỏng vấn tự động cho ứng viên
        var candFullName = $"{application.Candidate?.FirstName} {application.Candidate?.LastName}".Trim();
        var panelistStr = string.Join(", ", panelistNames);
        var candEmail = application.Candidate?.Email;
        var candJobTitle = application.JobPosting?.Title ?? "Vị trí tuyển dụng";

        if (!string.IsNullOrWhiteSpace(candEmail))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendInterviewInvitationAsync(
                        candEmail,
                        candFullName,
                        candJobTitle,
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
            StartTimeDisplay = FormatVietnamTime(startUtc),
            EndTimeDisplay = FormatVietnamTime(endUtc),
            InterviewTypeDisplay = interviewTypeDisplay,
            LocationOrLink = meetLocation,
            Panelists = panelistStr
        };
    }

    /// <inheritdoc/>
    public async Task<InterviewScheduleResultDto> RescheduleInterviewAsync(
        Guid interviewId,
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
                Message = "Dữ liệu đổi lịch không hợp lệ.",
                Errors = new List<string> { "Model không được null." }
            };
        }

        var interview = await _dbContext.Interviews
            .Include(i => i.Panelists)
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Application)
                .ThenInclude(a => a.JobPosting)
            .FirstOrDefaultAsync(i => i.Id == interviewId && !i.IsDeleted, cancellationToken);

        if (interview == null)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Không tìm thấy buổi phỏng vấn cần đổi lịch trong hệ thống.",
                Errors = new List<string> { "Buổi phỏng vấn không tồn tại." }
            };
        }

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
        var durationMinutes = model.DurationMinutes > 0 ? model.DurationMinutes : 60;
        var endUtc = model.EndTime.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(model.EndTime.Value, DateTimeKind.Utc))
            : startUtc.AddMinutes(durationMinutes);

        if (endUtc <= startUtc)
        {
            endUtc = startUtc.AddMinutes(durationMinutes);
        }

        var interviewerIds = model.GetNormalizedInterviewerIds();
        if (interviewerIds.Count == 0)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Vui lòng chọn ít nhất một người phỏng vấn cho hội đồng.",
                Errors = new List<string> { "Hội đồng phỏng vấn bắt buộc có ít nhất 1 thành viên." }
            };
        }

        // Kiểm tra xung đột thời gian (loại trừ chính buổi phỏng vấn đang sửa này)
        var conflictInput = new InterviewConflictCheckInputModel
        {
            StartTime = startUtc,
            EndTime = endUtc,
            InterviewerIds = interviewerIds,
            MeetingRoomId = model.MeetingRoomId,
            LocationOrLink = model.LocationOrLink,
            ExcludeInterviewId = interviewId
        };

        var conflictCheck = await CheckScheduleConflictAsync(conflictInput, cancellationToken);
        if (conflictCheck.HasConflict)
        {
            if (!model.IsOverridden)
            {
                var errorList = conflictCheck.Conflicts.Select(c =>
                    $"[{c.ConflictType}] {c.ConflictingEntityName} bị trùng lịch với ứng viên {c.ConflictingCandidateName} ({c.JobTitle}) khung giờ {c.TimeRangeDisplay}").ToList();

                throw new BusinessRuleValidationException(
                    conflictCheck.Message,
                    errorList,
                    conflictCheck);
            }

            if (string.IsNullOrWhiteSpace(model.OverrideReason) || model.OverrideReason.Trim().Length < 10)
            {
                throw new BusinessRuleValidationException(
                    "Bắt buộc nhập lý do ghi đè trùng lịch hợp lệ (tối thiểu 10 ký tự).",
                    new List<string> { "Lý do ghi đè phải có độ dài tối thiểu 10 ký tự để phục vụ lưu vết kiểm toán." },
                    conflictCheck);
            }
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var oldValues = JsonSerializer.Serialize(new
            {
                interview.StartTime,
                interview.EndTime,
                interview.LocationOrLink,
                interview.RoundNumber,
                interview.Status
            });

            interview.StartTime = startUtc;
            interview.EndTime = endUtc;
            if (!string.IsNullOrWhiteSpace(model.LocationOrLink))
            {
                interview.LocationOrLink = model.LocationOrLink.Trim();
            }
            if (model.RoundNumber >= 1)
            {
                interview.RoundNumber = model.RoundNumber;
            }
            if (!string.IsNullOrWhiteSpace(model.RoundTitle))
            {
                interview.Title = model.RoundTitle.Trim();
            }
            interview.Status = InterviewStatus.SCHEDULED;
            interview.CandidateConfirmed = CandidateConfirmStatus.PENDING;
            interview.UpdatedAt = DateTimeOffset.UtcNow;
            interview.UpdatedById = currentUserId != Guid.Empty ? currentUserId : null;

            // Cập nhật lại danh sách Panelists
            _dbContext.InterviewPanelists.RemoveRange(interview.Panelists);

            var interviewers = await _dbContext.Users
                .Where(u => interviewerIds.Contains(u.Id))
                .ToListAsync(cancellationToken);

            var isLead = true;
            var panelistNames = new List<string>();
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

            // Ghi AuditLog nếu có ghi đè
            if (model.IsOverridden && conflictCheck.HasConflict)
            {
                var auditLog = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    UserId = currentUserId != Guid.Empty ? currentUserId : null,
                    Action = "OVERRIDE_INTERVIEW_RESCHEDULE_CONFLICT",
                    EntityName = nameof(Interview),
                    EntityId = interview.Id.ToString(),
                    OldValues = oldValues,
                    NewValues = JsonSerializer.Serialize(new
                    {
                        InterviewId = interview.Id,
                        StartTime = startUtc,
                        EndTime = endUtc,
                        IsOverridden = true,
                        OverrideReason = model.OverrideReason?.Trim(),
                        ConflictsCount = conflictCheck.Conflicts.Count,
                        Conflicts = conflictCheck.Conflicts
                    }, new JsonSerializerOptions
                    {
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    }),
                    CreatedAt = DateTimeOffset.UtcNow
                };

                await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var candFullName = $"{interview.Application?.Candidate?.FirstName} {interview.Application?.Candidate?.LastName}".Trim();
            var panelistStr = string.Join(", ", panelistNames);

            return new InterviewScheduleResultDto
            {
                Success = true,
                Message = $"Đã cập nhật lại lịch phỏng vấn {interview.Title} cho ứng viên {candFullName} thành công!",
                InterviewId = interview.Id,
                RoundNumber = interview.RoundNumber,
                Title = interview.Title,
                StartTimeDisplay = FormatVietnamTime(startUtc),
                EndTimeDisplay = FormatVietnamTime(endUtc),
                LocationOrLink = interview.LocationOrLink,
                Panelists = panelistStr
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
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
    public async Task<List<MeetingRoomLookupDto>> GetAvailableMeetingRoomsAsync(
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.RecruitmentCatalogs
            .AsNoTracking()
            .Where(c => c.CatalogType == "MEETING_ROOM" && !c.IsDeleted)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(cancellationToken);

        if (!existing.Any())
        {
            var defaultRooms = new List<RecruitmentCatalog>
            {
                new() { Id = Guid.NewGuid(), CatalogType = "MEETING_ROOM", Code = "ROOM_RUBY", Name = "Phòng Họp Ruby (Tầng 8 - Trụ sở HN)", Description = "Sức chứa 6-8 người, trang bị màn hình & họp trực tuyến", DisplayOrder = 1, IsSystem = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), CatalogType = "MEETING_ROOM", Code = "ROOM_DIAMOND", Name = "Phòng Họp Diamond (Tầng 10 - Trụ sở HN)", Description = "Sức chứa 10-12 người, hệ thống Video Conference", DisplayOrder = 2, IsSystem = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), CatalogType = "MEETING_ROOM", Code = "ROOM_SAPPHIRE", Name = "Phòng Họp Sapphire (Tầng 12 - Trụ sở HN)", Description = "Sức chứa 4-6 người, phỏng vấn chuyên sâu", DisplayOrder = 3, IsSystem = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), CatalogType = "MEETING_ROOM", Code = "ROOM_LOTUS_HCM", Name = "Phòng Họp Lotus (Tầng 15 - Chi nhánh HCM)", Description = "Sức chứa 8-10 người, màn hình tương tác", DisplayOrder = 4, IsSystem = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), CatalogType = "MEETING_ROOM", Code = "ROOM_DANANG_01", Name = "Phòng Họp Sơn Trà (Hub Đà Nẵng)", Description = "Sức chứa 6 người, bảng kính & webcam 4K", DisplayOrder = 5, IsSystem = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }
            };

            await _dbContext.RecruitmentCatalogs.AddRangeAsync(defaultRooms, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            existing = defaultRooms;
        }

        return existing.Select(r => new MeetingRoomLookupDto
        {
            Id = r.Id,
            Name = r.Name,
            Code = r.Code,
            Location = r.Description
        }).ToList();
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
            StartTimeDisplay = FormatVietnamTime(i.StartTime),
            EndTimeDisplay = FormatVietnamTime(i.EndTime),
            InterviewTypeDisplay = i.InterviewType == InterviewType.ONLINE_MEET ? "Trực tuyến" : "Trực tiếp",
            LocationOrLink = i.LocationOrLink,
            Panelists = string.Join(", ", i.Panelists.Select(p => p.Interviewer?.FullName ?? "Hội đồng phỏng vấn")),
            Message = i.Status.ToString()
        }).ToList();
    }
}
