namespace Ats.Web.Models.Entities;

public class AuthAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public string Email { get; set; } = string.Empty;
    public Guid? UserId { get; set; } // Nullable vì có thể login bằng email không tồn tại
    
    public bool IsSuccess { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string EventType { get; set; } = "Login"; // "Login", "SessionCreate", "SessionRenew", "SessionExpire", "SessionLogout", "RemoteLogout"
    public string? SessionId { get; set; }
    
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    // SCRUM-138: Bổ sung các trường để lưu chi tiết thay đổi
    public Guid? PerformedBy { get; set; } // Người thực hiện thay đổi
    public string? OldValues { get; set; } // Dữ liệu trước khi sửa (JSON)
    public string? NewValues { get; set; } // Dữ liệu sau khi sửa (JSON)
}
