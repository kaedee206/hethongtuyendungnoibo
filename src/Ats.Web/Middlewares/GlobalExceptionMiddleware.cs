using System.Net;
using System.Text.Json;

namespace Ats.Web.Middlewares;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);

            // SCRUM-124: Xử lý các mã lỗi không phải Exception (401, 403, 404) cho API
            if (context.Request.Path.StartsWithSegments("/api") && 
                !context.Response.HasStarted && 
                (context.Response.StatusCode == 401 || 
                 context.Response.StatusCode == 403 || 
                 context.Response.StatusCode == 404))
            {
                // Nếu content type chưa được set hoặc body trống, ghi đè response
                if (string.IsNullOrEmpty(context.Response.ContentType))
                {
                    await HandleStatusCodeAsync(context, context.Response.StatusCode);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hệ thống không mong muốn");
            
            // SCRUM-124: Chỉ trả JSON chuẩn hoá cho API
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                await HandleExceptionAsync(context, ex);
            }
            else
            {
                throw; // Cho phép ASP.NET Core xử lý trang lỗi HTML mặc định
            }
        }
    }

    private static async Task HandleStatusCodeAsync(HttpContext context, int statusCode)
    {
        context.Response.ContentType = "application/json";
        
        string errorCode = "UNKNOWN_ERROR";
        string message = "Đã xảy ra lỗi.";
        string action = "NONE";

        switch (statusCode)
        {
            case (int)HttpStatusCode.Unauthorized: // 401
                errorCode = "UNAUTHORIZED";
                message = "Bạn chưa đăng nhập hoặc phiên làm việc đã hết hạn.";
                action = "LOGIN";
                break;
            case (int)HttpStatusCode.Forbidden: // 403
                errorCode = "FORBIDDEN";
                message = "Bạn không có quyền thực hiện hành động này.";
                action = "CONTACT_ADMIN";
                break;
            case (int)HttpStatusCode.NotFound: // 404
                errorCode = "NOT_FOUND";
                message = "Không tìm thấy tài nguyên yêu cầu.";
                action = "REDIRECT_HOME";
                break;
        }

        var result = JsonSerializer.Serialize(new
        {
            statusCode = statusCode,
            errorCode = errorCode,
            message = message,
            action = action
        });

        await context.Response.WriteAsync(result);
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        // SCRUM-124: Trả HTTP 500 với thông tin lỗi chung (không expose stack trace)
        var result = JsonSerializer.Serialize(new
        {
            statusCode = context.Response.StatusCode,
            errorCode = "INTERNAL_SERVER_ERROR",
            message = "Hệ thống đang gặp sự cố, vui lòng thử lại sau.",
            action = "RETRY_LATER"
        });

        return context.Response.WriteAsync(result);
    }
}
