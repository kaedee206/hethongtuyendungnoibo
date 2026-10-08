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

// ─── CRUD ViewModels (SCRUM-222 → 229) ───────────────────────────────────────

public class CompetencyCriterionFormViewModel
{
    /// <summary>null khi tạo mới, có giá trị khi chỉnh sửa tiêu chí đã tồn tại.</summary>
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>% trọng số (0-100). Tổng tất cả tiêu chí trong khung phải = 100.</summary>
    public int Weight { get; set; }

    public string? Rubric1 { get; set; }
    public string? Rubric2 { get; set; }
    public string? Rubric3 { get; set; }
    public string? Rubric4 { get; set; }
    public string? Rubric5 { get; set; }

    /// <summary>true nếu người dùng đã đánh dấu xóa tiêu chí này trong form.</summary>
    public bool IsMarkedForDeletion { get; set; }
}

public class CompetencyFrameworkFormViewModel
{
    public Guid? Id { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public List<CompetencyCriterionFormViewModel> Criteria { get; set; } = [];

    /// <summary>Danh sách ID chức danh được gán vào khung này (multi-select).</summary>
    public List<Guid> AssignedPositionIds { get; set; } = [];

    // Dùng cho hiển thị dropdown chức danh khi render form
    public List<JobPositionAssignedViewModel> AllPositions { get; set; } = [];

    // Tổng trọng số criteria — phải = 100 trước khi submit
    public int TotalWeight => Criteria.Where(c => !c.IsMarkedForDeletion).Sum(c => c.Weight);
    public bool IsWeightValid => TotalWeight == 100 || !Criteria.Any(c => !c.IsMarkedForDeletion);
}

public class CompetencyFrameworkDetailViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string StatusText => IsActive ? "Đang sử dụng" : "Ngừng sử dụng";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<CompetencyCriterionFormViewModel> Criteria { get; set; } = [];
    public List<JobPositionAssignedViewModel> AssignedPositions { get; set; } = [];
}

