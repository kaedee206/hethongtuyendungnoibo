using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

public class AuthService(ApplicationDbContext dbContext, IEmailService emailService) : IAuthService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IEmailService _emailService = emailService;

    public async Task<AuthResponseDto> AuthenticateAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        const string generalErrorMessage = "Email hoặc mật khẩu không chính xác.";

        if (user == null)
            return new AuthResponseDto(false, generalErrorMessage, null);

        // 1. Kiểm tra tài khoản có đang trong thời gian bị khóa tạm thời 15 phút hay không
        if (user.LockedUntil.HasValue)
        {
            if (user.LockedUntil.Value > DateTimeOffset.UtcNow)
            {
                var remainingMinutes = (int)Math.Ceiling((user.LockedUntil.Value - DateTimeOffset.UtcNow).TotalMinutes);
                return new AuthResponseDto(false, $"Tài khoản tạm thời bị khóa do nhập sai mật khẩu quá 5 lần. Vui lòng thử lại sau {remainingMinutes} phút.", null);
            }

            // Nếu đã vượt quá 15 phút khóa -> Reset về trạng thái bình thường
            user.LockedUntil = null;
            user.FailedLoginAttempts = 0;
        }

        // 2. Kiểm tra trạng thái kích hoạt tài khoản
        if (user.Status != "ACTIVE")
            return new AuthResponseDto(false, "Tài khoản chưa được kích hoạt hoặc đã bị khóa.", null);

        // 3. Kiểm tra mật khẩu
        if (user.PasswordHash != request.Password)
        {
            user.FailedLoginAttempts += 1;

            if (user.FailedLoginAttempts >= 5)
            {
                // Khóa tài khoản tạm thời 15 phút tính từ hiện tại
                user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(15);
                await _dbContext.SaveChangesAsync(cancellationToken);

                return new AuthResponseDto(false, "Mật khẩu không chính xác. Bạn đã nhập sai 5 lần liên tiếp, tài khoản bị tạm khóa 15 phút.", null);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            var remainingAttempts = 5 - user.FailedLoginAttempts;
            return new AuthResponseDto(false, $"Mật khẩu không chính xác. Bạn còn {remainingAttempts} lần thử.", null);
        }

        // 4. Đăng nhập thành công -> Reset lại số lần sai và thời gian khóa
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 2. Gửi email thông báo đăng nhập qua SMTP Google
        _ = _emailService.SendEmailAsync(new SendEmailRequestDto(
            user.Email,
            "Thông báo đăng nhập hệ thống ATS",
            $"<p>Xin chào <b>{user.FullName}</b>,</p><p>Tài khoản của bạn vừa đăng nhập thành công vào hệ thống ATS lúc {DateTime.Now:HH:mm dd/MM/yyyy}.</p>"

        ), cancellationToken);
        var userInfo = new UserInfoDto(user.Id, user.Email, user.FullName);
        return new AuthResponseDto(true, "Đăng nhập thành công.", userInfo);
    }
}
