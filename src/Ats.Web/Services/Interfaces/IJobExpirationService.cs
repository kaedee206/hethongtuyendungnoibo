using Ats.Web.Models.ViewModels.Jobs;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Dịch vụ quét và tự động ẩn các tin tuyển dụng quá hạn nhận hồ sơ (Scrum-38).
/// Hỗ trợ cả tác vụ nền định kỳ hằng ngày (Background Service) và kích hoạt thủ công từ giao diện quản trị.
/// </summary>
public interface IJobExpirationService
{
    /// <summary>
    /// Thực hiện quét toàn bộ tin tuyển dụng trong CSDL và tự động chuyển trạng thái các tin quá hạn sang EXPIRED.
    /// </summary>
    Task<JobExpirationScanResult> ProcessExpiredJobPostingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy thông tin kết quả lần quét tự động gần nhất.
    /// </summary>
    JobExpirationScanResult GetLastScanResult();
}
