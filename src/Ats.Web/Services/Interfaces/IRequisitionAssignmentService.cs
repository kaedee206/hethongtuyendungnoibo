using Ats.Web.Models.ViewModels.RequisitionAssignments;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Service quản lý phân công Recruiter phụ trách Yêu cầu tuyển dụng và ghi lịch sử chuyển giao (Scrum 26).
/// </summary>
public interface IRequisitionAssignmentService
{
    /// <summary>
    /// Lấy danh sách tổng hợp phân công recruiter cho các yêu cầu tuyển dụng kèm KPI.
    /// </summary>
    Task<RequisitionAssignmentListViewModel> GetAssignmentOverviewAsync(
        string? search = null,
        Guid? departmentId = null,
        Guid? recruiterId = null,
        string? assignmentFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy chi tiết phân công của một yêu cầu tuyển dụng để hiển thị Modal phân công.
    /// </summary>
    Task<RequisitionAssignmentItemViewModel?> GetAssignmentDetailAsync(
        Guid requisitionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Thực hiện phân công 1 recruiter chính và nhiều recruiter hỗ trợ.
    /// Tự động ghi lịch sử chuyển giao nếu thay đổi người phụ trách chính hoặc thay đổi hỗ trợ.
    /// </summary>
    Task<(bool Success, string Message)> AssignRecruitersAsync(
        RequisitionAssignRequestModel model,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách lịch sử chuyển giao của một yêu cầu tuyển dụng theo thứ tự thời gian mới nhất.
    /// </summary>
    Task<List<RequisitionHandoverHistoryViewModel>> GetHandoverHistoriesAsync(
        Guid requisitionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách chuyên viên tuyển dụng (Recruiters) đang hoạt động trong hệ thống.
    /// </summary>
    Task<List<RecruiterOptionViewModel>> GetAvailableRecruitersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách RequisitionId mà recruiter cụ thể được phân công (là chính hoặc hỗ trợ).
    /// Phục vụ cơ chế bảo mật: Recruiter chỉ nhìn thấy ứng viên của vị trí được giao.
    /// </summary>
    Task<List<Guid>> GetAssignedRequisitionIdsForRecruiterAsync(
        Guid recruiterUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra recruiter có quyền xem/tương tác với hồ sơ ứng tuyển này hay không.
    /// </summary>
    Task<bool> CanRecruiterAccessApplicationAsync(
        Guid recruiterUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default);
}
