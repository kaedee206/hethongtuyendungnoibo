using Ats.Web.Models.Enums;

namespace Ats.Web.Models.DTOs.Notifications;

/// <summary>
/// DTO thông điệp được đẩy vào hàng đợi bộ nhớ bất đồng bộ (Channel).
/// </summary>
public record NotificationMessage(
    Guid RecipientUserId,
    string Title,
    string Content,
    string? TargetUrl,
    NotificationType Type,
    DateTimeOffset? CreatedAt = null
);

/// <summary>
/// DTO trả về cho giao diện hiển thị danh sách thông báo.
/// </summary>
public class InAppNotificationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? TargetUrl { get; set; }
    public string? Link => TargetUrl;
    public NotificationType Type { get; set; }
    public string TypeName => Type.ToString();
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string TimeAgo { get; set; } = string.Empty;
    public string TimeDisplay { get; set; } = string.Empty;
}

/// <summary>
/// DTO tóm tắt số lượng thông báo chưa đọc và danh sách gần nhất.
/// </summary>
public class NotificationSummaryDto
{
    public bool Success { get; set; } = true;
    public int UnreadCount { get; set; }
    public List<InAppNotificationDto> Items { get; set; } = new();
}
