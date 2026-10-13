using Ats.Web.Models.DTOs.Sla;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Service quản lý cấu hình ngưỡng SLA và kiểm tra quá hạn SLA hồ sơ ứng viên (SCRUM-327).
/// </summary>
public interface ISlaService
{
    /// <summary>
    /// Lấy danh sách cấu hình SLA của tất cả các giai đoạn kèm thống kê số hồ sơ đang xử lý và số hồ sơ quá hạn.
    /// </summary>
    Task<List<PipelineStageSlaDto>> GetStageSlaConfigsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật ngưỡng số ngày SLA và trạng thái bật/tắt cho các giai đoạn tuyển dụng.
    /// </summary>
    Task<bool> UpdateStageSlaConfigsAsync(List<UpdateStageSlaDto> configs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tác vụ quét toàn bộ hồ sơ đang hoạt động, phát hiện hồ sơ quá hạn SLA, gắn cờ và gửi thông báo nhắc việc cho Recruiter (chống nhắc trùng trong cùng ngày).
    /// </summary>
    Task<SlaScanResultDto> ScanAndAlertOverdueApplicationsAsync(CancellationToken cancellationToken = default);
}
