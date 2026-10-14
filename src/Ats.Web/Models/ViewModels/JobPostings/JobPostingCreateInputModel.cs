using System.ComponentModel.DataAnnotations;
using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.JobPostings;

/// <summary>
/// Model nhận dữ liệu tạo mới tin tuyển dụng từ yêu cầu tuyển dụng đã duyệt (SCRUM-29).
/// </summary>
public class JobPostingCreateInputModel : IValidatableObject
{
    /// <summary>
    /// ID của yêu cầu tuyển dụng đã được phê duyệt.
    /// </summary>
    [Required(ErrorMessage = "Vui lòng chọn yêu cầu tuyển dụng hợp lệ.")]
    public Guid RequisitionId { get; set; }

    // ==========================================
    // DỮ LIỆU KẾ THỪA TỪ REQUISITION (CHO PHÉP SỬA)
    // ==========================================

    [Required(ErrorMessage = "Tiêu đề tin tuyển dụng không được để trống.")]
    [StringLength(250, ErrorMessage = "Tiêu đề tin tuyển dụng không được vượt quá 250 ký tự.")]
    [Display(Name = "Tiêu đề tin tuyển dụng")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Mô tả công việc (JD)")]
    public string? JobDescription { get; set; }

    [Display(Name = "Yêu cầu ứng viên")]
    public string? Requirements { get; set; }

    [Display(Name = "Quyền lợi & Phúc lợi")]
    public string? Benefits { get; set; }

    [Range(0, 1_000_000_000, ErrorMessage = "Mức lương tối thiểu phải từ 0 đến 1.000.000.000 VNĐ.")]
    [Display(Name = "Mức lương tối thiểu")]
    public decimal? MinSalary { get; set; }

    [Range(0, 1_000_000_000, ErrorMessage = "Mức lương tối đa phải từ 0 đến 1.000.000.000 VNĐ.")]
    [Display(Name = "Mức lương tối đa")]
    public decimal? MaxSalary { get; set; }

    [StringLength(10, ErrorMessage = "Đơn vị tiền tệ không được vượt quá 10 ký tự.")]
    [Display(Name = "Đơn vị tiền tệ")]
    public string Currency { get; set; } = "VND";

    // ==========================================
    // DỮ LIỆU KHAI BÁO MỚI BẮT BUỘC
    // ==========================================

    [Required(ErrorMessage = "Địa điểm làm việc không được để trống.")]
    [StringLength(250, ErrorMessage = "Địa điểm làm việc không được vượt quá 250 ký tự.")]
    [Display(Name = "Địa điểm làm việc")]
    public string WorkLocation { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn hình thức làm việc.")]
    [Display(Name = "Hình thức làm việc")]
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FULL_TIME;

    [Display(Name = "Hạn chót nộp hồ sơ")]
    public DateTime? ApplicationDeadline { get; set; }

    [Display(Name = "Công khai mức lương")]
    public bool IsSalaryDisclosed { get; set; } = true;

    // ==========================================
    // CỜ TRẠNG THÁI KHI LƯU
    // ==========================================

    /// <summary>
    /// true = Lưu nháp (DRAFT), false = Đăng ngay (PUBLISHED).
    /// </summary>
    [Display(Name = "Lưu nháp")]
    public bool SaveAsDraft { get; set; } = false;

    // ==========================================
    // DỮ LIỆU HỖ TRỢ HIỂN THỊ TRÊN GIAO DIỆN
    // ==========================================
    public string? RequisitionCode { get; set; }
    public string? DepartmentName { get; set; }
    public string? JobPositionTitle { get; set; }
    public int Quantity { get; set; }
    public DateOnly? TargetHireDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // 1. Kiểm tra dải lương
        if (MinSalary.HasValue && MaxSalary.HasValue && MaxSalary.Value < MinSalary.Value)
        {
            yield return new ValidationResult(
                "Mức lương tối đa phải lớn hơn hoặc bằng mức lương tối thiểu.",
                [nameof(MaxSalary)]);
        }

        // 2. Validate hạn chót nộp hồ sơ (ApplicationDeadline phải lớn hơn ngày hiện tại UTC)
        if (ApplicationDeadline.HasValue)
        {
            var deadlineUtc = ApplicationDeadline.Value.Kind == DateTimeKind.Utc
                ? ApplicationDeadline.Value
                : DateTime.SpecifyKind(ApplicationDeadline.Value, DateTimeKind.Utc);

            if (deadlineUtc <= DateTime.UtcNow)
            {
                yield return new ValidationResult(
                    "Hạn chót nộp hồ sơ bắt buộc phải lớn hơn thời điểm hiện tại (UTC).",
                    [nameof(ApplicationDeadline)]);
            }
        }
        else if (!SaveAsDraft)
        {
            yield return new ValidationResult(
                "Vui lòng chọn hạn chót nộp hồ sơ khi đăng tin tuyển dụng.",
                [nameof(ApplicationDeadline)]);
        }

        // 3. Nếu đăng tin ngay (PUBLISHED), các trường nội dung không được để trống
        if (!SaveAsDraft)
        {
            if (string.IsNullOrWhiteSpace(JobDescription))
            {
                yield return new ValidationResult(
                    "Vui lòng nhập mô tả công việc (JD) trước khi đăng tin tuyển dụng.",
                    [nameof(JobDescription)]);
            }

            if (string.IsNullOrWhiteSpace(Requirements))
            {
                yield return new ValidationResult(
                    "Vui lòng nhập yêu cầu ứng viên trước khi đăng tin tuyển dụng.",
                    [nameof(Requirements)]);
            }

            if (string.IsNullOrWhiteSpace(WorkLocation))
            {
                yield return new ValidationResult(
                    "Vui lòng nhập địa điểm làm việc trước khi đăng tin tuyển dụng.",
                    [nameof(WorkLocation)]);
            }
        }
    }
}
