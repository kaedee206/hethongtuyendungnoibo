namespace Ats.Web.Models.ViewModels.TalentPool;

/// <summary>
/// ViewModel cho màn hình Quản lý Kho Ứng viên Tiềm năng (Scrum 45).
/// </summary>
public class TalentPoolListViewModel
{
    public List<TalentPoolItemViewModel> Items { get; set; } = [];

    // Bộ lọc hiện tại
    public string? SearchQuery { get; set; }
    public string? SelectedJobTitle { get; set; }
    public string? SelectedSkill { get; set; }
    public string? SelectedStatus { get; set; }
    public int? MinRating { get; set; }

    // Helpers cho View
    public string? CurrentSearch => SearchQuery;
    public string? CurrentTargetJobTitle => SelectedJobTitle;
    public string? CurrentSkills => SelectedSkill;
    public string? CurrentStatus => SelectedStatus;

    // Thống kê số liệu KPI
    public int TotalCount => Items.Count;
    public int TotalInPool => TotalCount;
    public int AvailableCount => Items.Count(x => x.Status == "AVAILABLE");
    public int ContactedCount => Items.Count(x => x.Status == "CONTACTED");
    public int ReappliedCount => Items.Count(x => x.Status == "REAPPLIED" || x.Status == "HIRED");

    // Dữ liệu cho bộ lọc
    public List<string> AvailableJobTitles { get; set; } = [];
    public List<string> AvailableSkills { get; set; } = [];

    // Danh sách vị trí tuyển dụng đang mở để chọn gợi ý hoặc mời ứng tuyển
    public List<OpenJobOptionViewModel> OpenJobs { get; set; } = [];
}

public class TalentPoolItemViewModel
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string CandidateFullName => FullName;
    public string Email { get; set; } = string.Empty;
    public string CandidateEmail => Email;
    public string? Phone { get; set; }
    public string? CandidatePhone => Phone;
    public string InitialLetter => string.IsNullOrWhiteSpace(FullName) ? "?" : FullName[..1].ToUpper();
    public string CandidateInitials => InitialLetter;

    // Chức danh & Kỹ năng
    public string TargetJobTitle { get; set; } = string.Empty;
    public string? SkillsRaw { get; set; }
    public string? Skills => SkillsRaw;
    public List<string> SkillsList => string.IsNullOrWhiteSpace(SkillsRaw)
        ? []
        : SkillsRaw.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    public List<string> SkillList => SkillsList;

    public int? ExperienceYears { get; set; }
    public int? Rating { get; set; }
    public string RatingStars => Rating.HasValue ? string.Concat(Enumerable.Repeat("⭐", Math.Clamp(Rating.Value, 1, 5))) : "Chưa đánh giá";

    // Vòng cuối & Lý do lưu kho (Trọng tâm Scrum 45)
    public string? LastStageReached { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }

    // Trạng thái
    public string Status { get; set; } = "AVAILABLE";
    public string StatusDisplay => Status switch
    {
        "AVAILABLE" => "Sẵn sàng tiếp cận",
        "CONTACTED" => "Đã liên hệ",
        "REAPPLIED" => "Đã mời ứng tuyển",
        "HIRED" => "Tuyển dụng thành công",
        "ARCHIVED" => "Lưu trữ",
        _ => Status
    };

    public string StatusBadgeClass => Status switch
    {
        "AVAILABLE" => "bg-success text-white",
        "CONTACTED" => "bg-info text-dark",
        "REAPPLIED" => "bg-primary text-white",
        "HIRED" => "bg-success-subtle text-success border border-success",
        _ => "bg-secondary text-white"
    };

    // Người tạo & Thời gian
    public string AddedByUserName { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; }
    public string FormattedAddedAt => AddedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm");

    // Đơn ứng tuyển gốc
    public Guid? SourceApplicationId { get; set; }
    public string? SourceJobTitle { get; set; }

    // CV đính kèm
    public bool HasResume { get; set; }
    public Guid? ResumeId { get; set; }
    public string? ResumeFileName { get; set; }
    public string? ResumeFilePath { get; set; }
}

public class TalentPoolSuggestionListViewModel
{
    public Guid JobPostingId { get; set; }
    public Guid JobId => JobPostingId;
    public string JobTitle { get; set; } = string.Empty;
    public string JobSlug { get; set; } = string.Empty;
    public string JobCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string WorkLocation { get; set; } = string.Empty;
    public string? SalaryDisplay { get; set; }
    public string? Requirements { get; set; }
    public string? JobDescription { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public int TotalPoolCandidates { get; set; }
    public int MatchingCandidatesCount => Suggestions.Count;

    public List<string> SkillsRequired { get; set; } = [];
    public List<OpenJobOptionViewModel> AvailableOpenJobs { get; set; } = [];

    public List<TalentPoolCandidateSuggestionViewModel> Suggestions { get; set; } = [];
}

public class TalentPoolCandidateSuggestionViewModel
{
    public TalentPoolItemViewModel Candidate { get; set; } = null!;

    public Guid TalentPoolId => Candidate?.Id ?? Guid.Empty;
    public Guid CandidateId => Candidate?.CandidateId ?? Guid.Empty;
    public string CandidateFullName => Candidate?.FullName ?? string.Empty;
    public string CandidateEmail => Candidate?.Email ?? string.Empty;
    public string? CandidatePhone => Candidate?.Phone;
    public string TargetJobTitle => Candidate?.TargetJobTitle ?? string.Empty;
    public string? LastStageReached => Candidate?.LastStageReached;
    public string Reason => Candidate?.Reason ?? string.Empty;
    public int? Rating => Candidate?.Rating;
    public bool HasAppliedToThisJob => IsAlreadyApplied;
    public string CurrentApplicationStatus => IsAlreadyApplied ? "Đang xử lý" : "";

    /// <summary>
    /// Điểm phần trăm độ tương đồng và phù hợp (0 - 100%)
    /// </summary>
    public int MatchScore { get; set; }

    public string MatchLevel => MatchScore switch
    {
        >= 80 => "Rất phù hợp",
        >= 60 => "Phù hợp",
        _ => "Tiềm năng xem xét"
    };

    public string MatchLevelBadgeClass => MatchScore switch
    {
        >= 80 => "bg-success text-white",
        >= 60 => "bg-primary text-white",
        _ => "bg-warning text-dark"
    };

    /// <summary>
    /// Danh sách các kỹ năng trùng khớp giữa ứng viên và yêu cầu công việc
    /// </summary>
    public List<string> MatchingSkills { get; set; } = [];

    /// <summary>
    /// Các lý do giải thích tại sao hệ thống đề xuất ứng viên này
    /// </summary>
    public List<string> MatchReasons { get; set; } = [];

    /// <summary>
    /// Kiểm tra ứng viên đã có đơn nộp vào vị trí này chưa
    /// </summary>
    public bool IsAlreadyApplied { get; set; }
}

public class OpenJobOptionViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string Department => DepartmentName;
    public string WorkLocation { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class AddTalentPoolCandidateInputModel
{
    public Guid CandidateId { get; set; }
    public Guid? SourceApplicationId { get; set; }
    public string? LastStageReached { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string TargetJobTitle { get; set; } = string.Empty;
    public string? Skills { get; set; }
    public int? ExperienceYears { get; set; }
    public int? Rating { get; set; }
    public string? Notes { get; set; }
    public string? ReturnUrl { get; set; }
}

public class UpdateTalentPoolCandidateInputModel
{
    public Guid Id { get; set; }
    public string TargetJobTitle { get; set; } = string.Empty;
    public string? Skills { get; set; }
    public int? Rating { get; set; }
    public string Status { get; set; } = "AVAILABLE";
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class InviteTalentPoolCandidateInputModel
{
    public Guid TalentPoolCandidateId { get; set; }
    public Guid TalentPoolId
    {
        get => TalentPoolCandidateId;
        set => TalentPoolCandidateId = value;
    }
    public Guid JobPostingId { get; set; }
    public string? Note { get; set; }
    public bool SendEmail { get; set; } = true;
}

public class RemoveTalentPoolCandidateInputModel
{
    public Guid Id { get; set; }
    public string? Reason { get; set; }
}
