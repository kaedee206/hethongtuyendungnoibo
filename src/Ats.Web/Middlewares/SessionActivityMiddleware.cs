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
            // Tự động làm tươi Session (Sliding Expiration tự kích hoạt khi truy cập Session)
            context.Session.SetString("LastActive", DateTimeOffset.UtcNow.ToString("o"));

            // Cập nhật LastActivityAt trong DB mỗi 5 phút/lần để tránh spam SQL
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

        await _next(context);
    }
}