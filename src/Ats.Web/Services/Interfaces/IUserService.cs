using Ats.Web.Models.ViewModels.Users;

namespace Ats.Web.Services.Interfaces;

public interface IUserService
{
    Task<UserListViewModel> GetUsersAsync(string? keyword, string? role, string? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<UserCreateViewModel> GetUserCreateViewModelAsync(CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message, Guid? UserId)> CreateUserAsync(UserCreateViewModel model, Guid currentAdminId, string baseUrl, CancellationToken cancellationToken = default);
    Task<UserEditViewModel?> GetUserEditViewModelAsync(Guid id, Guid currentAdminId, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> UpdateUserAsync(UserEditViewModel model, Guid currentAdminId, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message, string? HandoverWarning)> LockUserAsync(Guid id, string lockReason, Guid currentAdminId, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string Message)> UnlockUserAsync(Guid id, Guid currentAdminId, CancellationToken cancellationToken = default);
    Task<byte[]> GenerateExcelTemplateAsync(CancellationToken cancellationToken = default);
    Task<Ats.Web.Models.DTOs.ImportExcelResultDto> ValidateExcelImportAsync(Stream excelStream, CancellationToken cancellationToken = default);
}
