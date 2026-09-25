using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? AvatarUrl { get; set; }
    public UserType UserType { get; set; } = UserType.INTERNAL;
    
    public Guid? DepartmentId { get; set; }
    public Guid? JobPositionId { get; set; }
    
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    public Department? Department { get; set; }
    public JobPosition? JobPosition { get; set; }
    
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}