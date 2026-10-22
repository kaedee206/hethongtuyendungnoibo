using Ats.Web.Models.DTOs;
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

    /// <summary>
    /// Tìm kiếm và lọc danh sách hồ sơ ứng viên theo nhiều điều kiện động và phân trang (SCRUM-44).
    /// </summary>
    Task<CandidateSearchIndexViewModel> GetFilteredCandidatesAsync(
        CandidateFilterCriteria criteria,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lưu lại tổ hợp điều kiện lọc hay dùng của Recruiter (SCRUM-44).
    /// </summary>
    Task<SavedCandidateFilterDto> SaveFilterAsync(
        Guid currentUserId,
        SaveCandidateFilterInputModel model,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách các bộ lọc đã lưu của Recruiter hiện tại (SCRUM-44).
    /// </summary>
    Task<IReadOnlyList<SavedCandidateFilterDto>> GetSavedFiltersAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xóa bộ lọc đã lưu của Recruiter (SCRUM-44).
    /// </summary>
    Task<bool> DeleteSavedFilterAsync(
        Guid filterId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sinh tệp mẫu Excel nhập danh sách ứng viên hàng loạt (ClosedXML)
    /// </summary>
    Task<byte[]> GenerateCandidateExcelTemplateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc và kiểm tra tính hợp lệ dữ liệu từng dòng ứng viên từ tệp Excel
    /// </summary>
    Task<Ats.Web.Models.DTOs.ImportCandidateResultDto> ValidateCandidateExcelImportAsync(Stream excelStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Thực thi lưu danh sách ứng viên hợp lệ từ Excel vào cơ sở dữ liệu
    /// </summary>
    Task<int> ExecuteCandidateImportAsync(Ats.Web.Models.DTOs.ExecuteCandidateImportRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra trùng lặp ứng viên qua Email hoặc Số điện thoại (SCRUM-47).
    /// Kiểm tra xem ứng viên đã tồn tại chưa và đã có đơn ứng tuyển đang hoạt động ở chính JobPostingId này hay chưa.
    /// </summary>
    Task<CandidateDuplicateCheckResultViewModel> CheckDuplicateCandidateAsync(
        string email, 
        string? phoneNumber, 
        Guid jobPostingId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Thêm ứng viên thủ công vào vị trí tuyển dụng (SCRUM-47).
    /// Hỗ trợ nguồn LinkedIn, Headhunt, Referral, Direct.
    /// Tái sử dụng hồ sơ nếu đã có trong hệ thống, lưu CV an toàn, tạo Application và ghi nhận AuditLog trong transaction.
    /// </summary>
    Task<Guid> AddManualCandidateAsync(
        CandidateManualCreateInputModel input, 
        string recruiterUserId, 
        CancellationToken cancellationToken = default);
}
