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
        CancellationToken cancellationToken = default);

    Task<bool> SendApplicationReceivedAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        string applicationCode,
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
}