using Ats.Web.Constants;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ats.Web.Data;
using Ats.Web.Exceptions;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Interviews;
using Ats.Web.Models.ViewModels.Workspace;
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
    ILogger<InterviewService> logger,
    INotificationService? notificationService = null) : IInterviewService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IEmailService _emailService = emailService;
    private readonly IRequisitionAssignmentService? _assignmentService = assignmentService;
    private readonly ILogger<InterviewService> _logger = logger;
    private readonly INotificationService? _notificationService = notificationService;

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

        // Rule 12: BẢO MẬT NGHIÊM NGẶT - Kiểm tra ngay lập tức không cho phép Ứng viên (Candidate) tham gia hội đồng phỏng vấn
        var interviewers = await _dbContext.Users
            .Where(u => interviewerIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

        var invalidCandidates = interviewers
            .Where(u => u.UserType != UserType.INTERNAL || !UserRoles.CanBeInterviewer(u.Role))
            .ToList();

        if (invalidCandidates.Any())
        {
            var invalidNames = string.Join(", ", invalidCandidates.Select(c => $"{c.FullName} ({c.Email})"));
            _logger.LogError("LỖI BẢO MẬT NGHIÊM TRỌNG (Rule 12): Cố gắng phân công Ứng viên (Candidate) tham gia hội đồng phỏng vấn: {InvalidCandidates}", invalidNames);
            throw new InvalidOperationException($"Lỗi bảo mật nghiêm ngặt (Rule 12): Ứng viên tuyệt đối không được phép tham gia hội đồng phỏng vấn ứng viên. Các tài khoản không hợp lệ: {invalidNames}");
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
        var interviewType = (model.InterviewFormat?.Equals("OFFLINE", StringComparison.OrdinalIgnoreCase) == true
            || model.InterviewType?.Equals("OFFLINE_OFFICE", StringComparison.OrdinalIgnoreCase) == true)
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

                var displayRole = UserRoles.GetDisplayName(inv.Role);
                panelistNames.Add($"{inv.FullName} ({displayRole})");
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

        // Gửi In-App Notification bất đồng bộ cho toàn bộ giám khảo được gán vào hội đồng
        if (_notificationService != null)
        {
            foreach (var inv in interviewers)
            {
                await _notificationService.QueueNotificationAsync(
                    inv.Id,
                    $"Lời mời tham gia hội đồng phỏng vấn: {candJobTitle}",
                    $"Bạn được mời tham gia phỏng vấn ứng viên {candFullName} cho vị trí '{candJobTitle}' ({roundTitle}) vào lúc {startUtc.ToOffset(TimeSpan.FromHours(7)):HH:mm dd/MM/yyyy}.",
                    $"/danh-gia/{interview.Id}",
                    NotificationType.InterviewInvitation,
                    cancellationToken);
            }
        }

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
            ? (meetLocation.Contains("teams", StringComparison.OrdinalIgnoreCase) ? "Trực tuyến (MS Teams)" : "Trực tuyến (Google Meet)")
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
        // Rule 12: Ứng viên (Candidate) tuyệt đối KHÔNG ĐƯỢC PHÉP tham gia hội đồng phỏng vấn ứng viên.
        // Chỉ nhân sự nội bộ (UserType == INTERNAL) có trạng thái ACTIVE và vai trò được phép mới xuất hiện trong danh sách giám khảo.
        var candidateAliases = UserRoles.GetAllRoleAliases(UserRoles.Candidate);

        var users = await _dbContext.Users
            .Where(u => u.Status == "ACTIVE" 
                     && u.UserType == UserType.INTERNAL
                     && !candidateAliases.Contains(u.Role))
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                u.Id,
                u.FullName,
                u.Email,
                u.Role
            })
            .ToListAsync(cancellationToken);

        return users
            .Where(u => UserRoles.CanBeInterviewer(u.Role))
            .Select(u => new InterviewerLookupDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                Role = u.Role
            })
            .ToList();
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

    /// <inheritdoc/>
    public async Task<ScheduleInterviewWizardViewModel> GetScheduleWizardDataAsync(
        Guid? applicationId,
        CancellationToken cancellationToken = default)
    {
        // 1. Lấy danh sách các ứng viên trong Pipeline đang hoạt động (chưa bị xóa, stage order <= 5)
        var applications = await _dbContext.Applications
            .Include(a => a.Candidate)
                .ThenInclude(c => c.Resumes)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
                    .ThenInclude(r => r.Department)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
                    .ThenInclude(r => r.JobPosition)
            .Include(a => a.CurrentStage)
            .Include(a => a.Interviews)
            .Where(a => !a.IsDeleted && (a.CurrentStage == null || a.CurrentStage.StageOrder <= 5))
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        var pipelineCandidates = applications.Select(a =>
        {
            var primaryResume = a.Candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? a.Candidate.Resumes.FirstOrDefault();
            return new CandidatePipelineOptionDto
            {
                ApplicationId = a.Id,
                CandidateId = a.CandidateId,
                FullName = $"{a.Candidate.FirstName} {a.Candidate.LastName}".Trim(),
                Email = a.Candidate.Email,
                Phone = a.Candidate.Phone ?? string.Empty,
                JobPostingId = a.JobPostingId,
                JobTitle = a.JobPosting?.Title ?? "Vị trí tuyển dụng",
                DepartmentName = a.JobPosting?.Requisition?.Department?.Name ?? "Ban Công Nghệ",
                CurrentStageName = a.CurrentStage?.Name ?? "Ứng tuyển mới",
                CurrentStageOrder = a.CurrentStage?.StageOrder ?? 1,
                HasCv = primaryResume != null,
                ResumeId = primaryResume?.Id,
                ResumeFileName = primaryResume?.FileName,
                MatchScore = a.Rating.HasValue ? a.Rating.Value * 20 : null,
                TotalInterviewsCount = a.Interviews.Count(i => !i.IsDeleted),
                AppliedAt = a.CreatedAt
            };
        }).ToList();

        // 2. Lấy danh sách giám khảo nội bộ có thể phân công
        var interviewers = await GetAvailableInterviewersAsync(cancellationToken);

        // 3. Danh sách phòng họp / liên kết họp mẫu (Hỗ trợ lọc Google Meet, Microsoft Teams và Văn phòng)
        var roomPresets = new List<RoomPresetDto>
        {
            // --- Nhóm 1: Google Meet ---
            new()
            {
                Id = "meet_auto",
                Name = "Google Meet (Tự động cấp link họp)",
                Type = "ONLINE_MEET",
                Platform = "MEET",
                LocationOrLink = "https://meet.google.com/nvr-atsw-int",
                Capacity = "Trực tuyến (Không giới hạn)",
                Description = "Tự động gửi liên kết Google Meet đồng bộ vào thư mời email ứng viên.",
                BadgeText = "Google Meet",
                IsDefault = true
            },
            new()
            {
                Id = "meet_panel",
                Name = "Google Meet Hội Đồng (Kèm ghi hình & phụ đề)",
                Type = "ONLINE_MEET",
                Platform = "MEET",
                LocationOrLink = "https://meet.google.com/nvr-exec-panel",
                Capacity = "Trực tuyến (Tự động lưu Drive)",
                Description = "Kích hoạt sẵn tính năng ghi âm và lưu trữ video phục vụ đánh giá năng lực.",
                BadgeText = "Meet Hội Đồng",
                IsDefault = false
            },

            // --- Nhóm 2: Microsoft Teams ---
            new()
            {
                Id = "teams_online",
                Name = "Microsoft Teams (Phòng kỹ thuật chuyên sâu)",
                Type = "ONLINE_MEET",
                Platform = "TEAMS",
                LocationOrLink = "https://teams.microsoft.com/l/meetup-join/noveratech-interview-panel",
                Capacity = "Trực tuyến (Không giới hạn)",
                Description = "Tối ưu cho phiên phỏng vấn Live Coding & chia sẻ màn hình kỹ thuật.",
                BadgeText = "MS Teams",
                IsDefault = false
            },
            new()
            {
                Id = "teams_livecode",
                Name = "Microsoft Teams Live Code (Lab Kỹ thuật 01)",
                Type = "ONLINE_MEET",
                Platform = "TEAMS",
                LocationOrLink = "https://teams.microsoft.com/l/meetup-join/noveratech-livecode-lab",
                Capacity = "Trực tuyến (Tích hợp VS Code)",
                Description = "Hỗ trợ chia sẻ màn hình 4K, micro chống ồn và whiteboard tương tác kiến trúc.",
                BadgeText = "Teams Lab",
                IsDefault = false
            },

            // --- Nhóm 3: Trực tiếp tại văn phòng ---
            new()
            {
                Id = "room_a1",
                Name = "Phòng Họp A1 (Tầng 3 - Trụ sở NoveraTech)",
                Type = "OFFLINE_OFFICE",
                Platform = "OFFLINE",
                LocationOrLink = "Phòng Họp A1, Tầng 3, Tòa nhà NoveraTech, 72 Trần Duy Hưng, Cầu Giấy, Hà Nội",
                Capacity = "Sức chứa 6 người",
                Description = "Màn hình 85 inch, Polycom đàm thoại, bảng trắng phân tích giải pháp.",
                BadgeText = "Trực tiếp A1",
                IsDefault = false
            },
            new()
            {
                Id = "room_b2",
                Name = "Phòng Sáng Tạo B2 (Tầng 5 - Tech Lab)",
                Type = "OFFLINE_OFFICE",
                Platform = "OFFLINE",
                LocationOrLink = "Phòng Sáng Tạo B2, Tầng 5, Tòa nhà NoveraTech, 72 Trần Duy Hưng, Cầu Giấy, Hà Nội",
                Capacity = "Sức chứa 10 người",
                Description = "Trang bị sẵn 2 máy trạm kỹ thuật, màn hình đôi để ứng viên demo sản phẩm.",
                BadgeText = "Tech Lab B2",
                IsDefault = false
            },
            new()
            {
                Id = "room_board",
                Name = "Phòng Hội Đồng Quản Trị (Tầng 8)",
                Type = "OFFLINE_OFFICE",
                Platform = "OFFLINE",
                LocationOrLink = "Phòng Executive Boardroom, Tầng 8, Tòa nhà NoveraTech, 72 Trần Duy Hưng, Cầu Giấy, Hà Nội",
                Capacity = "Sức chứa 16 người",
                Description = "Dành riêng cho phỏng vấn cấp Quản lý, Lead, Trưởng phòng và Giám đốc.",
                BadgeText = "Boardroom",
                IsDefault = false
            }
        };

        // 4. Xác định ứng viên mục tiêu được chọn sẵn
        Guid? targetApplicationId = applicationId;
        if (!targetApplicationId.HasValue && pipelineCandidates.Any())
        {
            targetApplicationId = pipelineCandidates.First().ApplicationId;
        }

        CandidateWizardDetailDto? selectedCandidate = null;
        if (targetApplicationId.HasValue)
        {
            selectedCandidate = await GetCandidateWizardDetailAsync(targetApplicationId.Value, cancellationToken);
        }

        var defaultStart = DateTime.Today.AddDays(1).AddHours(9); // Mặc định 9:00 sáng ngày mai

        var defaultFormInput = new ScheduleInterviewInputModel
        {
            ApplicationId = targetApplicationId ?? Guid.Empty,
            RoundNumber = selectedCandidate?.NextRoundNumber ?? 1,
            RoundTitle = selectedCandidate?.SuggestedRoundTitle ?? "Phỏng vấn Vòng 1",
            StartTime = defaultStart,
            DurationMinutes = 60,
            InterviewType = "ONLINE_MEET",
            LocationOrLink = "https://meet.google.com/nvr-atsw-int",
            InterviewerIds = interviewers.Take(2).Select(i => i.Id).ToList()
        };

        return new ScheduleInterviewWizardViewModel
        {
            PreselectedApplicationId = applicationId,
            SelectedCandidate = selectedCandidate,
            PipelineCandidates = pipelineCandidates,
            AvailableInterviewers = interviewers,
            RoomPresets = roomPresets,
            FormInput = defaultFormInput
        };
    }

    /// <inheritdoc/>
    public async Task<CandidateWizardDetailDto?> GetCandidateWizardDetailAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var app = await _dbContext.Applications
            .Include(a => a.Candidate)
                .ThenInclude(c => c.Resumes)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
                    .ThenInclude(r => r.Department)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
                    .ThenInclude(r => r.JobPosition)
            .Include(a => a.CurrentStage)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Panelists)
                    .ThenInclude(p => p.Interviewer)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Evaluations)
            .FirstOrDefaultAsync(a => a.Id == applicationId && !a.IsDeleted, cancellationToken);

        if (app == null) return null;

        var primaryResume = app.Candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? app.Candidate.Resumes.FirstOrDefault();

        var interviewHistory = app.Interviews
            .Where(i => !i.IsDeleted)
            .OrderBy(i => i.RoundNumber)
            .Select(i =>
            {
                var panelistsList = i.Panelists.Select(p => p.Interviewer?.FullName ?? "Giám khảo").ToList();
                var avgScore = i.Evaluations.Any(e => e.OverallScore.HasValue)
                    ? i.Evaluations.Where(e => e.OverallScore.HasValue).Average(e => (double)e.OverallScore!.Value)
                    : (double?)null;
                var latestEval = i.Evaluations.OrderByDescending(e => e.CreatedAt).FirstOrDefault();
                var conclusionText = latestEval?.Recommendation switch
                {
                    RecommendationType.STRONG_HIRE => "Rất nên tuyển",
                    RecommendationType.HIRE => "Nên tuyển",
                    RecommendationType.CONSIDER => "Cân nhắc",
                    RecommendationType.STRONG_NO_HIRE => "Không nên tuyển",
                    _ => null
                };

                return new InterviewHistorySummaryDto
                {
                    InterviewId = i.Id,
                    RoundNumber = i.RoundNumber,
                    Title = i.Title,
                    InterviewTypeDisplay = i.InterviewType == InterviewType.ONLINE_MEET ? "Trực tuyến (Meet)" : "Trực tiếp tại văn phòng",
                    LocationOrLink = i.LocationOrLink,
                    StartTime = i.StartTime,
                    EndTime = i.EndTime,
                    StatusDisplay = i.Status == InterviewStatus.SCHEDULED ? "Đã lên lịch" : (i.Status == InterviewStatus.COMPLETED ? "Đã hoàn thành" : "Đã hủy"),
                    CandidateConfirmedDisplay = i.CandidateConfirmed == CandidateConfirmStatus.ACCEPTED ? "Ứng viên đã xác nhận" : (i.CandidateConfirmed == CandidateConfirmStatus.DECLINED ? "Ứng viên từ chối" : "Chờ ứng viên xác nhận"),
                    PanelistsDisplay = panelistsList.Any() ? string.Join(", ", panelistsList) : "Chưa phân công",
                    AverageScore = avgScore.HasValue ? Math.Round(avgScore.Value, 1) : null,
                    FinalConclusion = conclusionText
                };
            }).ToList();

        var completedOrScheduledRounds = app.Interviews.Where(i => !i.IsDeleted).Select(i => i.RoundNumber).ToList();
        var nextRound = completedOrScheduledRounds.Any() ? completedOrScheduledRounds.Max() + 1 : 1;

        var jobTitle = app.JobPosting?.Title ?? "Chuyên môn";
        var suggestedTitle = nextRound switch
        {
            1 => $"Phỏng vấn Vòng 1: Sơ vấn & Trao đổi văn hóa - {jobTitle}",
            2 => $"Phỏng vấn Vòng 2: Đánh giá Năng lực Kỹ thuật Chuyên sâu - {jobTitle}",
            3 => $"Phỏng vấn Vòng 3: Phỏng vấn Ban Giám đốc & Thỏa thuận đãi ngộ - {jobTitle}",
            _ => $"Phỏng vấn Vòng {nextRound} - {jobTitle}"
        };

        return new CandidateWizardDetailDto
        {
            ApplicationId = app.Id,
            CandidateId = app.CandidateId,
            FullName = $"{app.Candidate.FirstName} {app.Candidate.LastName}".Trim(),
            Email = app.Candidate.Email,
            Phone = app.Candidate.Phone ?? string.Empty,
            CurrentTitle = !string.IsNullOrWhiteSpace(app.Candidate.CurrentTitle) ? app.Candidate.CurrentTitle : "Ứng viên chuyên môn",
            CurrentCompany = !string.IsNullOrWhiteSpace(app.Candidate.CurrentCompany) ? app.Candidate.CurrentCompany : "Đang tìm kiếm cơ hội",
            JobPostingId = app.JobPostingId,
            JobTitle = jobTitle,
            DepartmentName = app.JobPosting?.Requisition?.Department?.Name ?? "Ban Công Nghệ",
            JobLevel = app.JobPosting?.Requisition?.JobPosition?.JobLevel ?? "Chuyên viên",
            CurrentStageName = app.CurrentStage?.Name ?? "Ứng tuyển mới",
            CurrentStageOrder = app.CurrentStage?.StageOrder ?? 1,
            ActiveResumeId = primaryResume?.Id,
            ResumeFileName = primaryResume?.FileName,
            ResumeFileSizeDisplay = primaryResume != null ? $"{primaryResume.FileSize / 1024} KB" : null,
            MatchScore = app.Rating.HasValue ? app.Rating.Value * 20 : null,
            NextRoundNumber = nextRound,
            SuggestedRoundTitle = suggestedTitle,
            InterviewHistory = interviewHistory
        };
    }

    /// <inheritdoc/>
    public async Task<List<InterviewerBusyCheckDto>> CheckInterviewersAvailabilityAsync(
        List<Guid> interviewerIds,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default)
    {
        if (interviewerIds == null || !interviewerIds.Any())
        {
            return new List<InterviewerBusyCheckDto>();
        }

        var interviewers = await _dbContext.Users
            .Where(u => interviewerIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

        // Tìm các phỏng vấn trùng thời gian: [StartTime, EndTime] giao nhau với [startTime, endTime]
        // Giao nhau khi: i.StartTime < endTime && i.EndTime > startTime
        var conflictingInterviews = await _dbContext.InterviewPanelists
            .Include(p => p.Interview)
            .Where(p => interviewerIds.Contains(p.InterviewerId)
                        && !p.Interview.IsDeleted
                        && p.Interview.Status != InterviewStatus.CANCELLED
                        && p.Interview.StartTime.HasValue
                        && p.Interview.EndTime.HasValue
                        && p.Interview.StartTime.Value < endTime
                        && p.Interview.EndTime.Value > startTime)
            .ToListAsync(cancellationToken);

        var result = new List<InterviewerBusyCheckDto>();
        foreach (var inv in interviewers)
        {
            var conflict = conflictingInterviews.FirstOrDefault(p => p.InterviewerId == inv.Id);
            if (conflict != null)
            {
                var intv = conflict.Interview;
                result.Add(new InterviewerBusyCheckDto
                {
                    InterviewerId = inv.Id,
                    FullName = inv.FullName,
                    Role = inv.Role,
                    IsBusy = true,
                    StatusMessage = "Có thể trùng lịch",
                    ConflictTitle = intv.Title,
                    ConflictTimeDisplay = $"{intv.StartTime?.ToOffset(TimeSpan.FromHours(7)):HH:mm} - {intv.EndTime?.ToOffset(TimeSpan.FromHours(7)):HH:mm}"
                });
            }
            else
            {
                result.Add(new InterviewerBusyCheckDto
                {
                    InterviewerId = inv.Id,
                    FullName = inv.FullName,
                    Role = inv.Role,
                    IsBusy = false,
                    StatusMessage = "Sẵn sàng (Trống lịch)"
                });
            }
        }

        return result;
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

    /// <inheritdoc/>
    public async Task<InterviewDetailForEditDto?> GetInterviewDetailForEditAsync(
        Guid interviewId,
        Guid currentUserId = default,
        bool isRecruiterOnly = false,
        CancellationToken cancellationToken = default)
    {
        var interview = await _dbContext.Interviews
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Application)
                .ThenInclude(a => a.JobPosting)
            .Include(i => i.Panelists)
                .ThenInclude(p => p.Interviewer)
            .Include(i => i.Evaluations)
            .FirstOrDefaultAsync(i => i.Id == interviewId && !i.IsDeleted, cancellationToken);

        if (interview == null)
        {
            return null;
        }

        if (isRecruiterOnly && _assignmentService != null && currentUserId != Guid.Empty)
        {
            var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, interview.ApplicationId);
            if (!canAccess)
            {
                return null;
            }
        }

        var cand = interview.Application?.Candidate;
        var candName = cand != null ? $"{cand.FirstName} {cand.LastName}".Trim() : "Ứng viên";
        var hasSubmitted = interview.Evaluations.Any(e => e.IsSubmitted && !e.IsDeleted);

        var duration = 60;
        if (interview.StartTime.HasValue && interview.EndTime.HasValue)
        {
            var diff = (int)(interview.EndTime.Value - interview.StartTime.Value).TotalMinutes;
            if (diff > 0) duration = diff;
        }

        var activePanelists = interview.Panelists.Where(p => !p.IsDeleted).ToList();
        var panelistNames = activePanelists.Any()
            ? string.Join(", ", activePanelists.Select(p => p.Interviewer?.FullName ?? "Giám khảo"))
            : "Chưa phân công";

        string? cancellationReason = null;
        if (interview.Status == InterviewStatus.CANCELLED && !string.IsNullOrWhiteSpace(interview.CandidateNotes))
        {
            var marker = "[LÝ DO HUỶ]:";
            var idx = interview.CandidateNotes.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var sub = interview.CandidateNotes[(idx + marker.Length)..];
                var endIdx = sub.IndexOf('\n');
                cancellationReason = (endIdx >= 0 ? sub[..endIdx] : sub).Trim();
            }
            else
            {
                cancellationReason = interview.CandidateNotes.Trim();
            }
        }

        string? rescheduleReason = null;
        if (interview.Status == InterviewStatus.RESCHEDULED && !string.IsNullOrWhiteSpace(interview.CandidateNotes))
        {
            var marker = "[Dời lịch:";
            var idx = interview.CandidateNotes.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var sub = interview.CandidateNotes[(idx + marker.Length)..];
                var endBracket = sub.IndexOf(']');
                rescheduleReason = (endBracket >= 0 ? sub[..endBracket] : sub).Trim();
            }
        }

        return new InterviewDetailForEditDto
        {
            InterviewId = interview.Id,
            ApplicationId = interview.ApplicationId,
            CandidateName = candName,
            CandidateEmail = cand?.Email ?? string.Empty,
            JobTitle = interview.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng",
            RoundNumber = interview.RoundNumber,
            Title = interview.Title,
            StartTime = interview.StartTime?.UtcDateTime,
            EndTime = interview.EndTime?.UtcDateTime,
            DurationMinutes = duration,
            InterviewType = interview.InterviewType.ToString(),
            LocationOrLink = interview.LocationOrLink ?? string.Empty,
            InterviewerIds = activePanelists.Select(p => p.InterviewerId).ToList(),
            PanelistNames = panelistNames,
            Notes = interview.CandidateNotes,
            HasSubmittedEvaluation = hasSubmitted,
            Status = interview.Status.ToString(),
            CancellationReason = cancellationReason,
            RescheduleReason = rescheduleReason
        };
    }

    /// <inheritdoc/>
    public async Task<InterviewScheduleResultDto> RescheduleInterviewAsync(
        RescheduleInterviewInputModel model,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default)
    {
        if (model == null || model.InterviewId == Guid.Empty)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Dữ liệu yêu cầu dời lịch phỏng vấn không hợp lệ.",
                Errors = new List<string> { "Mã buổi phỏng vấn không được để trống." }
            };
        }

        var interview = await _dbContext.Interviews
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Application)
                .ThenInclude(a => a.JobPosting)
            .Include(i => i.Panelists)
                .ThenInclude(p => p.Interviewer)
            .Include(i => i.Evaluations)
            .FirstOrDefaultAsync(i => i.Id == model.InterviewId && !i.IsDeleted, cancellationToken);

        if (interview == null)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Không tìm thấy thông tin buổi phỏng vấn trong hệ thống.",
                Errors = new List<string> { "Buổi phỏng vấn không tồn tại hoặc đã bị xóa." }
            };
        }

        // 1. Kiểm tra quyền hạn phụ trách hồ sơ (Scrum 26)
        if (isRecruiterOnly && _assignmentService != null && currentUserId != Guid.Empty)
        {
            var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, interview.ApplicationId);
            if (!canAccess)
            {
                return new InterviewScheduleResultDto
                {
                    Success = false,
                    Message = "Bạn không có quyền dời lịch phỏng vấn cho hồ sơ này do không phụ trách vị trí tuyển dụng tương ứng.",
                    Errors = new List<string> { "Không có quyền phụ trách vị trí tuyển dụng tương ứng." }
                };
            }
        }

        // 2. QUY TẮC CỐT TỬ: Không sửa được buổi đã có phiếu đánh giá nộp (Scrum - Số 55)
        var hasSubmittedEval = interview.Evaluations.Any(e => e.IsSubmitted && !e.IsDeleted);
        if (hasSubmittedEval)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Không thể dời lịch phỏng vấn này vì đã có phiếu đánh giá được nộp bởi hội đồng phỏng vấn.",
                Errors = new List<string> { "Buổi phỏng vấn đã có ít nhất một phiếu đánh giá nộp, hệ thống khóa toàn bộ quyền sửa đổi." }
            };
        }

        // 3. Kiểm tra trạng thái buổi phỏng vấn
        if (interview.Status == InterviewStatus.CANCELLED)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Buổi phỏng vấn đã bị huỷ trước đó, không thể dời lịch. Vui lòng tạo một lịch phỏng vấn mới.",
                Errors = new List<string> { "Không thể dời lịch buổi phỏng vấn đã huỷ." }
            };
        }

        if (interview.Status == InterviewStatus.COMPLETED)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Buổi phỏng vấn đã hoàn thành, không thể dời lịch.",
                Errors = new List<string> { "Không thể dời lịch buổi phỏng vấn đã hoàn thành." }
            };
        }

        // 4. Kiểm tra ngày giờ bắt đầu mới
        var rawStartTime = model.GetNormalizedStartTime();
        if (!rawStartTime.HasValue)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Vui lòng chọn ngày giờ phỏng vấn mới.",
                Errors = new List<string> { "Ngày giờ phỏng vấn mới không được để trống." }
            };
        }

        var startUtc = new DateTimeOffset(DateTime.SpecifyKind(rawStartTime.Value, DateTimeKind.Utc));
        var nowUtc = DateTimeOffset.UtcNow;

        if (startUtc <= nowUtc)
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Không thể dời lịch phỏng vấn về thời điểm trong quá khứ. Vui lòng chọn ngày giờ trong tương lai.",
                Errors = new List<string> { "Thời điểm phỏng vấn mới phải lớn hơn thời gian hiện tại." }
            };
        }

        // 5. Kiểm tra thời lượng và kết thúc
        if (model.DurationMinutes <= 0) model.DurationMinutes = 60;
        var endUtc = model.NewEndTime.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(model.NewEndTime.Value, DateTimeKind.Utc))
            : (model.EndTime.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(model.EndTime.Value, DateTimeKind.Utc))
                : startUtc.AddMinutes(model.DurationMinutes));

        if (endUtc <= startUtc)
        {
            endUtc = startUtc.AddMinutes(model.DurationMinutes);
        }

        // 6. Kiểm tra người phỏng vấn
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

        // 7. Kiểm tra phòng hoặc liên kết họp
        if (string.IsNullOrWhiteSpace(model.LocationOrLink))
        {
            return new InterviewScheduleResultDto
            {
                Success = false,
                Message = "Vui lòng cung cấp phòng họp hoặc liên kết họp trực tuyến mới.",
                Errors = new List<string> { "Địa điểm hoặc liên kết họp không được để trống." }
            };
        }

        // Lưu thông tin cũ để đưa vào nội dung email so sánh
        var oldStartUtc = interview.StartTime ?? DateTimeOffset.UtcNow;
        var oldPanelistUsers = interview.Panelists.Where(p => !p.IsDeleted).Select(p => p.Interviewer).ToList();

        // 8. Cập nhật thông tin buổi phỏng vấn
        var meetLocation = model.LocationOrLink.Trim();
        var interviewType = model.InterviewType?.Equals("OFFLINE_OFFICE", StringComparison.OrdinalIgnoreCase) == true
            ? InterviewType.OFFLINE_OFFICE
            : (model.InterviewType?.Equals("ONLINE_MEET", StringComparison.OrdinalIgnoreCase) == true
                ? InterviewType.ONLINE_MEET
                : (meetLocation.Contains("http", StringComparison.OrdinalIgnoreCase) ? InterviewType.ONLINE_MEET : InterviewType.OFFLINE_OFFICE));

        interview.StartTime = startUtc;
        interview.EndTime = endUtc;
        interview.InterviewType = interviewType;
        interview.LocationOrLink = meetLocation;
        interview.Status = InterviewStatus.RESCHEDULED;
        interview.CandidateConfirmed = CandidateConfirmStatus.PENDING;

        var reason = model.GetNormalizedReason();
        var notes = model.GetNormalizedNotes();
        var prefixReason = !string.IsNullOrWhiteSpace(reason) ? $"[Dời lịch: {reason}]" : "";
        interview.CandidateNotes = string.Join(" ", new[] { prefixReason, notes }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        interview.UpdatedAt = DateTimeOffset.UtcNow;
        if (currentUserId != Guid.Empty) interview.UpdatedById = currentUserId;

        // 9. Cập nhật thành viên hội đồng phỏng vấn (Panelists)
        var newPanelists = await _dbContext.Users
            .Where(u => interviewerIds.Contains(u.Id) && u.Status == "ACTIVE")
            .ToListAsync(cancellationToken);

        // Xóa các panelist cũ không còn trong danh sách mới
        var currentPanelists = interview.Panelists.ToList();
        foreach (var p in currentPanelists)
        {
            if (!interviewerIds.Contains(p.InterviewerId))
            {
                _dbContext.InterviewPanelists.Remove(p);
            }
        }

        // Thêm panelist mới nếu chưa có
        var existingUserIds = currentPanelists.Select(p => p.InterviewerId).ToHashSet();
        var isLead = !currentPanelists.Any(p => p.IsLead);
        foreach (var user in newPanelists)
        {
            if (!existingUserIds.Contains(user.Id))
            {
                await _dbContext.InterviewPanelists.AddAsync(new InterviewPanelist
                {
                    Id = Guid.NewGuid(),
                    InterviewId = interview.Id,
                    InterviewerId = user.Id,
                    IsLead = isLead,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
                isLead = false;
            }
        }

        var newPanelistNames = newPanelists.Select(u => $"{u.FullName} ({u.Role})").ToList();
        var panelistStr = newPanelistNames.Any() ? string.Join(", ", newPanelistNames) : "Hội đồng phỏng vấn";

        // 10. Ghi nhận lịch sử giai đoạn hồ sơ ứng viên
        var cand = interview.Application?.Candidate;
        var candName = cand != null ? $"{cand.FirstName} {cand.LastName}".Trim() : "Ứng viên";
        var candEmail = cand?.Email;
        var jobTitle = interview.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng";
        var reasonComment = !string.IsNullOrWhiteSpace(reason) ? $". Lý do: {reason}" : "";

        await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
        {
            Id = Guid.NewGuid(),
            ApplicationId = interview.ApplicationId,
            FromStageId = interview.Application?.CurrentStageId,
            ToStageId = interview.Application?.CurrentStageId ?? Guid.Empty,
            ChangedByUserId = currentUserId != Guid.Empty ? currentUserId : Guid.Empty,
            Comment = $"Đã dời lịch {interview.Title}: từ {oldStartUtc.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm} sang {startUtc.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm}{reasonComment}.",
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        // 11. Gửi thông báo trong hệ thống (In-App Notifications) cho toàn bộ giám khảo liên quan
        var allAffectedInterviewerUsers = newPanelists.Concat(oldPanelistUsers)
            .GroupBy(u => u.Id)
            .Select(g => g.First())
            .ToList();

        if (_notificationService != null)
        {
            foreach (var invUser in allAffectedInterviewerUsers)
            {
                await _notificationService.QueueNotificationAsync(
                    invUser.Id,
                    $"Lịch phỏng vấn đã được dời: {jobTitle}",
                    $"Phiên phỏng vấn ứng viên {candName} ({interview.Title}) đã dời sang {startUtc.ToOffset(TimeSpan.FromHours(7)):HH:mm dd/MM/yyyy}. Hình thức: {(interviewType == InterviewType.ONLINE_MEET ? "Online Google Meet" : "Trực tiếp tại VP")}.{(!string.IsNullOrWhiteSpace(reason) ? $" Lý do: {reason}" : "")}",
                    $"/danh-gia/{interview.Id}",
                    NotificationType.InterviewInvitation,
                    cancellationToken);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // 12. Gửi email thông báo dời lịch tự động cho Ứng viên và Toàn bộ Người phỏng vấn (Scrum - Số 55)
        var typeDisplay = interviewType == InterviewType.ONLINE_MEET ? "Trực tuyến (Google Meet)" : "Trực tiếp tại văn phòng NoveraTech";

        _ = Task.Run(async () =>
        {
            // Gửi email cho Ứng viên
            if (!string.IsNullOrWhiteSpace(candEmail))
            {
                try
                {
                    await _emailService.SendInterviewRescheduledAsync(
                        candEmail,
                        candName,
                        isCandidate: true,
                        candidateName: candName,
                        jobTitle: jobTitle,
                        roundTitle: interview.Title,
                        oldStartTime: oldStartUtc,
                        newStartTime: startUtc,
                        newEndTime: endUtc,
                        newLocationOrLink: meetLocation,
                        newInterviewTypeDisplay: typeDisplay,
                        panelistNames: panelistStr,
                        rescheduleReason: reason);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Không thể gửi email dời lịch cho ứng viên {Email}: {Msg}", candEmail, ex.Message);
                }
            }

            // Gửi email cho từng Giám khảo trong hội đồng phỏng vấn
            foreach (var invUser in allAffectedInterviewerUsers)
            {
                if (!string.IsNullOrWhiteSpace(invUser.Email))
                {
                    try
                    {
                        await _emailService.SendInterviewRescheduledAsync(
                            invUser.Email,
                            invUser.FullName,
                            isCandidate: false,
                            candidateName: candName,
                            jobTitle: jobTitle,
                            roundTitle: interview.Title,
                            oldStartTime: oldStartUtc,
                            newStartTime: startUtc,
                            newEndTime: endUtc,
                            newLocationOrLink: meetLocation,
                            newInterviewTypeDisplay: typeDisplay,
                            panelistNames: panelistStr,
                            rescheduleReason: reason);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Không thể gửi email dời lịch cho giám khảo {Email}: {Msg}", invUser.Email, ex.Message);
                    }
                }
            }
        });

        return new InterviewScheduleResultDto
        {
            Success = true,
            Message = $"Đã dời lịch phỏng vấn {interview.Title} cho ứng viên {candName} sang {startUtc.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm} thành công!",
            InterviewId = interview.Id,
            RoundNumber = interview.RoundNumber,
            Title = interview.Title,
            StartTimeDisplay = startUtc.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm"),
            EndTimeDisplay = endUtc.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm"),
            InterviewTypeDisplay = typeDisplay,
            LocationOrLink = meetLocation,
            Panelists = panelistStr
        };
    }

    /// <inheritdoc/>
    public async Task<InterviewCancelResultDto> CancelInterviewAsync(
        CancelInterviewInputModel model,
        Guid currentUserId,
        bool isRecruiterOnly,
        CancellationToken cancellationToken = default)
    {
        if (model == null || model.InterviewId == Guid.Empty)
        {
            return new InterviewCancelResultDto
            {
                Success = false,
                Message = "Dữ liệu yêu cầu huỷ buổi phỏng vấn không hợp lệ.",
                Errors = new List<string> { "Mã buổi phỏng vấn không được để trống." }
            };
        }

        // BẮT BUỘC NHẬP LÝ DO HUỶ (Scrum - Số 55)
        if (string.IsNullOrWhiteSpace(model.Reason) || model.Reason.Trim().Length < 5)
        {
            return new InterviewCancelResultDto
            {
                Success = false,
                Message = "Vui lòng nhập lý do huỷ buổi phỏng vấn (tối thiểu 5 ký tự) để gửi thông báo giải thích cho ứng viên và hội đồng.",
                Errors = new List<string> { "Lý do huỷ bắt buộc phải có ít nhất 5 ký tự." }
            };
        }

        var interview = await _dbContext.Interviews
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Application)
                .ThenInclude(a => a.JobPosting)
            .Include(i => i.Panelists)
                .ThenInclude(p => p.Interviewer)
            .Include(i => i.Evaluations)
            .FirstOrDefaultAsync(i => i.Id == model.InterviewId && !i.IsDeleted, cancellationToken);

        if (interview == null)
        {
            return new InterviewCancelResultDto
            {
                Success = false,
                Message = "Không tìm thấy thông tin buổi phỏng vấn trong hệ thống.",
                Errors = new List<string> { "Buổi phỏng vấn không tồn tại hoặc đã bị xóa." }
            };
        }

        // 1. Kiểm tra quyền hạn phụ trách hồ sơ (Scrum 26)
        if (isRecruiterOnly && _assignmentService != null && currentUserId != Guid.Empty)
        {
            var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, interview.ApplicationId);
            if (!canAccess)
            {
                return new InterviewCancelResultDto
                {
                    Success = false,
                    Message = "Bạn không có quyền huỷ lịch phỏng vấn của hồ sơ này do không phụ trách vị trí tuyển dụng tương ứng.",
                    Errors = new List<string> { "Không có quyền phụ trách vị trí tuyển dụng tương ứng." }
                };
            }
        }

        // 2. QUY TẮC CỐT TỬ: Không huỷ được buổi đã có phiếu đánh giá nộp (Scrum - Số 55)
        var hasSubmittedEval = interview.Evaluations.Any(e => e.IsSubmitted && !e.IsDeleted);
        if (hasSubmittedEval)
        {
            return new InterviewCancelResultDto
            {
                Success = false,
                Message = "Không thể huỷ buổi phỏng vấn này vì đã có phiếu đánh giá được nộp bởi hội đồng phỏng vấn.",
                Errors = new List<string> { "Buổi phỏng vấn đã có ít nhất một phiếu đánh giá nộp, không được phép huỷ." }
            };
        }

        // 3. Kiểm tra trạng thái buổi phỏng vấn
        if (interview.Status == InterviewStatus.CANCELLED)
        {
            return new InterviewCancelResultDto
            {
                Success = false,
                Message = "Buổi phỏng vấn này đã ở trạng thái huỷ trước đó.",
                Errors = new List<string> { "Buổi phỏng vấn đã bị huỷ trước đó." }
            };
        }

        if (interview.Status == InterviewStatus.COMPLETED)
        {
            return new InterviewCancelResultDto
            {
                Success = false,
                Message = "Buổi phỏng vấn đã hoàn thành, không thể huỷ.",
                Errors = new List<string> { "Buổi phỏng vấn đã hoàn thành, không thể huỷ." }
            };
        }

        var cleanReason = model.Reason.Trim();
        var cand = interview.Application?.Candidate;
        var candName = cand != null ? $"{cand.FirstName} {cand.LastName}".Trim() : "Ứng viên";
        var candEmail = cand?.Email;
        var jobTitle = interview.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng";
        var scheduledTime = interview.StartTime ?? DateTimeOffset.UtcNow;

        // 4. Cập nhật trạng thái buổi phỏng vấn thành CANCELLED
        interview.Status = InterviewStatus.CANCELLED;
        var prevNotes = interview.CandidateNotes ?? string.Empty;
        interview.CandidateNotes = $"[LÝ DO HUỶ]: {cleanReason}\n{prevNotes}".Trim();
        interview.UpdatedAt = DateTimeOffset.UtcNow;
        if (currentUserId != Guid.Empty) interview.UpdatedById = currentUserId;

        // 5. Ghi nhận lịch sử ứng viên
        await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
        {
            Id = Guid.NewGuid(),
            ApplicationId = interview.ApplicationId,
            FromStageId = interview.Application?.CurrentStageId,
            ToStageId = interview.Application?.CurrentStageId ?? Guid.Empty,
            ChangedByUserId = currentUserId != Guid.Empty ? currentUserId : Guid.Empty,
            Comment = $"Đã huỷ {interview.Title} ({scheduledTime.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm}). Lý do: {cleanReason}.",
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        // 6. Gửi In-App Notification cho toàn bộ giám khảo trong hội đồng phỏng vấn
        var activePanelists = interview.Panelists.Where(p => !p.IsDeleted).Select(p => p.Interviewer).Where(u => u != null).ToList();
        if (_notificationService != null)
        {
            foreach (var panelistUser in activePanelists)
            {
                await _notificationService.QueueNotificationAsync(
                    panelistUser.Id,
                    $"Buổi phỏng vấn đã bị huỷ: {jobTitle}",
                    $"Buổi phỏng vấn ứng viên {candName} ({interview.Title}) dự kiến lúc {scheduledTime.ToOffset(TimeSpan.FromHours(7)):HH:mm dd/MM/yyyy} đã bị huỷ. Lý do: {cleanReason}",
                    "/dashboard/lich-phong-van",
                    NotificationType.InterviewInvitation,
                    cancellationToken);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // 7. Gửi email thông báo huỷ tự động cho Ứng viên và Toàn bộ Giám khảo (Scrum - Số 55)
        _ = Task.Run(async () =>
        {
            // Email cho Ứng viên
            if (!string.IsNullOrWhiteSpace(candEmail))
            {
                try
                {
                    await _emailService.SendInterviewCancelledAsync(
                        candEmail,
                        candName,
                        isCandidate: true,
                        candidateName: candName,
                        jobTitle: jobTitle,
                        roundTitle: interview.Title,
                        scheduledStartTime: scheduledTime,
                        cancellationReason: cleanReason);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Không thể gửi email huỷ lịch cho ứng viên {Email}: {Msg}", candEmail, ex.Message);
                }
            }

            // Email cho từng Giám khảo
            foreach (var panelistUser in activePanelists)
            {
                if (!string.IsNullOrWhiteSpace(panelistUser.Email))
                {
                    try
                    {
                        await _emailService.SendInterviewCancelledAsync(
                            panelistUser.Email,
                            panelistUser.FullName,
                            isCandidate: false,
                            candidateName: candName,
                            jobTitle: jobTitle,
                            roundTitle: interview.Title,
                            scheduledStartTime: scheduledTime,
                            cancellationReason: cleanReason);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Không thể gửi email huỷ lịch cho giám khảo {Email}: {Msg}", panelistUser.Email, ex.Message);
                    }
                }
            }
        });

        return new InterviewCancelResultDto
        {
            Success = true,
            Message = $"Đã huỷ buổi phỏng vấn {interview.Title} của ứng viên {candName} thành công. Hệ thống đã gửi thông báo đến các bên liên quan.",
            InterviewId = interview.Id,
            CancellationReason = cleanReason
        };
    }
}
