using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models.ViewModels.JobPositions;
 
public class JobPositionFormViewModel : IValidatableObject
{
    public Guid? Id { get; set; }

    public bool IsEdit => Id.HasValue && Id.Value != Guid.Empty;

    [Display(Name = "Mã chức danh")]
    [Required(ErrorMessage = "Mã chức danh không được để trống.")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "Mã chức danh phải từ 3 đến 30 ký tự.")]
    [RegularExpression(@"^[A-Za-z0-9_-]+$", ErrorMessage = "Mã chức danh chỉ được chứa chữ cái, chữ số, dấu gạch nối (-) và gạch dưới (_).")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Tên chức danh")]
    [Required(ErrorMessage = "Tên chức danh không được để trống.")]
    [StringLength(150, MinimumLength = 3, ErrorMessage = "Tên chức danh phải từ 3 đến 150 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Phòng ban trực thuộc")]
    [Required(ErrorMessage = "Vui lòng chọn phòng ban trực thuộc.")]
    public Guid DepartmentId { get; set; }

    [Display(Name = "Cấp bậc chuyên môn")]
    [Required(ErrorMessage = "Vui lòng chọn cấp bậc chuyên môn.")]
    public string JobLevel { get; set; } = string.Empty;

    [Display(Name = "Dải lương tối thiểu (VNĐ)")]
    [Required(ErrorMessage = "Dải lương tối thiểu không được để trống.")]
    [Range(0, 1_000_000_000, ErrorMessage = "Mức lương tối thiểu phải nằm trong khoảng từ 0 đến 1.000.000.000 VNĐ.")]
    public decimal? MinSalary { get; set; }

    [Display(Name = "Dải lương tối đa (VNĐ)")]
    [Required(ErrorMessage = "Dải lương tối đa không được để trống.")]
    [Range(0, 2_000_000_000, ErrorMessage = "Mức lương tối đa phải nằm trong khoảng từ 0 đến 2.000.000.000 VNĐ.")]
    public decimal? MaxSalary { get; set; }

    [Display(Name = "Mô tả vai trò & Trách nhiệm")]
    [StringLength(2000, ErrorMessage = "Mô tả vai trò không được vượt quá 2000 ký tự.")]
    public string? Description { get; set; }

    [Display(Name = "Tiêu chuẩn năng lực & Kỹ năng cốt lõi")]
    [StringLength(500, ErrorMessage = "Chuỗi tiêu chuẩn năng lực không vượt quá 500 ký tự.")]
    public string? StandardCompetencies { get; set; }

    [Display(Name = "Kích hoạt chức danh")]
    public bool IsActive { get; set; } = true;

    public List<SelectListItem> AvailableDepartments { get; set; } = [];
    public List<SelectListItem> AvailableJobLevels { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinSalary.HasValue && MaxSalary.HasValue)
        {
            if (MaxSalary.Value < MinSalary.Value)
            {
                yield return new ValidationResult(
                    $"Dải lương tối đa ({MaxSalary.Value:N0} VNĐ) phải lớn hơn hoặc bằng dải lương tối thiểu ({MinSalary.Value:N0} VNĐ).",
                    [nameof(MaxSalary)]
                );
            }
        }
    }

    public static List<SelectListItem> GetDefaultJobLevels(string? selectedLevel = null)
    {
        var levels = new List<(string Value, string Text)>
        {
            ("INTERN", "Thực tập sinh (Intern)"),
            ("FRESHER", "Mới tốt nghiệp (Fresher)"),
            ("JUNIOR", "Kỹ sư Junior (1 - 2 năm kinh nghiệm)"),
            ("MIDDLE", "Kỹ sư Middle (2 - 4 năm kinh nghiệm)"),
            ("SENIOR", "Kỹ sư Senior (4 - 7 năm kinh nghiệm)"),
            ("LEAD", "Trưởng nhóm kỹ thuật (Tech Lead / Team Lead)"),
            ("PRINCIPAL", "Kỹ sư chủ chốt / Chuyên gia (Principal / Staff)"),
            ("MANAGER", "Trưởng phòng / Quản lý chuyên môn (Manager)"),
            ("DIRECTOR", "Giám đốc bộ phận / Khối (Director / Head)")
        };

        return levels.Select(l => new SelectListItem
        {
            Value = l.Value,
            Text = l.Text,
            Selected = string.Equals(l.Value, selectedLevel, StringComparison.OrdinalIgnoreCase)
        }).ToList();
    }
}

