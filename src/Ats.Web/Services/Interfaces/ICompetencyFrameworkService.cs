using Ats.Web.Models.ViewModels.CompetencyFrameworks;

namespace Ats.Web.Services.Interfaces;

public interface ICompetencyFrameworkService
{
    Task<CompetencyFrameworkListViewModel> GetFrameworksAsync(
        string? keyword,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CompetencyFrameworkItemViewModel?> GetFrameworkByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<List<JobPositionAssignedViewModel>> GetAssociatedPositionsAsync(
        Guid frameworkId,
        CancellationToken cancellationToken = default);
}
