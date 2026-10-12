using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Jobs;

public class JobItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string DepartmentCategory { get; set; } = "it"; // it, ai, cloud, product, qa, hr, security
    public string WorkLocation { get; set; } = string.Empty;
    public string EmploymentType { get; set; } = "Toàn thời gian (Full-time)";
    public string ExperienceLevel { get; set; } = string.Empty;
    public string SalaryDisplay { get; set; } = string.Empty;
    public bool IsHot { get; set; }
    public string ShortSummary { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public string PositionDescription { get; set; } = string.Empty;
    public List<string> Responsibilities { get; set; } = new();
    public List<string> Requirements { get; set; } = new();
    public List<string> Benefits { get; set; } = new();
    public List<string> TechStack { get; set; } = new();
    public DateTime PostedDate { get; set; } = DateTime.UtcNow;
    public DateTime Deadline { get; set; } = DateTime.UtcNow.AddDays(30);
    public string RecruiterEmail { get; set; } = "careers@noveratech.digital";
    public string RecruiterPhone { get; set; } = "(+84) 24 7300 8899";
    public string WorkAddress { get; set; } = "Tầng 18, Novera Tower, Duy Tân, Cầu Giấy, Hà Nội";

    // Trạng thái theo Scrum-38
    public JobPostingStatus Status { get; set; } = JobPostingStatus.PUBLISHED;
    public bool IsClosed => Status == JobPostingStatus.CLOSED;
    public bool IsExpired => Status == JobPostingStatus.EXPIRED || (Deadline < DateTime.UtcNow);
    public bool IsActive => Status == JobPostingStatus.PUBLISHED && !IsExpired;
    public string? UnpublishReason { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

public class JobListViewModel
{
    public List<JobItemViewModel> Jobs { get; set; } = new();
    public List<JobItemViewModel> AllJobs { get; set; } = new();
    public string? SearchKeyword { get; set; }
    public string? SelectedDepartment { get; set; }
    public string? SelectedLocation { get; set; }
    public string? SelectedLevel { get; set; }
    public int TotalOpenings => TotalRecords > 0 ? TotalRecords : Jobs.Count;
    public int HotOpeningsCount => (AllJobs != null && AllJobs.Any() ? AllJobs : Jobs).Count(j => j.IsHot);
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 9;
    public int TotalRecords { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalRecords / (double)PageSize) : 0;
    
    // Thuộc tính phục vụ Recruiter Bar
    public bool IsRecruiterOrStaff { get; set; }
    public int ActiveCount { get; set; }
    public int ClosedCount { get; set; }
    public int ExpiredCount { get; set; }
}

public class JobDetailViewModel
{
    public JobItemViewModel Job { get; set; } = new();
    public List<JobItemViewModel> RelatedJobs { get; set; } = new();
}

public class CreateJobViewModel
{
    public string Title { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = "Công nghệ Thông tin (IT)";
    public int Quantity { get; set; } = 1;
    public string EmploymentType { get; set; } = "Toàn thời gian (Full-time)";
    public string ExperienceLevel { get; set; } = "SENIOR";
    public string WorkLocation { get; set; } = "Hà Nội (Hybrid 2 ngày WFH)";
    public string SalaryDisplay { get; set; } = "35 – 50 Triệu VNĐ";
    public DateTime ExpiredDate { get; set; } = DateTime.UtcNow.AddDays(30);

    public string Overview { get; set; } = string.Empty;
    public string PositionDescription { get; set; } = string.Empty;
    public string Responsibilities { get; set; } = string.Empty;
    public string Requirements { get; set; } = string.Empty;
    public string TechStack { get; set; } = string.Empty; // Comma-delimited list of tags
    public string Benefits { get; set; } = string.Empty;
}

public class UpdateJobPostingRequest
{
    public string JobId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string WorkLocation { get; set; } = string.Empty;
    public string ExperienceLevel { get; set; } = string.Empty;
    public string SalaryDisplay { get; set; } = string.Empty;
    public DateTime Deadline { get; set; } = DateTime.UtcNow.AddDays(30);
    public string Overview { get; set; } = string.Empty;
    public string PositionDescription { get; set; } = string.Empty;
    public string Responsibilities { get; set; } = string.Empty;
    public string Requirements { get; set; } = string.Empty;
    public string TechStack { get; set; } = string.Empty;
    public string Benefits { get; set; } = string.Empty;
}

public class PatchJobPostingDto
{
    public string? Title { get; set; }
    public string? Department { get; set; }
    public string? WorkLocation { get; set; }
    public string? EmploymentType { get; set; }
    public string? ExperienceLevel { get; set; }
    public string? SalaryDisplay { get; set; }
    public DateTime? Deadline { get; set; }
    public string? Overview { get; set; }
    public string? PositionDescription { get; set; }
    public string? Responsibilities { get; set; }
    public string? Requirements { get; set; }
    public string? TechStack { get; set; }
    public string? Benefits { get; set; }
    public string? Status { get; set; }
}

/// <summary>
/// Yêu cầu gỡ tin tuyển dụng thủ công bởi Recruiter/HR/Admin (Scrum-38).
/// </summary>
public class UnpublishJobRequest
{
    public string JobId { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

/// <summary>
/// Yêu cầu đăng lại / gia hạn tin tuyển dụng đã gỡ hoặc hết hạn.
/// </summary>
public class RepublishJobRequest
{
    public string JobId { get; set; } = string.Empty;
    public DateTime? NewDeadline { get; set; }
}

/// <summary>
/// Mục tin trong danh sách quản lý tin đăng dành cho Recruiter.
/// </summary>
public class JobManagementItemViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string WorkLocation { get; set; } = string.Empty;
    public string SalaryDisplay { get; set; } = string.Empty;
    public JobPostingStatus Status { get; set; }
    public string StatusText => Status switch
    {
        JobPostingStatus.PUBLISHED when !IsExpired => "Đang đăng tuyển",
        JobPostingStatus.CLOSED => "Đã gỡ thủ công",
        JobPostingStatus.EXPIRED or JobPostingStatus.PUBLISHED when IsExpired => "Đã quá hạn",
        JobPostingStatus.DRAFT => "Bản nháp",
        _ => Status.ToString()
    };
    public string StatusBadgeClass => Status switch
    {
        JobPostingStatus.PUBLISHED when !IsExpired => "bg-success text-white",
        JobPostingStatus.CLOSED => "bg-warning text-dark border",
        JobPostingStatus.EXPIRED or JobPostingStatus.PUBLISHED when IsExpired => "bg-danger text-white",
        JobPostingStatus.DRAFT => "bg-secondary text-white",
        _ => "bg-light text-dark"
    };

    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ExpiredAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? UnpublishReason { get; set; }
    public bool IsExpired => (ExpiredAt.HasValue && ExpiredAt.Value <= DateTimeOffset.UtcNow) || Status == JobPostingStatus.EXPIRED;
    public bool IsClosed => Status == JobPostingStatus.CLOSED;
    public bool IsPublished => Status == JobPostingStatus.PUBLISHED && !IsExpired;
    public int ApplicationsCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// ViewModel cho màn hình quản lý tin tuyển dụng (Views/Jobs/Manage.cshtml).
/// </summary>
public class JobManagementListViewModel
{
    public List<JobManagementItemViewModel> Items { get; set; } = new();
    public string? SearchKeyword { get; set; }
    public string? SelectedStatus { get; set; }
    public string? SelectedDepartment { get; set; }

    public int TotalPostings { get; set; }
    public int PublishedCount { get; set; }
    public int ClosedCount { get; set; }
    public int ExpiredCount { get; set; }
    public int DraftCount { get; set; }

    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalPostings / (double)PageSize) : 0;

    public DateTimeOffset? LastAutoScanTime { get; set; }
    public int LastAutoScanExpiredCount { get; set; }
}

/// <summary>
/// Kết quả lần chạy quét và ẩn tin quá hạn định kỳ hằng ngày.
/// </summary>
public class JobExpirationScanResult
{
    public bool Success { get; set; } = true;
    public DateTimeOffset ScanTime { get; set; } = DateTimeOffset.UtcNow;
    public int ScannedCount { get; set; }
    public int ExpiredCount { get; set; }
    public List<string> ExpiredJobCodes { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

