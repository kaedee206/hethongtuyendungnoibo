using Ats.Web.Models.DTOs;
using Ats.Web.Models.ViewModels.Profiles;
using Microsoft.AspNetCore.Http;

namespace Ats.Web.Services.Interfaces;

public interface IProfileService
{
    Task<ProfileUpdateViewModel?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> UpdateProfileAsync(Guid userId, UpdateProfileRequestDto dto, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message, string? AvatarUrl)> UploadAvatarAsync(Guid userId, IFormFile file, string webRootPath, CancellationToken cancellationToken = default);
}
