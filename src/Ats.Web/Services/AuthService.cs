using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

public class AuthService(ApplicationDbContext dbContext, IEmailService emailService) : IAuthService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IEmailService _emailService = emailService;

    public async Task<AuthResponseDto> AuthenticateAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        const string generalErrorMessage = "Email hoặc mật khẩu không chính xác.";
        string dummyHash = "$2a$11$9yC3Q2K5HjC9M3K4H6B8X.K7K8X9Y0Z1A2B3C4D5E6F7G8H9I0J1K";

        var user = await _dbContext.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.Trim().ToLower(), cancellationToken);

        bool isPasswordValid = false;

        if (user != null)
        {
            // Kiểm tra trạng thái tài khoản bị khóa vĩnh viễn / khóa bởi Admin (S1-10)
            if (user.Status == "LOCKED")
            {
                return new AuthResponseDto(false, "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.", null);
            }

            // Kiểm tra khóa tạm 15 phút do nhập sai 5 lần (S1-01)
            if (user.LockedUntil.HasValue)
            {
                if (user.LockedUntil.Value > DateTimeOffset.UtcNow)
                {
                    var remainingMinutes = Math.Ceiling((user.LockedUntil.Value - DateTimeOffset.UtcNow).TotalMinutes);
                    return new AuthResponseDto(false, $"Tài khoản tạm thời bị khóa do nhập sai nhiều lần. Vui lòng thử lại sau {remainingMinutes} phút.", null);
                }
                else
                {
                    // Đã hết thời hạn khóa tạm -> reset bộ đếm
                    user.LockedUntil = null;
                    user.FailedLoginAttempts = 0;
                }
            }

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
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
                }
            }
        }
        else
        {
            // Chống Timing Attack: verify giả lập khi email không tồn tại
            BCrypt.Net.BCrypt.Verify(request.Password, dummyHash);
        }

        if (user == null || !isPasswordValid || user.Status != "ACTIVE")
        {
            if (user != null && user.Status == "ACTIVE")
            {
                user.FailedLoginAttempts += 1;
                if (user.FailedLoginAttempts >= 5)
                {
                    user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(15);
                }
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return new AuthResponseDto(false, generalErrorMessage, null);
        }

        // Đăng nhập thành công -> Reset số lần đăng nhập sai
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.LastActivityAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Thu thập danh sách vai trò
        var roles = user.UserRoles
            .Select(ur => ur.Role.Name)
            .Distinct()
            .ToList();

        if (roles.Count == 0 && !string.IsNullOrWhiteSpace(user.Role))
        {
            roles.Add(user.Role);
        }

        if (roles.Count == 0)
        {
            roles.Add(UserRoles.Candidate);
        }

        // Vai trò chính ưu tiên quyền cao nhất
        var primaryRole = roles.FirstOrDefault(r => UserRoles.NormalizeRole(r) == UserRoles.Admin)
            ?? roles.FirstOrDefault(r => UserRoles.NormalizeRole(r) == UserRoles.HRManager)
            ?? roles.FirstOrDefault(r => UserRoles.NormalizeRole(r) == UserRoles.HiringManager)
            ?? roles.FirstOrDefault(r => UserRoles.NormalizeRole(r) == UserRoles.Recruiter)
            ?? roles.FirstOrDefault(r => UserRoles.NormalizeRole(r) == UserRoles.Approver)
            ?? roles.FirstOrDefault(r => UserRoles.NormalizeRole(r) == UserRoles.Interviewer)
            ?? roles.First();

        var normalizedPrimaryRole = UserRoles.NormalizeRole(primaryRole);

        string redirectUrl = normalizedPrimaryRole switch
        {
            UserRoles.Admin => "/Users",
            UserRoles.Recruiter => "/recruiter/pipeline",
            UserRoles.HiringManager => "/manager/yeu-cau-tuyen-dung",
            UserRoles.Interviewer => "/interviewer/lich-phong-van",
            UserRoles.HRManager => "/hrm/dashboard",
            UserRoles.Approver => "/approver/danh-sach-duyet",
            _ => "/candidate/ho-so-cua-toi"
        };

        // Gửi email thông báo đăng nhập qua email công ty
        _ = _emailService.SendEmailAsync(new SendEmailRequestDto(
            user.Email,
            "Thông báo đăng nhập hệ thống NoveraTech ATS",
            $"<p>Xin chào <b>{user.FullName}</b>,</p><p>Tài khoản của bạn vừa đăng nhập thành công vào hệ thống ATS lúc {DateTime.Now:HH:mm dd/MM/yyyy}.</p>"
        ), cancellationToken);

        var userInfo = new UserInfoDto(user.Id, user.Email, user.FullName, primaryRole, roles, redirectUrl);
        return new AuthResponseDto(true, "Đăng nhập thành công.", userInfo);
    }

    public async Task<(bool IsSuccess, string Message)> ForgotPasswordAsync(string email, string baseUrl, CancellationToken cancellationToken = default)
    {
        const string standardMessage = "Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi đến hòm thư của bạn.";

        if (string.IsNullOrWhiteSpace(email))
        {
            return (false, "Vui lòng nhập địa chỉ email.");
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.Trim().ToLower(), cancellationToken);
        if (user == null || user.Status != "ACTIVE")
        {
            // AC S1-03: Email không tồn tại vẫn hiển thị cùng 1 thông báo
            return (true, standardMessage);
        }

        // Tạo token đặt lại mật khẩu duy nhất, hiệu lực 30 phút
        var token = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        user.PasswordResetToken = token;
        user.PasswordResetTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Gửi email kèm liên kết
        var resetLink = $"{baseUrl.TrimEnd('/')}/Account/ResetPassword?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}";
        var emailBody = $@"
            <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                <h2 style='color: #2e4a8f;'>Yêu cầu đặt lại mật khẩu</h2>
                <p>Xin chào <b>{user.FullName}</b>,</p>
                <p>Chúng tôi đã nhận được yêu cầu đặt lại mật khẩu cho tài khoản NoveraTech ATS của bạn.</p>
                <p>Vui lòng bấm vào liên kết bên dưới để đặt lại mật khẩu (liên kết có hiệu lực trong <b>30 phút</b> và chỉ sử dụng được 1 lần):</p>
                <p><a href='{resetLink}' style='display: inline-block; background-color: #2e4a8f; color: #fff; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>Đặt lại mật khẩu</a></p>
                <p>Nếu bạn không gửi yêu cầu này, vui lòng bỏ qua email hoặc liên hệ với quản trị viên.</p>
                <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;' />
                <p style='font-size: 12px; color: #777;'>Hệ thống Tuyển dụng Nội bộ NoveraTech ATS</p>
            </div>";

        await _emailService.SendEmailAsync(new SendEmailRequestDto(
            user.Email,
            "Khôi phục mật khẩu tài khoản NoveraTech ATS",
            emailBody
        ), cancellationToken);

        return (true, standardMessage);
    }

    public async Task<(bool IsSuccess, string Message)> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.Trim().ToLower(), cancellationToken);
        if (user == null || user.PasswordResetToken != request.Token)
        {
            return (false, "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã được sử dụng.");
        }

        if (!user.PasswordResetTokenExpiresAt.HasValue || user.PasswordResetTokenExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            return (false, "Liên kết đặt lại mật khẩu đã hết hạn (chỉ có hiệu lực trong 30 phút).");
        }

        // Cập nhật mật khẩu mới và xóa token (chỉ dùng được 1 lần)
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiresAt = null;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        // Thu hồi toàn bộ phiên đăng nhập cũ để bảo mật
        var oldSessions = await _dbContext.UserSessions
            .Where(s => s.UserId == user.Id && !s.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var session in oldSessions)
        {
            session.IsRevoked = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (true, "Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.");
    }

    public async Task<(bool IsSuccess, string Message)> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, string currentSessionId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FindAsync([userId], cancellationToken);
        if (user == null)
        {
            return (false, "Không tìm thấy thông tin tài khoản.");
        }

        bool isCurrentValid = false;
        try
        {
            isCurrentValid = BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash);
        }
        catch
        {
            isCurrentValid = (user.PasswordHash == currentPassword);
        }

        if (!isCurrentValid)
        {
            return (false, "Mật khẩu hiện tại không chính xác.");
        }

        // Băm mật khẩu mới
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        // AC S1-04: Thu hồi các phiên đăng nhập khác của tài khoản này
        var otherSessions = await _dbContext.UserSessions
            .Where(s => s.UserId == userId && s.SessionId != currentSessionId && !s.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var s in otherSessions)
        {
            s.IsRevoked = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (true, "Đổi mật khẩu thành công. Các phiên đăng nhập trên thiết bị khác đã được thu hồi.");
    }

    public async Task LogoutAsync(Guid userId, string sessionId, CancellationToken cancellationToken = default)
    {
        // AC S1-02: Đăng xuất làm mất hiệu lực phiên ngay lập tức phía server
        var activeSessions = await _dbContext.UserSessions
            .Where(s => (s.SessionId == sessionId || s.UserId == userId) && !s.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var session in activeSessions)
        {
            session.IsRevoked = true;
        }

        var auditLog = new AuthAuditLog
        {
            UserId = userId,
            SessionId = sessionId,
            IsSuccess = true,
            EventType = "SessionLogout",
            Reason = "Người dùng chủ động đăng xuất",
            Timestamp = DateTimeOffset.UtcNow
        };
        _dbContext.AuthAuditLogs.Add(auditLog);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<(bool IsSuccess, string Message)> RegisterCandidateAsync(string fullName, string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return (false, "Vui lòng nhập địa chỉ email.");
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (false, "Vui lòng nhập họ và tên của bạn.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            return (false, "Mật khẩu phải có độ dài tối thiểu 6 ký tự.");
        }

        var normalizedEmail = email.Trim().ToLower();
        var existingUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);
        if (existingUser != null)
        {
            return (false, "Địa chỉ email này đã được sử dụng. Vui lòng đăng nhập hoặc dùng chức năng Quên mật khẩu.");
        }

        // Đảm bảo Role Candidate tồn tại
        var candidateRole = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Name == UserRoles.Candidate, cancellationToken);
        if (candidateRole == null)
        {
            candidateRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = UserRoles.Candidate,
                Description = UserRoles.GetDisplayName(UserRoles.Candidate),
                IsSystem = true
            };
            _dbContext.Roles.Add(candidateRole);
        }

        var newUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = fullName.Trim(),
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = UserRoles.Candidate,
            RoleId = candidateRole.Id,
            Status = "ACTIVE",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        newUser.UserRoles.Add(new UserRole
        {
            UserId = newUser.Id,
            RoleId = candidateRole.Id
        });

        _dbContext.Users.Add(newUser);

        // Ghi nhận Audit Log
        _dbContext.AuthAuditLogs.Add(new AuthAuditLog
        {
            Id = Guid.NewGuid(),
            UserId = newUser.Id,
            Email = newUser.Email,
            EventType = "CandidateRegister",
            IsSuccess = true,
            Reason = "Ứng viên tự đăng ký tài khoản thành công",
            Timestamp = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Gửi email chào mừng ứng viên
        var emailBody = $@"
            <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                <h2 style='color: #10b981;'>Chào mừng bạn đến với Cổng Tuyển Dụng NoveraTech!</h2>
                <p>Xin chào <b>{newUser.FullName}</b>,</p>
                <p>Tài khoản ứng viên của bạn đã được khởi tạo thành công trên hệ thống tuyển dụng NoveraTech ATS.</p>
                <p>Bạn có thể đăng nhập ngay để theo dõi các cơ hội nghề nghiệp và nộp hồ sơ ứng tuyển trực tuyến.</p>
                <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;' />
                <p style='font-size: 12px; color: #777;'>Ban Tuyển Dụng NoveraTech</p>
            </div>";

        _ = _emailService.SendEmailAsync(new SendEmailRequestDto(
            newUser.Email,
            "Chào mừng bạn đến với Cổng Tuyển Dụng NoveraTech",
            emailBody
        ), cancellationToken);

        return (true, "Đăng ký tài khoản thành công! Vui lòng đăng nhập để bắt đầu ứng tuyển.");
    }
}

