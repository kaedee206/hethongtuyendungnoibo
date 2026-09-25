using Ats.Web.Models.DTOs;

namespace Ats.Web.Services.Interfaces;

public interface IEmailService
{
    Task<bool> SendEmailAsync(SendEmailRequestDto request, CancellationToken cancellationToken = default);
}