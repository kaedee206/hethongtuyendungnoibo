using System.Security.Claims;
using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Ats.Web.Models.Entities;
using Ats.Web.Data;

namespace Ats.Web.Controllers;

[ApiController]
[Route("api/xac-thuc")]
public class AuthController(IAuthService authService, ApplicationDbContext dbContext) : ControllerBase
{
    private readonly IAuthService _authService = authService;
    private readonly ApplicationDbContext _dbContext = dbContext;

    /// <summary>
    /// API Đăng nhập tài khoản nội bộ (POST: /api/xac-thuc/dang-nhap)
    /// </summary>
    [HttpPost("dang-nhap")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _authService.AuthenticateAsync(request, cancellationToken);

        // SCRUM-85 & SCRUM-95: Ghi log audit cho lần đăng nhập / tạo phiên
        var auditLog = new AuthAuditLog
        {
            Email = request.Email,
            UserId = result.Data?.Id, // Có thể null nếu đăng nhập thất bại
            SessionId = result.IsSuccess ? HttpContext.Session.Id : null,
            IsSuccess = result.IsSuccess,
            EventType = result.IsSuccess ? "SessionCreate" : "LoginFailed",
            Reason = result.Message,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            UserAgent = Request.Headers["User-Agent"].ToString() ?? "Unknown",
            Timestamp = DateTimeOffset.UtcNow
        };
        _dbContext.AuthAuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!result.IsSuccess)
            return Unauthorized(new { message = result.Message });

        // SCRUM-84 & SCRUM-90 & SCRUM-94: Tạo và lưu thông tin phiên vào Session phía Server
        if (result.Data != null)
        {
            var currentSessionId = HttpContext.Session.Id;
            HttpContext.Session.SetString("UserId", result.Data.Id.ToString());
            HttpContext.Session.SetString("UserEmail", result.Data.Email);
            HttpContext.Session.SetString("UserRole", result.Data.Role);
            HttpContext.Session.SetString("FullName", result.Data.FullName);
            HttpContext.Session.SetString("LastActive", DateTimeOffset.UtcNow.ToString("o"));

            // Lưu UserSession vào CSDL để quản lý thiết bị
            var userSession = new UserSession
            {
                UserId = result.Data.Id,
                SessionId = currentSessionId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                UserAgent = Request.Headers["User-Agent"].ToString() ?? "Unknown",
                CreatedAt = DateTimeOffset.UtcNow,
                LastActiveAt = DateTimeOffset.UtcNow,
                IsRevoked = false
            };
            _dbContext.UserSessions.Add(userSession);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Tính toán và lưu Absolute Timeout
            var absoluteTimeoutStr = Environment.GetEnvironmentVariable("SESSION_ABSOLUTE_TIMEOUT_MINUTES") ?? "720"; // mặc định 12 tiếng
            int absoluteTimeoutMinutes = int.TryParse(absoluteTimeoutStr, out var parsedAbsolute) ? parsedAbsolute : 720;
            HttpContext.Session.SetString("AbsoluteExpiration", DateTimeOffset.UtcNow.AddMinutes(absoluteTimeoutMinutes).ToString("o"));

            // Thiết lập Cookie Authentication song song để hỗ trợ User.Identity trong request pipeline
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, result.Data.Id.ToString()),
                new(ClaimTypes.Email, result.Data.Email),
                new(ClaimTypes.Name, result.Data.FullName),
                new(ClaimTypes.Role, result.Data.Role)
            };
            var claimsIdentity = new ClaimsIdentity(claims, "AtsCookieScheme");
            await HttpContext.SignInAsync("AtsCookieScheme", new ClaimsPrincipal(claimsIdentity));
        }

        return Ok(result);
    }

    /// <summary>
    /// API Kiểm tra trạng thái phiên làm việc (GET: /api/xac-thuc/kiem-tra-phien)
    /// </summary>
    [HttpGet("kiem-tra-phien")]
    public IActionResult CheckSession()
    {
        var userId = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { isSuccess = false, message = "Phiên làm việc đã hết hạn hoặc chưa đăng nhập." });
        }

        return Ok(new
        {
            isSuccess = true,
            message = "Phiên làm việc đang hoạt động.",
            data = new
            {
                userId,
                email = HttpContext.Session.GetString("UserEmail"),
                fullName = HttpContext.Session.GetString("FullName"),
                role = HttpContext.Session.GetString("UserRole"),
                lastActive = HttpContext.Session.GetString("LastActive")
            }
        });
    }

    /// <summary>
    /// API Gia hạn phiên làm việc khi người dùng còn tương tác (POST: /api/xac-thuc/gia-han-phien)
    /// Dành cho Client gọi (Heartbeat/Ping) khi phát hiện thao tác chuột/phím.
    /// </summary>
    [HttpPost("gia-han-phien")]
    public IActionResult RenewSession()
    {
        var userId = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { isSuccess = false, message = "Phiên làm việc đã hết hạn." });
        }

        return Ok(new { isSuccess = true, message = "Gia hạn phiên làm việc thành công." });
    }

    /// <summary>
    /// API Lấy danh sách các phiên đăng nhập đang hoạt động (GET: /api/xac-thuc/danh-sach-phien)
    /// </summary>
    [HttpGet("danh-sach-phien")]
    public IActionResult GetActiveSessions()
    {
        var userIdString = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized(new { isSuccess = false, message = "Chưa đăng nhập." });

        var sessions = _dbContext.UserSessions
            .Where(s => s.UserId == userId && !s.IsRevoked)
            .OrderByDescending(s => s.LastActiveAt)
            .Select(s => new
            {
                s.Id,
                s.IpAddress,
                s.UserAgent,
                s.CreatedAt,
                s.LastActiveAt,
                IsCurrentSession = s.SessionId == HttpContext.Session.Id
            })
            .ToList();

        return Ok(new { isSuccess = true, data = sessions });
    }

    /// <summary>
    /// API Đăng xuất từ xa trên thiết bị khác (POST: /api/xac-thuc/dang-xuat-tu-xa/{sessionId})
    /// </summary>
    [HttpPost("dang-xuat-tu-xa/{sessionId}")]
    public async Task<IActionResult> RemoteLogout(Guid sessionId)
    {
        var userIdString = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized(new { isSuccess = false, message = "Chưa đăng nhập." });

        var sessionToRevoke = _dbContext.UserSessions.FirstOrDefault(s => s.Id == sessionId && s.UserId == userId);
        if (sessionToRevoke == null)
            return NotFound(new { isSuccess = false, message = "Không tìm thấy phiên làm việc." });

        sessionToRevoke.IsRevoked = true;

        // SCRUM-95: Ghi log sự kiện đăng xuất từ xa
        var auditLog = new AuthAuditLog
        {
            Email = HttpContext.Session.GetString("UserEmail") ?? "Unknown",
            UserId = userId,
            SessionId = sessionId.ToString(),
            IsSuccess = true,
            EventType = "RemoteLogout",
            Reason = $"Đăng xuất từ xa thiết bị có ID: {sessionId}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            UserAgent = Request.Headers["User-Agent"].ToString() ?? "Unknown",
            Timestamp = DateTimeOffset.UtcNow
        };
        _dbContext.AuthAuditLogs.Add(auditLog);

        await _dbContext.SaveChangesAsync();

        return Ok(new { isSuccess = true, message = "Đã đăng xuất phiên làm việc từ xa." });
    }

    /// <summary>
    /// API Đăng xuất (POST: /api/xac-thuc/dang-xuat)
    /// </summary>
    [HttpPost("dang-xuat")]
    public async Task<IActionResult> Logout()
    {
        var currentSessionId = HttpContext.Session.Id;
        var userIdString = HttpContext.Session.GetString("UserId");

        // SCRUM-95: Ghi log sự kiện đăng xuất (nếu có user)
        if (!string.IsNullOrEmpty(userIdString) && Guid.TryParse(userIdString, out var userId))
        {
            var auditLog = new AuthAuditLog
            {
                Email = HttpContext.Session.GetString("UserEmail") ?? "Unknown",
                UserId = userId,
                SessionId = currentSessionId,
                IsSuccess = true,
                EventType = "SessionLogout",
                Reason = "Người dùng chủ động đăng xuất",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                UserAgent = Request.Headers["User-Agent"].ToString() ?? "Unknown",
                Timestamp = DateTimeOffset.UtcNow
            };
            _dbContext.AuthAuditLogs.Add(auditLog);
            await _dbContext.SaveChangesAsync();
        }

        // 1. Xóa sạch toàn bộ dữ liệu lưu trong Session phía Server
        HttpContext.Session.Clear();

        // 2. Đăng xuất khỏi Authentication Scheme
        await HttpContext.SignOutAsync("AtsCookieScheme");

        // 3. Xóa các Cookie phiên làm việc ở Client
        Response.Cookies.Delete("Ats.Session");
        Response.Cookies.Delete("Ats.AuthCookie");
        Response.Cookies.Delete(".AspNetCore.Session");

        return Ok(new { isSuccess = true, message = "Đăng xuất thành công. Phiên làm việc đã bị hủy hoàn toàn trên máy chủ." });
    }

    /// <summary>
    /// API Yêu cầu đặt lại mật khẩu (POST: /api/xac-thuc/quen-mat-khau)
    /// </summary>
    [HttpPost("quen-mat-khau")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user == null)
        {
            // Để tránh User Enumeration, luôn trả về thông báo chung chung
            return Ok(new { isSuccess = true, message = "Nếu email hợp lệ, một liên kết khôi phục đã được gửi." });
        }

        // SCRUM-97: Tạo token ngẫu nhiên, an toàn (không đoán được)
        var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        
        // Hash token bằng SHA256 trước khi lưu vào DB để bảo mật
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawToken));
        var hashedToken = Convert.ToBase64String(hashedBytes);

        user.PasswordResetToken = hashedToken;
        user.PasswordResetTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        await _dbContext.SaveChangesAsync();

        // Ở môi trường thực tế, gửi rawToken qua EmailService.
        // Trả về rawToken trong payload chỉ dùng cho mục đích kiểm thử hiện tại.
        var resetLink = $"https://yourdomain.com/dat-lai-mat-khau?token={Uri.EscapeDataString(rawToken)}&email={Uri.EscapeDataString(request.Email)}";

        return Ok(new { isSuccess = true, message = "Đã tạo liên kết khôi phục mật khẩu thành công.", testLink = resetLink });
    }

    /// <summary>
    /// API Kiểm tra Token đặt lại mật khẩu (GET: /api/xac-thuc/kiem-tra-token-dat-lai-mat-khau)
    /// </summary>
    [HttpGet("kiem-tra-token-dat-lai-mat-khau")]
    public async Task<IActionResult> VerifyResetToken([FromQuery] string email, [FromQuery] string token)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null || string.IsNullOrEmpty(user.PasswordResetToken) || !user.PasswordResetTokenExpiresAt.HasValue)
        {
            return BadRequest(new { isSuccess = false, message = "Liên kết không hợp lệ." });
        }

        // SCRUM-97: Token hết hạn sau 30 phút trả về thông báo hết hạn
        if (DateTimeOffset.UtcNow > user.PasswordResetTokenExpiresAt.Value)
        {
            return BadRequest(new { isSuccess = false, message = "Liên kết khôi phục mật khẩu đã hết hạn." });
        }

        // Hash token từ request để so sánh với CSDL
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        var hashedToken = Convert.ToBase64String(hashedBytes);

        if (user.PasswordResetToken != hashedToken)
        {
            return BadRequest(new { isSuccess = false, message = "Liên kết không hợp lệ." });
        }

        return Ok(new { isSuccess = true, message = "Token hợp lệ." });
    }
}