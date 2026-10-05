using Ats.Web.Models.ViewModels.Jobs;

namespace Ats.Web.Services.Interfaces;

public interface IJobService
{
    Task<JobListViewModel> GetJobListAsync(string? search = null, string? department = null, string? location = null, string? level = null, int page = 1, int pageSize = 9);
    Task<List<JobItemViewModel>> GetHotJobsAsync(int count = 6);
    Task<JobDetailViewModel?> GetJobDetailAsync(string idOrSlug);
    Task<int> GetTotalActiveJobsCountAsync();
}
