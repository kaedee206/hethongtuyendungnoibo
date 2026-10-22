using Ats.Web.Models.DTOs;
using Ats.Web.Models.Enums;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Dịch vụ quản lý tùy chọn bật/tắt nhận thông báo cho nhân sự nội bộ (SCRUM-349).
/// </summary>
public interface INotificationPreferenceService
{
    /// <summary>
    /// Lấy danh sách cấu hình thông báo của người dùng kèm các giá trị mặc định và trạng thái bắt buộc.
    /// </summary>
    Task<List<NotificationPreferenceItemDto>> GetUserPreferencesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật trạng thái bật/tắt của một kênh thông báo đơn lẻ (In-App hoặc Email).
    /// </summary>
    Task<(bool Success, string Message)> UpdateSinglePreferenceAsync(Guid userId, UpdateSinglePreferenceRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật hàng loạt tất cả các tùy chọn thông báo.
    /// </summary>
    Task<(bool Success, string Message)> UpdateBatchPreferencesAsync(Guid userId, BatchUpdatePreferencesRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra người dùng có bật nhận thông báo In-App cho sự kiện này không.
    /// </summary>
    Task<bool> ShouldSendInAppAsync(Guid userId, NotificationType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra người dùng có bật nhận email thông báo cho sự kiện này không.
    /// </summary>
    Task<bool> ShouldSendEmailAsync(Guid userId, NotificationType type, CancellationToken cancellationToken = default);
}
