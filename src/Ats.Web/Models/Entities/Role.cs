namespace Ats.Web.Models.Entities;

public class Role
{
    public Guid Id { get; set; }
    public int? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; } = true;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
