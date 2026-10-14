using Ats.Web.Services.Interfaces;

namespace Ats.Web.Services;

/// <summary>
/// Hosted Background Service chạy tác vụ định kỳ tự động quét và ẩn tin tuyển dụng hết hạn (Scrum-38).
/// Sử dụng C# 12 Primary Constructor và xử lý vòng lặp có khả năng phục hồi.
/// </summary>
public class JobExpirationBackgroundService(
    IJobExpirationService expirationService,
    ILogger<JobExpirationBackgroundService> logger) : BackgroundService
{
    private readonly IJobExpirationService _expirationService = expirationService;
    private readonly ILogger<JobExpirationBackgroundService> _logger = logger;

    // Khoảng thời gian quét định kỳ: 1 giờ/lần (đảm bảo phản ứng kịp thời ngay trong ngày hết hạn)
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobExpirationBackgroundService đã khởi động. Sẽ quét tin hết hạn định kỳ mỗi {Interval} phút.", _checkInterval.TotalMinutes);

        // Chờ 8 giây sau khi ứng dụng khởi động và migrate DB hoàn tất
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Bắt đầu tác vụ quét tin tuyển dụng quá hạn tự động...");
                var result = await _expirationService.ProcessExpiredJobPostingsAsync(stoppingToken);

                if (result.Success && result.ExpiredCount > 0)
                {
                    _logger.LogInformation(
                        "Quét tự động hoàn tất: {ExpiredCount}/{ScannedCount} tin đã chuyển sang trạng thái EXPIRED ({Codes})",
                        result.ExpiredCount,
                        result.ScannedCount,
                        string.Join(", ", result.ExpiredJobCodes));
                }
                else
                {
                    _logger.LogDebug("Quét tự động hoàn tất: Không có tin nào quá hạn (Tổng quét: {ScannedCount})", result.ScannedCount);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong quá trình quét tin tuyển dụng quá hạn tự động.");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("JobExpirationBackgroundService đang dừng.");
    }
}
