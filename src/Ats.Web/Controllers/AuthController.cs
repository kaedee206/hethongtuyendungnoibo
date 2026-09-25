using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
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

        return Ok(result);
    }
}