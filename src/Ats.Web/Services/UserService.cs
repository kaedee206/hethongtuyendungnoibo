using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.ViewModels.Users;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Ats.Web.Services;

public class UserService(ApplicationDbContext dbContext, IEmailService emailService) : IUserService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IEmailService _emailService = emailService;

    public async Task<UserListViewModel> GetUsersAsync(string? keyword, string? role, string? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize <= 0) pageSize = 20;

        var query = _dbContext.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .AsNoTracking()
            .AsQueryable();

        // 1. Tìm kiếm theo tên, email, phòng ban (case-insensitive, partial match - AC S1-08)
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim().ToLower();
            query = query.Where(u =>
                u.Email.ToLower().Contains(term) ||
                u.FullName.ToLower().Contains(term) ||
                (u.Department != null && u.Department.ToLower().Contains(term)));
        }

        // 2. Lọc theo vai trò
        if (!string.IsNullOrWhiteSpace(role))
        {
            var normalizedRole = UserRoles.NormalizeRole(role);
            query = query.Where(u =>
                u.UserRoles.Any(ur => ur.Role.Name == role || ur.Role.Name == normalizedRole) ||
                u.Role == role || u.Role == normalizedRole);
        }

        // 3. Lọc theo trạng thái
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(u => u.Status == status);
        }

        var totalRecords = await query.CountAsync(cancellationToken);

        // Mặc định sắp xếp theo ngày tạo mới nhất
        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserItemViewModel
            {
                Id = u.Id,
                Email = u.Email,
                FullName = u.FullName,
                Department = u.Department,
                Roles = u.UserRoles.Select(ur => ur.Role.Name).ToList(),
                Status = u.Status,
                CreatedAt = u.CreatedAt,
                LockReason = u.LockReason,
                LockedAt = u.LockedAt
            })
            .ToListAsync(cancellationToken);

        // Bổ sung vai trò fallback nếu UserRoles rỗng
        foreach (var u in users)
        {
            if (u.Roles.Count == 0)
            {
                u.Roles.Add(UserRoles.Candidate);
            }
        }

        return new UserListViewModel
        {
            Users = users,
            Keyword = keyword,
            Role = role,
            Status = status,
            CurrentPage = page,
            PageSize = pageSize,
            TotalRecords = totalRecords
        };
    }

    public async Task<UserCreateViewModel> GetUserCreateViewModelAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _dbContext.Roles.AsNoTracking().ToListAsync(cancellationToken);
        var availableRoles = UserRoles.AllRoles.Select(roleCode =>
        {
            var dbRole = roles.FirstOrDefault(r => r.Name == roleCode || UserRoles.NormalizeRole(r.Name) == roleCode);
            return new RoleCheckboxItem
            {
                RoleName = roleCode,
                DisplayName = UserRoles.GetDisplayName(roleCode),
                Description = dbRole?.Description ?? string.Empty,
                IsSelected = false
            };
        }).ToList();

        return new UserCreateViewModel
        {
            AvailableRoles = availableRoles
        };
    }

    public async Task<(bool IsSuccess, string Message, Guid? UserId)> CreateUserAsync(UserCreateViewModel model, Guid currentAdminId, string baseUrl, CancellationToken cancellationToken = default)
    {
        var email = model.Email.Trim().ToLower();

        // Kiểm tra tên miền email nội bộ NoveraTech (AC S1-07)
        if (!email.EndsWith("@noveratech.digital", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Email nội bộ bắt buộc phải thuộc tên miền @noveratech.digital.", null);
        }

        // Kiểm tra email trùng lặp (AC S1-08)
        var isEmailTaken = await _dbContext.Users.AnyAsync(u => u.Email.ToLower() == email, cancellationToken);
        if (isEmailTaken)
        {
            return (false, "Email này đã được sử dụng trong hệ thống.", null);
        }

        var rolesToAssign = model.SelectedRoles?.Any() == true
            ? model.SelectedRoles
            : model.AvailableRoles?.Where(r => r.IsSelected).Select(r => r.RoleName).ToList() ?? new List<string>();

        if (rolesToAssign.Count == 0)
        {
            return (false, "Vui lòng chọn ít nhất một vai trò cho người dùng.", null);
        }

        // Tự động sinh mật khẩu tạm thời an toàn (tối thiểu 10 ký tự, có hoa, thường, số, ký tự đặc biệt)
        var tempPassword = GenerateSecurePassword();

        var newUser = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = model.FullName.Trim(),
            Department = string.IsNullOrWhiteSpace(model.Department) ? null : model.Department.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword),
            Status = "ACTIVE",
            Role = UserRoles.NormalizeRole(rolesToAssign.First()),
            CreatedBy = currentAdminId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Gán các vai trò đã chọn (AC S1-09)
        var allDbRoles = await _dbContext.Roles.ToListAsync(cancellationToken);
        foreach (var selectedRole in rolesToAssign)
        {
            var normalized = UserRoles.NormalizeRole(selectedRole);
            var roleEntity = allDbRoles.FirstOrDefault(r => r.Name == selectedRole || UserRoles.NormalizeRole(r.Name) == normalized);
            if (roleEntity == null)
            {
                roleEntity = new Role
                {
                    Id = Guid.NewGuid(),
                    Name = selectedRole,
                    Description = UserRoles.GetDisplayName(selectedRole),
                    IsSystem = true
                };
                _dbContext.Roles.Add(roleEntity);
                allDbRoles.Add(roleEntity);
            }

            newUser.UserRoles.Add(new UserRole
            {
                UserId = newUser.Id,
                RoleId = roleEntity.Id
            });
        }

        _dbContext.Users.Add(newUser);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Gửi email kích hoạt tài khoản kèm mật khẩu tạm thời (AC S1-08)
        var loginUrl = $"{baseUrl.TrimEnd('/')}/Account/Login";
        var emailBody = $@"
            <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                <h2 style='color: #2e4a8f;'>Chào mừng đến với NoveraTech ATS</h2>
                <p>Xin chào <b>{newUser.FullName}</b>,</p>
                <p>Tài khoản nội bộ của bạn đã được quản trị viên khởi tạo thành công trên Hệ thống Tuyển dụng Nội bộ NoveraTech ATS.</p>
                <div style='background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 15px; margin: 15px 0;'>
                    <p style='margin: 0 0 8px 0;'><b>Thông tin đăng nhập của bạn:</b></p>
                    <p style='margin: 0 0 5px 0;'>- Email: <b>{newUser.Email}</b></p>
                    <p style='margin: 0 0 5px 0;'>- Mật khẩu tạm thời: <code style='background: #e2e8f0; padding: 2px 6px; border-radius: 4px; font-size: 14px;'>{tempPassword}</code></p>
                    <p style='margin: 0;'>- Vai trò: <i>{string.Join(", ", (model.SelectedRoles ?? new List<string>()).Select(UserRoles.GetDisplayName))}</i></p>
                </div>
                <p><a href='{loginUrl}' style='display: inline-block; background-color: #2e4a8f; color: #fff; padding: 10px 20px; text-decoration: none; border-radius: 5px; font-weight: bold;'>Đăng nhập ngay</a></p>
                <p><i>Lưu ý: Vì lý do bảo mật, vui lòng đổi mật khẩu ngay sau lần đăng nhập đầu tiên.</i></p>
                <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;' />
                <p style='font-size: 12px; color: #777;'>Hệ thống Tuyển dụng Nội bộ NoveraTech ATS</p>
            </div>";

        _ = _emailService.SendEmailAsync(new SendEmailRequestDto(
            newUser.Email,
            "Tài khoản NoveraTech ATS đã được khởi tạo",
            emailBody
        ), cancellationToken);

        return (true, "Tạo tài khoản thành công. Thông tin mật khẩu tạm đã được gửi đến email người dùng.", newUser.Id);
    }

    public async Task<UserEditViewModel?> GetUserEditViewModelAsync(Guid id, Guid currentAdminId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user == null) return null;

        var roles = await _dbContext.Roles.AsNoTracking().ToListAsync(cancellationToken);
        var userRoleNames = user.UserRoles.Select(ur => ur.Role.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (userRoleNames.Count == 0 && !string.IsNullOrWhiteSpace(user.Role))
        {
            userRoleNames.Add(user.Role);
        }

        var isSelfAdmin = (id == currentAdminId);

        var availableRoles = UserRoles.AllRoles.Select(roleCode =>
        {
            var dbRole = roles.FirstOrDefault(r => r.Name == roleCode || UserRoles.NormalizeRole(r.Name) == roleCode);
            var isSelected = userRoleNames.Contains(roleCode) || userRoleNames.Contains(UserRoles.GetDisplayName(roleCode));
            
            // AC S1-09: Quản trị viên không thể tự thu hồi vai trò quản trị của chính mình
            var isDisabled = isSelfAdmin && (roleCode == UserRoles.Admin);

            return new RoleCheckboxItem
            {
                RoleName = roleCode,
                DisplayName = UserRoles.GetDisplayName(roleCode),
                Description = dbRole?.Description ?? string.Empty,
                IsSelected = isSelected,
                IsDisabled = isDisabled
            };
        }).ToList();

        return new UserEditViewModel
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Department = user.Department,
            Status = user.Status,
            IsSelfAdmin = isSelfAdmin,
            SelectedRoles = availableRoles.Where(r => r.IsSelected).Select(r => r.RoleName).ToList(),
            AvailableRoles = availableRoles
        };
    }

    public async Task<(bool IsSuccess, string Message)> UpdateUserAsync(UserEditViewModel model, Guid currentAdminId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == model.Id, cancellationToken);

        if (user == null)
        {
            return (false, "Không tìm thấy người dùng.");
        }

        var rolesToAssign = model.SelectedRoles?.Any() == true
            ? model.SelectedRoles
            : model.AvailableRoles?.Where(r => r.IsSelected).Select(r => r.RoleName).ToList() ?? new List<string>();

        // AC S1-09: Không thể tự thu hồi vai trò quản trị của chính mình
        if (model.Id == currentAdminId || model.IsSelf)
        {
            var hasAdmin = rolesToAssign.Any(r => UserRoles.NormalizeRole(r) == UserRoles.Admin);
            if (!hasAdmin)
            {
                return (false, "Bạn không thể tự thu hồi quyền Quản trị viên (Admin) của chính mình.");
            }
        }

        if (rolesToAssign.Count == 0)
        {
            return (false, "Người dùng phải có ít nhất một vai trò.");
        }

        user.FullName = model.FullName.Trim();
        user.Department = string.IsNullOrWhiteSpace(model.Department) ? null : model.Department.Trim();
        user.Status = model.Status;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.UpdatedBy = currentAdminId;

        // Cập nhật quan hệ đa vai trò (AC S1-09)
        var allDbRoles = await _dbContext.Roles.ToListAsync(cancellationToken);
        _dbContext.UserRoles.RemoveRange(user.UserRoles);

        foreach (var roleName in rolesToAssign)
        {
            var normalized = UserRoles.NormalizeRole(roleName);
            var roleEntity = allDbRoles.FirstOrDefault(r => r.Name == roleName || UserRoles.NormalizeRole(r.Name) == normalized);
            if (roleEntity == null)
            {
                roleEntity = new Role
                {
                    Id = Guid.NewGuid(),
                    Name = roleName,
                    Description = UserRoles.GetDisplayName(roleName),
                    IsSystem = true
                };
                _dbContext.Roles.Add(roleEntity);
                allDbRoles.Add(roleEntity);
            }

            user.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = roleEntity.Id
            });
        }

        user.Role = UserRoles.NormalizeRole(model.SelectedRoles?.FirstOrDefault() ?? user.Role);

        // Nếu trạng thái đổi sang LOCKED -> thu hồi toàn bộ phiên
        if (user.Status == "LOCKED")
        {
            var sessions = await _dbContext.UserSessions
                .Where(s => s.UserId == user.Id && !s.IsRevoked)
                .ToListAsync(cancellationToken);
            foreach (var s in sessions) s.IsRevoked = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (true, "Cập nhật thông tin tài khoản thành công. Quyền hạn mới có hiệu lực ngay lập tức.");
    }

    public async Task<(bool IsSuccess, string Message, string? HandoverWarning)> LockUserAsync(Guid id, string lockReason, Guid currentAdminId, CancellationToken cancellationToken = default)
    {
        if (id == currentAdminId)
        {
            return (false, "Bạn không thể tự khóa tài khoản của chính mình.", null);
        }

        if (string.IsNullOrWhiteSpace(lockReason))
        {
            return (false, "Bắt buộc nhập lý do khóa tài khoản.", null);
        }

        var user = await _dbContext.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản cần khóa.", null);
        }

        // AC S1-10: Cập nhật lý do khóa và thời điểm khóa
        user.Status = "LOCKED";
        user.LockReason = lockReason.Trim();
        user.LockedAt = DateTimeOffset.UtcNow;
        user.LockedBy = currentAdminId;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        // AC S1-10: Lập tức thu hồi mọi phiên đang mở
        var activeSessions = await _dbContext.UserSessions
            .Where(s => s.UserId == id && !s.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var session in activeSessions)
        {
            session.IsRevoked = true;
        }

        // AC S1-10: Cảnh báo nếu người đó phụ trách vị trí tuyển dụng (Recruiter / Hiring Manager)
        string? handoverWarning = null;
        var roles = user.UserRoles.Select(ur => UserRoles.NormalizeRole(ur.Role.Name)).ToHashSet();
        if (roles.Contains(UserRoles.Recruiter) || roles.Contains(UserRoles.HiringManager))
        {
            var roleNames = string.Join(" và ", roles.Where(r => r == UserRoles.Recruiter || r == UserRoles.HiringManager).Select(UserRoles.GetDisplayName));
            handoverWarning = $"Cảnh báo: Nhân sự này đang giữ vai trò [{roleNames}]. Vui lòng rà soát và bàn giao các yêu cầu và vị trí tuyển dụng phụ trách.";
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (true, "Đã khóa tài khoản thành công.", handoverWarning);
    }

    public async Task<(bool IsSuccess, string Message)> UnlockUserAsync(Guid id, Guid currentAdminId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FindAsync([id], cancellationToken);
        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản.");
        }

        user.Status = "ACTIVE";
        user.LockReason = null;
        user.LockedAt = null;
        user.LockedUntil = null;
        user.FailedLoginAttempts = 0;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.UpdatedBy = currentAdminId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (true, "Đã mở khóa tài khoản thành công.");
    }

    private static string GenerateSecurePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string specials = "@$!%*?&";

        var chars = new char[12];
        chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        chars[3] = specials[RandomNumberGenerator.GetInt32(specials.Length)];

        const string all = upper + lower + digits + specials;
        for (int i = 4; i < 12; i++)
        {
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        // Trộn ngẫu nhiên
        return new string(chars.OrderBy(_ => RandomNumberGenerator.GetInt32(100)).ToArray());
    }
}
