using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

/// <summary>
/// Triển khai dịch vụ quản lý phản hồi Job Offer và thông báo tuyển dụng (SP-8).
/// </summary>
public class OfferService(
    ApplicationDbContext dbContext,
    INotificationService notificationService,
    ILogger<OfferService> logger) : IOfferService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly INotificationService _notificationService = notificationService;
    private readonly ILogger<OfferService> _logger = logger;

    public async Task<(bool Success, string Message)> AcceptOfferAsync(
        Guid offerId,
        string? candidateComment = null,
        CancellationToken cancellationToken = default)
    {
        var offer = await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(jp => jp.Requisition)
            .FirstOrDefaultAsync(o => o.Id == offerId && !o.IsDeleted, cancellationToken);

        if (offer == null)
        {
            return (false, "Không tìm thấy Thư mời Offer tương ứng.");
        }

        var now = DateTimeOffset.UtcNow;
        offer.Status = OfferStatus.ACCEPTED;
        offer.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var candName = $"{offer.Application?.Candidate?.FirstName} {offer.Application?.Candidate?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(candName)) candName = "Ứng viên";

        var jobTitle = offer.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng";
        var candidateId = offer.Application?.CandidateId ?? Guid.Empty;
        var targetUrl = candidateId != Guid.Empty
            ? $"/candidates/details/{candidateId}#offers"
            : "/workspace/dashboard#offers";

        var content = $"Ứng viên {candName} đã ĐỒNG Ý nhận việc cho vị trí '{jobTitle}'.";
        if (!string.IsNullOrWhiteSpace(candidateComment))
        {
            content += $" Phản hồi: \"{candidateComment.Trim()}\"";
        }

        // Bắn thông báo tới Recruiter tạo offer và Recruiter phụ trách vị trí
        var recruiterIds = GetTargetRecruiterIds(offer);
        foreach (var recruiterId in recruiterIds)
        {
            await _notificationService.QueueNotificationAsync(
                recruiterId,
                $"Ứng viên đã ĐỒNG Ý nhận việc: {candName}",
                content,
                targetUrl,
                NotificationType.OfferResponse,
                cancellationToken);
        }

        return (true, $"Đã ghi nhận phản hồi đồng ý nhận việc từ ứng viên {candName}.");
    }

    public async Task<(bool Success, string Message)> RejectOfferAsync(
        Guid offerId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var offer = await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(jp => jp.Requisition)
            .FirstOrDefaultAsync(o => o.Id == offerId && !o.IsDeleted, cancellationToken);

        if (offer == null)
        {
            return (false, "Không tìm thấy Thư mời Offer tương ứng.");
        }

        var now = DateTimeOffset.UtcNow;
        offer.Status = OfferStatus.REJECTED;
        offer.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var candName = $"{offer.Application?.Candidate?.FirstName} {offer.Application?.Candidate?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(candName)) candName = "Ứng viên";

        var jobTitle = offer.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng";
        var candidateId = offer.Application?.CandidateId ?? Guid.Empty;
        var targetUrl = candidateId != Guid.Empty
            ? $"/candidates/details/{candidateId}#offers"
            : "/workspace/dashboard#offers";

        var content = $"Ứng viên {candName} đã TỪ CHỐI nhận việc cho vị trí '{jobTitle}'.";
        if (!string.IsNullOrWhiteSpace(reason))
        {
            content += $" Lý do từ chối: \"{reason.Trim()}\"";
        }

        var recruiterIds = GetTargetRecruiterIds(offer);
        foreach (var recruiterId in recruiterIds)
        {
            await _notificationService.QueueNotificationAsync(
                recruiterId,
                $"Ứng viên đã TỪ CHỐI nhận việc: {candName}",
                content,
                targetUrl,
                NotificationType.OfferResponse,
                cancellationToken);
        }

        return (true, $"Đã ghi nhận phản hồi từ chối Offer từ ứng viên {candName}.");
    }

    public async Task<(bool Success, string Message)> ReviseOfferAsync(
        Guid offerId,
        string? feedback = null,
        CancellationToken cancellationToken = default)
    {
        var offer = await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(jp => jp.Requisition)
            .FirstOrDefaultAsync(o => o.Id == offerId && !o.IsDeleted, cancellationToken);

        if (offer == null)
        {
            return (false, "Không tìm thấy Thư mời Offer tương ứng.");
        }

        var now = DateTimeOffset.UtcNow;
        offer.Status = OfferStatus.DRAFT;
        offer.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var candName = $"{offer.Application?.Candidate?.FirstName} {offer.Application?.Candidate?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(candName)) candName = "Ứng viên";

        var jobTitle = offer.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng";
        var candidateId = offer.Application?.CandidateId ?? Guid.Empty;
        var targetUrl = candidateId != Guid.Empty
            ? $"/candidates/details/{candidateId}#offers"
            : "/workspace/dashboard#offers";

        var content = $"Ứng viên {candName} đề xuất THƯƠNG LƯỢNG LẠI các điều khoản Offer cho vị trí '{jobTitle}'.";
        if (!string.IsNullOrWhiteSpace(feedback))
        {
            content += $" Nội dung đề xuất: \"{feedback.Trim()}\"";
        }

        var recruiterIds = GetTargetRecruiterIds(offer);
        foreach (var recruiterId in recruiterIds)
        {
            await _notificationService.QueueNotificationAsync(
                recruiterId,
                $"Ứng viên đề xuất THƯƠNG LƯỢNG LẠI Offer: {candName}",
                content,
                targetUrl,
                NotificationType.OfferResponse,
                cancellationToken);
        }

        return (true, $"Đã ghi nhận đề xuất thương lượng lại từ ứng viên {candName}.");
    }

    public async Task NotifyOfferPendingApprovalAsync(
        JobOffer offer,
        Guid candidateId,
        string candidateName,
        string jobTitle,
        decimal baseSalary,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Tìm danh sách người duyệt có thẩm quyền (Approver / HR Manager / Admin)
            var approvers = await _dbContext.Users
                .AsNoTracking()
                .Where(u => u.Status == "ACTIVE" &&
                           (u.Role == UserRoles.Approver ||
                            u.Role == UserRoles.HRManager ||
                            u.Role == UserRoles.Admin ||
                            u.UserRoles.Any(ur => ur.Role != null &&
                                (ur.Role.Name == UserRoles.Approver ||
                                 ur.Role.Name == UserRoles.HRManager ||
                                 ur.Role.Name == UserRoles.Admin))))
                .Select(u => u.Id)
                .Distinct()
                .ToListAsync(cancellationToken);

            var targetUrl = candidateId != Guid.Empty
                ? $"/candidates/details/{candidateId}#offers"
                : "/workspace/dashboard#offers";

            foreach (var approverId in approvers)
            {
                await _notificationService.QueueNotificationAsync(
                    approverId,
                    $"Đề xuất Offer chờ duyệt: {candidateName}",
                    $"Đề xuất Thư mời nhận việc cho ứng viên {candidateName} (Vị trí: '{jobTitle}', Lương cơ bản: {baseSalary:N0} VNĐ) đang chờ bạn xem xét và phê duyệt.",
                    targetUrl,
                    NotificationType.ApprovalPending,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi bắn thông báo chờ duyệt Offer cho candidateId={CandidateId}", candidateId);
        }
    }

    private static HashSet<Guid> GetTargetRecruiterIds(JobOffer offer)
    {
        var result = new HashSet<Guid>();
        if (offer.CreatedByRecruiterId != Guid.Empty)
        {
            result.Add(offer.CreatedByRecruiterId);
        }

        var assignedRecruiterId = offer.Application?.JobPosting?.Requisition?.AssignedRecruiterId;
        if (assignedRecruiterId.HasValue && assignedRecruiterId.Value != Guid.Empty)
        {
            result.Add(assignedRecruiterId.Value);
        }

        return result;
    }
}
