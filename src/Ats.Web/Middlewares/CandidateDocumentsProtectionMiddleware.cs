using System.Security.Claims;
using Ats.Web.Common;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Ats.Web.Middlewares;

/// <summary>
/// Middleware kiểm soát quyền truy cập tài liệu nhạy cảm của ứng viên (CVs, Resumes).
/// Phòng chống IDOR / BOLA (OWASP Top 10 A01) và đảm bảo tuân thủ Nghị định 13/2023/NĐ-CP về Bảo vệ dữ liệu cá nhân.
/// Tự động ghi nhật ký kiểm toán bảo mật (Security Audit Logging) khi có truy cập hồ sơ.
/// </summary>
public class CandidateDocumentsProtectionMiddleware
{
    private readonly RequestDelegate _next;

    public CandidateDocumentsProtectionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ISecurityAuditService? auditService = null)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (path.StartsWith("/uploads/cvs", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/uploads/resumes", StringComparison.OrdinalIgnoreCase))
        {
            var clientIp = ClientIpHelper.GetClientIp(context);

            // Yêu cầu bắt buộc xác thực: Người dùng phải đăng nhập để xem hồ sơ
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                auditService?.LogSecurityEvent(new SecurityAuditEvent
                {
                    EventType = SecurityAuditEventType.CandidateDocumentAccessDenied,
                    ClientIp = clientIp,
                    ResourcePath = path,
                    Action = "DOWNLOAD_CV_UNAUTHENTICATED",
                    IsSuccess = false,
                    Details = "Từ chối truy cập tài liệu ứng viên: Người dùng chưa xác thực (Nghị định 13/2023/NĐ-CP)"
                });

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                await context.Response.WriteAsync("Truy cập bị từ chối: Hồ sơ ứng viên yêu cầu xác thực tài khoản theo Nghị định 13/2023/NĐ-CP.");
                return;
            }

            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userEmail = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.Identity?.Name;

            auditService?.LogSecurityEvent(new SecurityAuditEvent
            {
                EventType = SecurityAuditEventType.CandidateDocumentAccessed,
                UserId = userId,
                UserNameOrEmail = userEmail,
                ClientIp = clientIp,
                ResourcePath = path,
                Action = "DOWNLOAD_CV_AUTHORIZED",
                IsSuccess = true,
                Details = "Truy cập tài liệu ứng viên thành công từ người dùng đã xác thực"
            });

            // Với dữ liệu PII nhạy cảm, không lưu cache trên trình duyệt công cộng hoặc proxy
            context.Response.Headers.CacheControl = "private, no-cache, no-store, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
        }

        await _next(context);
    }
}
