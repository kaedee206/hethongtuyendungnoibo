namespace Ats.Web.Models.ViewModels.CandidateMerge;

/// <summary>
/// Đại diện cho một ứng viên trong nhóm nghi vấn trùng lặp
/// </summary>
public class DuplicateCandidateItemDto
{
    public Guid CandidateId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? NormalizedPhone { get; set; }
    public string? CurrentCompany { get; set; }
    public string? CurrentTitle { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedDateDisplay { get; set; } = string.Empty;
    public string SourceName { get; set; } = "Cổng tuyển dụng";
    
    public int ApplicationsCount { get; set; }
    public int ResumesCount { get; set; }
    public List<string> AppliedJobTitles { get; set; } = new();
    public string LatestStageName { get; set; } = string.Empty;
    public string AvatarLetter => !string.IsNullOrWhiteSpace(FullName) ? FullName.Trim()[..1].ToUpper() : "?";
}

/// <summary>
/// Đại diện cho một nhóm/cặp hồ sơ ứng viên nghi vấn trùng lặp
/// </summary>
public class DuplicateCandidateGroupDto
{
    public string GroupKey { get; set; } = string.Empty;
    public DuplicateType DuplicateType { get; set; }
    public string DuplicateReason { get; set; } = string.Empty;
    public string MatchedValue { get; set; } = string.Empty; // SĐT hoặc Email trùng
    public int ConfidenceScore { get; set; } = 100; // 100% (cả email + SĐT), 90% (SĐT), 85% (Email)
    public string ConfidenceBadgeClass => ConfidenceScore >= 95 ? "bg-danger-subtle text-danger" : "bg-warning-subtle text-warning-emphasis";

    public List<DuplicateCandidateItemDto> Candidates { get; set; } = new();

    public Guid PrimarySuggestedId => Candidates.OrderBy(c => c.CreatedAt).FirstOrDefault()?.CandidateId ?? Guid.Empty;
    public Guid SecondarySuggestedId => Candidates.OrderByDescending(c => c.CreatedAt).FirstOrDefault()?.CandidateId ?? Guid.Empty;
}

public enum DuplicateType
{
    BothEmailAndPhone = 1,
    PhoneOnly = 2,
    EmailOnly = 3
}
