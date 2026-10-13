using Ats.Web.Services.Interfaces;

namespace Ats.Web.Services;

/// <summary>
/// Tác vụ định kỳ chạy nền mỗi ngày một lần để quét và nhắc việc hồ sơ quá hạn SLA cho Recruiter (SCRUM-327).
/// </summary>
public class SlaOverdueBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlaOverdueBackgroundService> _logger;
    // Chu kỳ quét mặc định: 24 giờ
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

    public SlaOverdueBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SlaOverdueBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Tác vụ định kỳ quét SLA ứng viên (SCRUM-327) đã khởi động.");

        // Chờ 30 giây sau khi ứng dụng khởi động để database và schema ổn định
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Bắt đầu chu kỳ quét kiểm tra quá hạn SLA cho các hồ sơ ứng viên...");
                using var scope = _scopeFactory.CreateScope();
                var slaService = scope.ServiceProvider.GetRequiredService<ISlaService>();

                var scanResult = await slaService.ScanAndAlertOverdueApplicationsAsync(stoppingToken);

                _logger.LogInformation("Kết quả quét SLA định kỳ: {Overdue} hồ sơ quá hạn, {Notif} thông báo nhắc việc đã gửi.",
                    scanResult.OverdueCount, scanResult.NotificationsCreated);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong quá trình chạy tác vụ quét định kỳ SLA: {Message}", ex.Message);
            }

            // Chờ đến chu kỳ quét tiếp theo (24 giờ)
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Tác vụ định kỳ quét SLA ứng viên đã dừng.");
    }
}
