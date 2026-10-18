namespace Ats.Web.Middlewares;

/// <summary>
/// Middleware bổ sung các HTTP Security Headers tiêu chuẩn (OWASP A05:2021 - Security Misconfiguration).
/// Bảo vệ ứng dụng chống Clickjacking, MIME-sniffing, XSS và rò rỉ dữ liệu.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.Remove("Server");
            context.Response.Headers.Remove("X-Powered-By");
            return Task.CompletedTask;
        });

        // 1. Chống MIME-sniffing: Ngăn trình duyệt tự ý đoán định dạng file tải lên
        if (!context.Response.Headers.ContainsKey("X-Content-Type-Options"))
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        }

        // 2. Chống Clickjacking: Ngăn nhúng website vào iframe từ domain độc hại
        if (!context.Response.Headers.ContainsKey("X-Frame-Options"))
        {
            context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
        }

        // 3. Chống rò rỉ URL chứa token: Chỉ gửi referrer cùng origin hoặc qua kết nối an toàn
        if (!context.Response.Headers.ContainsKey("Referrer-Policy"))
        {
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        }

        // 4. Giới hạn các API trình duyệt nhạy cảm (camera, microphone, geolocation)
        if (!context.Response.Headers.ContainsKey("Permissions-Policy"))
        {
            context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
        }

        // 5. X-XSS-Protection cho trình duyệt cũ
        if (!context.Response.Headers.ContainsKey("X-XSS-Protection"))
        {
            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
        }

        // 6. Content-Security-Policy (CSP) kiểm soát nguồn tài nguyên an toàn
        if (!context.Response.Headers.ContainsKey("Content-Security-Policy"))
        {
            context.Response.Headers.Append("Content-Security-Policy",
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net https://cdn.tailwindcss.com https://cdnjs.cloudflare.com https://static.cloudflareinsights.com; " +
                "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com https://cdnjs.cloudflare.com https://cdn.tailwindcss.com; " +
                "font-src 'self' https://fonts.gstatic.com https://cdn.jsdelivr.net https://cdnjs.cloudflare.com data:; " +
                "img-src 'self' data: https:; " +
                "connect-src 'self' https://cdn.tailwindcss.com https://cloudflareinsights.com https://*.cloudflareinsights.com; " +
                "frame-ancestors 'self';");
        }

        // 7. Strict-Transport-Security (HSTS): Ép buộc trình duyệt luôn sử dụng giao thức HTTPS bảo mật
        if (!context.Response.Headers.ContainsKey("Strict-Transport-Security"))
        {
            context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
        }

        await _next(context);
    }
}
