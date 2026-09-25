using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class Role : BaseEntity
{
    public Guid Id { get; set; }
    public RoleCode Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; } = false;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
