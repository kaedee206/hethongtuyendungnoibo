using System.ComponentModel.DataAnnotations.Schema;
using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

/// <summary>
/// Thực thể lưu trữ thông báo nội bộ (In-App Notification) cho cán bộ nhân sự và hội đồng tuyển dụng (User Story SP-8).
/// </summary>
public class InAppNotification : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Mã người dùng nhận thông báo (Foreign Key -> User).
    /// </summary>
    public Guid RecipientUserId { get; set; }

    /// <summary>
    /// Tiêu đề thông báo ngắn gọn (Tối đa 200 ký tự).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Nội dung chi tiết thông báo (Tối đa 1000 ký tự).
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Đường dẫn điều hướng trực tiếp khi người dùng nhấp vào thông báo (Deep Linking, tối đa 500 ký tự).
    /// </summary>
    public string? TargetUrl { get; set; }

    /// <summary>
    /// Phân loại nghiệp vụ thông báo.
    /// </summary>
    public NotificationType Type { get; set; } = NotificationType.System;

    /// <summary>
    /// Trạng thái đã đọc hay chưa.
    /// </summary>
    public bool IsRead { get; set; } = false;

    /// <summary>
    /// Thời điểm người dùng đọc thông báo (UTC).
    /// </summary>
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>
    /// Quan hệ Navigation đến bảng User.
    /// </summary>
    public User? RecipientUser { get; set; }

    #region Backward Compatibility Properties
    [NotMapped]
    public Guid UserId
    {
        get => RecipientUserId;
        set => RecipientUserId = value;
    }

    [NotMapped]
    public string? Link
    {
        get => TargetUrl;
        set => TargetUrl = value;
    }

    [NotMapped]
    public User? User
    {
        get => RecipientUser;
        set => RecipientUser = value;
    }
    #endregion
}
