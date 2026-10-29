using Ats.Web.Models.ViewModels.Workspace;

namespace Ats.Web.Services.Interfaces;

public interface IHrmRecruitmentDashboardService
{
    Task<HrmRecruitmentDashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken = default);
}
