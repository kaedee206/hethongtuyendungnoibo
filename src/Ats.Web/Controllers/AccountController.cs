using System.Security.Claims;
using Ats.Web.Models.ViewModels.Account;
using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller MVC xử lý giao diện xác thực người dùng (EP-01_UserRole).
/// Bao gồm: Đăng nhập (Login), Đăng xuất (Logout).
/// </summary>
public class AccountController(IAuthService authService, ApplicationDbContext dbContext) : Controller
{
    private readonly IAuthService _authService = authService;
    private readonly ApplicationDbContext _dbContext = dbContext;

    // ─────────────────────────────────────────────
    // GET: /Account/Login
    // ─────────────────────────────────────────────
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        // Nếu đã đăng nhập → redirect về trang chủ
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View(new AccountLoginViewModel { ReturnUrl = returnUrl });
    }

    // ─────────────────────────────────────────────
    // POST: /Account/Login
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(AccountLoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        // Gọi AuthService để xác thực (đã có sẵn dùng cho AuthController API)
        var request = new LoginRequestDto(model.Email, model.Password);
        var result = await _authService.AuthenticateAsync(request, cancellationToken);

        // Ghi audit log
        try
        {
            var auditLog = new AuthAuditLog
            {
                Email       = model.Email,
                UserId      = result.Data?.Id,
                SessionId   = result.IsSuccess ? HttpContext.Session.Id : null,
                IsSuccess   = result.IsSuccess,
                EventType   = result.IsSuccess ? "SessionCreate" : "LoginFailed",
                Reason      = result.Message,
                IpAddress   = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                UserAgent   = Request.Headers["User-Agent"].ToString(),
                Timestamp   = DateTimeOffset.UtcNow
            };
            _dbContext.AuthAuditLogs.Add(auditLog);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Bỏ qua nếu database tạm thời không khả dụng trong môi trường dev
        }

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Email hoặc mật khẩu không chính xác.");
            return View(model);
        }

        // Lưu Session
        var user = result.Data!;
        HttpContext.Session.SetString("UserId",   user.Id.ToString());
        HttpContext.Session.SetString("UserEmail", user.Email);
        HttpContext.Session.SetString("UserRole",  user.Role);
        HttpContext.Session.SetString("FullName",  user.FullName);
        HttpContext.Session.SetString("LastActive", DateTimeOffset.UtcNow.ToString("o"));

        var absoluteTimeoutStr    = Environment.GetEnvironmentVariable("SESSION_ABSOLUTE_TIMEOUT_MINUTES") ?? "720";
        int absoluteTimeoutMinutes = int.TryParse(absoluteTimeoutStr, out var p) ? p : 720;
        HttpContext.Session.SetString("AbsoluteExpiration", DateTimeOffset.UtcNow.AddMinutes(absoluteTimeoutMinutes).ToString("o"));

        // Lưu UserSession vào CSDL
        try
        {
            var userSession = new UserSession
            {
                UserId       = user.Id,
                SessionId    = HttpContext.Session.Id,
                IpAddress    = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                UserAgent    = Request.Headers["User-Agent"].ToString(),
                CreatedAt    = DateTimeOffset.UtcNow,
                LastActiveAt = DateTimeOffset.UtcNow,
                IsRevoked    = false
            };
            _dbContext.UserSessions.Add(userSession);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Bỏ qua nếu database tạm thời không khả dụng trong môi trường dev
        }

        // Thiết lập Cookie Authentication
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email,          user.Email),
            new(ClaimTypes.Name,           user.FullName),
            new(ClaimTypes.Role,           user.Role)
        };
        var identity  = new ClaimsIdentity(claims, "AtsCookieScheme");
        await HttpContext.SignInAsync("AtsCookieScheme", new ClaimsPrincipal(identity));

        // Redirect an toàn: chỉ chấp nhận local URL
        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);

        return RedirectToAction("Index", "Home");
    }

    // ─────────────────────────────────────────────
    // GET: /Account/Logout
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        // Thu hồi UserSession trong CSDL
        try
        {
            var sessionId = HttpContext.Session.Id;
            var userSession = await _dbContext.UserSessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
            if (userSession != null)
            {
                userSession.IsRevoked = true;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception)
        {
            // Bỏ qua lỗi DB khi đăng xuất
        }

        // Xóa Session & Cookie
        HttpContext.Session.Clear();
        await HttpContext.SignOutAsync("AtsCookieScheme");

        return RedirectToAction(nameof(Login));
    }

    // ─────────────────────────────────────────────
    // GET: /Account/Profile  (placeholder)
    // ─────────────────────────────────────────────
    [HttpGet]
    public IActionResult Profile()
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction(nameof(Login));

        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /Account/ChangePassword  (placeholder)
    // ─────────────────────────────────────────────
    [HttpGet]
    public IActionResult ChangePassword()
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction(nameof(Login));

        return View();
    }
}
