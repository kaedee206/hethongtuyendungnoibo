using Ats.Web.Data;
using Ats.Web.Models.DTOs.Notifications;
using Ats.Web.Models.Enums;
using Ats.Web.Services.Interfaces;
using Ats.Web.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

/// <summary>
/// Triển khai dịch vụ quản lý thông báo nội bộ (SP-8).
/// </summary>
public class NotificationService(
    ApplicationDbContext dbContext,
    INotificationChannel channel,
    ILogger<NotificationService> logger) : INotificationService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly INotificationChannel _channel = channel;
    private readonly ILogger<NotificationService> _logger = logger;

    public Task QueueNotificationAsync(
        Guid recipientUserId,
        string title,
        string content,
        string? targetUrl,
        NotificationType type,
        CancellationToken cancellationToken = default)
    {
        if (recipientUserId == Guid.Empty || string.IsNullOrWhiteSpace(title))
        {
            _logger.LogWarning("Bỏ qua thông báo không hợp lệ: RecipientUserId={UserId}, Title='{Title}'",
                recipientUserId, title);
            return Task.CompletedTask;
        }

        try
        {
            var message = new NotificationMessage(
                RecipientUserId: recipientUserId,
                Title: title.Trim(),
                Content: content?.Trim() ?? string.Empty,
                TargetUrl: string.IsNullOrWhiteSpace(targetUrl) ? null : targetUrl.Trim(),
                Type: type,
                CreatedAt: DateTimeOffset.UtcNow
            );

            _channel.TryWrite(message);
        }
        catch (Exception ex)
        {
            // Bảo đảm nghiệp vụ chính không bao giờ bị rollback khi lỗi đẩy thông báo
            _logger.LogError(ex, "Lỗi khi đẩy thông báo vào hàng đợi cho UserId={UserId}", recipientUserId);
        }

        return Task.CompletedTask;
    }

    public async Task<List<InAppNotificationDto>> GetRecentNotificationsAsync(
        Guid userId,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return new List<InAppNotificationDto>();
        }

        var validCount = count <= 0 ? 10 : Math.Min(count, 50);

        var notifications = await _dbContext.InAppNotifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == userId && !n.IsDeleted)
            .OrderByDescending(n => n.CreatedAt)
            .Take(validCount)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var vnOffset = TimeSpan.FromHours(7);

        return notifications.Select(n => new InAppNotificationDto
        {
            Id = n.Id,
            Title = n.Title,
            Content = n.Content,
            TargetUrl = n.TargetUrl,
            Type = n.Type,
            IsRead = n.IsRead,
            CreatedAt = n.CreatedAt,
            TimeAgo = FormatRelativeTime(n.CreatedAt, now),
            TimeDisplay = n.CreatedAt.ToOffset(vnOffset).ToString("HH:mm dd/MM")
        }).ToList();
    }

    public async Task<int> GetUnreadCountAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return 0;
        }

        return await _dbContext.InAppNotifications
            .AsNoTracking()
            .CountAsync(n => n.RecipientUserId == userId && !n.IsRead && !n.IsDeleted, cancellationToken);
    }

    public async Task<(bool Success, string Message, string? TargetUrl)> MarkAsReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (notificationId == Guid.Empty || userId == Guid.Empty)
        {
            return (false, "Thông tin định danh không hợp lệ.", null);
        }

        var notification = await _dbContext.InAppNotifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && !n.IsDeleted, cancellationToken);

        if (notification == null)
        {
            return (false, "Không tìm thấy thông báo.", null);
        }

        // Kiểm tra bảo mật: Người dùng A không thể đọc hoặc sửa thông báo của Người dùng B
        if (notification.RecipientUserId != userId)
        {
            _logger.LogWarning("Cảnh báo bảo mật: User {CurrentUserId} cố tình truy cập thông báo của User {OwnerUserId}",
                userId, notification.RecipientUserId);
            return (false, "Bạn không có quyền sửa đổi thông báo của người khác.", null);
        }

        if (!notification.IsRead)
        {
            var now = DateTimeOffset.UtcNow;
            notification.IsRead = true;
            notification.ReadAt = now;
            notification.UpdatedAt = now;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return (true, "Đã đánh dấu thông báo là đã đọc.", notification.TargetUrl);
    }

    public async Task<int> MarkAllAsReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return 0;
        }

        var unreadNotifications = await _dbContext.InAppNotifications
            .Where(n => n.RecipientUserId == userId && !n.IsRead && !n.IsDeleted)
            .ToListAsync(cancellationToken);

        if (unreadNotifications.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var item in unreadNotifications)
        {
            item.IsRead = true;
            item.ReadAt = now;
            item.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return unreadNotifications.Count;
    }

    private static string FormatRelativeTime(DateTimeOffset createdAt, DateTimeOffset now)
    {
        var diff = now - createdAt;
        if (diff.TotalSeconds < 60)
        {
            return "Vừa xong";
        }
        if (diff.TotalMinutes < 60)
        {
            var minutes = (int)Math.Max(1, diff.TotalMinutes);
            return $"{minutes} phút trước";
        }
        if (diff.TotalHours < 24)
        {
            var hours = (int)diff.TotalHours;
            return $"{hours} giờ trước";
        }
        if (diff.TotalDays < 7)
        {
            var days = (int)diff.TotalDays;
            return $"{days} ngày trước";
        }
        return createdAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy");
    }
}
