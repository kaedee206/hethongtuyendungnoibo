using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Dịch vụ quản lý cấu hình thông báo cá nhân cho nhân sự nội bộ (SCRUM-349).
/// </summary>
public class NotificationPreferenceService(
    ApplicationDbContext dbContext,
    ILogger<NotificationPreferenceService> logger) : INotificationPreferenceService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<NotificationPreferenceService> _logger = logger;

    private record NotificationMeta(
        NotificationType Type,
        string Name,
        string Description,
        string Category,
        string IconClass,
        bool IsMandatory);

    private static readonly List<NotificationMeta> DefinedMetas =
    [
        new(
            NotificationType.APPROVAL_REQUEST,
            "Yêu cầu phê duyệt Offer / Requisition",
            "Thông báo các yêu cầu duyệt đề xuất tuyển dụng hoặc thư mời nhận việc đang chờ bạn xử lý.",
            "Phê duyệt & Tuân thủ",
            "bi-shield-lock-fill",
            IsMandatory: true
        ),
        new(
            NotificationType.NEW_APPLICATION,
            "Nhận hồ sơ ứng tuyển mới",
            "Thông báo tức thì khi có ứng viên mới nộp hồ sơ vào các vị trí bạn phụ trách hoặc theo dõi.",
            "Ứng tuyển & Hồ sơ",
            "bi-person-plus-fill",
            IsMandatory: false
        ),
        new(
            NotificationType.INTERVIEW_SCHEDULED,
            "Lịch phỏng vấn mới / thay đổi",
            "Nhắc lịch phỏng vấn được xếp mới, cập nhật thời gian hoặc thay đổi hội đồng phỏng vấn.",
            "Phỏng vấn & Đánh giá",
            "bi-calendar-event-fill",
            IsMandatory: false
        ),
        new(
            NotificationType.INTERVIEW_EVALUATION_SUBMITTED,
            "Đánh giá phỏng vấn hoàn tất",
            "Thông báo khi thành viên hội đồng hoàn thành và gửi phiếu đánh giá điểm năng lực ứng viên.",
            "Phỏng vấn & Đánh giá",
            "bi-clipboard-check-fill",
            IsMandatory: false
        ),
        new(
            NotificationType.SLA_OVERDUE_WARNING,
            "Cảnh báo SLA hồ sơ tồn đọng / quá hạn",
            "Cảnh báo khi hồ sơ ứng viên ở một giai đoạn vượt quá số ngày cam kết dịch vụ (SLA) quy định.",
            "Vận hành & SLA",
            "bi-hourglass-split",
            IsMandatory: false
        ),
        new(
            NotificationType.STAGE_CHANGED,
            "Trạng thái ứng viên thay đổi",
            "Cập nhật khi hồ sơ ứng viên được chuyển qua giai đoạn tuyển dụng mới hoặc bị loại.",
            "Ứng tuyển & Hồ sơ",
            "bi-arrow-left-right",
            IsMandatory: false
        )
    ];

    public async Task<List<NotificationPreferenceItemDto>> GetUserPreferencesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var existingPrefs = await _dbContext.UserNotificationPreferences
            .Where(p => p.UserId == userId && !p.IsDeleted)
            .ToDictionaryAsync(p => p.NotificationType, cancellationToken);

        var result = new List<NotificationPreferenceItemDto>();

        foreach (var meta in DefinedMetas)
        {
            if (existingPrefs.TryGetValue(meta.Type, out var pref))
            {
                result.Add(new NotificationPreferenceItemDto
                {
                    NotificationType = meta.Type,
                    TypeName = meta.Name,
                    TypeDescription = meta.Description,
                    Category = meta.Category,
                    IconClass = meta.IconClass,
                    InAppEnabled = meta.IsMandatory || pref.InAppEnabled,
                    EmailEnabled = meta.IsMandatory || pref.EmailEnabled,
                    IsMandatory = meta.IsMandatory
                });
            }
            else
            {
                // Mặc định bật cho tất cả các loại khi người dùng chưa cấu hình riêng
                result.Add(new NotificationPreferenceItemDto
                {
                    NotificationType = meta.Type,
                    TypeName = meta.Name,
                    TypeDescription = meta.Description,
                    Category = meta.Category,
                    IconClass = meta.IconClass,
                    InAppEnabled = true,
                    EmailEnabled = true,
                    IsMandatory = meta.IsMandatory
                });
            }
        }

        return result;
    }

    public async Task<(bool Success, string Message)> UpdateSinglePreferenceAsync(
        Guid userId,
        UpdateSinglePreferenceRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var meta = DefinedMetas.FirstOrDefault(m => m.Type == request.NotificationType);
        if (meta == null)
        {
            return (false, "Loại thông báo không hợp lệ.");
        }

        // RÀO CHẮN BẢO MẬT: Không thể tắt thông báo bắt buộc tuân thủ
        if (meta.IsMandatory && !request.Enabled)
        {
            return (false, $"Thông báo '{meta.Name}' mang tính chất tuân thủ bắt buộc trong quy trình tuyển dụng và không thể bị vô hiệu hóa.");
        }

        var pref = await _dbContext.UserNotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.NotificationType == request.NotificationType && !p.IsDeleted, cancellationToken);

        if (pref == null)
        {
            pref = new UserNotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                NotificationType = request.NotificationType,
                InAppEnabled = true,
                EmailEnabled = true,
                IsMandatory = meta.IsMandatory,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.UserNotificationPreferences.Add(pref);
        }

        var channelNormalized = request.Channel?.Trim().ToLowerInvariant();
        if (channelNormalized == "inapp" || channelNormalized == "in_app")
        {
            pref.InAppEnabled = meta.IsMandatory || request.Enabled;
        }
        else if (channelNormalized == "email")
        {
            pref.EmailEnabled = meta.IsMandatory || request.Enabled;
        }
        else
        {
            return (false, "Kênh thông báo không hợp lệ (chỉ hỗ trợ 'inapp' hoặc 'email').");
        }

        pref.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("SCRUM-349: Cập nhật tùy chọn thông báo {Type} kênh {Channel} thành {Enabled} cho User {UserId}",
            request.NotificationType, channelNormalized, request.Enabled, userId);

        return (true, $"Đã cập nhật tùy chọn nhận thông báo cho '{meta.Name}' thành công.");
    }

    public async Task<(bool Success, string Message)> UpdateBatchPreferencesAsync(
        Guid userId,
        BatchUpdatePreferencesRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request?.Preferences == null || request.Preferences.Count == 0)
        {
            return (false, "Không có dữ liệu cấu hình nào được gửi lên.");
        }

        var existingPrefs = await _dbContext.UserNotificationPreferences
            .Where(p => p.UserId == userId && !p.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var item in request.Preferences)
        {
            var meta = DefinedMetas.FirstOrDefault(m => m.Type == item.NotificationType);
            if (meta == null) continue;

            var pref = existingPrefs.FirstOrDefault(p => p.NotificationType == item.NotificationType);
            if (pref == null)
            {
                pref = new UserNotificationPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    NotificationType = item.NotificationType,
                    InAppEnabled = meta.IsMandatory || item.InAppEnabled,
                    EmailEnabled = meta.IsMandatory || item.EmailEnabled,
                    IsMandatory = meta.IsMandatory,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _dbContext.UserNotificationPreferences.Add(pref);
            }
            else
            {
                pref.InAppEnabled = meta.IsMandatory || item.InAppEnabled;
                pref.EmailEnabled = meta.IsMandatory || item.EmailEnabled;
                pref.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("SCRUM-349: Lưu hàng loạt tùy chọn thông báo cho User {UserId}", userId);
        return (true, "Đã lưu toàn bộ cấu hình tùy chọn nhận thông báo thành công.");
    }

    public async Task<bool> ShouldSendInAppAsync(Guid userId, NotificationType type, CancellationToken cancellationToken = default)
    {
        var meta = DefinedMetas.FirstOrDefault(m => m.Type == type);
        if (meta?.IsMandatory == true) return true;

        var pref = await _dbContext.UserNotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.NotificationType == type && !p.IsDeleted, cancellationToken);

        return pref?.InAppEnabled ?? true;
    }

    public async Task<bool> ShouldSendEmailAsync(Guid userId, NotificationType type, CancellationToken cancellationToken = default)
    {
        var meta = DefinedMetas.FirstOrDefault(m => m.Type == type);
        if (meta?.IsMandatory == true) return true;

        var pref = await _dbContext.UserNotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.NotificationType == type && !p.IsDeleted, cancellationToken);

        return pref?.EmailEnabled ?? true;
    }
}
