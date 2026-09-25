using Ats.Web.Models.DTOs;
using Ats.Web.Models.ViewModels.Accounts;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Route("accounts")]
[Route("account")]
public class AccountsController(IAuthService authService) : Controller
{
    private readonly IAuthService _authService = authService;

    /// <summary>
    /// Giao diện đăng nhập (GET: /accounts/login hoặc /dang-nhap)
    /// </summary>
    [HttpGet("login")]
    [HttpGet("/dang-nhap")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        return View(new AccountLoginViewModel
        {
            ReturnUrl = returnUrl
        });
    }

    /// <summary>
    /// Xử lý đăng nhập từ Form MVC (POST: /accounts/login hoặc /dang-nhap)
    /// </summary>
    [HttpPost("login")]
    [HttpPost("/dang-nhap")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(AccountLoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var requestDto = new LoginRequestDto(model.Email, model.Password);
        var authResult = await _authService.AuthenticateAsync(requestDto, cancellationToken);

        if (!authResult.IsSuccess)
        {
            // Hiển thị thông báo chung ở asp-validation-summary theo AC S1-01
            ModelState.AddModelError(string.Empty, authResult.Message ?? "Email hoặc mật khẩu không chính xác.");
            return View(model);
        }

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }
}
