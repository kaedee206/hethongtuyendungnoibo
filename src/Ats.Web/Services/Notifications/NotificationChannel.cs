using System.Threading.Channels;
using Ats.Web.Models.DTOs.Notifications;

namespace Ats.Web.Services.Notifications;

/// <summary>
/// Triển khai INotificationChannel sử dụng System.Threading.Channels.Channel (SP-8).
/// Đảm bảo thread-safe, non-blocking và hiệu năng cao.
/// </summary>
public class NotificationChannel : INotificationChannel
{
    private readonly Channel<NotificationMessage> _channel;
    private readonly ILogger<NotificationChannel> _logger;

    public NotificationChannel(ILogger<NotificationChannel> logger)
    {
        _logger = logger;
        // Bounded channel tối đa 10,000 phần tử, nếu đầy thì DropWrite để không bao giờ treo thread nghiệp vụ
        var options = new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<NotificationMessage>(options);
    }

    public bool TryWrite(NotificationMessage message)
    {
        var written = _channel.Writer.TryWrite(message);
        if (!written)
        {
            _logger.LogWarning("Notification channel đã đầy, không thể ghi thông báo cho User: {UserId}, Title: {Title}",
                message.RecipientUserId, message.Title);
        }
        return written;
    }

    public IAsyncEnumerable<NotificationMessage> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
