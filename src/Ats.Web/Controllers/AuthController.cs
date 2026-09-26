using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[ApiController]
[Route("api/xac-thuc")] //[cite: 2]
public class AuthController(IAuthService authService) : ControllerBase
{
    private readonly IAuthService _authService = authService;

    /// <summary>
    /// API Đăng nhập tài khoản nội bộ (POST: /api/xac-thuc/dang-nhap)
    /// </summary>
    [HttpPost("dang-nhap")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _authService.AuthenticateAsync(request, cancellationToken);

        if (!result.IsSuccess)
            return Unauthorized(new { message = result.Message });

        // Lưu thông tin phiên vào Session cho các API khác dùng
        if (result.Data != null)
        {
            HttpContext.Session.SetString("UserId", result.Data.Id.ToString());
            HttpContext.Session.SetString("UserRole", result.Data.Role);
        }

        return Ok(result);
    }

    [HttpPost("dang-xuat")]
    public async Task<IActionResult> Logout()
    {
        // 1. Xóa sạch toàn bộ dữ liệu lưu trong Session phía Server
        HttpContext.Session.Clear();

        // 2. Đăng xuất khỏi Authentication Scheme (nếu dùng Cookie Auth)
        await HttpContext.SignOutAsync("AtsCookieScheme");

        // 3. Xóa Cookie phiên làm việc ở Client bằng cách đặt thời gian hết hạn về quá khứ
        Response.Cookies.Delete("Ats.Session");

        return Ok(new { isSuccess = true, message = "Đăng xuất thành công. Phiên làm việc đã bị hủy hoàn toàn trên máy chủ." });
    }
}