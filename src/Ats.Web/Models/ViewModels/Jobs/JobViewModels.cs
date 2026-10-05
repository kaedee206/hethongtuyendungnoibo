namespace Ats.Web.Models.ViewModels.Jobs;

public class JobItemViewModel
{
    public string Id { get; set; } = string.Empty;
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
    public List<string> Responsibilities { get; set; } = new();
    public List<string> Requirements { get; set; } = new();
    public List<string> Benefits { get; set; } = new();
    public List<string> TechStack { get; set; } = new();
    public DateTime PostedDate { get; set; } = DateTime.UtcNow;
    public DateTime Deadline { get; set; } = DateTime.UtcNow.AddDays(30);
    public string RecruiterEmail { get; set; } = "careers@noveratech.digital";
    public string RecruiterPhone { get; set; } = "(+84) 24 7300 8899";
    public string WorkAddress { get; set; } = "Tầng 18, Novera Tower, Duy Tân, Cầu Giấy, Hà Nội";
}

public class JobListViewModel
{
    public List<JobItemViewModel> Jobs { get; set; } = new();
    public string? SearchKeyword { get; set; }
    public string? SelectedDepartment { get; set; }
    public string? SelectedLocation { get; set; }
    public string? SelectedLevel { get; set; }
    public int TotalOpenings => Jobs.Count;
    public int HotOpeningsCount => Jobs.Count(j => j.IsHot);
}

public class JobDetailViewModel
{
    public JobItemViewModel Job { get; set; } = new();
    public List<JobItemViewModel> RelatedJobs { get; set; } = new();
}
