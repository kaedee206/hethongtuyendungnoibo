using Ats.Web.Models.DTOs.Notifications;
using Ats.Web.Models.Enums;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao diện dịch vụ thông báo nội bộ (In-App Notification) (SP-8).
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Đẩy thông báo vào hàng đợi bất đồng bộ non-blocking (O(1)).
    /// </summary>
    Task QueueNotificationAsync(
        Guid recipientUserId,
        string title,
        string content,
        string? targetUrl,
        NotificationType type,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách thông báo gần đây của người dùng kèm định dạng thời gian thân thiện.
    /// </summary>
    Task<List<InAppNotificationDto>> GetRecentNotificationsAsync(
        Guid userId,
        int count = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy số lượng thông báo chưa đọc của người dùng.
    /// </summary>
    Task<int> GetUnreadCountAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đánh dấu một thông báo đã đọc, kiểm tra chặt chẽ quyền sở hữu của người dùng.
    /// Trả về TargetUrl để điều hướng trực tiếp.
    /// </summary>
    Task<(bool Success, string Message, string? TargetUrl)> MarkAsReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đánh dấu toàn bộ thông báo chưa đọc của người dùng là đã đọc.
    /// </summary>
    Task<int> MarkAllAsReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
