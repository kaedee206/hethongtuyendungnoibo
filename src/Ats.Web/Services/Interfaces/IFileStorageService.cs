using Microsoft.AspNetCore.Http;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Dịch vụ quản lý lưu trữ tệp cục bộ cho hệ thống ATS.
/// Phân tách rõ ràng giữa thư mục lưu hình ảnh/media và thư mục lưu trữ CV ứng viên.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Lưu trữ hình ảnh / media (ảnh chân dung, avatar, ảnh hồ sơ ứng viên) vào thư mục uploads/media.
    /// </summary>
    /// <param name="file">Tệp tải lên</param>
    /// <param name="prefix">Tiền tố tên file (tùy chọn, vd: candidate, avatar, user)</param>
    /// <param name="cancellationToken">Hủy thao tác</param>
    /// <returns>Đường dẫn tương đối phục vụ web (ví dụ: /uploads/media/candidate_xxx.jpg)</returns>
    Task<string> SaveMediaAsync(IFormFile file, string? prefix = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lưu trữ hồ sơ ứng tuyển / CV của ứng viên (PDF, DOC, DOCX) vào thư mục uploads/cvs.
    /// </summary>
    /// <param name="file">Tệp CV tải lên</param>
    /// <param name="prefix">Tiền tố tên file (tùy chọn, vd: cv_candidate)</param>
    /// <param name="cancellationToken">Hủy thao tác</param>
    /// <returns>Đường dẫn tương đối phục vụ web (ví dụ: /uploads/cvs/cv_candidate_xxx.pdf)</returns>
    Task<string> SaveCvAsync(IFormFile file, string? prefix = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xóa tệp cục bộ theo đường dẫn tương đối (ví dụ: /uploads/media/xxx.jpg hoặc /uploads/cvs/yyy.pdf).
    /// </summary>
    Task<bool> DeleteFileAsync(string? relativePath);

    /// <summary>
    /// Lấy đường dẫn vật lý trên ổ đĩa từ đường dẫn web tương đối.
    /// </summary>
    string GetPhysicalPath(string relativePath);

    /// <summary>
    /// Kiểm tra xem tệp có tồn tại trên hệ thống cục bộ không.
    /// </summary>
    bool FileExists(string? relativePath);

    /// <summary>
    /// Đảm bảo tất cả các thư mục lưu trữ cục bộ (uploads, uploads/media, uploads/cvs,...) tồn tại trên máy chủ.
    /// </summary>
    void EnsureStorageDirectories();
}
