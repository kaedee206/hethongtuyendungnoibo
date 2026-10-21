using Ats.Web.Models.ViewModels.Onboarding;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao diện dịch vụ quản trị Checklist Onboarding cho người đã nhận việc (SP-7).
/// </summary>
public interface IOnboardingChecklistService
{
    /// <summary>
    /// Lấy chi tiết checklist onboarding của hồ sơ ứng tuyển kèm phân quyền thao tác của người dùng hiện tại.
    /// </summary>
    Task<CandidateOnboardingChecklistViewModel?> GetChecklistByApplicationIdAsync(
        Guid applicationId,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy dữ liệu mẫu checklist theo phòng ban (hoặc fallback về mẫu dùng chung nếu phòng ban chưa có mẫu riêng).
    /// </summary>
    Task<OnboardingTemplateLookupDto> GetTemplateForDepartmentAsync(
        Guid departmentId,
        DateOnly? firstWorkingDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Khởi tạo checklist Onboarding cho ứng viên đã nhận việc (chỉ Trưởng phòng Nhân sự - HR Manager).
    /// Sao chép các mục từ mẫu phòng ban thành checklist riêng biệt của ứng viên mà không làm đổi mẫu gốc.
    /// </summary>
    Task<CandidateOnboardingChecklistViewModel> InitializeChecklistAsync(
        CreateOnboardingChecklistInputModel model,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật trạng thái hoàn thành (tick / bỏ tick) cho một mục trong checklist.
    /// Tính tiến độ % tập trung:
    /// - Khi đạt 100%: Tự động chuyển ứng viên sang "Đã onboard" (CandidateStage.ONBOARDED) trong cùng transaction.
    /// - Khi bị bỏ tick (&lt; 100%): Tự động hoàn tác trạng thái ứng viên về "Tuyển dụng thành công" (CandidateStage.HIRED).
    /// </summary>
    Task<CandidateOnboardingChecklistViewModel> ToggleChecklistItemAsync(
        ToggleOnboardingItemInputModel model,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra xem hồ sơ ứng tuyển đã có checklist onboarding đang hoạt động hay chưa (chặn tạo trùng).
    /// </summary>
    Task<bool> HasActiveChecklistAsync(Guid applicationId, CancellationToken cancellationToken = default);
}
