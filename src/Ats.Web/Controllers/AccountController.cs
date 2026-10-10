using System.Linq;
using System.Security.Claims;
using Ats.Web.Common;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.ViewModels.Account;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller xử lý các chức năng xác thực người dùng (S1-01, S1-02, S1-03, S1-04).
/// Tuân thủ Clean Controller, Primary Constructor, ValidateAntiForgeryToken.
/// </summary>
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("AuthRateLimit")]
public class AccountController(
    IAuthService authService,
    IEmailService emailService,
    ILogger<AccountController> logger,
    IWebHostEnvironment? webHostEnvironment = null,
    ApplicationDbContext? dbContext = null,
    ISecurityAuditService? auditService = null) : Controller
{
    private readonly IAuthService _authService = authService;
    private readonly IEmailService _emailService = emailService;
    private readonly ILogger<AccountController> _logger = logger;
    private readonly IWebHostEnvironment? _webHostEnvironment = webHostEnvironment;
    private readonly ApplicationDbContext? _dbContext = dbContext;
    private readonly ISecurityAuditService? _auditService = auditService;

    private string GetClientIp()
    {
        return ClientIpHelper.GetClientIpAddress(HttpContext);
    }

    private string GetClientDevice()
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(userAgent) ? "Trình duyệt Web (Không xác định)" : userAgent;
    }

    private void DispatchLoginSuccessAlert(string email, string fullName)
    {
        var ip = GetClientIp();
        var userAgent = GetClientDevice();
        _ = Task.Run(async () =>
        {
            try
            {
                await _emailService.SendLoginSuccessAlertAsync(
                    email,
                    fullName,
                    ip,
                    "Việt Nam",
                    userAgent,
                    DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Không thể gửi email cảnh báo đăng nhập cho {Email}: {Msg}", email, ex.Message);
            }
        });
    }

    #region S1-01: Đăng nhập

    [HttpGet("/Account/Login")]
    [HttpGet("/accounts/login")]
    [HttpGet("/dang-nhap")]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl, bool expired = false, bool revoked = false)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToDefaultRoute();
        }

        // Tự động chuyển hướng tới Cổng Đăng nhập Nhân sự Nội bộ mới (StaffLogin)
        return RedirectToAction(nameof(StaffLogin), new { returnUrl, expired, revoked });
    }

    [HttpPost("/Account/Login")]
    [HttpPost("/accounts/login")]
    [HttpPost("/dang-nhap")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(AccountLoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var ip = GetClientIp();
        var loginDto = new LoginRequestDto(model.Email, model.Password);

        var result = await _authService.AuthenticateAsync(loginDto, ip);

        if (!result.IsSuccess || result.Data == null)
        {
            var isBlocked = result.Message?.Contains("bị tạm khóa", StringComparison.OrdinalIgnoreCase) == true;
            _auditService?.LogSecurityEvent(new SecurityAuditEvent
            {
                EventType = isBlocked ? SecurityAuditEventType.IpBlocked : SecurityAuditEventType.LoginFailed,
                ClientIp = ip,
                UserNameOrEmail = model.Email,
                Action = "LOGIN_STAFF_FAILED",
                IsSuccess = false,
                Details = result.Message
            });

            // Hiển thị thông báo chi tiết bao gồm số lần thử còn lại /5 lần
            ModelState.AddModelError(string.Empty, result.Message ?? "Email hoặc mật khẩu không chính xác.");
            return View(model);
        }

        var userInfo = result.Data;

        // Thiết lập Session
        HttpContext.Session.SetString("UserId", userInfo.Id.ToString());
        HttpContext.Session.SetString("UserEmail", userInfo.Email);
        HttpContext.Session.SetString("UserRole", userInfo.Role);
        HttpContext.Session.SetString("FullName", userInfo.FullName);
        
        var idleTimeoutMinutes = int.TryParse(Environment.GetEnvironmentVariable("SESSION_IDLE_TIMEOUT_MINUTES"), out var parsedTimeout) ? parsedTimeout : 30;
        var maxSessionHours = int.TryParse(Environment.GetEnvironmentVariable("SESSION_MAX_LIFETIME_HOURS"), out var parsedMax) ? parsedMax : 8;
        HttpContext.Session.SetString("AbsoluteExpiration", DateTimeOffset.UtcNow.AddHours(maxSessionHours).ToString("o"));

        // Thiết lập Cookie Authentication với ClaimsPrincipal hỗ trợ đa vai trò (S1-09)
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userInfo.Id.ToString()),
            new Claim(ClaimTypes.Email, userInfo.Email),
            new Claim(ClaimTypes.Name, userInfo.FullName)
        };

        foreach (var role in userInfo.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            var norm = UserRoles.NormalizeRole(role);
            if (!string.Equals(norm, role, StringComparison.OrdinalIgnoreCase))
            {
                claims.Add(new Claim(ClaimTypes.Role, norm));
            }
        }

        // Đảm bảo Primary Role cũng có mặt trong claim role
        if (!userInfo.Roles.Contains(userInfo.Role, StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, userInfo.Role));
        }
        var normPrimary = UserRoles.NormalizeRole(userInfo.Role);
        if (!claims.Any(c => c.Type == ClaimTypes.Role && string.Equals(c.Value, normPrimary, StringComparison.OrdinalIgnoreCase)))
        {
            claims.Add(new Claim(ClaimTypes.Role, normPrimary));
        }

        var claimsIdentity = new ClaimsIdentity(claims, "AtsCookieScheme");
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(idleTimeoutMinutes)
        };

        await HttpContext.SignInAsync("AtsCookieScheme", new ClaimsPrincipal(claimsIdentity), authProperties);

        _auditService?.LogSecurityEvent(new SecurityAuditEvent
        {
            EventType = SecurityAuditEventType.LoginSuccess,
            UserId = userInfo.Id.ToString(),
            UserNameOrEmail = userInfo.Email,
            ClientIp = ip,
            Action = "LOGIN_STAFF_SUCCESS",
            IsSuccess = true,
            Details = $"Đăng nhập thành công với chức danh và vai trò: {userInfo.Role}"
        });

        _logger.LogInformation("Người dùng {Email} đăng nhập thành công từ IP {Ip}.", PiiMaskingHelper.MaskEmail(model.Email), ip);
        DispatchLoginSuccessAlert(userInfo.Email, userInfo.FullName);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        return RedirectToRoleHome(userInfo.Roles);
    }

    #endregion

    #region S1-02: Đổi mật khẩu cá nhân

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword()
    {
        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            return RedirectToAction(nameof(Login));
        }

        var sessionId = HttpContext.Session.Id;
        var result = await _authService.ChangePasswordAsync(userId, model.CurrentPassword, model.NewPassword, sessionId);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        TempData["SuccessMessage"] = "Đổi mật khẩu thành công. Vui lòng ghi nhớ mật khẩu mới của bạn.";
        return RedirectToAction("Index", "Home");
    }

    #endregion

    #region S1-03: Quên mật khẩu & Đặt lại mật khẩu

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword()
    {
        return View(new ForgotPasswordViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        await _authService.ForgotPasswordAsync(model.Email.Trim().ToLower(), baseUrl);

        // Luôn hiển thị thông báo thành công chung để bảo mật thông tin (tránh Email Enumeration)
        model.EmailSent = true;
        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPassword(string token, string? email)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            TempData["ErrorMessage"] = "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.";
            return RedirectToAction(nameof(Login));
        }

        var model = new ResetPasswordViewModel
        {
            Token = token,
            Email = email ?? string.Empty
        };

        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var dto = new ResetPasswordRequestDto(model.Email.Trim().ToLower(), model.Token, model.NewPassword);
        var result = await _authService.ResetPasswordAsync(dto);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        TempData["SuccessMessage"] = "Đặt lại mật khẩu thành công! Bạn có thể đăng nhập ngay bằng mật khẩu mới.";
        return RedirectToAction(nameof(Login));
    }

    #endregion

    #region S1-04: Đăng xuất

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userIdString = HttpContext.Session.GetString("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var sessionId = HttpContext.Session.Id;

        if (Guid.TryParse(userIdString, out var userId))
        {
            await _authService.LogoutAsync(userId, sessionId);
        }

        HttpContext.Session.Clear();
        await HttpContext.SignOutAsync("AtsCookieScheme");

        Response.Cookies.Delete("Ats.Session");
        Response.Cookies.Delete("Ats.AuthCookie");
        Response.Cookies.Delete(".AspNetCore.Session");

        TempData["SuccessMessage"] = "Bạn đã đăng xuất khỏi hệ thống thành công.";
        return RedirectToAction(nameof(Login));
    }

    #endregion

    #region S1-01-NEW: Cổng Nhân sự Nội bộ (Enterprise Auth UI)

    /// <summary>
    /// GET /Account/StaffLogin — Cổng đăng nhập nhân sự nội bộ.
    /// Hiển thị Composite ViewModel với sliding panel Login/ForgotPassword.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult StaffLogin(string? returnUrl, bool expired = false, bool revoked = false)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToDefaultRoute();
        }

        var model = new StaffAuthCompositeViewModel
        {
            LoginInput = new StaffLoginInputModel { ReturnUrl = returnUrl },
            ForgotPasswordInput = new StaffForgotPasswordInputModel { ReturnUrl = returnUrl },
            ActivePanel = "login"
        };

        if (expired)
        {
            ModelState.AddModelError(string.Empty, "Phiên làm việc đã hết hạn. Vui lòng đăng nhập lại.");
        }
        else if (revoked)
        {
            ModelState.AddModelError(string.Empty, "Phiên làm việc đã bị thu hồi hoặc đăng xuất từ thiết bị khác.");
        }

        return View(model);
    }

    /// <summary>
    /// POST /Account/StaffLogin — Xử lý đăng nhập nhân sự, bảo vệ bằng AntiForgery + rate-limit thông qua AuthService.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StaffLogin(StaffAuthCompositeViewModel model)
    {
        // Chỉ validate LoginInput, clear validation errors của ForgotPasswordInput
        ModelState.Remove(nameof(model.ForgotPasswordInput) + "." + nameof(model.ForgotPasswordInput.Email));
        model.ActivePanel = "login";

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var loginDto = new LoginRequestDto(model.LoginInput.Email, model.LoginInput.Password);
        var result = await _authService.AuthenticateAsync(loginDto);

        if (!result.IsSuccess || result.Data == null)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Email hoặc mật khẩu không chính xác.");
            return View(model);
        }

        var userInfo = result.Data;

        // Session setup
        HttpContext.Session.SetString("UserId", userInfo.Id.ToString());
        HttpContext.Session.SetString("UserEmail", userInfo.Email);
        HttpContext.Session.SetString("UserRole", userInfo.Role);
        HttpContext.Session.SetString("FullName", userInfo.FullName);

        var idleTimeoutMinutes = int.TryParse(Environment.GetEnvironmentVariable("SESSION_IDLE_TIMEOUT_MINUTES"), out var parsedTimeout) ? parsedTimeout : 30;
        var maxSessionHours = int.TryParse(Environment.GetEnvironmentVariable("SESSION_MAX_LIFETIME_HOURS"), out var parsedMax) ? parsedMax : 8;
        HttpContext.Session.SetString("AbsoluteExpiration", DateTimeOffset.UtcNow.AddHours(maxSessionHours).ToString("o"));

        // Claims + Cookie
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userInfo.Id.ToString()),
            new Claim(ClaimTypes.Email, userInfo.Email),
            new Claim(ClaimTypes.Name, userInfo.FullName)
        };

        foreach (var role in userInfo.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            var norm = UserRoles.NormalizeRole(role);
            if (!string.Equals(norm, role, StringComparison.OrdinalIgnoreCase))
            {
                claims.Add(new Claim(ClaimTypes.Role, norm));
            }
        }

        if (!userInfo.Roles.Contains(userInfo.Role, StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, userInfo.Role));
        }
        var normPrimary = UserRoles.NormalizeRole(userInfo.Role);
        if (!claims.Any(c => c.Type == ClaimTypes.Role && string.Equals(c.Value, normPrimary, StringComparison.OrdinalIgnoreCase)))
        {
            claims.Add(new Claim(ClaimTypes.Role, normPrimary));
        }

        var claimsIdentity = new ClaimsIdentity(claims, "AtsCookieScheme");
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.LoginInput.RememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(idleTimeoutMinutes)
        };

        await HttpContext.SignInAsync("AtsCookieScheme", new ClaimsPrincipal(claimsIdentity), authProperties);

        _logger.LogInformation("Nhân sự {Email} đăng nhập qua StaffPortal từ IP {Ip}.", model.LoginInput.Email, ip);
        DispatchLoginSuccessAlert(userInfo.Email, userInfo.FullName);

        if (!string.IsNullOrEmpty(model.LoginInput.ReturnUrl) && Url.IsLocalUrl(model.LoginInput.ReturnUrl))
        {
            return Redirect(model.LoginInput.ReturnUrl);
        }

        return RedirectToRoleHome(userInfo.Roles);
    }

    /// <summary>
    /// POST /Account/StaffForgotPassword — Xử lý yêu cầu quên mật khẩu nhân sự.
    /// Trả về cùng view ở trạng thái "forgot" với thông báo chung (anti email enumeration).
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StaffForgotPassword(StaffAuthCompositeViewModel model)
    {
        // Chỉ validate ForgotPasswordInput
        ModelState.Remove(nameof(model.LoginInput) + "." + nameof(model.LoginInput.Email));
        ModelState.Remove(nameof(model.LoginInput) + "." + nameof(model.LoginInput.Password));
        model.ActivePanel = "forgot";

        if (!ModelState.IsValid)
        {
            return View("StaffLogin", model);
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        await _authService.ForgotPasswordAsync(model.ForgotPasswordInput.Email.Trim().ToLower(), baseUrl);

        // Anti Email Enumeration: luôn trả về thông báo thành công chung
        model.IsSuccess = true;
        model.StatusMessage = "Nếu email tổ chức tồn tại trong hệ thống, liên kết đặt lại mật khẩu đã được gửi đến hộp thư của bạn.";
        return View("StaffLogin", model);
    }

    #endregion

    #region S1-02-NEW: Cổng Tuyển dụng Ứng viên (Candidate Portal)

    /// <summary>
    /// GET /Account/CandidateAuth — Cổng xác thực ứng viên (đăng nhập / đăng ký / quên mật khẩu).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult CandidateAuth(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        var model = new CandidateAuthCompositeViewModel
        {
            LoginInput = new CandidateLoginInputModel { ReturnUrl = returnUrl },
            RegisterInput = new CandidateRegisterInputModel { ReturnUrl = returnUrl },
            ForgotPasswordInput = new CandidateForgotPasswordInputModel { ReturnUrl = returnUrl },
            ActivePanel = "signin"
        };

        return View(model);
    }

    /// <summary>
    /// POST /Account/CandidateLogin — Xử lý đăng nhập ứng viên.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CandidateLogin(CandidateAuthCompositeViewModel model)
    {
        // Chỉ validate LoginInput
        foreach (var key in ModelState.Keys.Where(k => !k.StartsWith("LoginInput.")).ToList())
        {
            ModelState.Remove(key);
        }
        model.ActivePanel = "signin";

        if (!ModelState.IsValid)
        {
            return View("CandidateAuth", model);
        }

        var ip = GetClientIp();
        var loginDto = new LoginRequestDto(model.LoginInput.Email, model.LoginInput.Password);
        var result = await _authService.AuthenticateAsync(loginDto, ip);

        if (!result.IsSuccess || result.Data == null)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Email hoặc mật khẩu không chính xác.");
            return View("CandidateAuth", model);
        }

        var userInfo = result.Data;

        HttpContext.Session.SetString("UserId", userInfo.Id.ToString());
        HttpContext.Session.SetString("UserEmail", userInfo.Email);
        HttpContext.Session.SetString("UserRole", userInfo.Role);
        HttpContext.Session.SetString("FullName", userInfo.FullName);

        var idleTimeoutMinutes = int.TryParse(Environment.GetEnvironmentVariable("SESSION_IDLE_TIMEOUT_MINUTES"), out var parsedTimeout) ? parsedTimeout : 30;
        var maxSessionHours = int.TryParse(Environment.GetEnvironmentVariable("SESSION_MAX_LIFETIME_HOURS"), out var parsedMax) ? parsedMax : 8;
        HttpContext.Session.SetString("AbsoluteExpiration", DateTimeOffset.UtcNow.AddHours(maxSessionHours).ToString("o"));

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userInfo.Id.ToString()),
            new Claim(ClaimTypes.Email, userInfo.Email),
            new Claim(ClaimTypes.Name, userInfo.FullName)
        };

        foreach (var role in userInfo.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            var norm = UserRoles.NormalizeRole(role);
            if (!string.Equals(norm, role, StringComparison.OrdinalIgnoreCase))
            {
                identityClaimsAdd(norm);
            }
        }

        void identityClaimsAdd(string role)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        if (!userInfo.Roles.Contains(userInfo.Role, StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, userInfo.Role));
        }
        var normPrimary = UserRoles.NormalizeRole(userInfo.Role);
        if (!claims.Any(c => c.Type == ClaimTypes.Role && string.Equals(c.Value, normPrimary, StringComparison.OrdinalIgnoreCase)))
        {
            claims.Add(new Claim(ClaimTypes.Role, normPrimary));
        }

        var claimsIdentity = new ClaimsIdentity(claims, "AtsCookieScheme");
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.LoginInput.RememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(idleTimeoutMinutes)
        };

        await HttpContext.SignInAsync("AtsCookieScheme", new ClaimsPrincipal(claimsIdentity), authProperties);

        _logger.LogInformation("Ứng viên {Email} đăng nhập từ IP {Ip}.", PiiMaskingHelper.MaskEmail(model.LoginInput.Email), ip);
        DispatchLoginSuccessAlert(userInfo.Email, userInfo.FullName);

        if (!string.IsNullOrEmpty(model.LoginInput.ReturnUrl) && Url.IsLocalUrl(model.LoginInput.ReturnUrl))
        {
            return Redirect(model.LoginInput.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    /// <summary>
    /// POST /Account/CandidateRegister — Bước 1: Tiếp nhận đăng ký ứng viên và gửi mã OTP qua email.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CandidateRegister(CandidateAuthCompositeViewModel model)
    {
        // Chỉ validate RegisterInput
        foreach (var key in ModelState.Keys.Where(k => !k.StartsWith("RegisterInput.")).ToList())
        {
            ModelState.Remove(key);
        }
        model.ActivePanel = "signup";

        if (!model.RegisterInput.AgreeToTerms)
        {
            ModelState.AddModelError("RegisterInput.AgreeToTerms", "Bạn phải đồng ý với Điều khoản dịch vụ và Chính sách quyền riêng tư để tạo tài khoản.");
        }

        if (!ModelState.IsValid)
        {
            if (IsAjaxRequest())
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return Json(new { success = false, message = errors.FirstOrDefault() ?? "Dữ liệu đăng ký không hợp lệ." });
            }
            return View("CandidateAuth", model);
        }

        var ip = GetClientIp();
        var (isSuccess, message) = await _authService.SendRegistrationOtpAsync(
            model.RegisterInput.FullName,
            model.RegisterInput.Email,
            model.RegisterInput.Password,
            model.RegisterInput.PhoneNumber,
            ip);

        if (!isSuccess)
        {
            if (IsAjaxRequest())
            {
                return Json(new { success = false, message });
            }
            model.IsSuccess = false;
            model.StatusMessage = message;
            return View("CandidateAuth", model);
        }

        _logger.LogInformation("Đã gửi mã OTP đăng ký tới email ứng viên: {Email} từ IP {Ip}", PiiMaskingHelper.MaskEmail(model.RegisterInput.Email), ip);

        if (IsAjaxRequest())
        {
            return Json(new { success = true, email = model.RegisterInput.Email, message });
        }

        model.OtpStep = "verify";
        model.PendingEmail = model.RegisterInput.Email;
        model.VerifyRegisterOtpInput.Email = model.RegisterInput.Email;
        model.VerifyRegisterOtpInput.ReturnUrl = model.RegisterInput.ReturnUrl;
        model.IsSuccess = true;
        model.StatusMessage = message;

        return View("CandidateAuth", model);
    }

    /// <summary>
    /// POST /Account/CandidateVerifyRegisterOtp — Bước 2: Xác thực mã OTP và kích hoạt tài khoản ứng viên.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CandidateVerifyRegisterOtp(CandidateAuthCompositeViewModel model)
    {
        foreach (var key in ModelState.Keys.Where(k => !k.StartsWith("VerifyRegisterOtpInput.")).ToList())
        {
            ModelState.Remove(key);
        }
        model.ActivePanel = "signup";
        model.OtpStep = "verify";

        if (!ModelState.IsValid)
        {
            if (IsAjaxRequest())
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return Json(new { success = false, message = errors.FirstOrDefault() ?? "Vui lòng nhập đúng mã OTP 6 số." });
            }
            return View("CandidateAuth", model);
        }

        var ip = GetClientIp();
        var (isSuccess, message) = await _authService.VerifyRegistrationOtpAsync(
            model.VerifyRegisterOtpInput.Email,
            model.VerifyRegisterOtpInput.OtpCode,
            ip);

        if (!isSuccess)
        {
            if (IsAjaxRequest())
            {
                return Json(new { success = false, message });
            }
            model.IsSuccess = false;
            model.StatusMessage = message;
            model.PendingEmail = model.VerifyRegisterOtpInput.Email;
            return View("CandidateAuth", model);
        }

        if (IsAjaxRequest())
        {
            return Json(new { success = true, message, email = model.VerifyRegisterOtpInput.Email });
        }

        // Chuyển sang form signin với email điền sẵn và thông báo thành công
        var successModel = new CandidateAuthCompositeViewModel
        {
            ActivePanel = "signin",
            IsSuccess = true,
            StatusMessage = message,
            LoginInput = new CandidateLoginInputModel
            {
                Email = model.VerifyRegisterOtpInput.Email,
                ReturnUrl = model.VerifyRegisterOtpInput.ReturnUrl
            }
        };

        return View("CandidateAuth", successModel);
    }

    /// <summary>
    /// POST /Account/CandidateForgotPassword — Bước 1: Gửi mã OTP đặt lại mật khẩu qua email ứng viên.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CandidateForgotPassword(CandidateAuthCompositeViewModel model)
    {
        foreach (var key in ModelState.Keys.Where(k => !k.StartsWith("ForgotPasswordInput.")).ToList())
        {
            ModelState.Remove(key);
        }
        model.ActivePanel = "reset";

        if (!ModelState.IsValid)
        {
            if (IsAjaxRequest())
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return Json(new { success = false, message = errors.FirstOrDefault() ?? "Vui lòng nhập địa chỉ email hợp lệ." });
            }
            return View("CandidateAuth", model);
        }

        var email = model.ForgotPasswordInput.Email.Trim().ToLower();
        var (isSuccess, message) = await _authService.SendForgotPasswordOtpAsync(email);

        if (IsAjaxRequest())
        {
            return Json(new { success = true, email, message });
        }

        model.OtpStep = "verify";
        model.PendingEmail = email;
        model.ResetPasswordOtpInput.Email = email;
        model.ResetPasswordOtpInput.ReturnUrl = model.ForgotPasswordInput.ReturnUrl;
        model.IsSuccess = true;
        model.StatusMessage = message;
        return View("CandidateAuth", model);
    }

    /// <summary>
    /// POST /Account/CandidateResetPasswordWithOtp — Bước 2: Xác thực OTP và lưu mật khẩu mới.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CandidateResetPasswordWithOtp(CandidateAuthCompositeViewModel model)
    {
        foreach (var key in ModelState.Keys.Where(k => !k.StartsWith("ResetPasswordOtpInput.")).ToList())
        {
            ModelState.Remove(key);
        }
        model.ActivePanel = "reset";
        model.OtpStep = "verify";

        if (!ModelState.IsValid)
        {
            if (IsAjaxRequest())
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return Json(new { success = false, message = errors.FirstOrDefault() ?? "Dữ liệu nhập vào chưa hợp lệ." });
            }
            return View("CandidateAuth", model);
        }

        var ip = GetClientIp();
        var (isSuccess, message) = await _authService.ResetPasswordWithOtpAsync(
            model.ResetPasswordOtpInput.Email,
            model.ResetPasswordOtpInput.OtpCode,
            model.ResetPasswordOtpInput.NewPassword,
            ip);

        if (!isSuccess)
        {
            if (IsAjaxRequest())
            {
                return Json(new { success = false, message });
            }
            model.IsSuccess = false;
            model.StatusMessage = message;
            model.PendingEmail = model.ResetPasswordOtpInput.Email;
            return View("CandidateAuth", model);
        }

        if (IsAjaxRequest())
        {
            return Json(new { success = true, message, email = model.ResetPasswordOtpInput.Email });
        }

        var successModel = new CandidateAuthCompositeViewModel
        {
            ActivePanel = "signin",
            IsSuccess = true,
            StatusMessage = message,
            LoginInput = new CandidateLoginInputModel
            {
                Email = model.ResetPasswordOtpInput.Email,
                ReturnUrl = model.ResetPasswordOtpInput.ReturnUrl
            }
        };

        return View("CandidateAuth", successModel);
    }

    /// <summary>
    /// POST /Account/CandidateResendOtp — Gửi lại mã OTP qua email (type: register | forgot_password).
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CandidateResendOtp(string email, string type)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Json(new { success = false, message = "Vui lòng nhập địa chỉ email." });
        }

        var (isSuccess, message) = await _authService.ResendOtpAsync(email, type);
        return Json(new { success = isSuccess, message });
    }

    private bool IsAjaxRequest()
    {
        return Request.Headers["X-Requested-With"] == "XMLHttpRequest"
            || Request.Headers.Accept.Any(a => a?.Contains("application/json") == true);
    }

    #endregion

    #region Google OAuth / External Login

    /// <summary>
    /// GET /Account/ExternalLogin — Khởi tạo quá trình xác thực với nhà cung cấp bên thứ 3 (Google).
    /// </summary>
    [HttpGet("/Account/ExternalLogin")]
    [AllowAnonymous]
    public IActionResult ExternalLogin(string provider = "Google", string? returnUrl = null, string userType = "candidate")
    {
        var isOidc = provider.Equals("NoveraOidcScheme", StringComparison.OrdinalIgnoreCase) ||
                     provider.Equals("NoveraOidc", StringComparison.OrdinalIgnoreCase) ||
                     provider.Equals("SSO", StringComparison.OrdinalIgnoreCase);

        if (isOidc)
        {
            var oidcAuthority = Environment.GetEnvironmentVariable("OIDC_AUTHORITY")?.Trim();
            var oidcClientId = Environment.GetEnvironmentVariable("OIDC_CLIENT_ID")?.Trim();
            bool isOidcConfigured = !string.IsNullOrWhiteSpace(oidcAuthority) && 
                                    !string.IsNullOrWhiteSpace(oidcClientId) &&
                                    !oidcClientId.Contains("YOUR_OIDC_CLIENT_ID", StringComparison.OrdinalIgnoreCase);

            if (!isOidcConfigured)
            {
                TempData["ErrorMessage"] = "Dịch vụ SSO NoveraTech ID chưa được thiết lập. Vui lòng liên hệ Quản trị viên hệ thống.";
                return RedirectToAction(nameof(StaffLogin), new { returnUrl });
            }

            var oidcRedirectUrl = Url.Action(nameof(ExternalCallback), "Account", new { returnUrl, userType, provider = "NoveraOidcScheme" });
            var oidcProps = new AuthenticationProperties { RedirectUri = oidcRedirectUrl };
            return Challenge(oidcProps, "NoveraOidcScheme");
        }

        var googleClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")?.Trim();
        var googleClientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET")?.Trim();
        bool isConfigured = !string.IsNullOrWhiteSpace(googleClientId) && 
                            !string.IsNullOrWhiteSpace(googleClientSecret) &&
                            !googleClientId.Contains("YOUR_GOOGLE_CLIENT_ID", StringComparison.OrdinalIgnoreCase);

        if (!isConfigured)
        {
            // Nếu chưa điền Google Client ID thực tế vào .env, chuyển tới trang hướng dẫn cấu hình chi tiết
            return RedirectToAction(nameof(GoogleConfigGuide), new { returnUrl, userType });
        }

        var redirectUrl = Url.Action(nameof(ExternalCallback), "Account", new { returnUrl, userType, provider = "Google" });
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, provider);
    }

    /// <summary>
    /// GET /Account/ExternalCallback — Nhận kết quả xác thực callback từ Google hoặc NoveraTech SSO.
    /// </summary>
    [HttpGet("/Account/ExternalCallback")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalCallback(string? returnUrl = null, string userType = "candidate", string? remoteError = null, string provider = "Google")
    {
        var isOidc = provider.Equals("NoveraOidcScheme", StringComparison.OrdinalIgnoreCase) ||
                     provider.Equals("NoveraOidc", StringComparison.OrdinalIgnoreCase) ||
                     provider.Equals("SSO", StringComparison.OrdinalIgnoreCase);

        var redirectLoginAction = userType.Equals("staff", StringComparison.OrdinalIgnoreCase) 
            ? nameof(StaffLogin) 
            : nameof(CandidateAuth);

        if (remoteError != null)
        {
            _logger.LogError("Lỗi xác thực từ nhà cung cấp liên kết ngoài: {Error}", remoteError);
            TempData["ErrorMessage"] = isOidc ? $"Lỗi từ dịch vụ SSO NoveraTech: {remoteError}" : $"Lỗi từ dịch vụ Google: {remoteError}";
            return RedirectToAction(redirectLoginAction, new { returnUrl });
        }

        var authenticateResult = await HttpContext.AuthenticateAsync("ExternalCookieScheme");
        if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
        {
            _logger.LogWarning("Không tìm thấy thông tin xác thực từ ExternalCookieScheme.");
            TempData["ErrorMessage"] = isOidc ? "Không thể lấy thông tin phiên đăng nhập từ SSO NoveraTech. Vui lòng thử lại." : "Không thể lấy thông tin tài khoản từ Google. Vui lòng thử lại.";
            return RedirectToAction(redirectLoginAction, new { returnUrl });
        }

        var claims = authenticateResult.Principal.Claims.ToList();
        var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value
                 ?? claims.FirstOrDefault(c => c.Type == "email")?.Value;
        var fullName = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value 
                    ?? claims.FirstOrDefault(c => c.Type == "name")?.Value 
                    ?? claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value
                    ?? email?.Split('@')[0] 
                    ?? (isOidc ? "Novera Staff" : "Google User");
        var providerKey = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value 
                       ?? claims.FirstOrDefault(c => c.Type == "sub")?.Value 
                       ?? Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(email))
        {
            TempData["ErrorMessage"] = isOidc ? "Không nhận được thông tin email từ hệ thống SSO NoveraTech." : "Không nhận được địa chỉ email từ tài khoản Google của bạn.";
            return RedirectToAction(redirectLoginAction, new { returnUrl });
        }

        // Kiểm tra an ninh bắt buộc cho Cổng Nhân sự Nội bộ
        if (userType.Equals("staff", StringComparison.OrdinalIgnoreCase) || isOidc)
        {
            var internalDomain = Environment.GetEnvironmentVariable("INTERNAL_EMAIL_DOMAIN") ?? "@noveratech.digital";
            if (!email.Trim().EndsWith(internalDomain, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Từ chối truy cập SSO nhân sự đối với email không thuộc tổ chức: {Email}", email);
                TempData["ErrorMessage"] = $"Chỉ cho phép tài khoản có đuôi {internalDomain} đăng nhập vào Cổng Nhân sự.";
                await HttpContext.SignOutAsync("ExternalCookieScheme");
                return RedirectToAction(nameof(StaffLogin), new { returnUrl });
            }
        }

        // Xử lý tạo/liên kết tài khoản trong cơ sở dữ liệu
        var providerName = isOidc ? "NoveraSSO" : "Google";
        var result = await _authService.ProcessExternalLoginAsync(email, fullName, providerName, providerKey);

        if (!result.IsSuccess || result.Data == null)
        {
            TempData["ErrorMessage"] = result.Message ?? "Đăng nhập không thành công.";
            return RedirectToAction(redirectLoginAction, new { returnUrl });
        }

        var userInfo = result.Data;

        // Thiết lập Session
        HttpContext.Session.SetString("UserId", userInfo.Id.ToString());
        HttpContext.Session.SetString("UserEmail", userInfo.Email);
        HttpContext.Session.SetString("UserRole", userInfo.Role);
        HttpContext.Session.SetString("FullName", userInfo.FullName);

        var idleTimeoutMinutes = int.TryParse(Environment.GetEnvironmentVariable("SESSION_IDLE_TIMEOUT_MINUTES"), out var parsedTimeout) ? parsedTimeout : 30;
        var maxSessionHours = int.TryParse(Environment.GetEnvironmentVariable("SESSION_MAX_LIFETIME_HOURS"), out var parsedMax) ? parsedMax : 8;
        HttpContext.Session.SetString("AbsoluteExpiration", DateTimeOffset.UtcNow.AddHours(maxSessionHours).ToString("o"));

        // Thiết lập Cookie phiên chính của hệ thống
        var identityClaims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userInfo.Id.ToString()),
            new Claim(ClaimTypes.Email, userInfo.Email),
            new Claim(ClaimTypes.Name, userInfo.FullName)
        };

        foreach (var role in userInfo.Roles)
        {
            identityClaims.Add(new Claim(ClaimTypes.Role, role));
            var norm = UserRoles.NormalizeRole(role);
            if (!string.Equals(norm, role, StringComparison.OrdinalIgnoreCase))
            {
                identityClaims.Add(new Claim(ClaimTypes.Role, norm));
            }
        }

        if (!userInfo.Roles.Contains(userInfo.Role, StringComparer.OrdinalIgnoreCase))
        {
            identityClaims.Add(new Claim(ClaimTypes.Role, userInfo.Role));
        }
        var normPrimary = UserRoles.NormalizeRole(userInfo.Role);
        if (!identityClaims.Any(c => c.Type == ClaimTypes.Role && string.Equals(c.Value, normPrimary, StringComparison.OrdinalIgnoreCase)))
        {
            identityClaims.Add(new Claim(ClaimTypes.Role, normPrimary));
        }

        var claimsIdentity = new ClaimsIdentity(identityClaims, "AtsCookieScheme");
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(idleTimeoutMinutes)
        };

        await HttpContext.SignInAsync("AtsCookieScheme", new ClaimsPrincipal(claimsIdentity), authProperties);
        await HttpContext.SignOutAsync("ExternalCookieScheme");

        _logger.LogInformation("Người dùng {Email} đăng nhập thành công qua {Provider}.", email, providerName);
        DispatchLoginSuccessAlert(userInfo.Email, userInfo.FullName);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        if (!string.IsNullOrEmpty(userInfo.RedirectUrl) && Url.IsLocalUrl(userInfo.RedirectUrl))
        {
            return Redirect(userInfo.RedirectUrl);
        }

        return RedirectToRoleHome(userInfo.Roles);
    }

    /// <summary>
    /// GET /Account/GoogleConfigGuide — Màn hình hướng dẫn chi tiết cách tạo và cấu hình Google Client ID trên Google Cloud Console,
    /// kèm tính năng Dev Quick Test để kiểm thử luồng đăng nhập ngay lập tức.
    /// </summary>
    [HttpGet("/Account/GoogleConfigGuide")]
    [AllowAnonymous]
    public IActionResult GoogleConfigGuide(string? returnUrl = null, string userType = "candidate")
    {
        var googleClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")?.Trim();
        var googleClientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET")?.Trim();
        bool isConfigured = !string.IsNullOrWhiteSpace(googleClientId) && 
                            !string.IsNullOrWhiteSpace(googleClientSecret) &&
                            !googleClientId.Contains("YOUR_GOOGLE_CLIENT_ID", StringComparison.OrdinalIgnoreCase);

        var callbackUrl = $"{Request.Scheme}://{Request.Host}/signin-google";

        var model = new GoogleConfigGuideViewModel
        {
            ReturnUrl = returnUrl,
            UserType = userType,
            IsConfigured = isConfigured,
            CallbackUrl = callbackUrl
        };

        return View(model);
    }

    /// <summary>
    /// POST /Account/DevMockGoogleLogin — Giả lập đăng nhập Google nhanh chóng trong môi trường phát triển (Development / Demo).
    /// </summary>
    [HttpPost("/Account/DevMockGoogleLogin")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DevMockGoogleLogin(string email, string fullName, string? returnUrl = null, string userType = "candidate")
    {
        // 1. Chỉ cho phép chạy trên môi trường Development
        if (_webHostEnvironment == null || !_webHostEnvironment.IsDevelopment())
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            email = "demo.candidate@gmail.com";
        }

        // 2. Kiểm tra định dạng @gmail.com
        if (!email.Trim().EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "Email giả lập phải có định dạng @gmail.com.";
            return RedirectToAction(nameof(GoogleConfigGuide), new { returnUrl, userType });
        }

        // 3. Nghiêm cấm mạo danh tài khoản nội bộ (Admin, HR, BOD...) qua mock endpoint
        if (_dbContext != null)
        {
            var cleanEmail = email.Trim().ToLower();
            var existingUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);
            if (existingUser != null && existingUser.Role != UserRoles.Candidate && existingUser.Role != "Candidate" && existingUser.Role != "Ứng viên nội bộ")
            {
                _logger.LogWarning("Phát hiện cố gắng mạo danh tài khoản nội bộ {Email} qua DevMockGoogleLogin.", cleanEmail);
                TempData["ErrorMessage"] = "Không thể sử dụng đăng nhập thử nghiệm cho tài khoản cán bộ nhân sự nội bộ. Vui lòng đăng nhập qua Cổng Nhân sự.";
                return RedirectToAction(nameof(GoogleConfigGuide), new { returnUrl, userType });
            }
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            fullName = "Nguyễn Văn Demo (Google)";
        }

        var providerKey = "mock-google-" + Guid.NewGuid().ToString("N");
        var result = await _authService.ProcessExternalLoginAsync(email, fullName, "GoogleMock", providerKey);

        if (!result.IsSuccess || result.Data == null)
        {
            TempData["ErrorMessage"] = result.Message ?? "Đăng nhập giả lập không thành công.";
            return RedirectToAction(nameof(GoogleConfigGuide), new { returnUrl, userType });
        }

        var userInfo = result.Data;

        // Session setup
        HttpContext.Session.SetString("UserId", userInfo.Id.ToString());
        HttpContext.Session.SetString("UserEmail", userInfo.Email);
        HttpContext.Session.SetString("UserRole", userInfo.Role);
        HttpContext.Session.SetString("FullName", userInfo.FullName);

        var idleTimeoutMinutes = int.TryParse(Environment.GetEnvironmentVariable("SESSION_IDLE_TIMEOUT_MINUTES"), out var parsedTimeout) ? parsedTimeout : 30;
        var maxSessionHours = int.TryParse(Environment.GetEnvironmentVariable("SESSION_MAX_LIFETIME_HOURS"), out var parsedMax) ? parsedMax : 8;
        HttpContext.Session.SetString("AbsoluteExpiration", DateTimeOffset.UtcNow.AddHours(maxSessionHours).ToString("o"));

        var identityClaims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userInfo.Id.ToString()),
            new Claim(ClaimTypes.Email, userInfo.Email),
            new Claim(ClaimTypes.Name, userInfo.FullName)
        };

        foreach (var role in userInfo.Roles)
        {
            identityClaims.Add(new Claim(ClaimTypes.Role, role));
            var norm = UserRoles.NormalizeRole(role);
            if (!string.Equals(norm, role, StringComparison.OrdinalIgnoreCase))
            {
                identityClaims.Add(new Claim(ClaimTypes.Role, norm));
            }
        }

        if (!userInfo.Roles.Contains(userInfo.Role, StringComparer.OrdinalIgnoreCase))
        {
            identityClaims.Add(new Claim(ClaimTypes.Role, userInfo.Role));
        }
        var normPrimary = UserRoles.NormalizeRole(userInfo.Role);
        if (!identityClaims.Any(c => c.Type == ClaimTypes.Role && string.Equals(c.Value, normPrimary, StringComparison.OrdinalIgnoreCase)))
        {
            identityClaims.Add(new Claim(ClaimTypes.Role, normPrimary));
        }

        var claimsIdentity = new ClaimsIdentity(identityClaims, "AtsCookieScheme");
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(idleTimeoutMinutes)
        };

        await HttpContext.SignInAsync("AtsCookieScheme", new ClaimsPrincipal(claimsIdentity), authProperties);

        _logger.LogInformation("Đăng nhập thử nghiệm Google Mock thành công cho tài khoản {Email}.", email);
        TempData["SuccessMessage"] = $"Đăng nhập thành công với tài khoản Google ({email})!";

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        if (!string.IsNullOrEmpty(userInfo.RedirectUrl) && Url.IsLocalUrl(userInfo.RedirectUrl))
        {
            return Redirect(userInfo.RedirectUrl);
        }

        return RedirectToRoleHome(userInfo.Roles);
    }

    #endregion

    #region Helpers

    private IActionResult RedirectToRoleHome(List<string> roles)
    {
        if (roles.Contains(UserRoles.Admin, StringComparer.OrdinalIgnoreCase))
        {
            return RedirectToAction("Index", "Users");
        }

        return RedirectToAction("Index", "Home");
    }

    private IActionResult RedirectToDefaultRoute()
    {
        if (User.IsInRole(UserRoles.Admin))
        {
            return RedirectToAction("Index", "Users");
        }

        return RedirectToAction("Index", "Home");
    }

    #endregion
}
