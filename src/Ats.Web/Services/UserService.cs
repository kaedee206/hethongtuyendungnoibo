using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.ViewModels.Users;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using ClosedXML.Excel;

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
        if (!string.IsNullOrWhiteSpace(user.Role))
        {
            roles.Add(UserRoles.NormalizeRole(user.Role));
        }
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

    public Task<byte[]> GenerateExcelTemplateAsync(CancellationToken cancellationToken = default)
    {
        using var workbook = new XLWorkbook();
        
        // Sheet 1: Hướng dẫn
        var instructionSheet = workbook.Worksheets.Add("Hướng dẫn");
        instructionSheet.Cell(1, 1).Value = "HƯỚNG DẪN NHẬP DỮ LIỆU NHÂN SỰ";
        instructionSheet.Cell(1, 1).Style.Font.Bold = true;
        instructionSheet.Cell(1, 1).Style.Font.FontSize = 14;
        
        instructionSheet.Cell(3, 1).Value = "1. Các cột có dấu (*) là bắt buộc nhập.";
        instructionSheet.Cell(4, 1).Value = "2. Cột Email phải có định dạng hợp lệ (vd: @noveratech.digital) và chưa tồn tại trong hệ thống.";
        instructionSheet.Cell(5, 1).Value = "3. Vai trò (*): Điền tên các vai trò (cách nhau bởi dấu phẩy, vd: Interviewer, Employee).";
        instructionSheet.Cell(6, 1).Value = "4. Không thay đổi thứ tự hoặc xóa các cột ở sheet 'Dữ liệu'.";
        instructionSheet.Columns().AdjustToContents();

        // Sheet 2: Dữ liệu
        var dataSheet = workbook.Worksheets.Add("Dữ liệu");
        
        // Header
        var headers = new string[] { "Họ và tên (*)", "Email (*)", "Số điện thoại", "Phòng ban", "Chức vụ", "Vai trò (*)" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = dataSheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        // Example data
        dataSheet.Cell(2, 1).Value = "Nguyễn Văn A";
        dataSheet.Cell(2, 2).Value = "nguyenvana@noveratech.digital";
        dataSheet.Cell(2, 3).Value = "0901234567";
        dataSheet.Cell(2, 4).Value = "Phòng IT";
        dataSheet.Cell(2, 5).Value = "Nhân viên phát triển phần mềm";
        dataSheet.Cell(2, 6).Value = "Interviewer, Employee";
        
        dataSheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Task.FromResult(stream.ToArray());
    }

    public async Task<ImportExcelResultDto> ValidateExcelImportAsync(Stream excelStream, CancellationToken cancellationToken = default)
    {
        var result = new ImportExcelResultDto();

        var validDepartments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validJobPositions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        var conn = _dbContext.Database.GetDbConnection();
        bool wasClosed = conn.State == System.Data.ConnectionState.Closed;
        if (wasClosed) await conn.OpenAsync(cancellationToken);
        try
        {
            using var cmd1 = conn.CreateCommand();
            cmd1.CommandText = "SELECT name FROM departments";
            using var reader1 = await cmd1.ExecuteReaderAsync(cancellationToken);
            while (await reader1.ReadAsync(cancellationToken))
            {
                validDepartments.Add(reader1.GetString(0));
            }
        }
        catch { /* ignore if table not exists */ }

        try
        {
            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = "SELECT name FROM job_positions";
            using var reader2 = await cmd2.ExecuteReaderAsync(cancellationToken);
            while (await reader2.ReadAsync(cancellationToken))
            {
                validJobPositions.Add(reader2.GetString(0));
            }
        }
        catch { /* ignore if table not exists */ }
        
        if (wasClosed) await conn.CloseAsync();

        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name == "Dữ liệu") ?? workbook.Worksheet(1);
        
        // SCRUM-183: Validate headers
        var expectedHeaders = new string[] { "Họ và tên (*)", "Email (*)", "Số điện thoại", "Phòng ban", "Chức vụ", "Vai trò (*)" };
        var headerRow = worksheet.Row(1);
        for (int i = 0; i < expectedHeaders.Length; i++)
        {
            if (!string.Equals(headerRow.Cell(i + 1).GetString().Trim(), expectedHeaders[i], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Cấu trúc tệp không hợp lệ. Vui lòng sử dụng tệp mẫu được cung cấp (sai hoặc thiếu tên cột).");
            }
        }

        var rows = worksheet.RowsUsed().Skip(1); // skip header
        
        // SCRUM-184: Prepare for duplicate detection
        var existingEmails = await _dbContext.Users.Select(u => u.Email.ToLower()).ToListAsync(cancellationToken);
        var existingEmailsSet = new HashSet<string>(existingEmails, StringComparer.OrdinalIgnoreCase);
        var emailsInFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            int rowIndex = row.RowNumber();
            var errors = new List<ImportExcelErrorDetailDto>();
            
            string fullName = row.Cell(1).GetString().Trim();
            string email = row.Cell(2).GetString().Trim();
            string phoneNumber = row.Cell(3).GetString().Trim();
            string department = row.Cell(4).GetString().Trim();
            string jobPosition = row.Cell(5).GetString().Trim();
            string roles = row.Cell(6).GetString().Trim();
            
            // Validate required
            if (string.IsNullOrEmpty(fullName))
                errors.Add(new ImportExcelErrorDetailDto { ColumnName = "Họ và tên", ErrorMessage = "Không được để trống" });
            if (string.IsNullOrEmpty(email))
                errors.Add(new ImportExcelErrorDetailDto { ColumnName = "Email", ErrorMessage = "Không được để trống" });
            if (string.IsNullOrEmpty(roles))
                errors.Add(new ImportExcelErrorDetailDto { ColumnName = "Vai trò", ErrorMessage = "Không được để trống" });
                
            // Validate Email format and duplicates
            if (!string.IsNullOrEmpty(email))
            {
                // Format check
                if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
                {
                    errors.Add(new ImportExcelErrorDetailDto { ColumnName = "Email", ErrorMessage = "Không đúng định dạng" });
                }
                else if (!email.EndsWith("@noveratech.digital", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new ImportExcelErrorDetailDto { ColumnName = "Email", ErrorMessage = "Phải là email nội bộ (@noveratech.digital)" });
                }
                
                // SCRUM-184: Duplicate in file check
                if (emailsInFile.TryGetValue(email, out int duplicateRowIndex))
                {
                    errors.Add(new ImportExcelErrorDetailDto { ColumnName = "Email", ErrorMessage = $"Email bị trùng lặp với dòng số {duplicateRowIndex} trong tệp" });
                }
                else
                {
                    emailsInFile[email] = rowIndex;
                }

                // SCRUM-184: Duplicate in system check
                if (existingEmailsSet.Contains(email))
                {
                    errors.Add(new ImportExcelErrorDetailDto { ColumnName = "Email", ErrorMessage = "Email đã tồn tại trong hệ thống" });
                }
            }

            // Validate Phone Number
            if (!string.IsNullOrEmpty(phoneNumber) && !System.Text.RegularExpressions.Regex.IsMatch(phoneNumber, @"^(0|\+84|84)[35789][0-9]{8}$"))
            {
                errors.Add(new ImportExcelErrorDetailDto { ColumnName = "Số điện thoại", ErrorMessage = "Không đúng định dạng" });
            }

            // Validate Department and Job Position
            if (!string.IsNullOrEmpty(department) && validDepartments.Count > 0 && !validDepartments.Contains(department))
            {
                errors.Add(new ImportExcelErrorDetailDto { ColumnName = "Phòng ban", ErrorMessage = "Không tồn tại trong hệ thống" });
            }
            if (!string.IsNullOrEmpty(jobPosition) && validJobPositions.Count > 0 && !validJobPositions.Contains(jobPosition))
            {
                errors.Add(new ImportExcelErrorDetailDto { ColumnName = "Chức vụ", ErrorMessage = "Không tồn tại trong hệ thống" });
            }

            if (errors.Count > 0)
            {
                result.InvalidRows.Add(new ImportExcelErrorRowDto { RowIndex = rowIndex, Errors = errors });
            }
            else
            {
                result.ValidRows.Add(new ImportExcelRowDto 
                { 
                    RowIndex = rowIndex, 
                    FullName = fullName, 
                    Email = email, 
                    PhoneNumber = phoneNumber,
                    Department = department, 
                    JobPosition = jobPosition, 
                    Roles = roles 
                });
            }
        }

        return result;
    }

    public async Task<ExecuteImportResultDto> ExecuteImportAsync(ExecuteImportRequestDto request, CancellationToken cancellationToken = default)
    {
        var result = new ExecuteImportResultDto();

        // SCRUM-180: Use transaction
        using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var allRoles = await _dbContext.Roles.ToListAsync(cancellationToken);
            string defaultPassword = "AtsUser@123456"; 
            
            foreach (var row in request.ValidRows)
            {
                // Check if email exists
                bool emailExists = await _dbContext.Users.AnyAsync(u => u.Email.ToLower() == row.Email.ToLower(), cancellationToken);
                if (emailExists)
                {
                    result.Errors.Add(new ImportExcelErrorRowDto
                    {
                        RowIndex = row.RowIndex,
                        Errors = new List<ImportExcelErrorDetailDto>
                        {
                            new() { ColumnName = "Email", ErrorMessage = "Email đã tồn tại trong hệ thống." }
                        }
                    });
                    result.TotalFailed++;
                    continue;
                }

                var newUser = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = row.FullName,
                    Email = row.Email.ToLower(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(defaultPassword),
                    Department = row.Department,
                    Status = "ACTIVE",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                var roleNames = row.Roles.Split(',').Select(r => r.Trim()).Where(r => !string.IsNullOrEmpty(r)).ToList();
                if (roleNames.Any())
                {
                    newUser.Role = Ats.Web.Constants.UserRoles.NormalizeRole(roleNames.First()); 
                }

                foreach (var roleName in roleNames)
                {
                    var normalizedName = Ats.Web.Constants.UserRoles.NormalizeRole(roleName);
                    var matchedRole = allRoles.FirstOrDefault(r => r.Name == normalizedName);
                    if (matchedRole != null)
                    {
                        newUser.UserRoles.Add(new UserRole
                        {
                            UserId = newUser.Id,
                            RoleId = matchedRole.Id
                        });
                        
                        if (newUser.RoleId == null)
                        {
                            newUser.RoleId = matchedRole.Id;
                        }
                    }
                }

                _dbContext.Users.Add(newUser);
                result.TotalSuccess++;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw; 
        }

        return result;
    }
}
