using Ats.Web.Models.Enums;

namespace Ats.Web.Models.DTOs.Offers;

/// <summary>
/// DTO tiếp nhận yêu cầu từ chối Thư mời nhận việc (User Story SP-7).
/// </summary>
public class RejectOfferDto
{
    public Guid OfferId { get; set; }

    /// <summary>
    /// Danh mục lý do từ chối: Lương, Địa điểm, Đã nhận việc khác, Cá nhân, Khác
    /// </summary>
    public OfferRejectionReasonCategory ReasonCategory { get; set; }

    /// <summary>
    /// Ghi chú chi tiết phản hồi từ ứng viên
    /// </summary>
    public string? RejectionNotes { get; set; }

    /// <summary>
    /// Mức lương kỳ vọng của ứng viên (bắt buộc hoặc khuyến nghị nếu từ chối vì Lương)
    /// </summary>
    public decimal? ExpectedSalary { get; set; }

    /// <summary>
    /// Cờ gợi ý đưa ứng viên vào Kho tiềm năng (Talent Pool)
    /// </summary>
    public bool AddToTalentPool { get; set; } = false;

    /// <summary>
    /// Chức danh quan tâm khi đưa vào kho tiềm năng
    /// </summary>
    public string? TalentPoolTargetJobTitle { get; set; }

    /// <summary>
    /// Lý do đưa vào kho tiềm năng
    /// </summary>
    public string? TalentPoolReason { get; set; }

    /// <summary>
    /// Đánh giá tiềm năng (1 - 5 sao)
    /// </summary>
    public int? TalentPoolRating { get; set; }

    /// <summary>
    /// Ghi chú bổ sung khi lưu vào kho tiềm năng
    /// </summary>
    public string? TalentPoolNotes { get; set; }
}

/// <summary>
/// DTO tiếp nhận yêu cầu đàm phán / tạo Offer sửa đổi (Revised Offer Flow).
/// </summary>
public class ReviseOfferDto
{
    public Guid OriginalOfferId { get; set; }

    /// <summary>
    /// Mức lương cơ bản thỏa thuận mới (Gross / tháng)
    /// </summary>
    public decimal BaseSalary { get; set; }

    /// <summary>
    /// Phụ cấp & thưởng mới
    /// </summary>
    public decimal? BonusAllowance { get; set; }

    /// <summary>
    /// Tổng gói thu nhập (nếu không truyền sẽ tự tính = BaseSalary + BonusAllowance)
    /// </summary>
    public decimal? TotalPackage { get; set; }

    /// <summary>
    /// Ngày dự kiến gia nhập điều chỉnh
    /// </summary>
    public DateOnly? ProposedJoinDate { get; set; }

    /// <summary>
    /// Thời gian thử việc (tháng)
    /// </summary>
    public int? ProbationPeriodMonths { get; set; } = 2;

    /// <summary>
    /// Loại hợp đồng lao động
    /// </summary>
    public string? ContractType { get; set; }

    /// <summary>
    /// Ghi chú thương lượng / lý do sửa đổi
    /// </summary>
    public string? RevisionNotes { get; set; }
}

/// <summary>
/// DTO danh mục lý do từ chối dùng để bind lên Dropdown giao diện.
/// </summary>
public class OfferRejectionReasonCategoryDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool RequiresExpectedSalary { get; set; }
}

/// <summary>
/// DTO hiển thị chi tiết Offer và lịch sử phiên bản.
/// </summary>
public class OfferItemDto
{
    public Guid OfferId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public Guid? ParentOfferId { get; set; }
    public decimal BaseSalary { get; set; }
    public decimal? BonusAllowance { get; set; }
    public decimal TotalPackage { get; set; }
    public DateOnly? ProposedJoinDate { get; set; }
    public int? ProbationPeriodMonths { get; set; }
    public string ContractType { get; set; } = string.Empty;
    public OfferStatus Status { get; set; }
    public string StatusDisplay { get; set; } = string.Empty;
    public OfferRejectionReasonCategory? RejectionReasonCategory { get; set; }
    public string? RejectionReasonCategoryDisplay { get; set; }
    public string? RejectionNotes { get; set; }
    public decimal? ExpectedSalary { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedDateDisplay => CreatedAt.ToString("dd/MM/yyyy HH:mm");
    public string FormattedBaseSalary => $"{BaseSalary:N0} VNĐ";
    public string FormattedTotalPackage => $"{TotalPackage:N0} VNĐ";
    public string FormattedExpectedSalary => ExpectedSalary.HasValue ? $"{ExpectedSalary.Value:N0} VNĐ" : string.Empty;
    public string FormattedJoinDate => ProposedJoinDate.HasValue ? ProposedJoinDate.Value.ToString("dd/MM/yyyy") : "Chưa ấn định";
}
