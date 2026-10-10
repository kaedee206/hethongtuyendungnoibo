namespace Ats.Web.Common;

/// <summary>
/// Helper xác định chính xác địa chỉ IP của Client khi chạy qua Reverse Proxy (Nginx, Docker, Cloudflare).
/// </summary>
public static class ClientIpHelper
{
    public static string GetClientIp(HttpContext? context) => GetClientIpAddress(context);

    public static string GetClientIpAddress(HttpContext? context)
    {
        if (context == null) return "Unknown";

        // 1. Kiểm tra header X-Forwarded-For (lấy IP client đầu tiên nếu qua chuỗi proxy)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ips.Length > 0 && !string.IsNullOrWhiteSpace(ips[0]))
            {
                var clientIp = ips[0].Trim();
                if (clientIp == "::1") return "127.0.0.1";
                return clientIp;
            }
        }

        // 2. Kiểm tra header X-Real-IP
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(realIp))
        {
            var cleanRealIp = realIp.Trim();
            if (cleanRealIp == "::1") return "127.0.0.1";
            return cleanRealIp;
        }

        // 3. Fallback lấy RemoteIpAddress từ kết nối mạng
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            if (remoteIp == "::1") return "127.0.0.1";
            return remoteIp;
        }

        return "127.0.0.1";
    }
}
