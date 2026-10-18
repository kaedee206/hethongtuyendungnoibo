using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Interviews;
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

        // 7. Xác định thông tin buổi phỏng vấn
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

        await _dbContext.Interviews.AddAsync(interview, cancellationToken);

        // 8. Gán danh sách Hội đồng phỏng vấn (Panelists)
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

            var displayRole = UserRoles.GetDisplayName(inv.Role);
            panelistNames.Add($"{inv.FullName} ({displayRole})");
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
            ? (meetLocation.Contains("teams", StringComparison.OrdinalIgnoreCase) ? "Trực tuyến (MS Teams)" : "Trực tuyến (Google Meet)")
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
}
