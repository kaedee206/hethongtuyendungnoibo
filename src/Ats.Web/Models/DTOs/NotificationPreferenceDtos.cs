using Ats.Web.Models.Enums;

namespace Ats.Web.Models.DTOs;

/// <summary>
/// DTO hiển thị từng loại cấu hình thông báo cá nhân cho nhân sự (SCRUM-349).
/// </summary>
public class NotificationPreferenceItemDto
{
    public NotificationType NotificationType { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string TypeDescription { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string IconClass { get; set; } = "bi-bell";
    public bool InAppEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;

    /// <summary>
    /// Các thông báo tuân thủ (ví dụ: Yêu cầu phê duyệt) có IsMandatory = true, không thể bị tắt bởi người dùng.
    /// </summary>
    public bool IsMandatory { get; set; } = false;
}

/// <summary>
/// DTO yêu cầu cập nhật một switch kênh thông báo qua AJAX.
/// </summary>
public class UpdateSinglePreferenceRequestDto
{
    public NotificationType NotificationType { get; set; }
    public string Channel { get; set; } = string.Empty; // "inapp" | "email"
    public bool Enabled { get; set; }
}

/// <summary>
/// DTO yêu cầu cập nhật hàng loạt danh sách tùy chọn thông báo.
/// </summary>
public class BatchUpdatePreferencesRequestDto
{
    public List<PreferenceUpdateItemDto> Preferences { get; set; } = new();
}

public class PreferenceUpdateItemDto
{
    public NotificationType NotificationType { get; set; }
    public bool InAppEnabled { get; set; }
    public bool EmailEnabled { get; set; }
}

public class NotificationPreferenceResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<NotificationPreferenceItemDto>? Preferences { get; set; }
}
