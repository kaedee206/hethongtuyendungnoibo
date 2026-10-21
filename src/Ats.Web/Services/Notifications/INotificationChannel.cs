using Ats.Web.Models.DTOs.Notifications;

namespace Ats.Web.Services.Notifications;

/// <summary>
/// Hàng đợi Channel in-memory xử lý phân phối thông báo bất đồng bộ non-blocking (SP-8).
/// </summary>
public interface INotificationChannel
{
    /// <summary>
    /// Đẩy thông điệp vào channel (thao tác O(1), không block luồng gọi).
    /// </summary>
    bool TryWrite(NotificationMessage message);

    /// <summary>
    /// Đọc toàn bộ thông điệp từ channel theo cơ chế bất đồng bộ.
    /// </summary>
    IAsyncEnumerable<NotificationMessage> ReadAllAsync(CancellationToken cancellationToken = default);
}
