using Ats.Web.Models.DTOs;

namespace Ats.Web.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> AuthenticateAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> ForgotPasswordAsync(string email, string baseUrl, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, string currentSessionId, CancellationToken cancellationToken = default);
    Task LogoutAsync(Guid userId, string sessionId, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> RegisterCandidateAsync(string fullName, string email, string password, CancellationToken cancellationToken = default);
}

