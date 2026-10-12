namespace Ats.Web.Models.ViewModels.CandidateMerge;

/// <summary>
/// ViewModel cho màn hình so sánh cạnh nhau (Side-by-side Comparison) của 2 hồ sơ trước khi gộp
/// </summary>
public class CandidateComparisonViewModel
{
    public CandidateDetailDto PrimaryCandidate { get; set; } = new();
    public CandidateDetailDto SecondaryCandidate { get; set; } = new();

    public string DuplicateReason { get; set; } = string.Empty;
    public bool HasConflictingActiveApplications { get; set; }
    public string ConflictWarningMessage { get; set; } = string.Empty;
}

public class CandidateDetailDto
{
    public Guid CandidateId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public string? CurrentCompany { get; set; }
    public string? CurrentTitle { get; set; }
    public string? LinkedinUrl { get; set; }
    public string SourceName { get; set; } = "Cổng tuyển dụng";
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedAtDisplay { get; set; } = string.Empty;

    public List<CandidateResumeItemDto> Resumes { get; set; } = new();
    public List<CandidateApplicationItemDto> Applications { get; set; } = new();
}

public class CandidateResumeItemDto
{
    public Guid ResumeId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public bool IsPrimary { get; set; }
    public string CreatedAtDisplay { get; set; } = string.Empty;
}

public class CandidateApplicationItemDto
{
    public Guid ApplicationId { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string TrackingCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public int StageOrder { get; set; }
    public string StatusDisplay { get; set; } = string.Empty;
    public string StatusBadgeClass { get; set; } = string.Empty;
    public string AppliedAtDisplay { get; set; } = string.Empty;

    public List<CandidateInterviewItemDto> Interviews { get; set; } = new();
}

public class CandidateInterviewItemDto
{
    public Guid InterviewId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string RoundTitle { get; set; } = string.Empty;
    public string StartTimeDisplay { get; set; } = string.Empty;
    public string StatusDisplay { get; set; } = string.Empty;
    public string PanelistNames { get; set; } = string.Empty;
    public string? ResultNotes { get; set; }
}
