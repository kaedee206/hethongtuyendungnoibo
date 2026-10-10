using Ats.Web.Common;
using Ats.Web.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Dịch vụ kiểm toán an ninh và truy vết hành vi bảo mật (OWASP Top 10 A09 / Nghị định 13/2023/NĐ-CP).
/// Hỗ trợ ghi log có cấu trúc (Structured Logging) phục vụ tích hợp SIEM/SOC và giám sát thời gian thực.
/// Tự động che giấu thông tin định danh cá nhân (PII) trước khi ghi log.
/// </summary>
public class SecurityAuditService : ISecurityAuditService
{
    private readonly ILogger<SecurityAuditService> _logger;

    public SecurityAuditService(ILogger<SecurityAuditService> logger)
    {
        _logger = logger;
    }

    public void LogSecurityEvent(SecurityAuditEvent auditEvent)
    {
        if (auditEvent == null) return;

        // Mask thông tin email/user trước khi ghi log nếu có dạng email
        var maskedUser = auditEvent.UserNameOrEmail;
        if (!string.IsNullOrWhiteSpace(maskedUser) && maskedUser.Contains('@'))
        {
            maskedUser = PiiMaskingHelper.MaskEmail(maskedUser);
        }

        var isSecurityAlert = !auditEvent.IsSuccess ||
                              auditEvent.EventType == SecurityAuditEventType.IpBlocked ||
                              auditEvent.EventType == SecurityAuditEventType.LoginFailed ||
                              auditEvent.EventType == SecurityAuditEventType.CandidateDocumentAccessDenied ||
                              auditEvent.EventType == SecurityAuditEventType.PrivacyConsentRejected;

        if (isSecurityAlert)
        {
            _logger.LogWarning(
                "[SECURITY_AUDIT_ALERT] Type={EventType} | Status=FAILED | IP={ClientIp} | User={User} | UserId={UserId} | Resource={Resource} | Action={Action} | Details={Details} | Time={Timestamp}",
                auditEvent.EventType,
                auditEvent.ClientIp,
                maskedUser ?? "ANONYMOUS",
                auditEvent.UserId ?? "N/A",
                auditEvent.ResourcePath ?? "N/A",
                auditEvent.Action,
                auditEvent.Details ?? string.Empty,
                auditEvent.Timestamp);
        }
        else
        {
            _logger.LogInformation(
                "[SECURITY_AUDIT_INFO] Type={EventType} | Status=SUCCESS | IP={ClientIp} | User={User} | UserId={UserId} | Resource={Resource} | Action={Action} | Details={Details} | Time={Timestamp}",
                auditEvent.EventType,
                auditEvent.ClientIp,
                maskedUser ?? "ANONYMOUS",
                auditEvent.UserId ?? "N/A",
                auditEvent.ResourcePath ?? "N/A",
                auditEvent.Action,
                auditEvent.Details ?? string.Empty,
                auditEvent.Timestamp);
        }
    }
}
