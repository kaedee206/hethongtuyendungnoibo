using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.Users;

public class UserListViewModel
{
    public List<UserItemViewModel> Users { get; set; } = new();
    public string? Keyword { get; set; }
    public string? Role { get; set; }
    public string? Status { get; set; }

    public string? RoleFilter
    {
        get => Role;
        set => Role = value;
    }

    public string? StatusFilter
    {
        get => Status;
        set => Status = value;
    }

    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRecords { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalRecords / (double)PageSize);
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;
}

public class UserItemViewModel
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public List<string> Roles { get; set; } = new();
    public string Status { get; set; } = "ACTIVE";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public string? LockReason { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
}

public class UserCreateViewModel
{
    [Required(ErrorMessage = "Email công ty không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [Display(Name = "Email công ty (@noveratech.digital)")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ và tên không được để trống.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Phòng ban")]
    public string? Department { get; set; }

    [Display(Name = "Vai trò phân quyền")]
    public List<string> SelectedRoles { get; set; } = new();

    public List<RoleCheckboxItem> AvailableRoles { get; set; } = new();
}

public class UserEditViewModel
{
    public Guid Id { get; set; }

    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ và tên không được để trống.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Phòng ban")]
    public string? Department { get; set; }

    [Display(Name = "Trạng thái tài khoản")]
    public string Status { get; set; } = "ACTIVE";

    public bool IsSelf { get; set; }
    public bool IsSelfAdmin
    {
        get => IsSelf;
        set => IsSelf = value;
    }

    [Display(Name = "Vai trò phân quyền")]
    public List<string> SelectedRoles { get; set; } = new();

    public List<RoleCheckboxItem> AvailableRoles { get; set; } = new();
}

public class RoleCheckboxItem
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public bool IsDisabled { get; set; }
}

public class UserLockInputModel
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập lý do khóa tài khoản.")]
    [MinLength(5, ErrorMessage = "Lý do khóa phải có ít nhất 5 ký tự.")]
    [Display(Name = "Lý do khóa tài khoản")]
    public string LockReason { get; set; } = string.Empty;
}
