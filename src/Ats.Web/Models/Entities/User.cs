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

    // Thuộc tính lưu Vai trò phân quyền chính (Mặc định: Candidate)
    public string Role { get; set; } = "Candidate";

    // SCRUM-129: Phòng ban (dạng chuỗi phục vụ quản trị và tìm kiếm)
    public string? Department { get; set; }
    
    // SCRUM-187: Cập nhật hồ sơ
    public string? PhoneNumber { get; set; }
    public string? JobTitle { get; set; }

    public Guid? DepartmentId { get; set; }
    public Department? DepartmentEntity { get; set; }

    public Guid? JobPositionId { get; set; }
    public JobPosition? JobPosition { get; set; }

    public int FailedLoginAttempts { get; set; } = 0;
    public DateTimeOffset? LockedUntil { get; set; }
    public string Status { get; set; } = "ACTIVE"; // ACTIVE, LOCKED, INACTIVE, PENDING
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

    // SCRUM-131: Bổ sung theo thiết kế database schema
    public Guid? RoleId { get; set; }
    public Role? RoleEntity { get; set; }
    public string? ActivationToken { get; set; }
    public Guid? CreatedBy { get; set; }

    // SCRUM-132: Ghi nhận người cập nhật
    public Guid? UpdatedBy { get; set; }

    // S1-10: Lý do khóa và thời điểm khóa
    public string? LockReason { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public Guid? LockedBy { get; set; }

    // S1-09: Hỗ trợ nhiều vai trò cùng lúc
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}