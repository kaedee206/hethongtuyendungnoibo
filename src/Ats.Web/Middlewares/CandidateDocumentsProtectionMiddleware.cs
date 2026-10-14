using System.Security.Claims;
using Ats.Web.Common;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

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

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext, ISecurityAuditService? auditService = null)
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
            var userEmail = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.Identity?.Name ?? string.Empty;
            Guid.TryParse(userId, out var parsedUserId);
            var lowerEmail = userEmail.ToLower();

            var isStaff = context.User.IsInRole(UserRoles.Admin) ||
                          context.User.IsInRole(UserRoles.HRManager) ||
                          context.User.IsInRole(UserRoles.HiringManager) ||
                          context.User.IsInRole(UserRoles.Recruiter) ||
                          context.User.IsInRole(UserRoles.Interviewer) ||
                          context.User.IsInRole("Quản trị viên") ||
                          context.User.IsInRole("Trưởng phòng nhân sự") ||
                          context.User.IsInRole("Chuyên viên tuyển dụng") ||
                          context.User.IsInRole("Người phỏng vấn");

            // Nếu không phải nhân sự nội bộ (là Ứng viên), kiểm tra quyền sở hữu đối với file CV (Chống IDOR / BOLA)
            if (!isStaff && !path.EndsWith("default_cv.pdf", StringComparison.OrdinalIgnoreCase))
            {
                var isOwner = await dbContext.Resumes
                    .AnyAsync(r => (r.FilePath == path || r.FilePath.EndsWith(path) || path.EndsWith(r.FilePath)) &&
                                   ((parsedUserId != Guid.Empty && r.Candidate.UserId == parsedUserId) ||
                                    (!string.IsNullOrEmpty(lowerEmail) && r.Candidate.Email.ToLower() == lowerEmail)));

                if (!isOwner)
                {
                    auditService?.LogSecurityEvent(new SecurityAuditEvent
                    {
                        EventType = SecurityAuditEventType.CandidateDocumentAccessDenied,
                        UserId = userId,
                        UserNameOrEmail = userEmail,
                        ClientIp = clientIp,
                        ResourcePath = path,
                        Action = "DOWNLOAD_CV_FORBIDDEN_IDOR",
                        IsSuccess = false,
                        Details = "Từ chối truy cập tài liệu ứng viên: Người dùng không có quyền truy cập hồ sơ của ứng viên khác (Chống IDOR)"
                    });

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                    await context.Response.WriteAsync("Truy cập bị từ chối: Bạn không có quyền truy cập hoặc tải tài liệu hồ sơ này.");
                    return;
                }
            }

            auditService?.LogSecurityEvent(new SecurityAuditEvent
            {
                EventType = SecurityAuditEventType.CandidateDocumentAccessed,
                UserId = userId,
                UserNameOrEmail = userEmail,
                ClientIp = clientIp,
                ResourcePath = path,
                Action = "DOWNLOAD_CV_AUTHORIZED",
                IsSuccess = true,
                Details = "Truy cập tài liệu ứng viên thành công từ người dùng đã xác thực và có quyền hạn"
            });

            // Với dữ liệu PII nhạy cảm, không lưu cache trên trình duyệt công cộng hoặc proxy
            context.Response.Headers.CacheControl = "private, no-cache, no-store, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
        }

        await _next(context);
    }
}
