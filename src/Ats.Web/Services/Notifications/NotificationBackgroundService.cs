using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.DTOs.Notifications;

namespace Ats.Web.Services.Notifications;

/// <summary>
/// BackgroundService tiêu thụ thông báo từ Channel và lưu trữ vào Cơ sở dữ liệu bất đồng bộ (SP-8).
/// Đảm bảo tính non-blocking hoàn toàn cho các transaction nghiệp vụ chính.
/// </summary>
public class NotificationBackgroundService : BackgroundService
{
    private readonly INotificationChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationBackgroundService> _logger;

    public NotificationBackgroundService(
        INotificationChannel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationBackgroundService> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationBackgroundService đã khởi động và sẵn sàng xử lý hàng đợi.");

        try
        {
            await foreach (var message in _channel.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessNotificationAsync(message, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi xảy ra khi lưu thông báo nền cho RecipientUserId={UserId}, Title='{Title}'",
                        message.RecipientUserId, message.Title);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định trong vòng lặp NotificationBackgroundService.");
        }

        _logger.LogInformation("NotificationBackgroundService đang dừng.");
    }

    public async Task ProcessNotificationAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var notification = new InAppNotification
        {
            Id = Guid.NewGuid(),
            RecipientUserId = message.RecipientUserId,
            Title = message.Title,
            Content = message.Content,
            TargetUrl = message.TargetUrl,
            Type = message.Type,
            IsRead = false,
            CreatedAt = message.CreatedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await dbContext.InAppNotifications.AddAsync(notification, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
