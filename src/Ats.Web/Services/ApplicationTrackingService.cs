using System.Security.Cryptography;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Tracking;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

/// <summary>
/// Service triển khai tra cứu hồ sơ ứng tuyển theo mã tra cứu và email (Scrum #35)
/// Tuân thủ quy tắc bảo mật: Không tiết lộ nhận xét nội bộ, điểm đánh giá hay tên người phỏng vấn.
/// </summary>
public class ApplicationTrackingService(
    ApplicationDbContext dbContext,
    ILogger<ApplicationTrackingService> logger) : IApplicationTrackingService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<ApplicationTrackingService> _logger = logger;

    public async Task<ApplicationTrackingResultViewModel> TrackApplicationAsync(
        ApplicationTrackingInputModel input, 
        CancellationToken cancellationToken = default)
    {
        var rawCode = input.TrackingCode?.Trim() ?? string.Empty;
        var cleanCode = rawCode.TrimStart('#').Trim().ToUpperInvariant();
        var cleanCodeNoHyphen = cleanCode.Replace("-", string.Empty);
        var cleanEmail = input.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(cleanCode) || string.IsNullOrWhiteSpace(cleanEmail))
        {
            return new ApplicationTrackingResultViewModel
            {
                IsFound = false,
                SearchTrackingCode = rawCode,
                SearchEmail = input.Email,
                ErrorMessage = "Vui lòng nhập đầy đủ Mã tra cứu và Địa chỉ Email để tiến hành kiểm tra hồ sơ."
            };
        }

        // Truy vấn hồ sơ với bảo mật AsNoTracking và so khớp cả 2 yếu tố
        var application = await _dbContext.Applications
            .AsNoTracking()
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
                .ThenInclude(j => j.Requisition)
                    .ThenInclude(r => r.Department)
            .Include(a => a.CurrentStage)
            .Include(a => a.Interviews)
            .Where(a => !a.IsDeleted && a.Candidate.Email.ToLower() == cleanEmail)
            .Where(a => (a.TrackingCode != null && a.TrackingCode.ToUpper() == cleanCode)
                     || (a.TrackingCode != null && a.TrackingCode.ToUpper().Replace("-", "") == cleanCodeNoHyphen)
                     || a.Id.ToString() == cleanCode
                     || a.Id.ToString("N").ToUpper().StartsWith(cleanCodeNoHyphen))
            .OrderByDescending(a => a.AppliedAt ?? a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (application == null)
        {
            _logger.LogWarning("Tra cứu hồ sơ không thành công. Mã: {TrackingCode}, Email: {Email}", rawCode, cleanEmail);
            return new ApplicationTrackingResultViewModel
            {
                IsFound = false,
                SearchTrackingCode = rawCode,
                SearchEmail = input.Email,
                ErrorMessage = "Không tìm thấy hồ sơ ứng tuyển khớp với thông tin mã tra cứu và email bạn đã cung cấp. Vui lòng kiểm tra lại chính xác mã và địa chỉ email đã dùng khi nộp đơn."
            };
        }

        var publicStatus = MapToPublicStatus(application);
        var statusDescription = GetStatusDescription(publicStatus, application);
        var stepperSteps = BuildStepperSteps(publicStatus, application);

        var effectiveTrackingCode = !string.IsNullOrWhiteSpace(application.TrackingCode)
            ? application.TrackingCode
            : $"NOV-APP-{application.CreatedAt:yyyyMMdd}-{application.Id.ToString("N")[..4].ToUpper()}";

        var candidateDisplayName = !string.IsNullOrWhiteSpace(application.Candidate.FirstName)
            ? application.Candidate.FirstName
            : "Ứng viên";

        var maskedEmail = MaskEmail(application.Candidate.Email);

        var employmentTypeName = application.JobPosting?.EmploymentType switch
        {
            EmploymentType.FULL_TIME => "Toàn thời gian",
            EmploymentType.PART_TIME => "Bán thời gian",
            EmploymentType.INTERN => "Thực tập sinh",
            EmploymentType.CONTRACT => "Hợp đồng",
            _ => "Toàn thời gian"
        };

        return new ApplicationTrackingResultViewModel
        {
            IsFound = true,
            SearchTrackingCode = rawCode,
            SearchEmail = input.Email,
            TrackingCode = effectiveTrackingCode,
            CandidateDisplayName = candidateDisplayName,
            MaskedEmail = maskedEmail,
            JobTitle = application.JobPosting?.Title ?? "Vị trí tuyển dụng",
            DepartmentName = application.JobPosting?.Requisition?.Department?.Name ?? "Khối Công nghệ & Sản phẩm",
            WorkLocation = application.JobPosting?.WorkLocation ?? "Hà Nội / Linh hoạt",
            EmploymentType = employmentTypeName,
            AppliedAt = application.AppliedAt ?? application.CreatedAt,
            LastUpdatedAt = application.UpdatedAt,
            Status = publicStatus,
            StatusDescription = statusDescription,
            StepperSteps = stepperSteps
        };
    }

    public async Task<string> GenerateUniqueTrackingCodeAsync(CancellationToken cancellationToken = default)
    {
        var todayPrefix = $"NOV-APP-{DateTime.UtcNow:yyyyMMdd}";
        for (int i = 0; i < 10; i++)
        {
            var randomHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(2)); // 4 ký tự hex viết hoa (vd: 8F2A)
            var candidateCode = $"{todayPrefix}-{randomHex}";
            var exists = await _dbContext.Applications.AnyAsync(a => a.TrackingCode == candidateCode, cancellationToken);
            if (!exists)
            {
                return candidateCode;
            }
        }
        return $"{todayPrefix}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}";
    }

    /// <summary>
    /// Ánh xạ trạng thái nội bộ sang 4 mức trạng thái tổng quát (Scrum #35)
    /// </summary>
    private static PublicApplicationStatus MapToPublicStatus(Application app)
    {
        // 1. Nếu trạng thái kết thúc (Hired, Rejected, Withdrawn)
        if (app.Status == ApplicationStatus.HIRED ||
            app.Status == ApplicationStatus.REJECTED ||
            app.Status == ApplicationStatus.WITHDRAWN)
        {
            return PublicApplicationStatus.Concluded;
        }

        var stageOrder = app.CurrentStage?.StageOrder ?? 1;
        var stageName = app.CurrentStage?.Name?.ToLowerInvariant() ?? string.Empty;

        // Stage 6 (Tuyển dụng thành công) hoặc Stage 7 (Từ chối)
        if (stageOrder >= 6 || stageName.Contains("thành công") || stageName.Contains("từ chối"))
        {
            return PublicApplicationStatus.Concluded;
        }

        // Stage 3, 4 hoặc có lịch phỏng vấn
        if (stageOrder is 3 or 4 || stageName.Contains("phỏng vấn") || (app.Interviews != null && app.Interviews.Any()))
        {
            return PublicApplicationStatus.InterviewInvited;
        }

        // Stage 2 (Sàng lọc CV) hoặc Stage 5 (Offer đang xử lý)
        if (stageOrder is 2 or 5 || stageName.Contains("sàng lọc") || stageName.Contains("offer"))
        {
            return PublicApplicationStatus.UnderReview;
        }

        // Mặc định cho Stage 1: Ứng tuyển mới
        return PublicApplicationStatus.Received;
    }

    /// <summary>
    /// Mô tả lịch sự, chuyên nghiệp theo từng trạng thái mà không để lộ dữ liệu nội bộ
    /// </summary>
    private static string GetStatusDescription(PublicApplicationStatus status, Application app)
    {
        return status switch
        {
            PublicApplicationStatus.Received =>
                "Hồ sơ của bạn đã được ghi nhận thành công trên hệ thống tuyển dụng NoveraTech và đang trong hàng đợi tiếp nhận xử lý của phòng nhân sự.",

            PublicApplicationStatus.UnderReview =>
                "Hồ sơ của bạn đang được hội đồng chuyên môn xem xét và đánh giá độ phù hợp với tiêu chuẩn của vị trí tuyển dụng.",

            PublicApplicationStatus.InterviewInvited =>
                "Hồ sơ của bạn đã vượt qua vòng sơ loại. Bộ phận tuyển dụng đã hoặc đang liên hệ bạn để sắp xếp lịch trao đổi/phỏng vấn. Vui lòng kiểm tra email thường xuyên.",

            PublicApplicationStatus.Concluded =>
                app.Status == ApplicationStatus.HIRED || (app.CurrentStage?.StageOrder == 6)
                    ? "Quy trình tuyển dụng cho vị trí này đã hoàn tất thành công. Chúc mừng bạn đã hoàn thành xuất sắc các giai đoạn tuyển dụng tại NoveraTech!"
                    : "Đợt tuyển dụng cho vị trí này đã hoàn tất. Cảm ơn bạn đã quan tâm và dành thời gian ứng tuyển vào NoveraTech. Hồ sơ của bạn đã được lưu trữ trong hệ thống nhân tài để chúng tôi liên hệ khi có cơ hội phù hợp hơn trong tương lai.",

            _ => "Hồ sơ của bạn đang được tiến hành xử lý theo đúng quy trình tuyển dụng chuẩn của NoveraTech."
        };
    }

    /// <summary>
    /// Xây dựng thanh tiến trình 4 giai đoạn chuẩn hóa
    /// </summary>
    private static List<TrackingStageStepViewModel> BuildStepperSteps(PublicApplicationStatus status, Application app)
    {
        var appliedDate = (app.AppliedAt ?? app.CreatedAt).ToString("dd/MM/yyyy");
        var updatedDate = app.UpdatedAt.ToString("dd/MM/yyyy");

        return status switch
        {
            PublicApplicationStatus.Received =>
            [
                new() { StepIndex = 1, Title = "Đã nhận", Subtitle = "Tiếp nhận hồ sơ", State = "active", DateDisplay = appliedDate },
                new() { StepIndex = 2, Title = "Đang xem xét", Subtitle = "Sàng lọc hồ sơ", State = "upcoming" },
                new() { StepIndex = 3, Title = "Mời phỏng vấn", Subtitle = "Vòng phỏng vấn", State = "upcoming" },
                new() { StepIndex = 4, Title = "Kết thúc", Subtitle = "Hoàn tất quy trình", State = "upcoming" }
            ],

            PublicApplicationStatus.UnderReview =>
            [
                new() { StepIndex = 1, Title = "Đã nhận", Subtitle = "Tiếp nhận hồ sơ", State = "completed", DateDisplay = appliedDate },
                new() { StepIndex = 2, Title = "Đang xem xét", Subtitle = "Đang đánh giá chuyên môn", State = "active", DateDisplay = updatedDate },
                new() { StepIndex = 3, Title = "Mời phỏng vấn", Subtitle = "Vòng phỏng vấn", State = "upcoming" },
                new() { StepIndex = 4, Title = "Kết thúc", Subtitle = "Hoàn tất quy trình", State = "upcoming" }
            ],

            PublicApplicationStatus.InterviewInvited =>
            [
                new() { StepIndex = 1, Title = "Đã nhận", Subtitle = "Tiếp nhận hồ sơ", State = "completed", DateDisplay = appliedDate },
                new() { StepIndex = 2, Title = "Đang xem xét", Subtitle = "Đã đạt sàng lọc", State = "completed" },
                new() { StepIndex = 3, Title = "Mời phỏng vấn", Subtitle = "Giai đoạn phỏng vấn", State = "active", DateDisplay = updatedDate },
                new() { StepIndex = 4, Title = "Kết thúc", Subtitle = "Hoàn tất quy trình", State = "upcoming" }
            ],

            PublicApplicationStatus.Concluded =>
            [
                new() { StepIndex = 1, Title = "Đã nhận", Subtitle = "Tiếp nhận hồ sơ", State = "completed", DateDisplay = appliedDate },
                new() { StepIndex = 2, Title = "Đang xem xét", Subtitle = "Đã xem xét", State = "completed" },
                new() { StepIndex = 3, Title = "Mời phỏng vấn", Subtitle = "Đã hoàn thành", State = "completed" },
                new() { StepIndex = 4, Title = "Kết thúc", Subtitle = "Đã hoàn tất", State = "completed", DateDisplay = updatedDate }
            ],

            _ =>
            [
                new() { StepIndex = 1, Title = "Đã nhận", Subtitle = "Tiếp nhận hồ sơ", State = "completed", DateDisplay = appliedDate },
                new() { StepIndex = 2, Title = "Đang xem xét", Subtitle = "Đang xử lý", State = "active" },
                new() { StepIndex = 3, Title = "Mời phỏng vấn", Subtitle = "Vòng phỏng vấn", State = "upcoming" },
                new() { StepIndex = 4, Title = "Kết thúc", Subtitle = "Hoàn tất quy trình", State = "upcoming" }
            ]
        };
    }

    /// <summary>
    /// Che mờ email (Masking) bảo vệ quyền riêng tư người dùng
    /// Ví dụ: tuan.nguyen@gmail.com -> t***n@gmail.com
    /// </summary>
    private static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return email;
        }

        var parts = email.Split('@');
        var username = parts[0];
        var domain = parts[1];

        if (username.Length <= 2)
        {
            return $"{username[0]}*@{domain}";
        }

        return $"{username[0]}***{username[^1]}@{domain}";
    }
}
