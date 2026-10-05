namespace Ats.Web.Models.ViewModels.CompetencyFrameworks;

public class JobPositionAssignedViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string JobLevel { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class CompetencyFrameworkItemViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string StatusText => IsActive ? "Đang sử dụng" : "Ngừng sử dụng";
    public int CompetenciesCount { get; set; }
    public List<JobPositionAssignedViewModel> AssociatedJobPositions { get; set; } = [];
    public int AssociatedJobPositionsCount => AssociatedJobPositions.Count;
    public DateTimeOffset UpdatedAt { get; set; }
}

public class CompetencyFrameworkListViewModel
{
    public List<CompetencyFrameworkItemViewModel> Frameworks { get; set; } = [];
    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public int TotalRecords { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 15;
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalRecords / (double)PageSize) : 0;
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;
    public int TotalActiveFrameworks { get; set; }
    public int TotalInactiveFrameworks { get; set; }
    public int TotalPositionsMapped { get; set; }
}
