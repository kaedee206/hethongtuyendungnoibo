using System.ComponentModel.DataAnnotations;
using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Offers;

/// <summary>
/// Model nhận dữ liệu tạo mới hoặc lưu nháp đề xuất Offer (SCRUM-340).
/// </summary>
public class JobOfferInputModel
{
    [Required(ErrorMessage = "Thiếu định danh hồ sơ ứng viên.")]
    public Guid ApplicationId { get; set; }

    public Guid? OfferId { get; set; }

    public Guid? JobPositionId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập hoặc chọn chức danh công việc.")]
    public string JobTitle { get; set; } = string.Empty;

    public string? WorkLocation { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mức lương cơ bản.")]
    [Range(1_000_000, 1_000_000_000, ErrorMessage = "Mức lương cơ bản phải từ 1.000.000 đến 1.000.000.000 VNĐ.")]
    public decimal BaseSalary { get; set; }

    [Range(0, 1_000_000_000, ErrorMessage = "Phụ cấp & Thưởng không được âm.")]
    public decimal? BonusAllowance { get; set; }

    public decimal? TotalPackage { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày dự kiến nhận việc.")]
    public DateOnly? ProposedJoinDate { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn thời hạn phản hồi Offer.")]
    public DateOnly? ExpirationDate { get; set; }

    [Range(0, 12, ErrorMessage = "Thời gian thử việc từ 0 đến 12 tháng.")]
    public int? ProbationPeriodMonths { get; set; } = 2;

    public string? ContractType { get; set; } = "Chính thức (Toàn thời gian)";

    /// <summary>
    /// Lý do giải trình bắt buộc nếu mức lương nằm ngoài dải chuẩn của chức danh.
    /// </summary>
    public string? SalaryJustification { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// True nếu lưu nháp (OfferStatus.DRAFT), False nếu gửi duyệt (OfferStatus.PENDING_APPROVAL).
    /// </summary>
    public bool IsDraft { get; set; } = false;
}

/// <summary>
/// ViewModel khởi tạo Form đề xuất Offer (SCRUM-340).
/// </summary>
public class JobOfferFormInitViewModel
{
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string? CandidatePhone { get; set; }

    public string JobPostingTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string? WorkLocation { get; set; }

    public Guid? DefaultJobPositionId { get; set; }
    public string DefaultJobTitle { get; set; } = string.Empty;

    public decimal? StandardMinSalary { get; set; }
    public decimal? StandardMaxSalary { get; set; }
    public string StandardSalaryRangeDisplay { get; set; } = "Chưa thiết lập";

    // Dữ liệu Offer hiện tại (nếu có bản nháp hoặc đang duyệt)
    public Guid? ExistingOfferId { get; set; }
    public OfferStatus? ExistingOfferStatus { get; set; }
    public string? ExistingOfferStatusDisplay { get; set; }
    public decimal? ExistingBaseSalary { get; set; }
    public decimal? ExistingBonusAllowance { get; set; }
    public DateOnly? ExistingProposedJoinDate { get; set; }
    public DateOnly? ExistingExpirationDate { get; set; }
    public int? ExistingProbationMonths { get; set; }
    public string? ExistingContractType { get; set; }
    public string? ExistingSalaryJustification { get; set; }
    public string? ExistingNotes { get; set; }
    public bool ExistingIsOutsideBand { get; set; }

    // Ràng buộc một offer hiệu lực
    public bool HasOtherActiveOffer { get; set; }
    public string? OtherActiveOfferWarning { get; set; }

    public List<JobPositionOptionViewModel> AvailablePositions { get; set; } = [];
}

/// <summary>
/// Option chức danh kèm dải lương chuẩn để hiển thị trong select.
/// </summary>
public class JobPositionOptionViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal? MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
    public string SalaryRangeDisplay { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel hiển thị thư mời nhận việc xem trước (SCRUM-340 Preview).
/// </summary>
public class JobOfferPreviewViewModel
{
    public Guid ApplicationId { get; set; }
    public Guid? OfferId { get; set; }

    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string? CandidatePhone { get; set; }

    public string JobTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string WorkLocation { get; set; } = "Tòa nhà Novera Tower, Cầu Giấy, Hà Nội";

    public decimal BaseSalary { get; set; }
    public decimal BonusAllowance { get; set; }
    public decimal TotalPackage { get; set; }

    public string FormattedBaseSalary => BaseSalary.ToString("N0") + " VNĐ";
    public string FormattedBonusAllowance => BonusAllowance.ToString("N0") + " VNĐ";
    public string FormattedTotalPackage => TotalPackage.ToString("N0") + " VNĐ";

    public DateOnly ProposedJoinDate { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public string FormattedProposedJoinDate => ProposedJoinDate.ToString("dd/MM/yyyy");
    public string FormattedExpirationDate => ExpirationDate.ToString("dd/MM/yyyy");

    public int ProbationPeriodMonths { get; set; } = 2;
    public int ProbationSalaryPercentage { get; set; } = 85;
    public decimal ProbationBaseSalary => Math.Round(BaseSalary * (ProbationSalaryPercentage / 100m));
    public string FormattedProbationBaseSalary => ProbationBaseSalary.ToString("N0") + " VNĐ";

    public string ContractType { get; set; } = "Chính thức (Toàn thời gian)";
    public string? Notes { get; set; }

    public string RecruiterName { get; set; } = string.Empty;
    public string RecruiterTitle { get; set; } = "Chuyên viên Tuyển dụng";
    public string CompanyName { get; set; } = "Tập đoàn Công nghệ NoveraTech";
    public string GeneratedDateFormatted { get; set; } = DateTime.UtcNow.ToString("dd/MM/yyyy");

    public bool IsOutsideBand { get; set; }
    public string? SalaryJustification { get; set; }
}

/// <summary>
/// Kết quả xử lý nghiệp vụ Offer.
/// </summary>
public class OfferOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? OfferId { get; set; }
    public bool IsDraft { get; set; }
    public List<string> Errors { get; set; } = [];

    public static OfferOperationResult Ok(string message, Guid? offerId = null, bool isDraft = false) =>
        new() { Success = true, Message = message, OfferId = offerId, IsDraft = isDraft };

    public static OfferOperationResult Fail(string message, List<string>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors ?? [] };
}
