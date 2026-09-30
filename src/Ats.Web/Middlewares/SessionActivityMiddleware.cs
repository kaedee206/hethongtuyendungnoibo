using Ats.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Middlewares;

public class SessionActivityMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        // Kiểm tra nếu người dùng đã đăng nhập (có Session/User Identity)
        var userIdString = context.Session.GetString("UserId");

        if (!string.IsNullOrEmpty(userIdString) && Guid.TryParse(userIdString, out var userId))
        {
            try
            {
                // Kiểm tra Absolute Timeout (SCRUM-90)
                var absoluteExpStr = context.Session.GetString("AbsoluteExpiration");
                if (!string.IsNullOrEmpty(absoluteExpStr) && DateTimeOffset.TryParse(absoluteExpStr, out var absoluteExp))
                {
                    if (DateTimeOffset.UtcNow > absoluteExp)
                    {
                        // SCRUM-95: Ghi log sự kiện hết hạn phiên
                        var auditLog = new Ats.Web.Models.Entities.AuthAuditLog
                        {
                            Email = context.Session.GetString("UserEmail") ?? "Unknown",
                            UserId = userId,
                            SessionId = context.Session.Id,
                            IsSuccess = true,
                            EventType = "SessionExpire",
                            Reason = "Phiên làm việc hết hạn do Absolute Timeout",
                            IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                            UserAgent = context.Request.Headers["User-Agent"].ToString() ?? "Unknown",
                            Timestamp = DateTimeOffset.UtcNow
                        };
                        dbContext.AuthAuditLogs.Add(auditLog);
                        await dbContext.SaveChangesAsync();

                        // Đã vượt quá giới hạn phiên tuyệt đối -> Bắt buộc đăng xuất
                        context.Session.Clear();
                        await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(context, "AtsCookieScheme");
                        context.Response.Cookies.Delete("Ats.Session");
                        context.Response.Cookies.Delete("Ats.AuthCookie");
                        context.Response.Cookies.Delete(".AspNetCore.Session");

                        context.Response.StatusCode = 401; // Unauthorized
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("{\"isSuccess\": false, \"message\": \"Phiên làm việc đã vượt quá thời gian tối đa cho phép. Vui lòng đăng nhập lại.\"}");
                        return;
                    }
                }

                // Kiểm tra trạng thái Session trong CSDL (SCRUM-94)
                var currentSessionId = context.Session.Id;
                var userSession = await dbContext.UserSessions.FirstOrDefaultAsync(s => s.SessionId == currentSessionId);
                
                if (userSession != null)
                {
                    if (userSession.IsRevoked)
                    {
                        // SCRUM-95: Ghi log sự kiện bị đăng xuất do Revoke
                        var revokeLog = new Ats.Web.Models.Entities.AuthAuditLog
                        {
                            Email = context.Session.GetString("UserEmail") ?? "Unknown",
                            UserId = userId,
                            SessionId = context.Session.Id,
                            IsSuccess = true,
                            EventType = "SessionRevoked",
                            Reason = "Phiên làm việc bị đá văng do đăng xuất từ xa",
                            IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                            UserAgent = context.Request.Headers["User-Agent"].ToString() ?? "Unknown",
                            Timestamp = DateTimeOffset.UtcNow
                        };
                        dbContext.AuthAuditLogs.Add(revokeLog);
                        await dbContext.SaveChangesAsync();

                        // Phiên đã bị đăng xuất từ xa
                        context.Session.Clear();
                        await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(context, "AtsCookieScheme");
                        context.Response.Cookies.Delete("Ats.Session");
                        context.Response.Cookies.Delete("Ats.AuthCookie");
                        context.Response.Cookies.Delete(".AspNetCore.Session");

                        context.Response.StatusCode = 401; // Unauthorized
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("{\"isSuccess\": false, \"message\": \"Phiên làm việc này đã bị đăng xuất từ thiết bị khác.\"}");
                        return;
                    }

                    // Cập nhật LastActiveAt cho UserSession mỗi 5 phút/lần
                    if ((DateTimeOffset.UtcNow - userSession.LastActiveAt).TotalMinutes >= 5)
                    {
                        userSession.LastActiveAt = DateTimeOffset.UtcNow;

                        // SCRUM-95: Ghi log sự kiện gia hạn phiên (mỗi 5 phút để tránh spam DB)
                        var renewLog = new Ats.Web.Models.Entities.AuthAuditLog
                        {
                            Email = context.Session.GetString("UserEmail") ?? "Unknown",
                            UserId = userId,
                            SessionId = context.Session.Id,
                            IsSuccess = true,
                            EventType = "SessionRenew",
                            Reason = "Gia hạn phiên làm việc tự động",
                            IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                            UserAgent = context.Request.Headers["User-Agent"].ToString() ?? "Unknown",
                            Timestamp = DateTimeOffset.UtcNow
                        };
                        dbContext.AuthAuditLogs.Add(renewLog);

                        await dbContext.SaveChangesAsync();
                    }
                }

                // Cập nhật LastActivityAt trong DB User mỗi 5 phút/lần
                var user = await dbContext.Users.FindAsync(userId);
                if (user != null)
                {
                    if (!user.LastActivityAt.HasValue || (DateTimeOffset.UtcNow - user.LastActivityAt.Value).TotalMinutes >= 5)
                    {
                        user.LastActivityAt = DateTimeOffset.UtcNow;
                        await dbContext.SaveChangesAsync();
                    }
                }
            }
            catch (Exception)
            {
                // Bỏ qua lỗi DB tạm thời trong middleware session activity để không gián đoạn người dùng
            }

            // Tự động làm tươi Session (Sliding Expiration tự kích hoạt khi truy cập Session)
            context.Session.SetString("LastActive", DateTimeOffset.UtcNow.ToString("o"));
        }

        await _next(context);
    }
}