using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

public class AuthService(ApplicationDbContext dbContext) : IAuthService
{
    private readonly ApplicationDbContext _dbContext = dbContext;

    public async Task<AuthResponseDto> AuthenticateAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        const string generalErrorMessage = "Email hoặc mật khẩu không chính xác.";

        if (user == null)
            return new AuthResponseDto(false, generalErrorMessage, null);

        if (user.Status != "ACTIVE")
            return new AuthResponseDto(false, "Tài khoản chưa được kích hoạt hoặc đã bị khóa.", null);

        // So sánh mật khẩu (Thực tế nâng cấp dùng BCrypt/Argon2)
        if (user.PasswordHash != request.Password)
            return new AuthResponseDto(false, generalErrorMessage, null);

        // Cập nhật thông tin đăng nhập thành công
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var userInfo = new UserInfoDto(user.Id, user.Email, user.FullName);
        return new AuthResponseDto(true, "Đăng nhập thành công.", userInfo);
    }
}
