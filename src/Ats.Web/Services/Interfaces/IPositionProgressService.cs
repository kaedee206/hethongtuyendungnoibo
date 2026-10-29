using Ats.Web.Models.ViewModels.Requisitions;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Service nghiệp vụ theo dõi tiến độ tuyển dụng vị trí sở hữu của Trưởng bộ phận (SCRUM-351).
/// </summary>
public interface IPositionProgressService
{
    /// <summary>
    /// Lấy danh sách tóm tắt tất cả các vị trí do Trưởng bộ phận sở hữu (kèm số ngày còn lại và cảnh báo).
    /// </summary>
    Task<List<PositionProgressSummaryDto>> GetOwnedPositionsSummaryAsync(
        Guid hiringManagerId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy chi tiết toàn diện tiến độ tuyển dụng của một vị trí cụ thể (hoặc vị trí mặc định đầu tiên nếu null).
    /// </summary>
    Task<PositionRecruitmentProgressViewModel?> GetPositionProgressDetailAsync(
        Guid? requisitionId, 
        Guid currentUserId, 
        bool isElevatedRole = false, 
        CancellationToken cancellationToken = default);
}
