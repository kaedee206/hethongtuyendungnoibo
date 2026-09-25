namespace Ats.Web.Models.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTimeOffset? LastLoginAt { get; set; }

    // Bổ sung thuộc tính theo dõi thời gian hoạt động cuối
    public DateTimeOffset? LastActivityAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}