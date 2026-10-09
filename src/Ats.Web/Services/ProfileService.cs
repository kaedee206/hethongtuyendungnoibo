using System.Text.RegularExpressions;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.ViewModels.Profiles;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

public partial class ProfileService : IProfileService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ProfileService> _logger;
    private readonly IFileStorageService? _fileStorageService;

    public ProfileService(
        ApplicationDbContext dbContext,
        ILogger<ProfileService> logger,
        IFileStorageService? fileStorageService = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _fileStorageService = fileStorageService;
    }

    [GeneratedRegex(@"^(0|\+84|84)[35789][0-9]{8}$")]
    private static partial Regex VietnamesePhoneRegex();

    public async Task<ProfileUpdateViewModel?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.DepartmentEntity)
            .Include(u => u.RoleEntity)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
            return null;

        return new ProfileUpdateViewModel
        {
            FullName = user.FullName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            JobTitle = user.JobTitle,
            DepartmentName = user.DepartmentEntity?.Name ?? user.Department ?? "Chưa phân bổ",
            RoleName = user.RoleEntity?.Name ?? user.Role ?? "Nhân viên",
            AvatarUrl = user.AvatarUrl
        };
    }

    public async Task<(bool Success, string Message)> UpdateProfileAsync(
        Guid userId,
        UpdateProfileRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            return (false, "Không tìm thấy thông tin tài khoản.");
        }

        // Validate FullName
        if (string.IsNullOrWhiteSpace(dto.FullName))
        {
            return (false, "Họ và tên không được để trống.");
        }

        // Validate VN Phone regex if provided
        if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
        {
            var cleanedPhone = dto.PhoneNumber.Trim().Replace(" ", "").Replace("-", "");
            if (!VietnamesePhoneRegex().IsMatch(cleanedPhone))
            {
                return (false, "Số điện thoại không hợp lệ. Vui lòng nhập đúng định dạng số điện thoại Việt Nam (VD: 0912345678, +84912345678).");
            }
            user.PhoneNumber = cleanedPhone;
        }
        else
        {
            user.PhoneNumber = null;
        }

        // Enforce Read-Only properties (Email, Department, Role cannot be altered by normal user)
        // If incoming dto has different email, role or department, reject or ignore
        user.FullName = dto.FullName.Trim();
        user.JobTitle = string.IsNullOrWhiteSpace(dto.JobTitle) ? null : dto.JobTitle.Trim();
        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.UpdatedBy = userId;

        // Record Audit Log
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Action = "UPDATE_PROFILE",
            EntityName = "User",
            EntityId = userId.ToString(),
            NewValues = $"{{\"FullName\":\"{user.FullName}\", \"PhoneNumber\":\"{user.PhoneNumber}\", \"JobTitle\":\"{user.JobTitle}\"}}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.AuditLogs.Add(auditLog);

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("User {UserId} updated profile successfully.", userId);

        return (true, "Cập nhật hồ sơ thành công.");
    }

    public async Task<(bool Success, string Message, string? AvatarUrl)> UploadAvatarAsync(
        Guid userId,
        IFormFile file,
        string webRootPath,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return (false, "Vui lòng chọn file hình ảnh.", null);
        }

        // S2-03: Max 2MB
        const long maxSizeBytes = 2 * 1024 * 1024;
        if (file.Length > maxSizeBytes)
        {
            return (false, "Kích thước ảnh đại diện không được vượt quá 2MB.", null);
        }

        // S2-03: Allowed formats: JPG, JPEG, PNG
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension))
        {
            return (false, "Định dạng file không hỗ trợ. Vui lòng chọn ảnh định dạng JPG hoặc PNG.", null);
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            return (false, "Không tìm thấy người dùng.", null);
        }

        string relativeUrl;
        if (_fileStorageService != null)
        {
            relativeUrl = await _fileStorageService.SaveMediaAsync(file, $"avatar_{userId:N}", cancellationToken);
        }
        else
        {
            var uploadsFolder = Path.Combine(webRootPath, "uploads", "media");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = $"{userId}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{extension}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            relativeUrl = $"/uploads/media/{uniqueFileName}";
        }
        user.AvatarUrl = relativeUrl;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.UpdatedBy = userId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("User {UserId} updated avatar to {AvatarUrl}.", userId, relativeUrl);

        return (true, "Tải lên ảnh đại diện thành công.", relativeUrl);
    }
}
