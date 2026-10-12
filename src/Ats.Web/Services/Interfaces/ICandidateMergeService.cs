using Ats.Web.Models.ViewModels.CandidateMerge;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao diện dịch vụ phát hiện và gộp hồ sơ ứng viên trùng lặp (Scrum #40)
/// Đảm bảo tính toàn vẹn dữ liệu, giữ nguyên lịch sử ứng tuyển và hỗ trợ hoàn tác trong 24 giờ
/// </summary>
public interface ICandidateMergeService
{
    /// <summary>
    /// Chuẩn hóa số điện thoại theo định dạng chuẩn Việt Nam (bỏ ký tự lạ, chuyển +84 thành 0)
    /// </summary>
    string NormalizePhoneNumber(string? phone);

    /// <summary>
    /// Quét toàn bộ hệ thống phát hiện các nhóm/cặp ứng viên nghi vấn trùng lặp (theo Email hoặc SĐT)
    /// </summary>
    Task<List<DuplicateCandidateGroupDto>> DetectDuplicatesAsync();

    /// <summary>
    /// Lấy tổng quan các chỉ số trùng lặp và danh sách nhóm trùng cho màn hình quản trị
    /// </summary>
    Task<CandidateDuplicateOverviewViewModel> GetDuplicateOverviewAsync();

    /// <summary>
    /// Kiểm tra xem một ứng viên cụ thể có nghi vấn trùng lặp với ai không
    /// </summary>
    Task<List<DuplicateCandidateItemDto>> FindDuplicatesForCandidateAsync(Guid candidateId);

    /// <summary>
    /// Lấy thông tin chi tiết so sánh cạnh nhau (Side-by-side) của 2 hồ sơ trước khi gộp
    /// </summary>
    Task<CandidateComparisonViewModel?> GetComparisonAsync(Guid primaryId, Guid secondaryId);

    /// <summary>
    /// Thực hiện gộp hồ sơ: chuyển toàn bộ Applications và Resumes từ Secondary sang Primary
    /// </summary>
    Task<(bool Success, string Message, Guid? MergeHistoryId)> MergeCandidatesAsync(
        MergeCandidatesRequestModel model, Guid currentUserId);

    /// <summary>
    /// Hoàn tác phiên gộp hồ sơ nếu còn trong thời hạn 24 giờ
    /// </summary>
    Task<(bool Success, string Message)> UndoMergeAsync(Guid mergeHistoryId, Guid currentUserId);

    /// <summary>
    /// Lấy danh sách lịch sử các phiên gộp hồ sơ kèm trạng thái đếm ngược thời gian hoàn tác
    /// </summary>
    Task<List<CandidateMergeHistoryDto>> GetMergeHistoryAsync();
}
