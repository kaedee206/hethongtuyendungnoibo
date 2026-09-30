using System.Linq;
using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.ViewModels.Account;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller xử lý các chức năng xác thực người dùng (S1-01, S1-02, S1-03, S1-04).
/// Tuân thủ Clean Controller, Primary Constructor, ValidateAntiForgeryToken.
/// </summary>
public class AccountController(
    IAuthService authService,
    ILogger<AccountController> logger) : Controller
{
    private readonly IAuthService _authService = authService;
    private readonly ILogger<AccountController> _logger = logger;

    #region S1-01: Đăng nhập

    [HttpGet]
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

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(AccountLoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var loginDto = new LoginRequestDto(model.Email, model.Password);

        var result = await _authService.AuthenticateAsync(loginDto);

        if (!result.IsSuccess || result.Data == null)
        {
            // S1-01 AC: Thông báo lỗi chung, không tiết lộ email tồn tại hay không
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
        }

        // Đảm bảo Primary Role cũng có mặt trong claim role
        if (!userInfo.Roles.Contains(userInfo.Role, StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, userInfo.Role));
        }

        var claimsIdentity = new ClaimsIdentity(claims, "AtsCookieScheme");
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(idleTimeoutMinutes)
        };

        await HttpContext.SignInAsync("AtsCookieScheme", new ClaimsPrincipal(claimsIdentity), authProperties);

        _logger.LogInformation("Người dùng {Email} đăng nhập thành công từ IP {Ip}.", model.Email, ip);

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
        }

        if (!userInfo.Roles.Contains(userInfo.Role, StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, userInfo.Role));
        }

        var claimsIdentity = new ClaimsIdentity(claims, "AtsCookieScheme");
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.LoginInput.RememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(idleTimeoutMinutes)
        };

        await HttpContext.SignInAsync("AtsCookieScheme", new ClaimsPrincipal(claimsIdentity), authProperties);

        _logger.LogInformation("Nhân sự {Email} đăng nhập qua StaffPortal từ IP {Ip}.", model.LoginInput.Email, ip);

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
        foreach (var key in ModelState.Keys.Where(k => k.StartsWith("RegisterInput.") || k.StartsWith("ForgotPasswordInput.")).ToList())
        {
            ModelState.Remove(key);
        }
        model.ActivePanel = "signin";

        if (!ModelState.IsValid)
        {
            return View("CandidateAuth", model);
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var loginDto = new LoginRequestDto(model.LoginInput.Email, model.LoginInput.Password);
        var result = await _authService.AuthenticateAsync(loginDto);

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
        }

        if (!userInfo.Roles.Contains(userInfo.Role, StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, userInfo.Role));
        }

        var claimsIdentity = new ClaimsIdentity(claims, "AtsCookieScheme");
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.LoginInput.RememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(idleTimeoutMinutes)
        };

        await HttpContext.SignInAsync("AtsCookieScheme", new ClaimsPrincipal(claimsIdentity), authProperties);

        _logger.LogInformation("Ứng viên {Email} đăng nhập từ IP {Ip}.", model.LoginInput.Email, ip);

        if (!string.IsNullOrEmpty(model.LoginInput.ReturnUrl) && Url.IsLocalUrl(model.LoginInput.ReturnUrl))
        {
            return Redirect(model.LoginInput.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    /// <summary>
    /// POST /Account/CandidateRegister — Đăng ký tài khoản ứng viên.
    /// TODO: Implement khi IAuthService hỗ trợ self-registration. Hiện tại phản hồi placeholder.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CandidateRegister(CandidateAuthCompositeViewModel model)
    {
        // Chỉ validate RegisterInput
        foreach (var key in ModelState.Keys.Where(k => k.StartsWith("LoginInput.") || k.StartsWith("ForgotPasswordInput.")).ToList())
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
            return View("CandidateAuth", model);
        }


        var (isSuccess, message) = await _authService.RegisterCandidateAsync(
            model.RegisterInput.FullName,
            model.RegisterInput.Email,
            model.RegisterInput.Password);

        if (!isSuccess)
        {
            model.IsSuccess = false;
            model.StatusMessage = message;
            return View("CandidateAuth", model);
        }

        _logger.LogInformation("Ứng viên đăng ký thành công: {Email} - {FullName}", model.RegisterInput.Email, model.RegisterInput.FullName);

        // Chuyển sang form signin với email điền sẵn và thông báo thành công
        var successModel = new CandidateAuthCompositeViewModel
        {
            ActivePanel = "signin",
            IsSuccess = true,
            StatusMessage = message,
            LoginInput = new CandidateLoginInputModel
            {
                Email = model.RegisterInput.Email,
                ReturnUrl = model.RegisterInput.ReturnUrl
            }
        };

        return View("CandidateAuth", successModel);
    }


    /// <summary>
    /// POST /Account/CandidateForgotPassword — Gửi link reset password cho ứng viên.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CandidateForgotPassword(CandidateAuthCompositeViewModel model)
    {
        foreach (var key in ModelState.Keys.Where(k => k.StartsWith("LoginInput.") || k.StartsWith("RegisterInput.")).ToList())
        {
            ModelState.Remove(key);
        }
        model.ActivePanel = "reset";

        if (!ModelState.IsValid)
        {
            return View("CandidateAuth", model);
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        await _authService.ForgotPasswordAsync(model.ForgotPasswordInput.Email.Trim().ToLower(), baseUrl);

        // Anti Email Enumeration
        model.IsSuccess = true;
        model.StatusMessage = "Nếu email tồn tại trong hệ thống, liên kết đặt lại mật khẩu đã được gửi đến hộp thư của bạn.";
        return View("CandidateAuth", model);
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
