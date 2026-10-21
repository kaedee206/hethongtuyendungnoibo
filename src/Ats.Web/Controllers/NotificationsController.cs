using System.Security.Claims;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// API Controller phục vụ tương tác In-App Notification (SP-8).
/// Yêu cầu xác thực người dùng, tuân thủ Primary Constructor, Clean Controller và kebab-case REST.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class NotificationsController(
    INotificationService notificationService,
    ILogger<NotificationsController> logger) : ControllerBase
{
    private readonly INotificationService _notificationService = notificationService;
    private readonly ILogger<NotificationsController> _logger = logger;

    private Guid GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// GET /api/notifications/recent?count=10
    /// Lấy danh sách thông báo mới nhất kèm tổng số lượng thông báo chưa đọc.
    /// </summary>
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecentNotifications(
        [FromQuery] int count = 10,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { success = false, message = "Chưa xác thực người dùng." });
        }

        var unreadCount = await _notificationService.GetUnreadCountAsync(userId, cancellationToken);
        var items = await _notificationService.GetRecentNotificationsAsync(userId, count, cancellationToken);

        return Ok(new
        {
            success = true,
            unreadCount,
            items
        });
    }

    /// <summary>
    /// GET /api/notifications/unread-count
    /// Lấy nhanh số lượng thông báo chưa đọc phục vụ định kỳ polling nhẹ.
    /// </summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { success = false, message = "Chưa xác thực người dùng." });
        }

        var unreadCount = await _notificationService.GetUnreadCountAsync(userId, cancellationToken);
        return Ok(new
        {
            success = true,
            unreadCount
        });
    }

    /// <summary>
    /// POST /api/notifications/{id}/read hoặc POST /api/notifications/mark-read/{id}
    /// Đánh dấu một thông báo đã đọc và trả về TargetUrl để điều hướng.
    /// </summary>
    [HttpPost("{id:guid}/read")]
    [HttpPost("mark-read/{id:guid}")]
    public async Task<IActionResult> MarkAsRead(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { success = false, message = "Chưa xác thực người dùng." });
        }

        var (success, message, targetUrl) = await _notificationService.MarkAsReadAsync(id, userId, cancellationToken);
        if (!success)
        {
            if (message.Contains("không có quyền", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message });
            }
            return NotFound(new { success = false, message });
        }

        return Ok(new
        {
            success = true,
            message,
            targetUrl
        });
    }

    /// <summary>
    /// POST /api/notifications/mark-all-read
    /// Đánh dấu tất cả thông báo chưa đọc của người dùng hiện tại là đã đọc.
    /// </summary>
    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { success = false, message = "Chưa xác thực người dùng." });
        }

        var updatedCount = await _notificationService.MarkAllAsReadAsync(userId, cancellationToken);
        return Ok(new
        {
            success = true,
            message = $"Đã đánh dấu {updatedCount} thông báo là đã đọc.",
            updatedCount
        });
    }
}
