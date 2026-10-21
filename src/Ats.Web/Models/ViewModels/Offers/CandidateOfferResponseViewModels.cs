using Ats.Web.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.Offers;

/// <summary>
/// Model hiển thị thông tin chi tiết thư mời nhận việc (Offer Letter) cho Ứng viên xem và phản hồi (SCRUM-342).
/// </summary>
public class CandidateOfferDetailsViewModel
{
    public Guid OfferId { get; set; }
    public Guid ApplicationId { get; set; }
    public string Token { get; set; } = string.Empty;

    // Thông tin ứng viên
    public Guid? CandidateUserId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string? CandidatePhone { get; set; }

    // Thông tin công việc
    public string JobTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string WorkLocation { get; set; } = string.Empty;
    public string ContractType { get; set; } = string.Empty;

    // Gói đãi ngộ
    public decimal BaseSalary { get; set; }
    public decimal BonusAllowance { get; set; }
    public decimal TotalPackage { get; set; }
    public int ProbationPeriodMonths { get; set; }
    public int ProbationSalaryPercentage { get; set; } = 85;

    // Thời hạn và ngày tiếp nhận
    public DateOnly? ProposedJoinDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public string? ExpirationDateFormatted => ExpirationDate?.ToString("dd/MM/yyyy");
    public string? ProposedJoinDateFormatted => ProposedJoinDate?.ToString("dd/MM/yyyy");

    // Đếm ngược và trạng thái hiệu lực
    public bool IsExpired { get; set; }
    public int DaysRemaining { get; set; }
    public int HoursRemaining { get; set; }
    public string RemainingTimeDisplay { get; set; } = string.Empty;

    // Trạng thái Offer
    public OfferStatus Status { get; set; }
    public string StatusDisplay { get; set; } = string.Empty;
    public bool CanRespond { get; set; }

    // Phản hồi của ứng viên (nếu đã phản hồi)
    public DateTimeOffset? CandidateRespondedAt { get; set; }
    public string? CandidateRespondedAtFormatted => CandidateRespondedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string? CandidateFeedback { get; set; }
    public bool HasResponded => Status == OfferStatus.ACCEPTED || Status == OfferStatus.REJECTED;
    public bool IsAccepted => Status == OfferStatus.ACCEPTED;
    public bool IsRejected => Status == OfferStatus.REJECTED;

    // Thư mời chính thức
    public string? OfferLetterPath { get; set; }
    public string? Notes { get; set; }
    public string RecruiterName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = "Tập đoàn Công nghệ NoveraTech";

    // Phản hồi nhanh từ hành động (nếu có)
    public bool? FeedbackIsSuccess { get; set; }
    public string? FeedbackMessage { get; set; }
}

/// <summary>
/// Model nhận phản hồi từ Ứng viên: Chấp nhận hoặc Từ chối kèm ô ghi ý kiến (SCRUM-342).
/// </summary>
public class CandidateOfferResponseInputModel
{
    [Required(ErrorMessage = "Mã xác thực thư mời không hợp lệ.")]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Quyết định của ứng viên: ACCEPT hoặc DECLINE
    /// </summary>
    [Required(ErrorMessage = "Vui lòng chọn quyết định Chấp nhận hoặc Từ chối.")]
    public string Decision { get; set; } = string.Empty;

    /// <summary>
    /// Ô ghi ý kiến / phản hồi / nguyện vọng của ứng viên
    /// </summary>
    [MaxLength(2000, ErrorMessage = "Ý kiến phản hồi không được vượt quá 2000 ký tự.")]
    [Display(Name = "Ý kiến phản hồi")]
    public string? Feedback { get; set; }
}

/// <summary>
/// Kết quả xử lý phản hồi thư mời nhận việc từ ứng viên.
/// </summary>
public class CandidateOfferResponseResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public OfferStatus? NewStatus { get; set; }
    public string? CandidateFeedback { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }

    public static CandidateOfferResponseResult Ok(string message, OfferStatus newStatus, string? feedback = null) =>
        new() { Success = true, Message = message, NewStatus = newStatus, CandidateFeedback = feedback, RespondedAt = DateTimeOffset.UtcNow };

    public static CandidateOfferResponseResult Fail(string message) =>
        new() { Success = false, Message = message };
}

/// <summary>
/// Model gửi thư mời nhận việc kèm liên kết phản hồi cho ứng viên (SCRUM-342).
/// </summary>
public class SendOfferToCandidateInputModel
{
    [Required(ErrorMessage = "Thiếu định danh Offer.")]
    public Guid OfferId { get; set; }

    [MaxLength(1000, ErrorMessage = "Lời nhắn gửi kèm không được vượt quá 1000 ký tự.")]
    public string? CustomMessage { get; set; }
}
