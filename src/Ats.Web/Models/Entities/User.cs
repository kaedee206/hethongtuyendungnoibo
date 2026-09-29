namespace Ats.Web.Models.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    // Thuộc tính lưu Vai trò phân quyền (Mặc định: Candidate)
    public string Role { get; set; } = "Candidate";

    public int FailedLoginAttempts { get; set; } = 0;
    public DateTimeOffset? LockedUntil { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTimeOffset? LastLoginAt { get; set; }

    // Bổ sung thuộc tính theo dõi thời gian hoạt động cuối
    public DateTimeOffset? LastActivityAt { get; set; }

    // Bổ sung thuộc tính cho chức năng Đặt lại mật khẩu (SCRUM-97)
    public string? PasswordResetToken { get; set; }
    public DateTimeOffset? PasswordResetTokenExpiresAt { get; set; }

    // SCRUM-115: Giới hạn số lần đổi mật khẩu thất bại
    public int FailedChangePasswordAttempts { get; set; } = 0;
    public DateTimeOffset? ChangePasswordLockedUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}