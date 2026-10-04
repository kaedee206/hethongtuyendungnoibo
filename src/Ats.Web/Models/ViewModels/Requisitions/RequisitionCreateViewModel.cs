using System.ComponentModel.DataAnnotations;
using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Requisitions;

public class JobPositionOptionViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string JobLevel { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public decimal? MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
}

public class DepartmentOptionViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsUserDepartment { get; set; }
}

public class RequisitionCreateViewModel : IValidatableObject
{
    [Required(ErrorMessage = "Vui lòng chọn chức danh cần tuyển dụng.")]
    public Guid? JobPositionId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn phòng ban phụ trách yêu cầu tuyển dụng.")]
    public Guid? DepartmentId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập số lượng cần tuyển.")]
    [Range(1, 100, ErrorMessage = "Số lượng cần tuyển phải là số nguyên dương từ 1 đến 100 người.")]
    public int Quantity { get; set; } = 1;

    [Required(ErrorMessage = "Vui lòng chọn lý do tuyển dụng.")]
    public HeadcountType HeadcountType { get; set; } = HeadcountType.NEW_HEADCOUNT;

    [MaxLength(500, ErrorMessage = "Mô tả chi tiết lý do tuyển dụng không được vượt quá 500 ký tự.")]
    public string? ReasonDetail { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mức lương tối thiểu đề xuất.")]
    [Range(1_000_000, 1_000_000_000, ErrorMessage = "Mức lương tối thiểu phải từ 1.000.000 VNĐ đến 1.000.000.000 VNĐ.")]
    public decimal? MinSalary { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mức lương tối đa đề xuất.")]
    [Range(1_000_000, 1_000_000_000, ErrorMessage = "Mức lương tối đa phải từ 1.000.000 VNĐ đến 1.000.000.000 VNĐ.")]
    public decimal? MaxSalary { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày cần nhân sự có mặt.")]
    public DateOnly? TargetHireDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(30));

    /// <summary>
    /// Mô tả công việc chi tiết (trách nhiệm, nhiệm vụ chính, KPIs) định dạng HTML phong phú.
    /// Có thể để trống khi lưu nháp, nhưng bắt buộc khi gửi duyệt.
    /// </summary>
    public string? JobDescription { get; set; }

    /// <summary>
    /// Yêu cầu ứng viên chi tiết (trình độ học vấn, kinh nghiệm, kỹ năng, chứng chỉ) định dạng HTML phong phú.
    /// Có thể để trống khi lưu nháp, nhưng bắt buộc khi gửi duyệt.
    /// </summary>
    public string? Requirements { get; set; }

    /// <summary>
    /// Cờ xác định hành động nộp form: true = Lưu nháp (DRAFT), false = Gửi duyệt (PENDING_APPROVAL).
    /// </summary>
    public bool IsDraft { get; set; }

    // Dữ liệu hỗ trợ giao diện Dropdown
    public List<JobPositionOptionViewModel> JobPositionOptions { get; set; } = [];
    public List<DepartmentOptionViewModel> DepartmentOptions { get; set; } = [];

    // Cờ phân quyền hiển thị
    public bool CanChangeDepartment { get; set; } = true;
    public string? CurrentUserDepartmentName { get; set; }
    public string? CurrentUserName { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinSalary.HasValue && MaxSalary.HasValue && MaxSalary.Value < MinSalary.Value)
        {
            yield return new ValidationResult(
                "Mức lương tối đa phải lớn hơn hoặc bằng mức lương tối thiểu đề xuất.",
                [nameof(MaxSalary)]);
        }

        if (TargetHireDate.HasValue && TargetHireDate.Value < DateOnly.FromDateTime(DateTime.Today))
        {
            yield return new ValidationResult(
                "Ngày cần nhân sự có mặt phải từ ngày hôm nay trở đi.",
                [nameof(TargetHireDate)]);
        }

        // Quy chuẩn nghiệp vụ: Khi gửi duyệt bắt buộc phải có nội dung mô tả công việc và yêu cầu ứng viên.
        // Khi lưu nháp (IsDraft == true), cho phép để trống linh hoạt.
        if (!IsDraft)
        {
            if (string.IsNullOrWhiteSpace(StripHtml(JobDescription)))
            {
                yield return new ValidationResult(
                    "Vui lòng nhập mô tả công việc (trách nhiệm, nhiệm vụ chính, KPI...) khi gửi duyệt.",
                    [nameof(JobDescription)]);
            }

            if (string.IsNullOrWhiteSpace(StripHtml(Requirements)))
            {
                yield return new ValidationResult(
                    "Vui lòng nhập yêu cầu ứng viên (trình độ học vấn, kinh nghiệm, kỹ năng, chứng chỉ...) khi gửi duyệt.",
                    [nameof(Requirements)]);
            }
        }
    }

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var decoded = System.Net.WebUtility.HtmlDecode(html)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", " ", StringComparison.OrdinalIgnoreCase);

        var stripped = System.Text.RegularExpressions.Regex.Replace(decoded, "<.*?>", string.Empty);
        return stripped.Trim();
    }
}
