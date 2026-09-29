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
public class AuthController(IAuthService authService, ApplicationDbContext dbContext, IEmailService emailService) : ControllerBase
{
    private readonly IAuthService _authService = authService;
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IEmailService _emailService = emailService;

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
        
        // Luôn trả về 1 thông điệp duy nhất để ngăn ngừa User Enumeration (SCRUM-100)
        var genericMessage = "Nếu email hợp lệ, một liên kết khôi phục đã được gửi.";

        if (user != null)
        {
            // SCRUM-107: Ghi log yêu cầu khôi phục mật khẩu
            _dbContext.AuthAuditLogs.Add(new AuthAuditLog {
                Email = request.Email,
                UserId = user.Id,
                IsSuccess = true,
                EventType = "PasswordResetRequest",
                Reason = "Yêu cầu khôi phục mật khẩu",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                UserAgent = Request.Headers["User-Agent"].ToString() ?? "Unknown",
                Timestamp = DateTimeOffset.UtcNow
            });

            // Tạo token ngẫu nhiên, an toàn (không đoán được)
            var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            
            // Hash token bằng SHA256 trước khi lưu vào DB để bảo mật
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawToken));
            var hashedToken = Convert.ToBase64String(hashedBytes);

            user.PasswordResetToken = hashedToken;
            user.PasswordResetTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
            await _dbContext.SaveChangesAsync();

            var resetLink = $"https://yourdomain.com/dat-lai-mat-khau?token={Uri.EscapeDataString(rawToken)}&email={Uri.EscapeDataString(request.Email)}";
            var emailBody = $@"
                <h3>Yêu cầu đặt lại mật khẩu</h3>
                <p>Xin chào,</p>
                <p>Bạn đã yêu cầu đặt lại mật khẩu cho tài khoản hệ thống ATS. Vui lòng click vào liên kết bên dưới để đặt lại mật khẩu:</p>
                <p><a href='{resetLink}'>{resetLink}</a></p>
                <p>Liên kết này sẽ hết hạn trong vòng 30 phút.</p>
                <p>Nếu bạn không yêu cầu, vui lòng bỏ qua email này.</p>
            ";

            var sendEmailRequest = new SendEmailRequestDto(
                ToEmail: request.Email,
                Subject: "[ATS] Yêu cầu khôi phục mật khẩu",
                Body: emailBody
            );

            // Gửi email không đợi (Fire-and-forget) để đảm bảo thời gian phản hồi API là như nhau
            // Phải tạo Service Scope mới vì IEmailService của Controller sẽ bị huỷ khi Request kết thúc
            _ = Task.Run(async () => 
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var scopedEmailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                await scopedEmailService.SendEmailAsync(sendEmailRequest);
            });
        }
        else
        {
            // Thực hiện tính toán giả và delay (Dummy Hash & Delay) để mô phỏng thời gian lưu DB
            // Điều này giúp phản hồi API luôn đồng nhất về mặt thời gian (chống Timing Attack)
            var dummyRaw = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            _ = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(dummyRaw));
            
            await Task.Delay(20); // Mô phỏng thời gian _dbContext.SaveChangesAsync()
        }

        return Ok(new { isSuccess = true, message = genericMessage });
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
    /// <summary>
    /// API Đặt lại mật khẩu mới (POST: /api/xac-thuc/dat-lai-mat-khau)
    /// </summary>
    [HttpPost("dat-lai-mat-khau")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var userAgent = Request.Headers["User-Agent"].ToString() ?? "Unknown";

        // 1. Kiểm tra Token có tồn tại và hợp lệ không
        if (user == null || string.IsNullOrEmpty(user.PasswordResetToken) || !user.PasswordResetTokenExpiresAt.HasValue)
        {
            // SCRUM-107: Log thất bại
            _dbContext.AuthAuditLogs.Add(new AuthAuditLog { Email = request.Email, UserId = user?.Id, IsSuccess = false, EventType = "PasswordResetFailed", Reason = "Liên kết không hợp lệ hoặc đã được sử dụng", IpAddress = ip, UserAgent = userAgent });
            await _dbContext.SaveChangesAsync();
            return BadRequest(new { isSuccess = false, message = "Liên kết không hợp lệ hoặc đã được sử dụng." });
        }

        // 2. Kiểm tra Token có hết hạn không
        if (DateTimeOffset.UtcNow > user.PasswordResetTokenExpiresAt.Value)
        {
            _dbContext.AuthAuditLogs.Add(new AuthAuditLog { Email = request.Email, UserId = user.Id, IsSuccess = false, EventType = "PasswordResetFailed", Reason = "Liên kết khôi phục mật khẩu đã hết hạn", IpAddress = ip, UserAgent = userAgent });
            await _dbContext.SaveChangesAsync();
            return BadRequest(new { isSuccess = false, message = "Liên kết khôi phục mật khẩu đã hết hạn." });
        }

        // 3. Hash token từ request để so sánh với CSDL
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(request.Token));
        var hashedToken = Convert.ToBase64String(hashedBytes);

        if (user.PasswordResetToken != hashedToken)
        {
            _dbContext.AuthAuditLogs.Add(new AuthAuditLog { Email = request.Email, UserId = user.Id, IsSuccess = false, EventType = "PasswordResetFailed", Reason = "Liên kết không hợp lệ (Sai token)", IpAddress = ip, UserAgent = userAgent });
            await _dbContext.SaveChangesAsync();
            return BadRequest(new { isSuccess = false, message = "Liên kết không hợp lệ." });
        }

        // 4. Kiểm tra mật khẩu mới có trùng mật khẩu cũ không (SCRUM-105)
        bool isSamePassword = false;
        try
        {
            isSamePassword = BCrypt.Net.BCrypt.Verify(request.NewPassword, user.PasswordHash);
        }
        catch { /* Bỏ qua nếu PasswordHash cũ không phải định dạng BCrypt */ }

        if (isSamePassword)
        {
            _dbContext.AuthAuditLogs.Add(new AuthAuditLog { Email = request.Email, UserId = user.Id, IsSuccess = false, EventType = "PasswordResetFailed", Reason = "Mật khẩu mới trùng với mật khẩu hiện tại", IpAddress = ip, UserAgent = userAgent });
            await _dbContext.SaveChangesAsync();
            return BadRequest(new { isSuccess = false, message = "Mật khẩu mới không được trùng với mật khẩu hiện tại." });
        }

        // 5. Nếu hợp lệ, đặt lại mật khẩu mới bằng BCrypt
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        
        // 6. SCRUM-99: Vô hiệu hóa Token ngay lập tức để không thể tái sử dụng
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiresAt = null;
        
        // (Tùy chọn) Mở khóa tài khoản nếu đang bị khóa
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;

        // 7. SCRUM-106: Huỷ tất cả phiên đăng nhập hiện tại trên mọi thiết bị
        var activeSessions = await _dbContext.UserSessions
            .Where(s => s.UserId == user.Id && !s.IsRevoked)
            .ToListAsync();
            
        foreach (var session in activeSessions)
        {
            session.IsRevoked = true;
            
            // Có thể ghi thêm Log (tuỳ chọn)
            _dbContext.AuthAuditLogs.Add(new AuthAuditLog
            {
                Email = user.Email,
                UserId = user.Id,
                SessionId = session.SessionId,
                IsSuccess = true,
                EventType = "RemoteLogout",
                Reason = "Hệ thống tự động hủy phiên do người dùng đổi mật khẩu",
                IpAddress = ip,
                UserAgent = "System",
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        // SCRUM-107: Log thành công
        _dbContext.AuthAuditLogs.Add(new AuthAuditLog {
            Email = request.Email,
            UserId = user.Id,
            IsSuccess = true,
            EventType = "PasswordResetSuccess",
            Reason = "Đặt lại mật khẩu thành công",
            IpAddress = ip,
            UserAgent = userAgent
        });

        await _dbContext.SaveChangesAsync();

        return Ok(new { isSuccess = true, message = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại trên tất cả thiết bị." });
    }

    /// <summary>
    /// API Đổi mật khẩu cho người dùng đang đăng nhập (POST: /api/xac-thuc/doi-mat-khau)
    /// </summary>
    [HttpPost("doi-mat-khau")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // 1. Kiểm tra User đã đăng nhập chưa
        var userIdString = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized(new { isSuccess = false, message = "Vui lòng đăng nhập để thực hiện chức năng này." });
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
        {
            return Unauthorized(new { isSuccess = false, message = "Không tìm thấy thông tin tài khoản." });
        }

        // SCRUM-115: Kiểm tra tài khoản có đang bị khóa chức năng đổi mật khẩu không
        if (user.ChangePasswordLockedUntil.HasValue && user.ChangePasswordLockedUntil.Value > DateTimeOffset.UtcNow)
        {
            var remainingTime = (int)Math.Ceiling((user.ChangePasswordLockedUntil.Value - DateTimeOffset.UtcNow).TotalMinutes);
            return BadRequest(new { isSuccess = false, message = $"Chức năng đổi mật khẩu bị khóa tạm thời do nhập sai quá nhiều lần. Vui lòng thử lại sau {remainingTime} phút." });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var userAgent = Request.Headers["User-Agent"].ToString() ?? "Unknown";

        // 2. SCRUM-109: Xác thực mật khẩu hiện tại
        bool isCurrentPasswordValid = false;
        try
        {
            isCurrentPasswordValid = BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash);
        }
        catch { /* Bỏ qua lỗi format */ }

        if (!isCurrentPasswordValid)
        {
            // SCRUM-115: Tăng số lần nhập sai và khóa nếu quá 5 lần
            user.FailedChangePasswordAttempts++;
            if (user.FailedChangePasswordAttempts >= 5)
            {
                user.ChangePasswordLockedUntil = DateTimeOffset.UtcNow.AddMinutes(15);
                user.FailedChangePasswordAttempts = 0; // Reset lại đếm để lần sau đếm lại từ đầu sau khi hết khóa
            }
            await _dbContext.SaveChangesAsync();

            // Trả lỗi rõ ràng nhưng không quá chi tiết để tránh dò rỉ thông tin
            _dbContext.AuthAuditLogs.Add(new AuthAuditLog { Email = user.Email, UserId = user.Id, IsSuccess = false, EventType = "ChangePasswordFailed", Reason = "Sai mật khẩu hiện tại", IpAddress = ip, UserAgent = userAgent });
            await _dbContext.SaveChangesAsync();
            return BadRequest(new { isSuccess = false, message = "Mật khẩu hiện tại không chính xác." });
        }
        
        // Reset đếm nếu nhập đúng
        if (user.FailedChangePasswordAttempts > 0 || user.ChangePasswordLockedUntil.HasValue)
        {
            user.FailedChangePasswordAttempts = 0;
            user.ChangePasswordLockedUntil = null;
        }

        // 3. Đảm bảo mật khẩu mới không trùng mật khẩu cũ
        if (request.CurrentPassword == request.NewPassword)
        {
            _dbContext.AuthAuditLogs.Add(new AuthAuditLog { Email = user.Email, UserId = user.Id, IsSuccess = false, EventType = "ChangePasswordFailed", Reason = "Mật khẩu mới trùng mật khẩu hiện tại", IpAddress = ip, UserAgent = userAgent });
            await _dbContext.SaveChangesAsync();
            return BadRequest(new { isSuccess = false, message = "Mật khẩu mới không được trùng với mật khẩu hiện tại." });
        }

        // 4. Lưu mật khẩu mới
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

        // 5. Huỷ tất cả các phiên đăng nhập khác (SCRUM-106 áp dụng tương tự)
        var currentSessionId = HttpContext.Session.Id;
        var otherSessions = await _dbContext.UserSessions
            .Where(s => s.UserId == user.Id && s.SessionId != currentSessionId && !s.IsRevoked)
            .ToListAsync();
            
        foreach (var session in otherSessions)
        {
            session.IsRevoked = true;
            _dbContext.AuthAuditLogs.Add(new AuthAuditLog
            {
                Email = user.Email,
                UserId = user.Id,
                SessionId = session.SessionId,
                IsSuccess = true,
                EventType = "RemoteLogout",
                Reason = "Hủy phiên do người dùng đổi mật khẩu",
                IpAddress = ip,
                UserAgent = "System",
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        _dbContext.AuthAuditLogs.Add(new AuthAuditLog {
            Email = user.Email,
            UserId = user.Id,
            IsSuccess = true,
            EventType = "ChangePasswordSuccess",
            Reason = "Đổi mật khẩu thành công",
            IpAddress = ip,
            UserAgent = userAgent
        });

        await _dbContext.SaveChangesAsync();

        return Ok(new { isSuccess = true, message = "Đổi mật khẩu thành công." });
    }
}