using Ats.Web.Models.ViewModels.Jobs;

namespace Ats.Web.Services.Interfaces;

public interface IJobService
{
    Task<JobListViewModel> GetJobListAsync(string? search = null, string? department = null, string? location = null, string? employmentType = null, int page = 1, int pageSize = 9);
    Task<List<JobItemViewModel>> GetHotJobsAsync(int count = 6);
    Task<JobDetailViewModel?> GetJobDetailAsync(string idOrSlug);
    Task<int> GetTotalActiveJobsCountAsync();

    // Scrum-38: Nghiệp vụ quản trị tin tuyển dụng, gỡ tin thủ công và khôi phục/đăng lại tin
    Task<JobManagementListViewModel> GetJobManagementListAsync(string? search = null, Models.Enums.JobPostingStatus? status = null, int page = 1, int pageSize = 15);
    Task<(bool Success, string Message)> UnpublishJobPostingAsync(string idOrSlug, Guid userId, string? reason = null);
    Task<(bool Success, string Message)> RepublishJobPostingAsync(string idOrSlug, Guid userId, DateTimeOffset? newDeadline = null);
}
