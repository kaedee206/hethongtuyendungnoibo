using Ats.Web.Models.DTOs;

namespace Ats.Web.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> AuthenticateAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
}
