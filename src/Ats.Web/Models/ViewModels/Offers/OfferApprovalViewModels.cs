using System.ComponentModel.DataAnnotations;
using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Offers;

/// <summary>
/// Các mã hành động phê duyệt Offer chuẩn hóa (SCRUM-341).
/// </summary>
public static class OfferApprovalDecisionType
{
    public const string Approve = "APPROVE";
    public const string Reject = "REJECT";
    public const string RequestChanges = "CHANGES_REQUESTED";
}

/// <summary>
/// Model nhận dữ liệu quyết định phê duyệt Offer từ Form/AJAX (SCRUM-341).
/// </summary>
public class OfferApprovalDecisionInputModel
{
    [Required(ErrorMessage = "Thiếu định danh đề xuất Offer.")]
    public Guid OfferId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn hành động quyết định (Duyệt, Từ chối hoặc Yêu cầu sửa).")]
    public string Decision { get; set; } = string.Empty;

    /// <summary>
    /// Ý kiến của người duyệt. Bắt buộc khi Từ chối (REJECT) hoặc Yêu cầu sửa (CHANGES_REQUESTED).
    /// </summary>
    public string? Comment { get; set; }
}

/// <summary>
/// Kết quả xử lý hành động phê duyệt Offer (SCRUM-341).
/// </summary>
public class OfferApprovalOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? OfferId { get; set; }
    public OfferStatus? OfferStatus { get; set; }
    public ApprovalStatus? StepStatus { get; set; }
    public bool IsFinalApproval { get; set; }
    public int? NextLevel { get; set; }

    public static OfferApprovalOperationResult Ok(
        string message,
        Guid offerId,
        OfferStatus offerStatus,
        ApprovalStatus stepStatus,
        bool isFinalApproval = false,
        int? nextLevel = null) => new()
    {
        Success = true,
        Message = message,
        OfferId = offerId,
        OfferStatus = offerStatus,
        StepStatus = stepStatus,
        IsFinalApproval = isFinalApproval,
        NextLevel = nextLevel
    };

    public static OfferApprovalOperationResult Fail(string message) => new()
    {
        Success = false,
        Message = message
    };
}

/// <summary>
/// ViewModel hàng đợi phê duyệt Offer (Offer Approval Queue).
/// </summary>
public class OfferApprovalQueueViewModel
{
    public int TotalPending { get; set; }
    public int WithinBandPending { get; set; }
    public int AboveBandPending { get; set; }
    public List<OfferApprovalQueueItemViewModel> Items { get; set; } = new();
}

/// <summary>
/// Thẻ hiển thị tóm tắt một đề xuất Offer trong hàng đợi phê duyệt.
/// </summary>
public class OfferApprovalQueueItemViewModel
{
    public Guid OfferId { get; set; }
    public Guid ApplicationId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;

    public decimal ProposedSalary { get; set; }
    public string SalaryDisplay { get; set; } = string.Empty;

    public decimal? StandardMinSalary { get; set; }
    public decimal? StandardMaxSalary { get; set; }
    public string SalaryBandDisplay { get; set; } = string.Empty;

    public bool IsAboveBudget { get; set; }
    public decimal? PercentOverBand { get; set; }
    public string? SalaryJustification { get; set; }

    public int CurrentLevel { get; set; } = 1;
    public string CurrentLevelDisplay { get; set; } = string.Empty;
    public string RequiredApproverRole { get; set; } = string.Empty;
    public string RequiredApproverRoleDisplay { get; set; } = string.Empty;

    public bool CanCurrentUserApprove { get; set; }
    public string CreatedAtFormatted { get; set; } = string.Empty;
    public string RecruiterName { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel chi tiết một đề xuất Offer kèm so sánh hạn mức và lịch sử duyệt (SCRUM-341).
/// </summary>
public class OfferApprovalDetailViewModel
{
    public Guid OfferId { get; set; }
    public Guid ApplicationId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string? CandidatePhone { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string WorkLocation { get; set; } = string.Empty;

    public decimal ProposedSalary { get; set; }
    public decimal BonusAllowance { get; set; }
    public decimal TotalPackage { get; set; }
    public string SalaryDisplay { get; set; } = string.Empty;
    public string TotalPackageDisplay { get; set; } = string.Empty;

    public string ProposedJoinDateFormatted { get; set; } = string.Empty;
    public string ExpirationDateFormatted { get; set; } = string.Empty;
    public int ProbationPeriodMonths { get; set; } = 2;
    public string ContractType { get; set; } = string.Empty;

    // So sánh hạn mức dải lương chuẩn
    public decimal? StandardMinSalary { get; set; }
    public decimal? StandardMaxSalary { get; set; }
    public string SalaryBandDisplay { get; set; } = string.Empty;
    public bool IsAboveBudget { get; set; }
    public decimal? PercentOverBand { get; set; }
    public string? SalaryJustification { get; set; }
    public string? Notes { get; set; }

    // Trạng thái phê duyệt
    public OfferStatus Status { get; set; }
    public string StatusDisplay { get; set; } = string.Empty;
    public int CurrentLevel { get; set; } = 1;
    public string CurrentLevelDisplay { get; set; } = string.Empty;
    public int TotalLevelsRequired { get; set; } = 1;

    // Quyền của người xem hiện tại
    public bool CanApprove { get; set; }
    public bool CanReject { get; set; }
    public bool CanRequestChanges { get; set; }
    public string? UserActionRestrictionReason { get; set; }

    public string RecruiterName { get; set; } = string.Empty;
    public string CreatedAtFormatted { get; set; } = string.Empty;

    // Lịch sử duyệt bất biến (Immutable Approval History)
    public List<OfferApprovalHistoryItemViewModel> ApprovalHistory { get; set; } = new();
}

/// <summary>
/// Bản ghi một bước trong lịch sử phê duyệt không thể sửa và không thể xóa (SCRUM-341).
/// </summary>
public class OfferApprovalHistoryItemViewModel
{
    public Guid Id { get; set; }
    public int Level { get; set; }
    public string LevelDisplay { get; set; } = string.Empty;

    public Guid ApproverId { get; set; }
    public string ApproverName { get; set; } = string.Empty;
    public string ApproverRole { get; set; } = string.Empty;
    public string ApproverRoleDisplay { get; set; } = string.Empty;

    public ApprovalStatus Status { get; set; }
    public string StatusDisplay { get; set; } = string.Empty;
    public string BadgeCssClass { get; set; } = string.Empty;

    public string? Comment { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string DecidedAtFormatted { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
