using System.Security.Cryptography;
using System.Text;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.CandidateInterviews;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

/// <summary>
/// Service triển khai nghiệp vụ Thư mời phỏng vấn và Xác nhận/Đổi lịch dành cho Ứng viên (Scrum - Số 54).
/// Tuân thủ quy chuẩn: Primary Constructor, Service Layer phân tầng sạch, múi giờ Việt Nam, bảo mật HMAC Token.
/// </summary>
public class CandidateInterviewService(
    ApplicationDbContext dbContext,
    ILogger<CandidateInterviewService> logger,
    IConfiguration? configuration = null) : ICandidateInterviewService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<CandidateInterviewService> _logger = logger;
    private readonly string _tokenSecret = configuration?["Security:InterviewTokenSecret"] ?? "NoveraTech_Ats_Interview_Invitation_Key_2026_Secure_HMAC";
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

    /// <summary>
    /// Sinh mã token bảo mật gắn liền duy nhất với buổi phỏng vấn này.
    /// Định dạng: {interviewId:D}_{16_hex_hmac_hash}
    /// </summary>
    public string GenerateInvitationToken(Guid interviewId, string candidateEmail)
    {
        var emailNormalized = (candidateEmail ?? string.Empty).Trim().ToLowerInvariant();
        var rawData = $"{interviewId:D}:{emailNormalized}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_tokenSecret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        var hashHex = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();

        return $"{interviewId:D}_{hashHex}";
    }

    /// <summary>
    /// Kiểm tra token và trích xuất InterviewId.
    /// </summary>
    public bool ValidateInvitationToken(string token, out Guid interviewId)
    {
        interviewId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Trim().Split('_');
        if (parts.Length != 2)
            return false;

        if (!Guid.TryParse(parts[0], out var parsedId))
            return false;

        var candidateHash = parts[1].Trim().ToLowerInvariant();

        // Tìm phỏng vấn tương ứng để xác thực chữ ký số gắn với đúng ứng viên
        var interview = _dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .FirstOrDefault(i => i.Id == parsedId && !i.IsDeleted);

        if (interview == null || interview.Application?.Candidate == null)
            return false;

        var expectedToken = GenerateInvitationToken(interview.Id, interview.Application.Candidate.Email);
        var expectedParts = expectedToken.Split('_');
        var expectedHash = expectedParts[1];

        // So sánh chuỗi hash với thời gian thực thi hằng số chống timing attack
        var candidateBytes = Encoding.UTF8.GetBytes(candidateHash);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHash);

        if (candidateBytes.Length != expectedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes))
        {
            _logger.LogWarning("Mã token thư mời phỏng vấn không hợp lệ hoặc đã bị can thiệp. ID: {Id}", parsedId);
            return false;
        }

        interviewId = parsedId;
        return true;
    }

    /// <summary>
    /// Lấy thông tin chi tiết của buổi phỏng vấn để ứng viên xem xét và phản hồi.
    /// </summary>
    public async Task<CandidateInterviewConfirmationViewModel?> GetInterviewConfirmationAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateInvitationToken(token, out var interviewId))
            return null;

        var interview = await _dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Application)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(j => j.Requisition)
                        .ThenInclude(r => r.Department)
            .Include(i => i.Panelists)
                .ThenInclude(p => p.Interviewer)
            .FirstOrDefaultAsync(i => i.Id == interviewId && !i.IsDeleted, cancellationToken);

        if (interview == null || interview.Application?.Candidate == null)
            return null;

        var cand = interview.Application.Candidate;
        var candName = $"{cand.FirstName} {cand.LastName}".Trim();
        var jobTitle = interview.Application.JobPosting?.Title ?? "Vị trí tuyển dụng";
        var deptName = interview.Application.JobPosting?.Requisition?.Department?.Name ?? "NoveraTech";

        var panelistNames = interview.Panelists.Any(p => !p.IsDeleted)
            ? string.Join(", ", interview.Panelists.Where(p => !p.IsDeleted).Select(p => p.Interviewer.FullName))
            : "Hội đồng chuyên môn NoveraTech";

        // Định dạng thời gian theo múi giờ Việt Nam (UTC+7 / Asia/Ho_Chi_Minh)
        string timeDisplay;
        string dateDisplay;
        if (interview.StartTime.HasValue && interview.EndTime.HasValue)
        {
            var startVn = TimeZoneInfo.ConvertTime(interview.StartTime.Value, VietnamTimeZone);
            var endVn = TimeZoneInfo.ConvertTime(interview.EndTime.Value, VietnamTimeZone);
            timeDisplay = $"{startVn:HH:mm} - {endVn:HH:mm} (GMT+7)";
            dateDisplay = $"{startVn:dddd, dd/MM/yyyy}";
        }
        else if (interview.StartTime.HasValue)
        {
            var startVn = TimeZoneInfo.ConvertTime(interview.StartTime.Value, VietnamTimeZone);
            timeDisplay = $"{startVn:HH:mm} (GMT+7)";
            dateDisplay = $"{startVn:dddd, dd/MM/yyyy}";
        }
        else
        {
            timeDisplay = "Theo thông báo lịch làm việc";
            dateDisplay = "Đang sắp xếp";
        }

        // Hướng dẫn chuẩn bị chuẩn hóa
        var prepGuide = BuildPreparationGuide(interview);

        var isExpired = interview.Status != InterviewStatus.SCHEDULED ||
                        (interview.EndTime.HasValue && interview.EndTime.Value < DateTimeOffset.UtcNow);

        return new CandidateInterviewConfirmationViewModel
        {
            InterviewId = interview.Id,
            ApplicationId = interview.ApplicationId,
            Token = token,
            CandidateName = candName,
            CandidateEmail = cand.Email,
            CandidateUserId = cand.UserId,
            JobTitle = jobTitle,
            DepartmentName = deptName,
            RoundTitle = interview.Title,
            RoundNumber = interview.RoundNumber,
            InterviewType = interview.InterviewType,
            LocationOrLink = string.IsNullOrWhiteSpace(interview.LocationOrLink)
                ? (interview.InterviewType == InterviewType.ONLINE_MEET ? "Google Meet (Link gửi qua lịch hẹn)" : "Trụ sở Công ty NoveraTech")
                : interview.LocationOrLink,
            StartTime = interview.StartTime,
            EndTime = interview.EndTime,
            TimeDisplay = timeDisplay,
            DateDisplay = dateDisplay,
            PanelistNames = panelistNames,
            CandidateConfirmed = interview.CandidateConfirmed,
            CandidateNotes = interview.CandidateNotes,
            InterviewStatus = interview.Status,
            PreparationGuide = prepGuide,
            IsExpired = isExpired
        };
    }

    /// <summary>
    /// Xử lý xác nhận tham gia buổi phỏng vấn từ ứng viên.
    /// </summary>
    public async Task<(bool Success, string Message)> ConfirmAttendanceAsync(
        CandidateInterviewConfirmInputModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateInvitationToken(model.Token, out var interviewId))
            return (false, "Mã xác thực không hợp lệ hoặc liên kết chỉ áp dụng cho đúng buổi phỏng vấn được mời.");

        var interview = await _dbContext.Interviews
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Panelists)
            .FirstOrDefaultAsync(i => i.Id == interviewId && !i.IsDeleted, cancellationToken);

        if (interview == null)
            return (false, "Không tìm thấy buổi phỏng vấn yêu cầu.");

        if (interview.Status != InterviewStatus.SCHEDULED)
            return (false, "Buổi phỏng vấn này hiện không ở trạng thái sẵn sàng để xác nhận.");

        var candName = $"{interview.Application?.Candidate?.FirstName} {interview.Application?.Candidate?.LastName}".Trim();

        interview.CandidateConfirmed = CandidateConfirmStatus.ACCEPTED;
        if (!string.IsNullOrWhiteSpace(model.Notes))
        {
            interview.CandidateNotes = $"Ghi chú từ ứng viên: {model.Notes.Trim()}";
        }
        interview.UpdatedAt = DateTimeOffset.UtcNow;

        // Ghi nhận lịch sử tương tác và tiến trình hồ sơ
        var startStr = interview.StartTime.HasValue
            ? TimeZoneInfo.ConvertTime(interview.StartTime.Value, VietnamTimeZone).ToString("dd/MM/yyyy HH:mm")
            : "theo lịch đã hẹn";

        var candUserId = interview.Application?.Candidate?.UserId ?? (interview.Panelists.FirstOrDefault()?.InterviewerId ?? Guid.Empty);
        var currentStageId = interview.Application?.CurrentStageId ?? Guid.Empty;

        if (currentStageId != Guid.Empty && candUserId != Guid.Empty)
        {
            _dbContext.ApplicationStageHistories.Add(new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = interview.ApplicationId,
                FromStageId = currentStageId,
                ToStageId = currentStageId,
                ChangedByUserId = candUserId,
                Comment = $"Ứng viên {candName} đã xác nhận tham dự buổi phỏng vấn \"{interview.Title}\" ({startStr}).",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        if (interview.Application != null)
        {
            _dbContext.CandidateInteractions.Add(new CandidateInteraction
            {
                Id = Guid.NewGuid(),
                CandidateId = interview.Application.CandidateId,
                ApplicationId = interview.ApplicationId,
                CreatedByUserId = candUserId != Guid.Empty ? candUserId : Guid.NewGuid(),
                InteractionType = CandidateInteractionType.MEETING,
                Title = "Ứng viên xác nhận tham gia phỏng vấn",
                Content = $"Ứng viên đã xác nhận tham dự phiên phỏng vấn \"{interview.Title}\" vào lúc {startStr}." +
                          (!string.IsNullOrWhiteSpace(model.Notes) ? $" Lời nhắn: {model.Notes.Trim()}" : ""),
                InteractionDate = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Ứng viên đã xác nhận tham dự buổi phỏng vấn {InterviewId}", interview.Id);

        return (true, "Bạn đã xác nhận tham dự buổi phỏng vấn thành công! NoveraTech rất mong đợi được gặp bạn.");
    }

    /// <summary>
    /// Xử lý yêu cầu đề nghị đổi lịch phỏng vấn từ ứng viên.
    /// </summary>
    public async Task<(bool Success, string Message)> RequestRescheduleAsync(
        CandidateInterviewRescheduleInputModel model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.Reason))
            return (false, "Vui lòng nhập lý do đề nghị đổi lịch phỏng vấn.");

        if (!ValidateInvitationToken(model.Token, out var interviewId))
            return (false, "Mã xác thực không hợp lệ hoặc liên kết chỉ áp dụng cho đúng buổi phỏng vấn được mời.");

        var interview = await _dbContext.Interviews
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Panelists)
            .FirstOrDefaultAsync(i => i.Id == interviewId && !i.IsDeleted, cancellationToken);

        if (interview == null)
            return (false, "Không tìm thấy buổi phỏng vấn yêu cầu.");

        if (interview.Status != InterviewStatus.SCHEDULED)
            return (false, "Buổi phỏng vấn này hiện không ở trạng thái có thể yêu cầu đổi lịch.");

        var candName = $"{interview.Application?.Candidate?.FirstName} {interview.Application?.Candidate?.LastName}".Trim();

        var noteContent = $"Yêu cầu đổi lịch từ ứng viên: {model.Reason.Trim()}";
        if (!string.IsNullOrWhiteSpace(model.ProposedTime))
        {
            noteContent += $" | Khung giờ đề xuất: {model.ProposedTime.Trim()}";
        }

        interview.CandidateConfirmed = CandidateConfirmStatus.DECLINED;
        interview.CandidateNotes = noteContent;
        interview.UpdatedAt = DateTimeOffset.UtcNow;

        var candUserId = interview.Application?.Candidate?.UserId ?? (interview.Panelists.FirstOrDefault()?.InterviewerId ?? Guid.Empty);
        var currentStageId = interview.Application?.CurrentStageId ?? Guid.Empty;

        if (currentStageId != Guid.Empty && candUserId != Guid.Empty)
        {
            _dbContext.ApplicationStageHistories.Add(new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = interview.ApplicationId,
                FromStageId = currentStageId,
                ToStageId = currentStageId,
                ChangedByUserId = candUserId,
                Comment = $"Ứng viên {candName} đề nghị đổi lịch phỏng vấn \"{interview.Title}\". Lý do: {model.Reason.Trim()}.",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        if (interview.Application != null)
        {
            _dbContext.CandidateInteractions.Add(new CandidateInteraction
            {
                Id = Guid.NewGuid(),
                CandidateId = interview.Application.CandidateId,
                ApplicationId = interview.ApplicationId,
                CreatedByUserId = candUserId != Guid.Empty ? candUserId : Guid.NewGuid(),
                InteractionType = CandidateInteractionType.MEETING,
                Title = "Ứng viên yêu cầu đổi lịch phỏng vấn",
                Content = noteContent,
                InteractionDate = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Ứng viên yêu cầu đổi lịch phỏng vấn {InterviewId}. Lý do: {Reason}", interview.Id, model.Reason);

        return (true, "Yêu cầu đổi lịch phỏng vấn của bạn đã được ghi nhận. Chuyên viên tuyển dụng NoveraTech sẽ liên hệ lại với bạn trong 24 giờ làm việc.");
    }

    /// <summary>
    /// Xây dựng URL xác nhận cho email.
    /// </summary>
    public string BuildConfirmationUrl(HttpRequest request, Guid interviewId, string candidateEmail)
    {
        var scheme = request.Scheme ?? "https";
        var host = request.Host.Value;
        return BuildConfirmationUrl(interviewId, candidateEmail, $"{scheme}://{host}");
    }

    /// <summary>
    /// Xây dựng URL xác nhận dựa trên baseUrl.
    /// </summary>
    public string BuildConfirmationUrl(Guid interviewId, string candidateEmail, string baseUrl)
    {
        var token = GenerateInvitationToken(interviewId, candidateEmail);
        var cleanBase = (baseUrl ?? string.Empty).TrimEnd('/');
        return $"{cleanBase}/xac-nhan-phong-van/{token}";
    }

    private static string BuildPreparationGuide(Interview interview)
    {
        var sb = new StringBuilder();

        // 1. Ghi chú cụ thể từ hội đồng nếu có
        if (!string.IsNullOrWhiteSpace(interview.CandidateNotes) &&
            !interview.CandidateNotes.StartsWith("Yêu cầu đổi lịch"))
        {
            sb.AppendLine($"• Lời nhắn từ Nhà tuyển dụng: {interview.CandidateNotes.Trim()}\n");
        }

        // 2. Hướng dẫn theo hình thức phỏng vấn
        if (interview.InterviewType == InterviewType.ONLINE_MEET)
        {
            sb.AppendLine("• Đường truyền & Thiết bị: Vui lòng kiểm tra camera, micro và kết nối internet ổn định trước giờ hẹn 5 - 10 phút.");
            sb.AppendLine("• Không gian: Chọn không gian yên tĩnh, đủ ánh sáng và hạn chế tiếng ồn xung quanh.");
            sb.AppendLine("• Tài liệu demo: Chuẩn bị sẵn CV, Portfolio hoặc source code dự án tiêu biểu để sẵn sàng chia sẻ màn hình khi trao đổi chuyên môn.");
            sb.AppendLine("• Tham gia phòng họp: Nhấn vào liên kết Google Meet trước thời gian bắt đầu ít nhất 5 phút.");
        }
        else
        {
            sb.AppendLine("• Thời gian có mặt: Vui lòng có mặt tại địa điểm trước giờ hẹn 10 - 15 phút.");
            sb.AppendLine("• Giấy tờ: Mang theo CCCD/CMND khi làm thủ tục vào toà nhà tại quầy lễ tân NoveraTech.");
            sb.AppendLine("• Trang phục: Trang phục lịch sự, chuyên nghiệp.");
            sb.AppendLine("• Tài liệu: Mang theo bản in CV hoặc thiết bị cá nhân (Laptop) nếu cần trình chiếu dự án.");
        }

        return sb.ToString().TrimEnd();
    }
}
