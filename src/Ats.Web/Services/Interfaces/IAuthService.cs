using Ats.Web.Models.DTOs;

namespace Ats.Web.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> AuthenticateAsync(LoginRequestDto request, string? clientIp = null, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> ForgotPasswordAsync(string email, string baseUrl, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, string currentSessionId, CancellationToken cancellationToken = default);
    Task LogoutAsync(Guid userId, string sessionId, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> RegisterCandidateAsync(string fullName, string email, string password, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> SendRegistrationOtpAsync(string fullName, string email, string password, string phoneNumber, string? clientIp = null, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> SendRegistrationOtpAsync(string fullName, string email, string password, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> VerifyRegistrationOtpAsync(string email, string otpCode, string? clientIp = null, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> SendForgotPasswordOtpAsync(string email, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> ResetPasswordWithOtpAsync(string email, string otpCode, string newPassword, string? clientIp = null, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> ResendOtpAsync(string email, string type, CancellationToken cancellationToken = default);
    Task<AuthResponseDto> ProcessExternalLoginAsync(string email, string fullName, string provider, string providerKey, CancellationToken cancellationToken = default);
}

