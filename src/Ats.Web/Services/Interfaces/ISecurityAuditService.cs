namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Các phân loại sự kiện kiểm toán an ninh (OWASP Top 10 A09: Security Logging and Monitoring Failures).
/// </summary>
public enum SecurityAuditEventType
{
    LoginSuccess,
    LoginFailed,
    IpBlocked,
    CandidateDocumentAccessed,
    CandidateDocumentAccessDenied,
    PrivacyConsentAccepted,
    PrivacyConsentRejected,
    UserLocked,
    UserUnlocked,
    RoleChanged
}

/// <summary>
/// Thực thể ghi nhận sự kiện kiểm toán bảo mật có cấu trúc (Structured Audit Event).
/// </summary>
public class SecurityAuditEvent
{
    public SecurityAuditEventType EventType { get; set; }
    public string? UserId { get; set; }
    public string? UserNameOrEmail { get; set; }
    public string ClientIp { get; set; } = string.Empty;
    public string? ResourcePath { get; set; }
    public string Action { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Giao diện dịch vụ ghi log kiểm toán bảo mật và truy vết hoạt động nhạy cảm.
/// </summary>
public interface ISecurityAuditService
{
    /// <summary>
    /// Ghi nhận sự kiện kiểm toán bảo mật theo định dạng cấu trúc chuẩn.
    /// </summary>
    void LogSecurityEvent(SecurityAuditEvent auditEvent);
}
