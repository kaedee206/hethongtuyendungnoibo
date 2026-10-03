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
    }
}
