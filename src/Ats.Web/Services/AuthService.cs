using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Ats.Web.Constants;

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

        if (user.Status != "ACTIVE")
            return new AuthResponseDto(false, "Tài khoản chưa được kích hoạt hoặc đã bị khóa.", null);

        // So sánh mật khẩu (Thực tế nâng cấp dùng BCrypt/Argon2)
        if (user.PasswordHash != request.Password)
            return new AuthResponseDto(false, generalErrorMessage, null);

        // Cập nhật thông tin đăng nhập thành công
        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.LastActivityAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 2. Gửi email thông báo đăng nhập qua SMTP Google
        _ = _emailService.SendEmailAsync(new SendEmailRequestDto(
            user.Email,
            "Thông báo đăng nhập hệ thống ATS",
            $"<p>Xin chào <b>{user.FullName}</b>,</p><p>Tài khoản của bạn vừa đăng nhập thành công vào hệ thống ATS lúc {DateTime.Now:HH:mm dd/MM/yyyy}.</p>"

        ), cancellationToken);

        // LOGIC SCRUM-48: Ánh xạ 7 vai trò sang đường dẫn tương ứng
        string redirectUrl = user.Role switch
        {
            UserRoles.Candidate => "/candidate/ho-so-cua-toi",
            UserRoles.Recruiter => "/recruiter/pipeline",
            UserRoles.HiringManager => "/manager/yeu-cau-tuyen-dung",
            UserRoles.Interviewer => "/interviewer/lich-phong-van",
            UserRoles.HRManager => "/hrm/dashboard",
            UserRoles.Approver => "/approver/danh-sach-duyet",
            UserRoles.Admin => "/admin/dashboard",
            _ => "/candidate/ho-so-cua-toi"
        };
        // Đóng gói DTO
        var userInfo = new UserInfoDto(user.Id, user.Email, user.FullName, user.Role, redirectUrl);
        return new AuthResponseDto(true, "Đăng nhập thành công.", userInfo);
    }
}
