using Ats.Web.Models.ViewModels.CompetencyFrameworks;

namespace Ats.Web.Services.Interfaces;

public interface ICompetencyFrameworkService
{
    // ─── Queries ──────────────────────────────────────────────────────────────

    Task<CompetencyFrameworkListViewModel> GetFrameworksAsync(
        string? keyword,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CompetencyFrameworkItemViewModel?> GetFrameworkByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CompetencyFrameworkDetailViewModel?> GetDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<List<JobPositionAssignedViewModel>> GetAssociatedPositionsAsync(
        Guid frameworkId,
        CancellationToken cancellationToken = default);

    // ─── Form preparation ─────────────────────────────────────────────────────

    /// <summary>Chuẩn bị ViewModel có đầy đủ danh sách chức danh cho form tạo/sửa.</summary>
    Task<CompetencyFrameworkFormViewModel> PrepareFormAsync(
        Guid? id,
        CancellationToken cancellationToken = default);

    // ─── Commands ─────────────────────────────────────────────────────────────

    Task<(bool Success, string Message, Guid? NewId)> CreateAsync(
        CompetencyFrameworkFormViewModel model,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> UpdateAsync(
        CompetencyFrameworkFormViewModel model,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> ToggleActiveAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> AssignPositionAsync(
        Guid frameworkId,
        Guid positionId,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> UnassignPositionAsync(
        Guid frameworkId,
        Guid positionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// SCRUM-226: Lấy chi tiết khung năng lực theo chức danh phục vụ phiếu phỏng vấn.
    /// </summary>
    Task<CompetencyFrameworkDetailViewModel?> GetByPositionAsync(
        Guid jobPositionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// SCRUM-225: Nhân bản (clone) khung năng lực sang một bản ghi mới độc lập.
    /// </summary>
    Task<(bool Success, string Message, Guid? NewId)> CloneAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

