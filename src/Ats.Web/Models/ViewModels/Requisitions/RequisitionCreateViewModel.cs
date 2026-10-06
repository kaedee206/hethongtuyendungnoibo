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

public class CatalogOptionViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
}

public class RequisitionCreateViewModel : IValidatableObject
{
    /// <summary>
    /// Định danh yêu cầu tuyển dụng (nếu đang chỉnh sửa hoặc auto-save bản nháp hiện có).
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>
    /// Mã yêu cầu tuyển dụng (ví dụ: REQ-20261005-A1B2).
    /// </summary>
    public string? Code { get; set; }

    public Guid? JobPositionId { get; set; }

    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// Địa điểm làm việc từ danh mục dùng chung (SCRUM-173: CatalogType = LOCATION).
    /// </summary>
    public Guid? LocationId { get; set; }

    /// <summary>
    /// Hình thức làm việc từ danh mục dùng chung (SCRUM-173: CatalogType = WORK_TYPE).
    /// </summary>
    public Guid? WorkTypeId { get; set; }

    public int Quantity { get; set; } = 1;

    public HeadcountType HeadcountType { get; set; } = HeadcountType.NEW_HEADCOUNT;

    [MaxLength(500, ErrorMessage = "Mô tả chi tiết lý do tuyển dụng không được vượt quá 500 ký tự.")]
    public string? ReasonDetail { get; set; }

    public decimal? MinSalary { get; set; }

    public decimal? MaxSalary { get; set; }

    /// <summary>
    /// Giải trình lý do khi mức lương đề xuất nằm ngoài dải lương chuẩn của vị trí.
    /// </summary>
    public string? SalaryBandExplanation { get; set; }

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
    public List<CatalogOptionViewModel> LocationOptions { get; set; } = [];
    public List<CatalogOptionViewModel> WorkTypeOptions { get; set; } = [];

    // Cờ phân quyền hiển thị
    public bool CanChangeDepartment { get; set; } = true;
    public string? CurrentUserDepartmentName { get; set; }
    public string? CurrentUserName { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // 1. Kiểm tra dải lương nếu người dùng đã nhập
        if (MinSalary.HasValue && (MinSalary.Value < 1_000_000 || MinSalary.Value > 1_000_000_000))
        {
            yield return new ValidationResult(
                "Mức lương tối thiểu phải từ 1.000.000 VNĐ đến 1.000.000.000 VNĐ.",
                [nameof(MinSalary)]);
        }

        if (MaxSalary.HasValue && (MaxSalary.Value < 1_000_000 || MaxSalary.Value > 1_000_000_000))
        {
            yield return new ValidationResult(
                "Mức lương tối đa phải từ 1.000.000 VNĐ đến 1.000.000.000 VNĐ.",
                [nameof(MaxSalary)]);
        }

        if (MinSalary.HasValue && MaxSalary.HasValue && MaxSalary.Value < MinSalary.Value)
        {
            yield return new ValidationResult(
                "Mức lương tối đa phải lớn hơn hoặc bằng mức lương tối thiểu đề xuất.",
                [nameof(MaxSalary)]);
        }

        // 2. Kiểm tra ngày nếu đã nhập
        if (TargetHireDate.HasValue && TargetHireDate.Value < DateOnly.FromDateTime(DateTime.Today))
        {
            yield return new ValidationResult(
                "Ngày cần nhân sự có mặt phải từ ngày hôm nay trở đi.",
                [nameof(TargetHireDate)]);
        }

        // 3. Quy chuẩn nghiệp vụ: Khi gửi duyệt (!IsDraft) bắt buộc phải có đầy đủ toàn bộ thông tin
        if (!IsDraft)
        {
            if (!JobPositionId.HasValue || JobPositionId.Value == Guid.Empty)
            {
                yield return new ValidationResult(
                    "Vui lòng chọn chức danh cần tuyển dụng.",
                    [nameof(JobPositionId)]);
            }

            if (!DepartmentId.HasValue || DepartmentId.Value == Guid.Empty)
            {
                yield return new ValidationResult(
                    "Vui lòng chọn phòng ban phụ trách yêu cầu tuyển dụng.",
                    [nameof(DepartmentId)]);
            }

            if (!LocationId.HasValue || LocationId.Value == Guid.Empty)
            {
                yield return new ValidationResult(
                    "Vui lòng chọn địa điểm làm việc từ danh mục dùng chung.",
                    [nameof(LocationId)]);
            }

            if (!WorkTypeId.HasValue || WorkTypeId.Value == Guid.Empty)
            {
                yield return new ValidationResult(
                    "Vui lòng chọn hình thức làm việc từ danh mục dùng chung.",
                    [nameof(WorkTypeId)]);
            }

            if (Quantity < 1 || Quantity > 100)
            {
                yield return new ValidationResult(
                    "Số lượng cần tuyển phải là số nguyên dương từ 1 đến 100 người.",
                    [nameof(Quantity)]);
            }

            if (!MinSalary.HasValue)
            {
                yield return new ValidationResult(
                    "Vui lòng nhập mức lương tối thiểu đề xuất.",
                    [nameof(MinSalary)]);
            }

            if (!MaxSalary.HasValue)
            {
                yield return new ValidationResult(
                    "Vui lòng nhập mức lương tối đa đề xuất.",
                    [nameof(MaxSalary)]);
            }

            if (!TargetHireDate.HasValue)
            {
                yield return new ValidationResult(
                    "Vui lòng chọn ngày cần nhân sự có mặt.",
                    [nameof(TargetHireDate)]);
            }

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

            if (JobPositionId.HasValue && JobPositionOptions != null && JobPositionOptions.Count > 0)
            {
                var pos = JobPositionOptions.FirstOrDefault(p => p.Id == JobPositionId.Value);
                if (pos != null && pos.MinSalary.HasValue && pos.MaxSalary.HasValue)
                {
                    bool isOutsideBand = (MinSalary.HasValue && MinSalary.Value < pos.MinSalary.Value) ||
                                         (MaxSalary.HasValue && MaxSalary.Value > pos.MaxSalary.Value);
                    if (isOutsideBand && string.IsNullOrWhiteSpace(SalaryBandExplanation))
                    {
                        yield return new ValidationResult(
                            "Mức lương đề xuất nằm ngoài dải lương chuẩn của vị trí. Bắt buộc phải nhập giải trình lý do vượt khung.",
                            [nameof(SalaryBandExplanation)]);
                    }
                }
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
