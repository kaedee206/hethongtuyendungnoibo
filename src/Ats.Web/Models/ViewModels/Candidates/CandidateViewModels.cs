using Ats.Web.Models.DTOs;
using Ats.Web.Models.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models.ViewModels.Candidates;

public class CandidateDetailsViewModel
{
    // Thông tin cá nhân
    public Guid CandidateId { get; set; }
    public Guid? UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string InitialLetter => string.IsNullOrWhiteSpace(FullName) ? "?" : FullName[..1].ToUpper();
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public string? CurrentCompany { get; set; }
    public string? CurrentTitle { get; set; }
    public string? LinkedinUrl { get; set; }
    public string? AvatarUrl { get; set; }
    public string Source { get; set; } = "PORTAL";
    public string? ReferrerName { get; set; }
    public bool IsBlacklisted { get; set; }
    public string? BlacklistReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string FormattedCreatedAt => CreatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm");

    // Thông tin bảo mật & Phân quyền truy cập
    public bool HasAccess { get; set; } = true;
    public string? AccessDeniedReason { get; set; }
    public bool IsAssignedRecruiter { get; set; }
    public bool IsHiringManager { get; set; }
    public string CurrentUserName { get; set; } = string.Empty;
    public string CurrentUserRole { get; set; } = string.Empty;
    public bool CanManageDossier { get; set; } = true;

    // Danh sách CV đính kèm & Xem trước
    public List<CandidateResumeViewModel> Resumes { get; set; } = [];
    public CandidateResumeViewModel? ActiveResume => Resumes.FirstOrDefault(r => r.IsPrimary) ?? Resumes.FirstOrDefault();

    // Lịch sử ứng tuyển các vị trí (toàn bộ các lần nộp đơn từ trước đến nay)
    public List<CandidateApplicationHistoryViewModel> ApplicationHistories { get; set; } = [];

    // Nhãn (Tags / Labels)
    public List<CandidateTagViewModel> Tags { get; set; } = [];

    // Ghi chú nội bộ (Recruitment Notes)
    public List<CandidateNoteViewModel> Notes { get; set; } = [];

    public List<CandidateTagCatalogItemViewModel> AvailableTags { get; set; } = [];
}

public class CandidateResumeViewModel
{
    public Guid ResumeId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileSizeDisplay => FileSize > 1024 * 1024
        ? $"{(double)FileSize / (1024 * 1024):F1} MB"
        : $"{(double)FileSize / 1024:F0} KB";
    public string MimeType { get; set; } = "application/pdf";
    public bool IsPrimary { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public string FormattedUploadedAt => UploadedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm");
    public string? ParsedText { get; set; }
    public bool IsPdf => MimeType.Contains("pdf", StringComparison.OrdinalIgnoreCase) || FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
}

public class CandidateApplicationHistoryViewModel
{
    public Guid ApplicationId { get; set; }
    public Guid JobPostingId { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string JobSlug { get; set; } = string.Empty;
    public string RequisitionCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string WorkLocation { get; set; } = string.Empty;
    public string EmploymentType { get; set; } = string.Empty;
    public string? SalaryDisplay { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }
    public string FormattedAppliedAt => AppliedAt.HasValue
        ? AppliedAt.Value.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm")
        : "Không rõ";

    // Giai đoạn & Trạng thái
    public Guid CurrentStageId { get; set; }
    public string CurrentStageName { get; set; } = string.Empty;
    public int StageOrder { get; set; }
    public string StageColor { get; set; } = "#2563EB";
    public string Status { get; set; } = "IN_PROCESS";
    public string StatusText => Status switch
    {
        "IN_PROCESS" => "Đang xử lý quy trình",
        "HIRED" => "Tuyển dụng thành công",
        "REJECTED" => "Từ chối hồ sơ",
        "WITHDRAWN" => "Ứng viên rút đơn",
        _ => Status
    };
    public string StatusBadgeClass => Status switch
    {
        "HIRED" => "bg-success text-white",
        "REJECTED" => "bg-danger text-white",
        "WITHDRAWN" => "bg-secondary text-white",
        _ => "bg-primary text-white"
    };

    public int? Rating { get; set; }
    public string? RejectionReason { get; set; }
    public string? RejectionNote { get; set; }

    // Người phụ trách
    public string? AssignedRecruiterName { get; set; }
    public string? AssignedRecruiterEmail { get; set; }
    public string? HiringManagerName { get; set; }
    public string? HiringManagerEmail { get; set; }

    // File CV sử dụng cho lần nộp này
    public Guid ResumeId { get; set; }
    public string ResumeFileName { get; set; } = string.Empty;
    public string ResumeFilePath { get; set; } = string.Empty;

    // Chi tiết phỏng vấn
    public List<CandidateInterviewHistoryItemViewModel> Interviews { get; set; } = [];

    // Chi tiết Offer nếu có
    public CandidateOfferHistoryItemViewModel? Offer { get; set; }

    // Lịch sử chuyển bước (Audit Trail)
    public List<CandidateStageHistoryItemViewModel> StageHistories { get; set; } = [];
}

public class CandidateInterviewHistoryItemViewModel
{
    public Guid InterviewId { get; set; }
    public int RoundNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string InterviewType { get; set; } = string.Empty;
    public string LocationOrLink { get; set; } = string.Empty;
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public string FormattedTime => StartTime.HasValue
        ? StartTime.Value.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm")
        : "Chưa ấn định";
    public string Status { get; set; } = "SCHEDULED";
    public string CandidateConfirmed { get; set; } = "PENDING";
    public List<string> Panelists { get; set; } = [];
    public decimal? OverallScore { get; set; }
    public string? Recommendation { get; set; }
    public string? Feedback { get; set; }
    public string? InterviewerName { get; set; }
}

public class CandidateOfferHistoryItemViewModel
{
    public Guid OfferId { get; set; }
    public decimal BaseSalary { get; set; }
    public decimal? BonusAllowance { get; set; }
    public decimal? TotalPackage { get; set; }
    public DateOnly? ProposedJoinDate { get; set; }
    public string ContractType { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING_APPROVAL";
    public string FormattedBaseSalary => $"{BaseSalary:N0} VNĐ";
    public string FormattedJoinDate => ProposedJoinDate.HasValue ? ProposedJoinDate.Value.ToString("dd/MM/yyyy") : "Chưa ấn định";
}

public class CandidateStageHistoryItemViewModel
{
    public Guid Id { get; set; }
    public string? FromStageName { get; set; }
    public string ToStageName { get; set; } = string.Empty;
    public string? ChangedByUserName { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string FormattedCreatedAt => CreatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm");
}

public class CandidateTagViewModel
{
    public Guid Id { get; set; }
    public Guid CatalogId { get; set; }
    public string TagName { get; set; } = string.Empty;
    public string ColorCode { get; set; } = "#059669";
    public string? CreatedByUserName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CandidateTagCatalogItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColorCode { get; set; } = "#059669";
}

public class CandidateNoteViewModel
{
    public Guid Id { get; set; }
    public Guid AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorRole { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string NoteType { get; set; } = "GENERAL";
    public string NoteTypeBadge => NoteType switch
    {
        "INTERVIEW" => "Phỏng vấn",
        "SALARY" => "Đãi ngộ",
        "EVALUATION" => "Đánh giá",
        "RECRUITER_CALL" => "Trao đổi ứng viên",
        "FLAG" => "Lưu ý quan trọng",
        _ => "Ghi chú chung"
    };
    public string NoteTypeClass => NoteType switch
    {
        "INTERVIEW" => "bg-info text-dark",
        "SALARY" => "bg-warning text-dark",
        "EVALUATION" => "bg-success text-white",
        "FLAG" => "bg-danger text-white",
        _ => "bg-secondary text-white"
    };
    public DateTimeOffset CreatedAt { get; set; }
    public string FormattedCreatedAt => CreatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm");
    public bool CanDelete { get; set; }
}

public class CandidateListViewModel
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    public string? CurrentTitle { get; set; }
    public string? CurrentCompany { get; set; }
    public int ApplicationCount { get; set; }
    public string? LatestJobTitle { get; set; }
    public Guid? LatestJobPostingId { get; set; }
    public CandidateStage? LatestStage { get; set; }
    public string? LatestStageName { get; set; }
    public string? LatestStageColor { get; set; }
    public CandidateSource Source { get; set; } = CandidateSource.PORTAL;
    public string SourceDisplayName => Source switch
    {
        CandidateSource.PORTAL => "Cổng tuyển dụng",
        CandidateSource.REFERRAL => "Giới thiệu nội bộ",
        CandidateSource.LINKEDIN => "LinkedIn",
        CandidateSource.HEADHUNTER => "Headhunter",
        _ => "Khác"
    };
    public int? Rating { get; set; }
    public string RatingStars => Rating.HasValue && Rating.Value > 0
        ? string.Concat(Enumerable.Repeat("⭐", Math.Clamp(Rating.Value, 1, 5)))
        : "—";
    public string? CoverLetterSnippet { get; set; }
    public bool HasCoverLetter => !string.IsNullOrWhiteSpace(CoverLetterSnippet);
    public DateTimeOffset? LatestAppliedAt { get; set; }
    public string FormattedLatestAppliedAt => LatestAppliedAt.HasValue
        ? LatestAppliedAt.Value.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy")
        : "Chưa nộp";
    public List<CandidateTagViewModel> Tags { get; set; } = [];
    public bool IsBlacklisted { get; set; }
}

/// <summary>
/// ViewModel chính cho màn hình danh sách, tra cứu và lọc hồ sơ ứng viên (SCRUM-44).
/// </summary>
public class CandidateSearchIndexViewModel
{
    public PagedResult<CandidateListViewModel> PagedResult { get; set; } = new();
    public IReadOnlyList<CandidateListViewModel> Candidates => PagedResult.Items;
    public CandidateFilterCriteria Criteria { get; set; } = new();
    public List<SavedCandidateFilterDto> SavedFilters { get; set; } = [];

    // Danh sách chọn lựa cho bộ lọc
    public List<SelectListItem> AvailableJobPostings { get; set; } = [];
    public List<string> AvailableTags { get; set; } = [];
    public List<SelectListItem> AvailableStages { get; set; } = [];
    public List<SelectListItem> AvailableSources { get; set; } = [];

    // Số liệu thống kê nhanh
    public int TotalCandidates => PagedResult.TotalCount;
    public int LastMonthCandidatesCount { get; set; }
    public int HighRatingCandidatesCount { get; set; }
    public int MultiAppCandidatesCount { get; set; }
    public int TaggedCandidatesCount { get; set; }
}

public class CandidateTagInputModel
{
    public Guid CandidateId { get; set; }
    public Guid CatalogId { get; set; }
}

public class CandidateNoteInputModel
{
    public Guid CandidateId { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(4000)]
    public string Content { get; set; } = string.Empty;
    public string NoteType { get; set; } = "GENERAL";
}
