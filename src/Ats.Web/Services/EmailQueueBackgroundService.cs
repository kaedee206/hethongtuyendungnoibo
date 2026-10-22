using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Services.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace Ats.Web.Services;

/// <summary>
/// BackgroundService xử lý hàng đợi gửi email bất đồng bộ (SP-8).
/// Chịu trách nhiệm tiêu thụ các EmailLog từ IEmailQueueChannel, gửi qua SMTP,
/// tự động thử lại tối đa 3 lần, và đảm bảo không mất email khi ứng dụng khởi động lại.
/// </summary>
public class EmailQueueBackgroundService : BackgroundService
{
    private readonly IEmailQueueChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailQueueBackgroundService> _logger;

    public EmailQueueBackgroundService(
        IEmailQueueChannel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailQueueBackgroundService> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Khởi động EmailQueueBackgroundService...");

        // Bước 1: Phục hồi các email còn đang chờ (QUEUED / PENDING) từ DB khi ứng dụng khởi động lại
        await RecoverPendingEmailsAsync(stoppingToken);

        // Bước 2: Vòng lặp tiêu thụ từ hàng đợi Channel
        try
        {
            await foreach (var emailLogId in _channel.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessEmailLogAsync(emailLogId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi bất thường khi xử lý email log ID {EmailLogId}", emailLogId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Worker đang dừng bình thường
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Lỗi nghiêm trọng trong vòng lặp EmailQueueBackgroundService");
        }

        _logger.LogInformation("EmailQueueBackgroundService đã dừng.");
    }

    /// <summary>
    /// Nạp lại các email đang chờ trong database vào channel để không bị thất lạc khi server restart.
    /// </summary>
    private async Task RecoverPendingEmailsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var pendingLogs = await db.EmailLogs
                .Where(e => (e.Status == "QUEUED" || e.Status == "PENDING") && e.RetryCount < e.MaxRetries)
                .OrderBy(e => e.CreatedAt)
                .Select(e => e.Id)
                .Take(200)
                .ToListAsync(cancellationToken);

            if (pendingLogs.Count > 0)
            {
                _logger.LogInformation("Phục hồi {Count} email đang chờ từ CSDL vào hàng đợi xử lý", pendingLogs.Count);
                foreach (var id in pendingLogs)
                {
                    await _channel.EnqueueAsync(id, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể phục hồi các email đang chờ từ CSDL lúc khởi động");
        }
    }

    /// <summary>
    /// Xử lý gửi một EmailLog, quản lý retry và cập nhật trạng thái vào database.
    /// </summary>
    private async Task ProcessEmailLogAsync(Guid emailLogId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var log = await db.EmailLogs.FirstOrDefaultAsync(e => e.Id == emailLogId, cancellationToken);
        if (log == null)
        {
            _logger.LogWarning("Không tìm thấy EmailLog với ID {EmailLogId}", emailLogId);
            return;
        }

        // Bỏ qua nếu email đã gửi thành công trước đó (idempotent)
        if (log.Status == "SENT")
        {
            return;
        }

        var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.gmail.com";
        var smtpPortStr = Environment.GetEnvironmentVariable("SMTP_PORT") ?? "587";
        var smtpPort = int.TryParse(smtpPortStr, out var port) ? port : 587;
        var smtpEmail = Environment.GetEnvironmentVariable("SMTP_EMAIL") ?? "";
        var smtpPassword = Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? "";

        // Kiểm tra cấu hình SMTP
        if (string.IsNullOrWhiteSpace(smtpEmail) || string.IsNullOrWhiteSpace(smtpPassword))
        {
            log.Status = "FAILED";
            log.ErrorMessage = "Chưa cấu hình tài khoản SMTP gửi mail.";
            log.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Bỏ qua gửi email do chưa cấu hình SMTP: {Subject} -> {ToEmail}", log.Subject, log.RecipientEmail);
            return;
        }

        bool sentSuccess = false;
        string? lastError = null;

        // Cơ chế thử lại tối đa MaxRetries lần
        while (log.RetryCount < log.MaxRetries && !sentSuccess && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var email = new MimeMessage();
                email.From.Add(new MailboxAddress("NoveraTech Careers & ATS", smtpEmail));
                email.To.Add(MailboxAddress.Parse(log.RecipientEmail));
                email.Subject = log.Subject;

                var builder = new BodyBuilder { HtmlBody = log.HtmlBody ?? string.Empty };
                email.Body = builder.ToMessageBody();

                using var smtp = new SmtpClient();
                smtp.Timeout = 5000;
                await smtp.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls, cancellationToken);
                await smtp.AuthenticateAsync(smtpEmail, smtpPassword, cancellationToken);
                await smtp.SendAsync(email, cancellationToken);
                await smtp.DisconnectAsync(true, cancellationToken);

                sentSuccess = true;
                _logger.LogInformation("Đã gửi email thành công ngầm tới {ToEmail} (Tiêu đề: {Subject}, Lần thử: {Retry})",
                    log.RecipientEmail, log.Subject, log.RetryCount + 1);
            }
            catch (Exception ex)
            {
                log.RetryCount++;
                lastError = ex.Message;
                _logger.LogWarning(ex, "Lỗi gửi email tới {ToEmail} (Lần thử {Retry}/{Max}): {Message}",
                    log.RecipientEmail, log.RetryCount, log.MaxRetries, ex.Message);

                if (log.RetryCount < log.MaxRetries)
                {
                    // Delay nhẹ trước khi thử lại (exponential backoff 1s, 2s...)
                    var delaySeconds = Math.Min(Math.Pow(2, log.RetryCount - 1), 5);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }
            }
        }

        // Cập nhật trạng thái cuối cùng
        if (sentSuccess)
        {
            log.Status = "SENT";
            log.SentAt = DateTimeOffset.UtcNow;
            log.ErrorMessage = null;
        }
        else
        {
            log.Status = "FAILED";
            log.ErrorMessage = string.IsNullOrWhiteSpace(lastError) ? "Không thể gửi email sau nhiều lần thử." : lastError;
        }

        log.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
