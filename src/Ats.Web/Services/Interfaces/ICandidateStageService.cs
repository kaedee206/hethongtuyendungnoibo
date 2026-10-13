using Ats.Web.Models.DTOs;
using Ats.Web.Models.ViewModels.Candidates;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Service quản lý chuyển giai đoạn ứng viên & ghi nhận lý do khi loại hồ sơ (SCRUM-42).
/// Cung cấp các thao tác cập nhật giai đoạn, lưu vết lịch sử bất biến và tra cứu danh mục lý do loại.
/// </summary>
public interface ICandidateStageService
{
    /// <summary>
    /// Lấy danh sách các lý do loại hồ sơ đang kích hoạt (IsActive == true) để hiển thị dropdown lựa chọn.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Danh sách các lý do loại hợp lệ</returns>
    Task<IReadOnlyList<RejectionReasonLookupDto>> GetActiveRejectionReasonsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy toàn bộ lịch sử chuyển giai đoạn của một hồ sơ ứng tuyển theo thứ tự thời gian tăng dần.
    /// </summary>
    /// <param name="applicationId">Mã hồ sơ ứng tuyển</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Danh sách lịch sử chuyển giai đoạn</returns>
    Task<IReadOnlyList<CandidateStageHistoryDto>> GetStageHistoryAsync(Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Thực hiện chuyển giai đoạn ứng viên, ghi nhận lịch sử và thực thi toàn bộ quy tắc nghiệp vụ:
    /// 1. Bắt buộc chọn RejectionReasonId hợp lệ đang kích hoạt khi chuyển sang REJECTED.
    /// 2. Ghi nhật ký lịch sử CandidateStageHistory trong transaction tường minh theo chuẩn UTC.
    /// 3. Rào chắn chuyển sang OFFER: phải qua INTERVIEW, trừ khi có quyền HRManager và xác nhận SkipInterviewConfirmedByHrManager.
    /// </summary>
    /// <param name="model">Dữ liệu yêu cầu chuyển giai đoạn</param>
    /// <param name="currentUserId">Mã định danh người dùng thực hiện thao tác</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Bản ghi lịch sử chuyển giai đoạn vừa tạo</returns>
    Task<CandidateStageHistoryDto> TransitionStageAsync(
        TransitionCandidateStageInputModel model,
        Guid currentUserId,
        CancellationToken cancellationToken = default);
}
