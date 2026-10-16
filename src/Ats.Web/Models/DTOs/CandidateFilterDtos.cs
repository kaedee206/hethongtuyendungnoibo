using Ats.Web.Models.Enums;

namespace Ats.Web.Models.DTOs;

/// <summary>
/// Tiêu chí tìm kiếm và lọc hồ sơ ứng viên theo nhiều điều kiện (SCRUM-44).
/// </summary>
public class CandidateFilterCriteria
{
    /// <summary>
    /// Từ khóa tìm kiếm tổng hợp: Họ tên, Email, Số điện thoại, Chức danh.
    /// </summary>
    public string? Search { get; set; }

    /// <summary>
    /// Từ khóa tìm kiếm riêng trong thư giới thiệu (Cover Letter / Lời nhắn / Ghi chú giới thiệu).
    /// </summary>
    public string? CoverLetterKeyword { get; set; }

    /// <summary>
    /// Lọc theo vị trí tin tuyển dụng (JobPostingId).
    /// </summary>
    public Guid? JobPostingId { get; set; }

    /// <summary>
    /// Lọc theo giai đoạn tuyển dụng (APPLIED, SCREENING, INTERVIEW, OFFER, HIRED, REJECTED).
    /// </summary>
    public CandidateStage? Stage { get; set; }

    /// <summary>
    /// Lọc theo kênh nguồn ứng viên (PORTAL, REFERRAL, LINKEDIN, HEADHUNTER, OTHER).
    /// </summary>
    public CandidateSource? Source { get; set; }

    /// <summary>
    /// Lọc theo nhãn chuyên môn (Tags).
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// Khoảng thời gian nộp: Từ ngày.
    /// </summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>
    /// Khoảng thời gian nộp: Đến ngày.
    /// </summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>
    /// Bộ chọn thời gian tiện ích: "last_month" (Tháng trước), "this_month" (Tháng này), "last_30_days", "last_7_days", "all".
    /// </summary>
    public string? DatePreset { get; set; }

    /// <summary>
    /// Mức đánh giá tối thiểu (1 - 5 sao).
    /// </summary>
    public int? MinRating { get; set; }

    /// <summary>
    /// Số trang hiện tại (Mặc định: 1).
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Số lượng bản ghi mỗi trang (Mặc định: 15).
    /// </summary>
    public int PageSize { get; set; } = 15;

    /// <summary>
    /// Kiểm tra xem có bất kỳ điều kiện lọc nào đang được kích hoạt hay không.
    /// </summary>
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Search) ||
        !string.IsNullOrWhiteSpace(CoverLetterKeyword) ||
        JobPostingId.HasValue ||
        Stage.HasValue ||
        Source.HasValue ||
        !string.IsNullOrWhiteSpace(Tag) ||
        FromDate.HasValue ||
        ToDate.HasValue ||
        (!string.IsNullOrWhiteSpace(DatePreset) && DatePreset != "all") ||
        MinRating.HasValue;
}

/// <summary>
/// DTO biểu diễn bộ lọc đã lưu của Recruiter (SCRUM-44).
/// </summary>
public class SavedCandidateFilterDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public CandidateFilterCriteria Criteria { get; set; } = new();
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string FormattedCreatedAt => CreatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm");
}

/// <summary>
/// Input Model để gửi yêu cầu lưu bộ lọc mới từ giao diện.
/// </summary>
public class SaveCandidateFilterInputModel
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefault { get; set; } = false;
    public CandidateFilterCriteria Criteria { get; set; } = new();
}
