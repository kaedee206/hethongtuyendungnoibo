namespace Ats.Web.Models.ViewModels.Profiles;

public class ProfileUpdateViewModel
{
    // CÁC TRƯỜNG CHỈ ĐỌC (Sẽ hiển thị nhưng không cho sửa)
    public string Email { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string? RoleName { get; set; }
    public List<string> Roles { get; set; } = new();

    // CÁC TRƯỜNG CHO PHÉP EDIT
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? JobTitle { get; set; }
    public string? AvatarUrl { get; set; }
}
