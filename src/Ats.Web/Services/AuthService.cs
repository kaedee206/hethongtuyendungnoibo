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
        
        // Dummy hash để chống Timing Attack khi user == null
        // Giá trị hash mẫu của Bcrypt
        string dummyHash = "$2a$11$9yC3Q2K5HjC9M3K4H6B8X.K7K8X9Y0Z1A2B3C4D5E6F7G8H9I0J1K";
        
        bool isPasswordValid = false;

        if (user != null)
        {
            try
            {
                isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                // Fallback nếu database còn lưu mật khẩu dạng plain-text cũ
                isPasswordValid = (user.PasswordHash == request.Password);
                if (isPasswordValid)
                {
                    // Tự động nâng cấp hash mật khẩu lên BCrypt
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
                }
            }
        }
        else
        {
            // Chạy hàm verify giả lập để có thời gian phản hồi tương đương (chống timing attack)
            BCrypt.Net.BCrypt.Verify(request.Password, dummyHash);
        }

        if (user == null || !isPasswordValid || user.Status != "ACTIVE" || (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTimeOffset.UtcNow))
        {
            if (user != null && user.Status == "ACTIVE" && (!user.LockedUntil.HasValue || user.LockedUntil.Value <= DateTimeOffset.UtcNow))
            {
                // Chỉ đếm số lần sai nếu tài khoản đang active và không bị khóa
                if (!isPasswordValid)
                {
                    user.FailedLoginAttempts += 1;
                    if (user.FailedLoginAttempts >= 5)
                    {
                        user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(15);
                    }
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            
            // AC3: Dù là lỗi sai pass, bị khóa, không tồn tại hay inactive -> Trả về 1 message chung
            return new AuthResponseDto(false, generalErrorMessage, null);
        }

        // 4. Đăng nhập thành công -> Reset lại số lần sai và thời gian khóa
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
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
