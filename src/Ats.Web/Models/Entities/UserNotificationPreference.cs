using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

/// <summary>
/// Thực thể lưu cấu hình bật/tắt nhận thông báo theo từng loại và kênh cho nhân sự (SCRUM-349).
/// </summary>
public class UserNotificationPreference : BaseEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationType NotificationType { get; set; }
    public bool InAppEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;

    /// <summary>
    /// Các thông báo tuân thủ (ví dụ: Yêu cầu phê duyệt) có IsMandatory = true, không thể bị tắt bởi người dùng.
    /// </summary>
    public bool IsMandatory { get; set; } = false;

    public User User { get; set; } = null!;
}
