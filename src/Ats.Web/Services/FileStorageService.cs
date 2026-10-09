using System.Text.RegularExpressions;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Triển khai dịch vụ lưu trữ tệp cục bộ trên máy chủ.
/// Tách biệt hoàn toàn thư mục lưu trữ media (ảnh/avatar) và thư mục lưu trữ CV ứng viên.
/// </summary>
public class FileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FileStorageService> _logger;

    public const string UploadsFolder = "uploads";
    public const string MediaFolder = "media";
    public const string CvsFolder = "cvs";

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    private static readonly HashSet<string> AllowedCvExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx"
    };

    private const long MaxImageSizeBytes = 10 * 1024 * 1024; // 10MB
    private const long MaxCvSizeBytes = 25 * 1024 * 1024;    // 25MB

    public FileStorageService(IWebHostEnvironment environment, ILogger<FileStorageService> logger)
    {
        _environment = environment;
        _logger = logger;
        EnsureStorageDirectories();
    }

    public async Task<string> SaveMediaAsync(IFormFile file, string? prefix = null, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("Tệp hình ảnh không hợp lệ hoặc rỗng.", nameof(file));
        }

        if (file.Length > MaxImageSizeBytes)
        {
            throw new ArgumentException($"Dung lượng hình ảnh vượt quá giới hạn cho phép (tối đa {MaxImageSizeBytes / (1024 * 1024)}MB).", nameof(file));
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !AllowedImageExtensions.Contains(extension))
        {
            throw new ArgumentException($"Định dạng hình ảnh '{extension}' không được hỗ trợ. Chỉ chấp nhận các định dạng: {string.Join(", ", AllowedImageExtensions)}.", nameof(file));
        }

        return await SaveFileInternalAsync(file, MediaFolder, prefix ?? "media", extension, cancellationToken);
    }

    public async Task<string> SaveCvAsync(IFormFile file, string? prefix = null, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("Tệp CV không hợp lệ hoặc rỗng.", nameof(file));
        }

        if (file.Length > MaxCvSizeBytes)
        {
            throw new ArgumentException($"Dung lượng tệp CV vượt quá giới hạn cho phép (tối đa {MaxCvSizeBytes / (1024 * 1024)}MB).", nameof(file));
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !AllowedCvExtensions.Contains(extension))
        {
            throw new ArgumentException($"Định dạng tệp CV '{extension}' không được hỗ trợ. Chỉ chấp nhận: .pdf, .doc, .docx.", nameof(file));
        }

        return await SaveFileInternalAsync(file, CvsFolder, prefix ?? "cv", extension, cancellationToken);
    }

    private async Task<string> SaveFileInternalAsync(
        IFormFile file, 
        string targetSubFolder, 
        string prefix, 
        string extension, 
        CancellationToken cancellationToken)
    {
        var targetDir = Path.Combine(GetWebRootPath(), UploadsFolder, targetSubFolder);
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        // Tạo tên tệp độc nhất an toàn (chống ghi đè & chống Path Traversal)
        var sanitizedOriginal = SanitizeFileName(Path.GetFileNameWithoutExtension(file.FileName));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var uniqueToken = Guid.NewGuid().ToString("N")[..8];
        var finalFileName = $"{prefix}_{timestamp}_{uniqueToken}_{sanitizedOriginal}{extension}";

        var physicalPath = Path.Combine(targetDir, finalFileName);

        using (var stream = new FileStream(physicalPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        _logger.LogInformation("Đã lưu tệp thành công vào thư mục cục bộ {Folder}: {FileName} ({SizeBytes} bytes)", 
            targetSubFolder, finalFileName, file.Length);

        // Trả về đường dẫn relative dạng URL web
        return $"/{UploadsFolder}/{targetSubFolder}/{finalFileName}";
    }

    public Task<bool> DeleteFileAsync(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Task.FromResult(false);
        }

        try
        {
            var physicalPath = GetPhysicalPath(relativePath);
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
                _logger.LogInformation("Đã xóa tệp cục bộ: {Path}", physicalPath);
                return Task.FromResult(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể xóa tệp cục bộ {Path}: {Message}", relativePath, ex.Message);
        }

        return Task.FromResult(false);
    }

    public string GetPhysicalPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentNullException(nameof(relativePath));
        }

        // Chuẩn hóa đường dẫn tương đối, bỏ dấu / ở đầu nếu có
        var cleanPath = relativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(GetWebRootPath(), cleanPath);
    }

    public bool FileExists(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;
        try
        {
            return File.Exists(GetPhysicalPath(relativePath));
        }
        catch
        {
            return false;
        }
    }

    public void EnsureStorageDirectories()
    {
        try
        {
            var webRoot = GetWebRootPath();
            var directoriesToEnsure = new[]
            {
                Path.Combine(webRoot, UploadsFolder),
                Path.Combine(webRoot, UploadsFolder, MediaFolder),       // uploads/media (Ảnh/Media ứng viên và hệ thống)
                Path.Combine(webRoot, UploadsFolder, CvsFolder),         // uploads/cvs (Hồ sơ, CV của ứng viên)
                Path.Combine(webRoot, UploadsFolder, "avatars"),         // uploads/avatars (alias tương thích ngược)
                Path.Combine(webRoot, UploadsFolder, "resumes"),         // uploads/resumes (alias tương thích ngược)
                Path.Combine(webRoot, "templates")                       // templates (Mẫu Excel, tài liệu mẫu)
            };

            foreach (var dir in directoriesToEnsure)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi khởi tạo các thư mục lưu trữ cục bộ: {Message}", ex.Message);
        }
    }

    private string GetWebRootPath()
    {
        if (!string.IsNullOrEmpty(_environment.WebRootPath))
        {
            return _environment.WebRootPath;
        }

        // Fallback nếu WebRootPath chưa được khởi tạo
        var fallbackPath = Path.Combine(_environment.ContentRootPath, "wwwroot");
        if (!Directory.Exists(fallbackPath))
        {
            Directory.CreateDirectory(fallbackPath);
        }
        return fallbackPath;
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "file";
        // Loại bỏ ký tự đặc biệt, giữ lại chữ, số, dấu gạch
        var sanitized = Regex.Replace(fileName, @"[^a-zA-Z0-9_\-\.]", "_");
        return sanitized.Length > 40 ? sanitized[..40] : sanitized;
    }
}
