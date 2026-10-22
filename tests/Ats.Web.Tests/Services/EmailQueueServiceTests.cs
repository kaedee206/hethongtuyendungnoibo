using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Bộ kiểm thử đơn vị cho hệ thống hàng đợi email bất đồng bộ và nhật ký email (User story SP-8):
/// 1. AC1: Hiển thị thời điểm gửi, mẫu email đã dùng, trạng thái gửi thành công hay thất bại.
/// 2. AC2: Gửi lại được thư thất bại (chỉ FAILED, chống gửi trùng / idempotent).
/// 3. AC3: Email được gửi bất đồng bộ qua hàng đợi (Background Channel), không làm chậm thao tác gốc.
/// </summary>
public class EmailQueueServiceTests
{
    private static (EmailService emailService, IServiceProvider serviceProvider, IEmailQueueChannel queueChannel) CreateTestContext()
    {
        var dbName = $"AtsTestDb_{Guid.NewGuid():N}";
        var queueChannel = new EmailQueueChannel();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName)
                   .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var emailService = new EmailService(scopeFactory, queueChannel, NullLogger<EmailService>.Instance);
        return (emailService, serviceProvider, queueChannel);
    }

    [Fact]
    public async Task SendEmailAsync_ShouldSaveQueuedLogAndEnqueueToChannelImmediately_WithoutBlocking()
    {
        // Arrange
        var (emailService, serviceProvider, queueChannel) = CreateTestContext();
        var candidateId = Guid.NewGuid();

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var candidate = new Candidate
            {
                Id = candidateId,
                Email = "ungvien.sp8@ats.test",
                FirstName = "Văn A",
                LastName = "Nguyễn",
                Source = CandidateSource.PORTAL,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await db.Candidates.AddAsync(candidate);
            await db.SaveChangesAsync();
        }

        var request = new SendEmailRequestDto(
            ToEmail: "ungvien.sp8@ats.test",
            Subject: "Thư mời phỏng vấn Vòng 1",
            Body: "<p>Kính mời bạn tham gia phỏng vấn...</p>",
            CandidateId: candidateId,
            EmailType: "Thư mời phỏng vấn"
        );

        // Act
        var result = await emailService.SendEmailAsync(request);

        // Assert
        Assert.True(result, "SendEmailAsync phải trả về true khi đã enqueue thành công.");

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var savedLog = await db.EmailLogs.FirstOrDefaultAsync(e => e.RecipientEmail == "ungvien.sp8@ats.test");
            Assert.NotNull(savedLog);
            Assert.Equal("QUEUED", savedLog.Status);
            Assert.Equal(candidateId, savedLog.CandidateId);
            Assert.Equal("Thư mời phỏng vấn", savedLog.EmailType);
            Assert.Equal("Thư mời phỏng vấn Vòng 1", savedLog.Subject);
            Assert.Equal(0, savedLog.RetryCount);
            Assert.Equal(3, savedLog.MaxRetries);
            Assert.Null(savedLog.SentAt);

            // Kiểm tra Channel đã nhận được ID của email log
            var dequeuedId = await queueChannel.DequeueAsync(CancellationToken.None);
            Assert.Equal(savedLog.Id, dequeuedId);
        }
    }

    [Fact]
    public async Task SendEmailAsync_ShouldAutoDetectCandidateId_WhenCandidateIdNotProvided()
    {
        // Arrange
        var (emailService, serviceProvider, _) = CreateTestContext();
        var candidateId = Guid.NewGuid();

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var candidate = new Candidate
            {
                Id = candidateId,
                Email = "autodetect@ats.test",
                FirstName = "Thị B",
                LastName = "Lê",
                Source = CandidateSource.PORTAL,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await db.Candidates.AddAsync(candidate);
            await db.SaveChangesAsync();
        }

        var request = new SendEmailRequestDto(
            ToEmail: "autodetect@ats.test",
            Subject: "Xác nhận nộp hồ sơ",
            Body: "<p>Hồ sơ của bạn đã được ghi nhận.</p>",
            CandidateId: null, // Không truyền candidateId
            EmailType: "Tiếp nhận hồ sơ"
        );

        // Act
        var result = await emailService.SendEmailAsync(request);

        // Assert
        Assert.True(result);

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var savedLog = await db.EmailLogs.FirstOrDefaultAsync(e => e.RecipientEmail == "autodetect@ats.test");
            Assert.NotNull(savedLog);
            Assert.Equal(candidateId, savedLog.CandidateId); // Tự động liên kết đúng Candidate
        }
    }

    [Fact]
    public async Task ResendEmailAsync_ShouldSucceedAndRequeue_WhenStatusIsFailed()
    {
        // Arrange
        var (emailService, serviceProvider, queueChannel) = CreateTestContext();
        var userId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var logId = Guid.NewGuid();

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var failedLog = new EmailLog
            {
                Id = logId,
                CandidateId = candidateId,
                RecipientEmail = "failed.candidate@ats.test",
                Subject = "Thông báo kết quả phỏng vấn",
                EmailType = "Thông báo kết quả",
                HtmlBody = "<p>Nội dung kết quả...</p>",
                Status = "FAILED",
                ErrorMessage = "SMTP timeout connecting to smtp.gmail.com:587",
                RetryCount = 3,
                MaxRetries = 3,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                SentAt = null
            };
            await db.EmailLogs.AddAsync(failedLog);
            await db.SaveChangesAsync();
        }

        // Act
        var result = await emailService.ResendEmailAsync(logId, userId);

        // Assert
        Assert.True(result);

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var updatedLog = await db.EmailLogs.FindAsync(logId);
            Assert.NotNull(updatedLog);
            Assert.Equal("QUEUED", updatedLog.Status);
            Assert.Equal(userId, updatedLog.TriggeredByUserId);
            Assert.Null(updatedLog.ErrorMessage); // Đã dọn thông báo lỗi cũ để sẵn sàng thử lại

            var dequeuedId = await queueChannel.DequeueAsync(CancellationToken.None);
            Assert.Equal(logId, dequeuedId);
        }
    }

    [Fact]
    public async Task ResendEmailAsync_ShouldReturnFalse_WhenStatusIsSent()
    {
        // Arrange
        var (emailService, serviceProvider, _) = CreateTestContext();
        var logId = Guid.NewGuid();

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sentLog = new EmailLog
            {
                Id = logId,
                RecipientEmail = "already.sent@ats.test",
                Subject = "Thư mời",
                Status = "SENT",
                SentAt = DateTimeOffset.UtcNow
            };
            await db.EmailLogs.AddAsync(sentLog);
            await db.SaveChangesAsync();
        }

        // Act (AC2: Chặn gửi lại email đã thành công)
        var result = await emailService.ResendEmailAsync(logId, Guid.NewGuid());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ResendEmailAsync_ShouldReturnFalse_WhenStatusIsQueued()
    {
        // Arrange
        var (emailService, serviceProvider, _) = CreateTestContext();
        var logId = Guid.NewGuid();

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var queuedLog = new EmailLog
            {
                Id = logId,
                RecipientEmail = "queued@ats.test",
                Subject = "Đang gửi",
                Status = "QUEUED"
            };
            await db.EmailLogs.AddAsync(queuedLog);
            await db.SaveChangesAsync();
        }

        // Act (AC2: Chặn gửi lại email đang trong hàng đợi để chống gửi trùng)
        var result = await emailService.ResendEmailAsync(logId, Guid.NewGuid());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ResendEmailAsync_ShouldReturnFalse_WhenLogNotFound()
    {
        // Arrange
        var (emailService, _, _) = CreateTestContext();

        // Act & Assert
        var result = await emailService.ResendEmailAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.False(result);
    }

    [Fact]
    public async Task GetCandidateEmailLogsAsync_ShouldFilterCorrectly_ByStatus()
    {
        // Arrange
        var (emailService, serviceProvider, _) = CreateTestContext();
        var candidateId = Guid.NewGuid();
        var logSentId = Guid.NewGuid();
        var logQueuedId = Guid.NewGuid();
        var logFailedId = Guid.NewGuid();

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var logSent = new EmailLog
            {
                Id = logSentId,
                CandidateId = candidateId,
                RecipientEmail = "cand@ats.test",
                Subject = "Thư mời 1",
                EmailType = "Mời phỏng vấn",
                Status = "SENT",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-3),
                SentAt = DateTimeOffset.UtcNow.AddHours(-3)
            };

            var logQueued = new EmailLog
            {
                Id = logQueuedId,
                CandidateId = candidateId,
                RecipientEmail = "cand@ats.test",
                Subject = "Thư mời 2",
                EmailType = "Xác nhận",
                Status = "QUEUED",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
            };

            var logFailed = new EmailLog
            {
                Id = logFailedId,
                CandidateId = candidateId,
                RecipientEmail = "cand@ats.test",
                Subject = "Thư mời 3",
                EmailType = "Offer",
                Status = "FAILED",
                ErrorMessage = "550 User not found",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
            };

            await db.EmailLogs.AddRangeAsync(logSent, logQueued, logFailed);
            await db.SaveChangesAsync();
        }

        // Act & Assert
        // 1. Tất cả (sắp xếp mới nhất trước: logQueuedId tạo lúc -1h, logFailedId -2h, logSentId -3h)
        var allLogs = await emailService.GetCandidateEmailLogsAsync(candidateId, "ALL");
        Assert.Equal(3, allLogs.Count);
        Assert.Equal(logQueuedId, allLogs[0].Id);

        // 2. Lọc SENT
        var sentLogs = await emailService.GetCandidateEmailLogsAsync(candidateId, "SENT");
        Assert.Single(sentLogs);
        Assert.Equal(logSentId, sentLogs[0].Id);

        // 3. Lọc QUEUED
        var queuedLogs = await emailService.GetCandidateEmailLogsAsync(candidateId, "QUEUED");
        Assert.Single(queuedLogs);
        Assert.Equal(logQueuedId, queuedLogs[0].Id);

        // 4. Lọc FAILED
        var failedLogs = await emailService.GetCandidateEmailLogsAsync(candidateId, "FAILED");
        Assert.Single(failedLogs);
        Assert.Equal(logFailedId, failedLogs[0].Id);
    }
}
