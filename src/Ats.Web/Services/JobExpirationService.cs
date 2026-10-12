using Ats.Web.Data;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Jobs;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

/// <summary>
/// Thực thi nghiệp vụ quét và tự động ẩn tin tuyển dụng quá hạn nhận hồ sơ (Scrum-38).
/// Tuân thủ quy chuẩn C# 12 Primary Constructor và ghi log bảo mật có cấu trúc.
/// </summary>
public class JobExpirationService(
    IServiceScopeFactory scopeFactory,
    ILogger<JobExpirationService> logger) : IJobExpirationService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<JobExpirationService> _logger = logger;

    private static JobExpirationScanResult _lastScanResult = new()
    {
        ScanTime = DateTimeOffset.UtcNow,
        ScannedCount = 0,
        ExpiredCount = 0,
        Message = "Chưa thực hiện lần quét nào."
    };

    public async Task<JobExpirationScanResult> ProcessExpiredJobPostingsAsync(CancellationToken cancellationToken = default)
    {
        var scanStartTime = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expiredCodes = new List<string>();

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var auditService = scope.ServiceProvider.GetService<ISecurityAuditService>();

        try
        {
            // 1. Quét toàn bộ tin đang PUBLISHED mà ExpiredAt đã qua hoặc TargetHireDate của yêu cầu đã qua
            var publishedPostings = await dbContext.JobPostings
                .Include(j => j.Requisition)
                .Where(j => !j.IsDeleted && j.Status == JobPostingStatus.PUBLISHED)
                .ToListAsync(cancellationToken);

            var expiredPostings = publishedPostings
                .Where(j => (j.ExpiredAt.HasValue && j.ExpiredAt.Value <= scanStartTime) ||
                            (j.Requisition != null && j.Requisition.TargetHireDate.HasValue && j.Requisition.TargetHireDate.Value < today))
                .ToList();

            foreach (var posting in expiredPostings)
            {
                posting.Status = JobPostingStatus.EXPIRED;
                posting.UpdatedAt = scanStartTime;

                var displayCode = !string.IsNullOrWhiteSpace(posting.Code) ? posting.Code : posting.Slug;
                expiredCodes.Add(displayCode);

                auditService?.LogSecurityEvent(new SecurityAuditEvent
                {
                    EventType = SecurityAuditEventType.JobPostingAutoExpired,
                    Action = "AUTO_EXPIRE_JOB_POSTING",
                    ResourcePath = $"/jobs/detail/{posting.Slug}",
                    IsSuccess = true,
                    Details = $"Tác vụ định kỳ hằng ngày: Tự động ẩn tin quá hạn [{displayCode}] '{posting.Title}' (Hạn: {posting.ExpiredAt:dd/MM/yyyy HH:mm UTC})."
                });
            }

            if (expiredPostings.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Tác vụ định kỳ hằng ngày: Đã tự động ẩn {Count} tin tuyển dụng quá hạn nhận hồ sơ: {Codes}",
                    expiredPostings.Count, string.Join(", ", expiredCodes));
            }
            else
            {
                _logger.LogInformation("Tác vụ định kỳ hằng ngày: Đã kiểm tra {Count} tin đang đăng tuyển, không có tin nào quá hạn.", publishedPostings.Count);
            }

            var result = new JobExpirationScanResult
            {
                ScanTime = scanStartTime,
                ScannedCount = publishedPostings.Count,
                ExpiredCount = expiredPostings.Count,
                ExpiredJobCodes = expiredCodes,
                Message = expiredPostings.Count > 0
                    ? $"Đã tự động ẩn thành công {expiredPostings.Count} tin tuyển dụng quá hạn nhận hồ sơ."
                    : "Tất cả các tin đang mở đều còn trong hạn nhận hồ sơ hợp lệ."
            };

            _lastScanResult = result;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra trong quá trình quét tự động ẩn tin tuyển dụng quá hạn");
            var errorResult = new JobExpirationScanResult
            {
                ScanTime = scanStartTime,
                ScannedCount = 0,
                ExpiredCount = 0,
                Message = "Lỗi khi quét tin quá hạn: " + ex.Message
            };
            _lastScanResult = errorResult;
            return errorResult;
        }
    }

    public JobExpirationScanResult GetLastScanResult() => _lastScanResult;
}
