using System.Threading.Channels;
using Ats.Web.Services.Interfaces;

namespace Ats.Web.Services;

/// <summary>
/// Triển khai hàng đợi xử lý email trong bộ nhớ dựa trên System.Threading.Channels (SP-8).
/// Hoạt động như Singleton, trung gian giữa EmailService và EmailQueueBackgroundService.
/// </summary>
public class EmailQueueChannel : IEmailQueueChannel
{
    private readonly Channel<Guid> _channel;

    public EmailQueueChannel()
    {
        var options = new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateUnbounded<Guid>(options);
    }

    public async ValueTask EnqueueAsync(Guid emailLogId, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(emailLogId, cancellationToken);
    }

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
