namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao tiếp hàng đợi email trong bộ nhớ sử dụng System.Threading.Channels (SP-8).
/// Đảm bảo email gửi đi được xử lý bất đồng bộ không làm chậm HTTP request.
/// </summary>
public interface IEmailQueueChannel
{
    /// <summary>
    /// Đẩy mã EmailLog vào hàng đợi xử lý ngầm.
    /// </summary>
    ValueTask EnqueueAsync(Guid emailLogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc một mã EmailLog từ hàng đợi (chờ nếu chưa có).
    /// </summary>
    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc các mã EmailLog từ hàng đợi theo dạng stream bất đồng bộ.
    /// </summary>
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken = default);
}
