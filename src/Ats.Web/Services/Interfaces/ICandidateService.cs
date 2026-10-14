using Ats.Web.Models.ViewModels.Candidates;

namespace Ats.Web.Services.Interfaces;

public interface ICandidateService
{
    /// <summary>
    /// Lấy toàn bộ hồ sơ hợp nhất (360-degree Candidate Dossier) bao gồm thông tin cá nhân,
    /// danh sách CV xem trước, toàn bộ lịch sử ứng tuyển qua các thời kỳ, nhãn và ghi chú nội bộ.
    /// Kiểm tra chặt chẽ quyền hạn: Chỉ Recruiter phụ trách hoặc Hiring Manager của vị trí ứng viên nộp đơn mới có quyền xem.
    /// </summary>
    Task<CandidateDetailsViewModel?> GetUnifiedProfileAsync(
        Guid candidateId, 
        Guid currentUserId, 
        string currentUserRole, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra quyền truy cập hồ sơ hợp nhất của người dùng hiện tại đối với ứng viên.
    /// </summary>
    Task<bool> CanAccessCandidateAsync(
        Guid candidateId, 
        Guid currentUserId, 
        string currentUserRole, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách hồ sơ ứng viên trong hệ thống hỗ trợ tìm kiếm và phân trang.
    /// </summary>
    Task<IReadOnlyList<CandidateListViewModel>> GetAllAsync(
        string? search = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Thêm nhãn (Tag / Label) phân loại ứng viên.
    /// </summary>
    Task<CandidateTagViewModel> AddTagAsync(
        Guid candidateId, 
        string tagName, 
        string? colorCode, 
        Guid currentUserId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xóa nhãn (Tag / Label) khỏi ứng viên.
    /// </summary>
    Task<bool> RemoveTagAsync(
        Guid tagId, 
        Guid currentUserId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Thêm ghi chú nội bộ (Recruitment Note) cho ứng viên.
    /// </summary>
    Task<CandidateNoteViewModel> AddNoteAsync(
        Guid candidateId, 
        string content, 
        string noteType, 
        Guid currentUserId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xóa ghi chú nội bộ của ứng viên.
    /// </summary>
    Task<bool> DeleteNoteAsync(
        Guid noteId, 
        Guid currentUserId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy thông tin tệp CV để tải xuống hoặc xem trực tiếp an toàn.
    /// </summary>
    Task<(string PhysicalPath, string FileName, string MimeType)?> GetResumeFileAsync(
        Guid resumeId, 
        Guid currentUserId, 
        string currentUserRole, 
        CancellationToken cancellationToken = default);
}
