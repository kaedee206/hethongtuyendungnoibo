using Ats.Web.Models.Entities;
using Ats.Web.Models.ViewModels.JobPostings;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Service quản lý nghiệp vụ đăng tin tuyển dụng từ yêu cầu tuyển dụng (SCRUM-29).
/// </summary>
public interface IJobPostingService
{
    /// <summary>
    /// Lấy dữ liệu kế thừa từ yêu cầu tuyển dụng đã được duyệt để truyền sang form soạn thảo tin tuyển dụng.
    /// Bắt buộc Requisition phải có trạng thái APPROVED.
    /// </summary>
    Task<JobPostingPreFillViewModel> GetJobPostingPreFillAsync(Guid requisitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo tin tuyển dụng mới từ yêu cầu tuyển dụng đã duyệt.
    /// Kiểm tra quyền Recruiter/Admin, mở transaction, lưu entity, ghi nhận AuditLog và commit an toàn.
    /// </summary>
    Task<JobPosting> CreateJobPostingFromRequisitionAsync(JobPostingCreateInputModel input, string createdByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra người dùng có quyền Recruiter hoặc Admin hay không.
    /// </summary>
    Task<bool> CanUserCreateJobPostingAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy chi tiết tin tuyển dụng theo ID.
    /// </summary>
    Task<JobPosting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách các tin tuyển dụng đang mở tiếp nhận hồ sơ (PUBLISHED) phục vụ chọn vị trí khi thêm ứng viên thủ công.
    /// </summary>
    Task<IReadOnlyList<JobPosting>> GetPublishedJobPostingsAsync(CancellationToken cancellationToken = default);
}
