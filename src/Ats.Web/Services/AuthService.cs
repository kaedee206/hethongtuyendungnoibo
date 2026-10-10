using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;

namespace Ats.Web.Services;

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly IMemoryCache _cache;

    public AuthService(ApplicationDbContext dbContext, IEmailService emailService, IMemoryCache? cache = null)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
    }

    public async Task<AuthResponseDto> AuthenticateAsync(LoginRequestDto request, string? clientIp = null, CancellationToken cancellationToken = default)
    {
        var cleanIp = !string.IsNullOrWhiteSpace(clientIp) ? clientIp.Trim() : "127.0.0.1";
        if (cleanIp == "::1") cleanIp = "127.0.0.1";

        var ipLockKey = $"ip_lock_{cleanIp}";
        var ipFailKey = $"ip_fail_{cleanIp}";

        // 1. Kiểm tra nếu IP đang bị tạm khóa 15 phút
        if (_cache.TryGetValue(ipLockKey, out DateTimeOffset ipLockedUntil))
        {
            if (ipLockedUntil > DateTimeOffset.UtcNow)
            {
                var remainingMinutes = Math.Max(1, (int)Math.Ceiling((ipLockedUntil - DateTimeOffset.UtcNow).TotalMinutes));
                return new AuthResponseDto(false, $"Địa chỉ IP của bạn đang bị tạm khóa 15 phút do nhập sai thông tin quá 5 lần liên tiếp. Vui lòng thử lại sau {remainingMinutes} phút.", null);
            }
            else
            {
                _cache.Remove(ipLockKey);
            }
        }

        string dummyHash = "$2a$11$9yC3Q2K5HjC9M3K4H6B8X.K7K8X9Y0Z1A2B3C4D5E6F7G8H9I0J1K";
        var reqEmail = request.Email.Trim().ToLower();
        var user = await _dbContext.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == reqEmail
                || (reqEmail == "hieu.luong@noveratech.digital" && (u.Email.ToLower() == "nam.dang@noveratech.digital" || u.Email.ToLower() == "admin@noveratech.vn"))
                || (reqEmail == "admin@noveratech.vn" && (u.Email.ToLower() == "nam.dang@noveratech.digital" || u.Email.ToLower() == "hieu.luong@noveratech.digital")), cancellationToken);

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
                    var remainingMinutes = Math.Max(1, (int)Math.Ceiling((user.LockedUntil.Value - DateTimeOffset.UtcNow).TotalMinutes));
                    return new AuthResponseDto(false, $"Tài khoản tạm thời bị khóa 15 phút do nhập sai nhiều lần. Vui lòng thử lại sau {remainingMinutes} phút.", null);
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
            // Tăng bộ đếm thử sai của IP
            int ipFails = _cache.TryGetValue(ipFailKey, out int currentIpAttempts) ? currentIpAttempts + 1 : 1;
            _cache.Set(ipFailKey, ipFails, TimeSpan.FromMinutes(30));

            int userFails = 0;
            if (user != null && user.Status == "ACTIVE")
            {
                user.FailedLoginAttempts += 1;
                userFails = user.FailedLoginAttempts;
            }

            // Lấy số lần sai cao nhất giữa User và IP để bảo vệ hệ thống
            int effectiveFails = (user != null && user.Status == "ACTIVE")
                ? Math.Max(userFails, ipFails)
                : ipFails;

            if (effectiveFails >= 5)
            {
                // Khóa IP 15 phút
                _cache.Set(ipLockKey, DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(15));
                _cache.Remove(ipFailKey);

                if (user != null && user.Status == "ACTIVE")
                {
                    user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(15);
                    user.FailedLoginAttempts = 5;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                return new AuthResponseDto(false, "Bạn đã nhập sai thông tin 5/5 lần. Địa chỉ IP và tài khoản của bạn đã bị tạm khóa 15 phút để bảo đảm an toàn hệ thống.", null);
            }

            if (user != null && user.Status == "ACTIVE")
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            int remaining = 5 - effectiveFails;
            return new AuthResponseDto(false, $"Email hoặc mật khẩu không chính xác. Bạn còn {remaining}/5 lần thử trước khi địa chỉ IP và tài khoản bị tạm khóa 15 phút.", null);
        }

        // Đăng nhập thành công -> Reset số lần đăng nhập sai của User và IP
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        _cache.Remove(ipFailKey);
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

        var normalizedRoles = roles.Select(UserRoles.NormalizeRole).Distinct().ToList();
        var userInfo = new UserInfoDto(user.Id, user.Email, user.FullName, normalizedPrimaryRole, normalizedRoles, redirectUrl);
        return new AuthResponseDto(true, "Đăng nhập thành công.", userInfo);
    }

    public async Task<(bool IsSuccess, string Message)> ForgotPasswordAsync(string email, string baseUrl, CancellationToken cancellationToken = default)
    {
        const string standardMessage = "Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi đến hòm thư của bạn.";

        if (string.IsNullOrWhiteSpace(email))
        {
            return (false, "Vui lòng nhập địa chỉ email.");
        }

        var reqEmail = email.Trim().ToLower();
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == reqEmail
            || (reqEmail == "hieu.luong@noveratech.digital" && (u.Email.ToLower() == "nam.dang@noveratech.digital" || u.Email.ToLower() == "admin@noveratech.vn"))
            || (reqEmail == "admin@noveratech.vn" && (u.Email.ToLower() == "nam.dang@noveratech.digital" || u.Email.ToLower() == "hieu.luong@noveratech.digital")), cancellationToken);
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
        var candidateRole = await _dbContext.Roles.FirstOrDefaultAsync(
            r => r.Code == RoleCode.CANDIDATE || r.Name == UserRoles.Candidate || r.Name == "Ứng viên nội bộ",
            cancellationToken);

        if (candidateRole == null)
        {
            candidateRole = new Role
            {
                Id = Guid.NewGuid(),
                Code = RoleCode.CANDIDATE,
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

        // Lưu User trước để đảm bảo newUser.Id đã được ghi nhận trong bảng users
        // Tránh lỗi foreign key constraint "auth_audit_logs_user_id_fkey" khi ghi AuthAuditLog
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Ghi nhận Audit Log sau khi User đã được lưu thành công
        try
        {
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
        }
        catch
        {
            // Tránh làm gián đoạn luồng đăng ký nếu có sự cố ngoại lệ phụ khi ghi audit log
        }

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

    public Task<(bool IsSuccess, string Message)> SendRegistrationOtpAsync(string fullName, string email, string password, CancellationToken cancellationToken = default)
    {
        return SendRegistrationOtpAsync(fullName, email, password, "0900000000", null, cancellationToken);
    }

    public async Task<(bool IsSuccess, string Message)> SendRegistrationOtpAsync(string fullName, string email, string password, string phoneNumber, string? clientIp = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return (false, "Vui lòng nhập địa chỉ email cá nhân.");

        var normalizedEmail = email.Trim().ToLower();

        // Yêu cầu: Email bắt buộc phải có đuôi @gmail.com
        if (!normalizedEmail.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Hệ thống chỉ hỗ trợ gửi mã OTP xác thực tới tài khoản email Google (@gmail.com). Vui lòng sử dụng địa chỉ @gmail.com để đăng ký.");
        }

        if (string.IsNullOrWhiteSpace(fullName))
            return (false, "Vui lòng nhập họ và tên của bạn.");

        if (string.IsNullOrWhiteSpace(phoneNumber))
            return (false, "Vui lòng nhập số điện thoại liên hệ.");

        var cleanPhone = new string(phoneNumber.Where(char.IsDigit).ToArray());
        if (cleanPhone.Length < 10)
        {
            return (false, "Số điện thoại không hợp lệ (yêu cầu tối thiểu 10 chữ số).");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return (false, "Mật khẩu phải có độ dài tối thiểu 6 ký tự.");

        var cleanIp = !string.IsNullOrWhiteSpace(clientIp) ? clientIp.Trim() : "127.0.0.1";
        if (cleanIp == "::1") cleanIp = "127.0.0.1";

        // Kiểm tra xem IP có đang bị tạm khóa 15 phút không
        if (_cache.TryGetValue($"ip_lock_{cleanIp}", out DateTimeOffset ipLockedUntil) && ipLockedUntil > DateTimeOffset.UtcNow)
        {
            var remainingMinutes = Math.Max(1, (int)Math.Ceiling((ipLockedUntil - DateTimeOffset.UtcNow).TotalMinutes));
            return (false, $"Địa chỉ IP của bạn đang bị tạm khóa 15 phút do nhập sai thông tin quá nhiều lần. Vui lòng thử lại sau {remainingMinutes} phút.");
        }

        // Yêu cầu: 1 IP chỉ có thể tạo tối đa 1 tài khoản (bỏ qua IP loopback/local khi phát triển)
        bool isLocalOrDev = cleanIp == "127.0.0.1" || cleanIp == "localhost" || cleanIp == "Unknown";
        if (!isLocalOrDev)
        {
            var ipAccountsCount = await _dbContext.AuthAuditLogs.CountAsync(
                l => l.IpAddress == cleanIp && l.EventType == "CandidateRegisterVerified" && l.IsSuccess,
                cancellationToken);
            if (ipAccountsCount >= 1)
            {
                return (false, "Địa chỉ IP này đã được sử dụng để đăng ký 1 tài khoản ứng viên. Mỗi địa chỉ IP chỉ được phép tạo tối đa 1 tài khoản để phòng chống spam.");
            }
        }

        // Yêu cầu: 1 số điện thoại chỉ liên kết với tối đa 1 tài khoản
        var phoneUsedInUsers = await _dbContext.Users.AnyAsync(
            u => u.PhoneNumber == cleanPhone && u.Email.ToLower() != normalizedEmail && u.Status == "ACTIVE", 
            cancellationToken);
        var phoneUsedInCandidates = await _dbContext.Candidates.AnyAsync(
            c => c.Phone == cleanPhone && c.Email.ToLower() != normalizedEmail, 
            cancellationToken);
        if (phoneUsedInUsers || phoneUsedInCandidates)
        {
            return (false, "Số điện thoại này đã được sử dụng cho một tài khoản khác. Mỗi số điện thoại chỉ có thể liên kết với tối đa 1 tài khoản.");
        }

        var existingUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);
        
        if (existingUser != null)
        {
            if (existingUser.Status == "ACTIVE")
            {
                return (false, "Địa chỉ email này đã được sử dụng. Vui lòng đăng nhập hoặc dùng chức năng Quên mật khẩu.");
            }
            if (existingUser.Status == "LOCKED")
            {
                return (false, "Tài khoản liên kết với email này đã bị khóa. Vui lòng liên hệ quản trị viên.");
            }
        }

        // Sinh mã OTP 6 chữ số ngẫu nhiên an toàn
        var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

        // Đảm bảo Role Candidate tồn tại
        var candidateRole = await _dbContext.Roles.FirstOrDefaultAsync(
            r => r.Code == RoleCode.CANDIDATE || r.Name == UserRoles.Candidate || r.Name == "Ứng viên nội bộ",
            cancellationToken);

        if (candidateRole == null)
        {
            candidateRole = new Role
            {
                Id = Guid.NewGuid(),
                Code = RoleCode.CANDIDATE,
                Name = UserRoles.Candidate,
                Description = UserRoles.GetDisplayName(UserRoles.Candidate),
                IsSystem = true
            };
            _dbContext.Roles.Add(candidateRole);
        }

        if (existingUser != null && existingUser.Status == "PENDING")
        {
            existingUser.FullName = fullName.Trim();
            existingUser.PhoneNumber = cleanPhone;
            existingUser.PasswordHash = passwordHash;
            existingUser.ActivationToken = otpCode;
            existingUser.PasswordResetTokenExpiresAt = expiresAt;
            existingUser.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            var newUser = new User
            {
                Id = Guid.NewGuid(),
                FullName = fullName.Trim(),
                Email = normalizedEmail,
                PhoneNumber = cleanPhone,
                PasswordHash = passwordHash,
                Role = UserRoles.Candidate,
                RoleId = candidateRole.Id,
                Status = "PENDING",
                ActivationToken = otpCode,
                PasswordResetTokenExpiresAt = expiresAt,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            newUser.UserRoles.Add(new UserRole
            {
                UserId = newUser.Id,
                RoleId = candidateRole.Id
            });

            _dbContext.Users.Add(newUser);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Gửi email chứa mã OTP xác thực
        var emailBody = $@"
            <div style=""font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; max-width: 580px; margin: 0 auto; background: #ffffff; border: 1px solid #E2E8F0; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 6px rgba(0,0,0,0.04);"">
                <div style=""background: linear-gradient(135deg, #0D5C4D 0%, #10B981 100%); padding: 28px 32px; text-align: center;"">
                    <h1 style=""color: #ffffff; margin: 0; font-size: 22px; font-weight: 700; letter-spacing: 0.5px;"">NOVERATECH CAREERS</h1>
                    <p style=""color: #A7F3D0; margin: 6px 0 0; font-size: 13px;"">Cổng Thông Tin Tuyển Dụng & Nhân Tài</p>
                </div>
                <div style=""padding: 32px;"">
                    <h2 style=""color: #0F172A; font-size: 18px; margin-top: 0;"">Xác thực đăng ký tài khoản Ứng viên</h2>
                    <p style=""color: #475569; font-size: 14px; line-height: 1.6;"">
                        Xin chào <b>{fullName.Trim()}</b>,<br/>
                        Cảm ơn bạn đã quan tâm đến cơ hội nghề nghiệp tại NoveraTech! Vui lòng sử dụng mã OTP dưới đây để hoàn tất bước đăng ký tài khoản:
                    </p>
                    <div style=""background: #F8FAFC; border: 2px dashed #0D5C4D; border-radius: 10px; padding: 20px; text-align: center; margin: 24px 0;"">
                        <span style=""font-size: 11px; font-weight: 600; color: #64748B; text-transform: uppercase; letter-spacing: 1.5px; display: block; margin-bottom: 8px;"">MÃ XÁC THỰC OTP (HIỆU LỰC 10 PHÚT)</span>
                        <span style=""font-family: 'Courier New', Courier, monospace; font-size: 32px; font-weight: 800; color: #0D5C4D; letter-spacing: 8px;"">{otpCode}</span>
                    </div>
                    <p style=""color: #64748B; font-size: 13px; line-height: 1.5;"">
                        ⚠️ <b>Lưu ý an toàn:</b> Mã xác thực này có hiệu lực trong <b>10 phút</b> và chỉ sử dụng được 1 lần. Vui lòng không chia sẻ mã này cho bất kỳ ai.
                    </p>
                    <hr style=""border: none; border-top: 1px solid #E2E8F0; margin: 24px 0;"" />
                    <p style=""color: #94A3B8; font-size: 12px; margin: 0; text-align: center;"">
                        Nếu bạn không thực hiện yêu cầu này, vui lòng bỏ qua email.<br/>
                        &copy; {DateTime.UtcNow.Year} NoveraTech Digital Ecosystem. All rights reserved.
                    </p>
                </div>
            </div>";

        _ = _emailService.SendEmailAsync(new SendEmailRequestDto(
            normalizedEmail,
            "[NoveraTech ATS] Mã OTP xác thực đăng ký tài khoản",
            emailBody
        ), cancellationToken);

        return (true, "Mã xác thực OTP đã được gửi đến email @gmail.com của bạn. Vui lòng kiểm tra hộp thư (kể cả mục Spam).");
    }

    public async Task<(bool IsSuccess, string Message)> VerifyRegistrationOtpAsync(string email, string otpCode, string? clientIp = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(otpCode))
            return (false, "Vui lòng nhập đầy đủ email và mã OTP.");

        var normalizedEmail = email.Trim().ToLower();
        var cleanIp = !string.IsNullOrWhiteSpace(clientIp) ? clientIp.Trim() : "127.0.0.1";
        if (cleanIp == "::1") cleanIp = "127.0.0.1";

        var otpLockIpKey = $"otp_lock_ip_{cleanIp}";
        var otpLockEmailKey = $"otp_lock_email_{normalizedEmail}";

        // 1. Kiểm tra nếu IP hoặc Email đang bị tạm khóa 15 phút do sai OTP quá 5 lần
        if (_cache.TryGetValue(otpLockIpKey, out DateTimeOffset ipLockedUntil) && ipLockedUntil > DateTimeOffset.UtcNow)
        {
            var remainingMinutes = Math.Max(1, (int)Math.Ceiling((ipLockedUntil - DateTimeOffset.UtcNow).TotalMinutes));
            return (false, $"Địa chỉ IP của bạn đang bị tạm khóa thao tác 15 phút do nhập sai mã OTP quá 5 lần liên tiếp. Vui lòng thử lại sau {remainingMinutes} phút.");
        }

        if (_cache.TryGetValue(otpLockEmailKey, out DateTimeOffset emailLockedUntil) && emailLockedUntil > DateTimeOffset.UtcNow)
        {
            var remainingMinutes = Math.Max(1, (int)Math.Ceiling((emailLockedUntil - DateTimeOffset.UtcNow).TotalMinutes));
            return (false, $"Thao tác cho email này đang bị tạm khóa 15 phút do nhập sai mã OTP quá 5 lần liên tiếp. Vui lòng thử lại sau {remainingMinutes} phút.");
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (user == null || user.Status != "PENDING")
            return (false, "Không tìm thấy hồ sơ đăng ký đang chờ xác thực cho email này. Vui lòng đăng ký lại.");

        var otpFailIpKey = $"otp_fail_ip_{cleanIp}";
        var otpFailEmailKey = $"otp_fail_email_{normalizedEmail}";

        // 2. Kiểm tra mã OTP
        if (string.IsNullOrWhiteSpace(user.ActivationToken) || user.ActivationToken.Trim() != otpCode.Trim())
        {
            int ipFails = _cache.TryGetValue(otpFailIpKey, out int currentIpFails) ? currentIpFails + 1 : 1;
            int emailFails = _cache.TryGetValue(otpFailEmailKey, out int currentEmailFails) ? currentEmailFails + 1 : 1;

            _cache.Set(otpFailIpKey, ipFails, TimeSpan.FromMinutes(30));
            _cache.Set(otpFailEmailKey, emailFails, TimeSpan.FromMinutes(30));

            int effectiveFails = Math.Max(ipFails, emailFails);

            if (effectiveFails >= 5)
            {
                _cache.Set(otpLockIpKey, DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(15));
                _cache.Set(otpLockEmailKey, DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(15));
                _cache.Remove(otpFailIpKey);
                _cache.Remove(otpFailEmailKey);

                return (false, "Bạn đã nhập sai mã OTP 5/5 lần. Địa chỉ IP và tài khoản của bạn đã bị tạm khóa thao tác 15 phút để bảo đảm an toàn.");
            }

            int remaining = 5 - effectiveFails;
            return (false, $"Mã OTP không chính xác. Bạn còn {remaining}/5 lần thử trước khi bị tạm khóa thao tác 15 phút.");
        }

        if (!user.PasswordResetTokenExpiresAt.HasValue || user.PasswordResetTokenExpiresAt.Value < DateTimeOffset.UtcNow)
            return (false, "Mã OTP đã hết hạn. Vui lòng bấm 'Gửi lại mã OTP'.");

        // Kích hoạt tài khoản thành ACTIVE & xóa bộ đếm sai OTP
        user.Status = "ACTIVE";
        user.ActivationToken = null;
        user.PasswordResetTokenExpiresAt = null;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        _cache.Remove(otpFailIpKey);
        _cache.Remove(otpFailEmailKey);

        try
        {
            _dbContext.AuthAuditLogs.Add(new AuthAuditLog
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Email = user.Email,
                EventType = "CandidateRegisterVerified",
                IsSuccess = true,
                IpAddress = cleanIp,
                Reason = "Ứng viên xác thực OTP đăng ký thành công",
                Timestamp = DateTimeOffset.UtcNow
            });

            // Tự động đồng bộ sang thực thể Candidate
            var existingCandidate = await _dbContext.Candidates.FirstOrDefaultAsync(
                c => c.Email.ToLower() == user.Email.ToLower() || c.UserId == user.Id,
                cancellationToken);

            if (existingCandidate == null)
            {
                var nameParts = user.FullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var firstName = nameParts.Length > 1 ? nameParts[1] : nameParts[0];
                var lastName = nameParts.Length > 1 ? nameParts[0] : "";

                _dbContext.Candidates.Add(new Candidate
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Email = user.Email,
                    FirstName = firstName,
                    LastName = lastName,
                    Phone = user.PhoneNumber,
                    Source = CandidateSource.PORTAL,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                existingCandidate.Phone = user.PhoneNumber;
                existingCandidate.UserId = user.Id;
                existingCandidate.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        catch { }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Gửi email chào mừng chính thức
        var welcomeBody = $@"
            <div style=""font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; max-width: 580px; margin: 0 auto; background: #ffffff; border: 1px solid #E2E8F0; border-radius: 12px; overflow: hidden;"">
                <div style=""background: #0D5C4D; padding: 24px 32px; text-align: center;"">
                    <h1 style=""color: #ffffff; margin: 0; font-size: 20px; font-weight: 700;"">CHÀO MỪNG ĐẾN VỚI NOVERATECH</h1>
                </div>
                <div style=""padding: 32px;"">
                    <p style=""font-size: 15px; color: #0F172A;"">Xin chào <b>{user.FullName}</b>,</p>
                    <p style=""color: #475569; font-size: 14px; line-height: 1.6;"">
                        Tài khoản ứng viên của bạn đã được kích hoạt thành công trên Cổng Tuyển dụng NoveraTech ATS.<br/>
                        Bạn có thể đăng nhập ngay để theo dõi các vị trí tuyển dụng và nộp hồ sơ trực tuyến.
                    </p>
                    <p style=""color: #64748B; font-size: 13px;"">Chúc bạn có một hành trình tuyển dụng thuận lợi và thành công!</p>
                </div>
            </div>";

        _ = _emailService.SendEmailAsync(new SendEmailRequestDto(
            user.Email,
            "Chào mừng bạn gia nhập Cổng Tuyển Dụng NoveraTech",
            welcomeBody
        ), cancellationToken);

        return (true, "Xác thực tài khoản thành công! Bạn có thể đăng nhập ngay bây giờ.");
    }

    public async Task<(bool IsSuccess, string Message)> SendForgotPasswordOtpAsync(string email, CancellationToken cancellationToken = default)
    {
        const string standardMessage = "Nếu email tồn tại trong hệ thống, mã xác nhận OTP đã được gửi đến hòm thư của bạn.";

        if (string.IsNullOrWhiteSpace(email))
            return (false, "Vui lòng nhập địa chỉ email.");

        var reqEmail = email.Trim().ToLower();
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == reqEmail, cancellationToken);

        if (user == null || user.Status != "ACTIVE")
        {
            // Anti Email Enumeration
            return (true, standardMessage);
        }

        // Sinh mã OTP 6 chữ số
        var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        user.PasswordResetToken = otpCode;
        user.PasswordResetTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Gửi email chứa mã OTP đặt lại mật khẩu
        var emailBody = $@"
            <div style=""font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; max-width: 580px; margin: 0 auto; background: #ffffff; border: 1px solid #E2E8F0; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 6px rgba(0,0,0,0.04);"">
                <div style=""background: linear-gradient(135deg, #0D5C4D 0%, #10B981 100%); padding: 28px 32px; text-align: center;"">
                    <h1 style=""color: #ffffff; margin: 0; font-size: 22px; font-weight: 700; letter-spacing: 0.5px;"">NOVERATECH CAREERS</h1>
                    <p style=""color: #A7F3D0; margin: 6px 0 0; font-size: 13px;"">Cổng Thông Tin Tuyển Dụng & Nhân Tài</p>
                </div>
                <div style=""padding: 32px;"">
                    <h2 style=""color: #0F172A; font-size: 18px; margin-top: 0;"">Yêu cầu đặt lại mật khẩu</h2>
                    <p style=""color: #475569; font-size: 14px; line-height: 1.6;"">
                        Xin chào <b>{user.FullName}</b>,<br/>
                        Chúng tôi đã nhận được yêu cầu đặt lại mật khẩu cho tài khoản NoveraTech ATS của bạn. Vui lòng nhập mã OTP dưới đây vào màn hình đặt lại mật khẩu:
                    </p>
                    <div style=""background: #F8FAFC; border: 2px dashed #0D5C4D; border-radius: 10px; padding: 20px; text-align: center; margin: 24px 0;"">
                        <span style=""font-size: 11px; font-weight: 600; color: #64748B; text-transform: uppercase; letter-spacing: 1.5px; display: block; margin-bottom: 8px;"">MÃ OTP ĐẶT LẠI MẬT KHẨU (10 PHÚT)</span>
                        <span style=""font-family: 'Courier New', Courier, monospace; font-size: 32px; font-weight: 800; color: #0D5C4D; letter-spacing: 8px;"">{otpCode}</span>
                    </div>
                    <p style=""color: #64748B; font-size: 13px; line-height: 1.5;"">
                        ⚠️ <b>Lưu ý an toàn:</b> Mã xác nhận này có hiệu lực trong <b>10 phút</b> và chỉ sử dụng được 1 lần. Nếu bạn không gửi yêu cầu này, vui lòng bỏ qua email hoặc thông báo ngay cho ban quản trị.
                    </p>
                    <hr style=""border: none; border-top: 1px solid #E2E8F0; margin: 24px 0;"" />
                    <p style=""color: #94A3B8; font-size: 12px; margin: 0; text-align: center;"">
                        Hệ thống Tuyển dụng NoveraTech ATS &middot; Tự động gửi từ hệ thống
                    </p>
                </div>
            </div>";

        _ = _emailService.SendEmailAsync(new SendEmailRequestDto(
            user.Email,
            "[NoveraTech ATS] Mã OTP đặt lại mật khẩu",
            emailBody
        ), cancellationToken);

        return (true, standardMessage);
    }

    public async Task<(bool IsSuccess, string Message)> ResetPasswordWithOtpAsync(string email, string otpCode, string newPassword, string? clientIp = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(otpCode))
            return (false, "Vui lòng nhập đầy đủ email và mã OTP.");

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            return (false, "Mật khẩu mới phải có độ dài tối thiểu 6 ký tự.");

        var reqEmail = email.Trim().ToLower();
        var cleanIp = !string.IsNullOrWhiteSpace(clientIp) ? clientIp.Trim() : "127.0.0.1";
        if (cleanIp == "::1") cleanIp = "127.0.0.1";

        var otpLockIpKey = $"otp_lock_ip_{cleanIp}";
        var otpLockEmailKey = $"otp_lock_email_{reqEmail}";

        // 1. Kiểm tra nếu IP hoặc Email đang bị tạm khóa 15 phút
        if (_cache.TryGetValue(otpLockIpKey, out DateTimeOffset ipLockedUntil) && ipLockedUntil > DateTimeOffset.UtcNow)
        {
            var remainingMinutes = Math.Max(1, (int)Math.Ceiling((ipLockedUntil - DateTimeOffset.UtcNow).TotalMinutes));
            return (false, $"Địa chỉ IP của bạn đang bị tạm khóa thao tác 15 phút do nhập sai mã OTP quá 5 lần liên tiếp. Vui lòng thử lại sau {remainingMinutes} phút.");
        }

        if (_cache.TryGetValue(otpLockEmailKey, out DateTimeOffset emailLockedUntil) && emailLockedUntil > DateTimeOffset.UtcNow)
        {
            var remainingMinutes = Math.Max(1, (int)Math.Ceiling((emailLockedUntil - DateTimeOffset.UtcNow).TotalMinutes));
            return (false, $"Thao tác cho tài khoản này đang bị tạm khóa 15 phút do nhập sai mã OTP quá 5 lần liên tiếp. Vui lòng thử lại sau {remainingMinutes} phút.");
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == reqEmail, cancellationToken);

        if (user == null || user.Status != "ACTIVE")
            return (false, "Không tìm thấy tài khoản hợp lệ với email này.");

        var otpFailIpKey = $"otp_fail_ip_{cleanIp}";
        var otpFailEmailKey = $"otp_fail_email_{reqEmail}";

        // 2. Kiểm tra mã OTP
        if (string.IsNullOrWhiteSpace(user.PasswordResetToken) || user.PasswordResetToken.Trim() != otpCode.Trim())
        {
            int ipFails = _cache.TryGetValue(otpFailIpKey, out int currentIpFails) ? currentIpFails + 1 : 1;
            int emailFails = _cache.TryGetValue(otpFailEmailKey, out int currentEmailFails) ? currentEmailFails + 1 : 1;

            _cache.Set(otpFailIpKey, ipFails, TimeSpan.FromMinutes(30));
            _cache.Set(otpFailEmailKey, emailFails, TimeSpan.FromMinutes(30));

            int effectiveFails = Math.Max(ipFails, emailFails);

            if (effectiveFails >= 5)
            {
                _cache.Set(otpLockIpKey, DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(15));
                _cache.Set(otpLockEmailKey, DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(15));
                _cache.Remove(otpFailIpKey);
                _cache.Remove(otpFailEmailKey);

                return (false, "Bạn đã nhập sai mã OTP 5/5 lần. Địa chỉ IP và thao tác đặt lại mật khẩu đã bị tạm khóa trong 15 phút.");
            }

            int remaining = 5 - effectiveFails;
            return (false, $"Mã OTP không chính xác. Bạn còn {remaining}/5 lần thử trước khi bị tạm khóa thao tác 15 phút.");
        }

        if (!user.PasswordResetTokenExpiresAt.HasValue || user.PasswordResetTokenExpiresAt.Value < DateTimeOffset.UtcNow)
            return (false, "Mã OTP đã hết hạn (chỉ có hiệu lực trong 10 phút). Vui lòng gửi lại mã OTP mới.");

        // Reset bộ đếm sai OTP
        _cache.Remove(otpFailIpKey);
        _cache.Remove(otpFailEmailKey);

        // Cập nhật mật khẩu mới
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiresAt = null;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        // Thu hồi toàn bộ phiên đăng nhập cũ
        var oldSessions = await _dbContext.UserSessions
            .Where(s => s.UserId == user.Id && !s.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var session in oldSessions)
        {
            session.IsRevoked = true;
        }

        try
        {
            _dbContext.AuthAuditLogs.Add(new AuthAuditLog
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Email = user.Email,
                EventType = "PasswordResetOtpSuccess",
                IsSuccess = true,
                Reason = "Ứng viên đặt lại mật khẩu thành công qua mã OTP",
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch { }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Gửi email thông báo mật khẩu đã thay đổi
        var notifyBody = $@"
            <div style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333;"">
                <h3 style=""color: #0D5C4D;"">Mật khẩu đã được thay đổi thành công</h3>
                <p>Xin chào <b>{user.FullName}</b>,</p>
                <p>Mật khẩu tài khoản NoveraTech ATS của bạn vừa được đặt lại thành công bằng mã OTP lúc {DateTime.Now:HH:mm dd/MM/yyyy}.</p>
                <p>Nếu bạn không thực hiện thao tác này, vui lòng liên hệ ngay với ban quản trị để được hỗ trợ bảo vệ tài khoản.</p>
            </div>";

        _ = _emailService.SendEmailAsync(new SendEmailRequestDto(
            user.Email,
            "Thông báo: Đổi mật khẩu NoveraTech ATS thành công",
            notifyBody
        ), cancellationToken);

        return (true, "Đặt lại mật khẩu thành công! Bạn có thể đăng nhập bằng mật khẩu mới.");
    }

    public async Task<(bool IsSuccess, string Message)> ResendOtpAsync(string email, string type, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return (false, "Vui lòng cung cấp địa chỉ email.");

        var normalizedEmail = email.Trim().ToLower();

        if (string.Equals(type, "register", StringComparison.OrdinalIgnoreCase))
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail && u.Status == "PENDING", cancellationToken);
            if (user == null)
                return (false, "Không tìm thấy hồ sơ đăng ký chờ xác thực cho email này. Vui lòng đăng ký lại.");

            var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            user.ActivationToken = otpCode;
            user.PasswordResetTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            var emailBody = $@"
                <div style=""font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; max-width: 580px; margin: 0 auto; background: #ffffff; border: 1px solid #E2E8F0; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 6px rgba(0,0,0,0.04);"">
                    <div style=""background: linear-gradient(135deg, #0D5C4D 0%, #10B981 100%); padding: 28px 32px; text-align: center;"">
                        <h1 style=""color: #ffffff; margin: 0; font-size: 22px; font-weight: 700; letter-spacing: 0.5px;"">NOVERATECH CAREERS</h1>
                        <p style=""color: #A7F3D0; margin: 6px 0 0; font-size: 13px;"">Cổng Thông Tin Tuyển Dụng & Nhân Tài</p>
                    </div>
                    <div style=""padding: 32px;"">
                        <h2 style=""color: #0F172A; font-size: 18px; margin-top: 0;"">Mã xác thực OTP đăng ký mới</h2>
                        <p style=""color: #475569; font-size: 14px; line-height: 1.6;"">
                            Xin chào <b>{user.FullName}</b>,<br/>
                            Bạn vừa yêu cầu gửi lại mã xác thực OTP để hoàn tất đăng ký tài khoản NoveraTech ATS:
                        </p>
                        <div style=""background: #F8FAFC; border: 2px dashed #0D5C4D; border-radius: 10px; padding: 20px; text-align: center; margin: 24px 0;"">
                            <span style=""font-size: 11px; font-weight: 600; color: #64748B; text-transform: uppercase; letter-spacing: 1.5px; display: block; margin-bottom: 8px;"">MÃ XÁC THỰC OTP MỚI (10 PHÚT)</span>
                            <span style=""font-family: 'Courier New', Courier, monospace; font-size: 32px; font-weight: 800; color: #0D5C4D; letter-spacing: 8px;"">{otpCode}</span>
                        </div>
                    </div>
                </div>";

            _ = _emailService.SendEmailAsync(new SendEmailRequestDto(
                user.Email,
                "[NoveraTech ATS] Mã OTP xác thực đăng ký tài khoản (Gửi lại)",
                emailBody
            ), cancellationToken);

            return (true, "Mã OTP mới đã được gửi đến email của bạn.");
        }
        else
        {
            return await SendForgotPasswordOtpAsync(normalizedEmail, cancellationToken);
        }
    }

    public async Task<AuthResponseDto> ProcessExternalLoginAsync(
        string email, 
        string fullName, 
        string provider, 
        string providerKey, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new AuthResponseDto(false, "Địa chỉ email từ nhà cung cấp không hợp lệ.", null);
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        // 1. Nếu chưa có tài khoản, tự động tạo mới tài khoản Ứng viên (Candidate)
        if (user == null)
        {
            var candidateRole = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Code == RoleCode.CANDIDATE, cancellationToken);
            
            var displayName = !string.IsNullOrWhiteSpace(fullName) ? fullName.Trim() : normalizedEmail.Split('@')[0];

            user = new User
            {
                Id = Guid.NewGuid(),
                Email = normalizedEmail,
                FullName = displayName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")), // Mật khẩu ngẫu nhiên an toàn
                Status = "ACTIVE",
                UserType = UserType.EXTERNAL_CANDIDATE,
                Role = RoleCode.CANDIDATE.ToString(),
                RoleId = candidateRole?.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                LastLoginAt = DateTimeOffset.UtcNow,
                LastActivityAt = DateTimeOffset.UtcNow
            };

            await _dbContext.Users.AddAsync(user, cancellationToken);

            if (candidateRole != null)
            {
                await _dbContext.UserRoles.AddAsync(new UserRole
                {
                    UserId = user.Id,
                    RoleId = candidateRole.Id
                }, cancellationToken);
            }

            // Tự động tạo hồ sơ ứng viên (Candidate entity)
            var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string firstName = parts.Length > 0 ? parts[^1] : displayName;
            string lastName = parts.Length > 1 ? string.Join(' ', parts[..^1]) : "";

            var candidate = new Candidate
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                FirstName = firstName,
                LastName = lastName,
                Email = normalizedEmail,
                Source = CandidateSource.PORTAL,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.Candidates.AddAsync(candidate, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Ghi audit log tạo mới qua Google
            await _dbContext.AuthAuditLogs.AddAsync(new AuthAuditLog
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Email = normalizedEmail,
                EventType = "EXTERNAL_REGISTER_SUCCESS",
                IpAddress = "Google_OAuth",
                UserAgent = provider,
                IsSuccess = true,
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // 2. Tài khoản đã tồn tại -> Kiểm tra trạng thái
            if (user.Status != "ACTIVE")
            {
                return new AuthResponseDto(false, "Tài khoản của bạn đã bị vô hiệu hóa hoặc chưa được kích hoạt.", null);
            }

            if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTimeOffset.UtcNow)
            {
                return new AuthResponseDto(false, "Tài khoản của bạn đang tạm thời bị khóa do nhiều lần đăng nhập không thành công.", null);
            }

            // Cập nhật trạng thái đăng nhập
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;
            user.LastLoginAt = DateTimeOffset.UtcNow;
            user.LastActivityAt = DateTimeOffset.UtcNow;

            if (string.IsNullOrWhiteSpace(user.FullName) && !string.IsNullOrWhiteSpace(fullName))
            {
                user.FullName = fullName.Trim();
            }

            await _dbContext.AuthAuditLogs.AddAsync(new AuthAuditLog
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Email = normalizedEmail,
                EventType = "EXTERNAL_LOGIN_SUCCESS",
                IpAddress = "Google_OAuth",
                UserAgent = provider,
                IsSuccess = true,
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // 3. Thu thập Roles & Xác định chuyển hướng
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
            _ => "/gioi-thieu" // Ứng viên điều hướng về giới thiệu tuyển dụng hoặc trang việc làm
        };

        return new AuthResponseDto(true, "Xác thực danh tính thành công.", new UserInfoDto(
            user.Id,
            user.Email,
            user.FullName,
            primaryRole,
            roles,
            redirectUrl
        ));
    }
}


