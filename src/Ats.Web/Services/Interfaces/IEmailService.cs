using Ats.Web.Models.DTOs;

namespace Ats.Web.Services.Interfaces;

public interface IEmailService
{
    Task<bool> SendEmailAsync(SendEmailRequestDto request, CancellationToken cancellationToken = default);

    Task<bool> SendLoginSuccessAlertAsync(
        string email,
        string fullName,
        string ipAddress,
        string location,
        string userAgent,
        DateTimeOffset loginTime,
        bool isCandidate = false,
        string? roleName = null,
        CancellationToken cancellationToken = default);

    Task<bool> SendApplicationReceivedAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        string applicationCode,
        string? withdrawUrl = null,
        CancellationToken cancellationToken = default);

    Task<bool> SendRecruiterWithdrawalAlertAsync(
        string recruiterEmail,
        string recruiterName,
        string candidateName,
        string candidateEmail,
        string jobTitle,
        string? reason,
        DateTimeOffset withdrawnAt,
        CancellationToken cancellationToken = default);

    Task<bool> SendCandidateWithdrawalConfirmedAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        string? reason,
        CancellationToken cancellationToken = default);

    Task<bool> SendScreeningPassedAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        CancellationToken cancellationToken = default);

    Task<bool> SendInterviewInvitationAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        string roundTitle,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        string locationOrLink,
        string panelists,
        string? preparationGuide = null,
        string? confirmationUrl = null,
        CancellationToken cancellationToken = default);

    Task<bool> SendOfferLetterNotificationAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        decimal baseSalary,
        DateTime? startDate,
        CancellationToken cancellationToken = default);

    Task<bool> SendRejectionLetterAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        string? reason,
        CancellationToken cancellationToken = default);

    Task<bool> SendSlaOverdueAlertAsync(
        string recruiterEmail,
        string recruiterName,
        string candidateName,
        string candidateEmail,
        string jobTitle,
        string stageName,
        int daysInStage,
        int slaDays,
        int overdueDays,
        CancellationToken cancellationToken = default);

    Task<bool> SendInterviewRescheduledAsync(
        string recipientEmail,
        string recipientName,
        bool isCandidate,
        string candidateName,
        string jobTitle,
        string roundTitle,
        DateTimeOffset oldStartTime,
        DateTimeOffset newStartTime,
        DateTimeOffset newEndTime,
        string newLocationOrLink,
        string newInterviewTypeDisplay,
        string panelistNames,
        string? rescheduleReason,
        CancellationToken cancellationToken = default);

    Task<bool> SendInterviewCancelledAsync(
        string recipientEmail,
        string recipientName,
        bool isCandidate,
        string candidateName,
        string jobTitle,
        string roundTitle,
        DateTimeOffset scheduledStartTime,
        string cancellationReason,
        CancellationToken cancellationToken = default);

    Task<bool> ResendEmailAsync(Guid emailLogId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<List<Ats.Web.Models.Entities.EmailLog>> GetCandidateEmailLogsAsync(Guid candidateId, string? statusFilter = null, CancellationToken cancellationToken = default);
}