using System.Security.Claims;
using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

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

        // SCRUM-85: Ghi log audit cho lần đăng nhập
        var auditLog = new AuthAuditLog
        {
            Email = request.Email,
            UserId = result.Data?.Id, // Có thể null nếu đăng nhập thất bại
            IsSuccess = result.IsSuccess,
            Reason = result.Message,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            UserAgent = Request.Headers["User-Agent"].ToString() ?? "Unknown",
            Timestamp = DateTimeOffset.UtcNow
        };
        _dbContext.AuthAuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!result.IsSuccess)
            return Unauthorized(new { message = result.Message });

        // SCRUM-84: Tạo và lưu thông tin phiên vào Session phía Server
        if (result.Data != null)
        {
            HttpContext.Session.SetString("UserId", result.Data.Id.ToString());
            HttpContext.Session.SetString("UserEmail", result.Data.Email);
            HttpContext.Session.SetString("UserRole", result.Data.Role);
            HttpContext.Session.SetString("FullName", result.Data.FullName);
            HttpContext.Session.SetString("LastActive", DateTimeOffset.UtcNow.ToString("o"));

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
    /// API Đăng xuất (POST: /api/xac-thuc/dang-xuat)
    /// </summary>
    [HttpPost("dang-xuat")]
    public async Task<IActionResult> Logout()
    {
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
}