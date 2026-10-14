using Ats.Web.Models.Entities;
using Ats.Web.Models.ViewModels.TalentPool;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao diện dịch vụ quản lý Kho Ứng viên Tiềm năng (Scrum 45 - Talent Pool Management).
/// </summary>
public interface ITalentPoolService
{
    /// <summary>
    /// Lấy danh sách ứng viên trong kho tiềm năng kèm bộ lọc đa chiều và thống kê KPI.
    /// </summary>
    Task<TalentPoolListViewModel> GetTalentPoolListAsync(
        string? search = null,
        string? jobTitle = null,
        string? skill = null,
        string? status = null,
        int? minRating = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gợi ý danh sách ứng viên phù hợp từ kho tiềm năng cho một vị trí tuyển dụng mới mở.
    /// </summary>
    Task<TalentPoolSuggestionListViewModel?> GetSuggestedCandidatesForJobAsync(
        Guid jobPostingId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đánh dấu và đưa một ứng viên vào kho tiềm năng kèm lý do chi tiết.
    /// </summary>
    Task<(bool Success, string Message, Guid? Id)> AddCandidateToPoolAsync(
        AddTalentPoolCandidateInputModel model,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật thông tin chuyên môn, trạng thái hoặc ghi chú của ứng viên trong kho.
    /// </summary>
    Task<(bool Success, string Message)> UpdateCandidateInPoolAsync(
        UpdateTalentPoolCandidateInputModel model,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gỡ bỏ ứng viên khỏi kho tiềm năng (Soft delete).
    /// </summary>
    Task<(bool Success, string Message)> RemoveCandidateFromPoolAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mời ứng viên tiềm năng vào một vị trí tuyển dụng mới (tạo đơn nộp mới tự động).
    /// </summary>
    Task<(bool Success, string Message, Guid? NewApplicationId)> InviteCandidateToJobAsync(
        InviteTalentPoolCandidateInputModel model,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách các vị trí tuyển dụng đang mở.
    /// </summary>
    Task<List<OpenJobOptionViewModel>> GetOpenJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra ứng viên đã có trong kho tiềm năng hay chưa.
    /// </summary>
    Task<bool> IsCandidateInPoolAsync(Guid candidateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy thông tin ứng viên trong kho theo ID.
    /// </summary>
    Task<TalentPoolCandidate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
