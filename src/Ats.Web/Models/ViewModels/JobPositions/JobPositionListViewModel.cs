using Ats.Web.Models.Entities;

namespace Ats.Web.Models.ViewModels.JobPositions;

public class JobPositionListViewModel
{
    public List<JobPositionItemViewModel> Positions { get; set; } = [];
    public string? Keyword { get; set; }
    public string? LevelFilter { get; set; }
    public Guid? DepartmentFilter { get; set; }
    public int TotalRecords { get; set; }
}

public class JobPositionItemViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string JobLevel { get; set; } = string.Empty;
    public decimal? MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
