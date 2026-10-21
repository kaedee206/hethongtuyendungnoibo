using System.ComponentModel.DataAnnotations;
using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Onboarding;

/// <summary>
/// ViewModel hiển thị toàn bộ Checklist Onboarding của ứng viên (SP-7).
/// </summary>
public class CandidateOnboardingChecklistViewModel
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string? CandidatePhone { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;

    public DateOnly FirstWorkingDate { get; set; }
    public string FormattedFirstWorkingDate => FirstWorkingDate.ToString("dd/MM/yyyy");

    /// <summary>
    /// Tiến độ hoàn thành (0 - 100%) - Tính toán tập trung tại Service, cấm tính lặp ở View.
    /// </summary>
    public int ProgressPercentage { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string FormattedCompletedAt => CompletedAt.HasValue
        ? CompletedAt.Value.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm")
        : string.Empty;

    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }

    public bool CanInitialize { get; set; }
    public bool CanUpdateItem { get; set; }

    public string StatusBadgeClass => IsCompleted
        ? "bg-success text-white"
        : (ProgressPercentage > 0 ? "bg-primary text-white" : "bg-secondary text-white");

    public string StatusDisplayName => IsCompleted
        ? "Đã onboard"
        : (ProgressPercentage > 0 ? "Đang onboarding" : "Chưa bắt đầu");

    public List<OnboardingItemDto> Items { get; set; } = [];

    /// <summary>
    /// Danh sách các mục được gom theo 4 nhóm chuẩn hóa: Tài khoản, Thiết bị, Chỗ ngồi, Hồ sơ giấy tờ.
    /// </summary>
    public List<OnboardingCategoryGroupDto> CategoryGroups { get; set; } = [];
}

/// <summary>
/// DTO biểu diễn một nhóm hạng mục trong 4 nhóm chuẩn hóa.
/// </summary>
public class OnboardingCategoryGroupDto
{
    public OnboardingCategory Category { get; set; }
    public string CategoryName => Category.GetDisplayName();
    public string CategoryIcon => Category.GetIcon();
    public string CategoryBadgeClass => Category.GetBadgeClass();
    public int TotalCount => Items.Count;
    public int CompletedCount => Items.Count(i => i.IsCompleted);
    public int CategoryProgressPercentage => TotalCount == 0
        ? 0
        : (int)Math.Round((double)CompletedCount * 100 / TotalCount, MidpointRounding.AwayFromZero);

    public List<OnboardingItemDto> Items { get; set; } = [];
}

/// <summary>
/// DTO chi tiết một hạng mục công việc trong checklist.
/// </summary>
public class OnboardingItemDto
{
    public Guid Id { get; set; }
    public Guid ChecklistId { get; set; }
    public string Title { get; set; } = string.Empty;
    public OnboardingCategory Category { get; set; }
    public string CategoryName => Category.GetDisplayName();
    public string CategoryIcon => Category.GetIcon();
    public string CategoryBadgeClass => Category.GetBadgeClass();

    public Guid AssignedToUserId { get; set; }
    public string AssignedToUserName { get; set; } = string.Empty;
    public string AssignedToUserEmail { get; set; } = string.Empty;

    public DateOnly DueDate { get; set; }
    public string FormattedDueDate => DueDate.ToString("dd/MM/yyyy");

    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string FormattedCompletedAt => CompletedAt.HasValue
        ? CompletedAt.Value.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm")
        : string.Empty;

    public Guid? CompletedByUserId { get; set; }
    public string? CompletedByUserName { get; set; }

    /// <summary>
    /// Cho biết người dùng hiện tại có quyền tick hoàn thành mục này hay không.
    /// (HR Manager hoặc chính người được phân công phụ trách mục).
    /// </summary>
    public bool CanToggle { get; set; }
}

/// <summary>
/// DTO phục vụ lấy dữ liệu mẫu checklist phòng ban khi mở modal khởi tạo.
/// </summary>
public class OnboardingTemplateLookupDto
{
    public Guid TemplateId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public List<OnboardingTemplateItemDto> Items { get; set; } = [];
}

/// <summary>
/// DTO biểu diễn một mục trong mẫu checklist phòng ban.
/// </summary>
public class OnboardingTemplateItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public OnboardingCategory Category { get; set; }
    public string CategoryName => Category.GetDisplayName();
    public Guid? DefaultAssignedUserId { get; set; }
    public string? DefaultAssignedUserName { get; set; }
    public int DaysBeforeJoinDateOffset { get; set; } = 1;
    public int DisplayOrder { get; set; } = 1;
}

/// <summary>
/// Model tiếp nhận dữ liệu khởi tạo Checklist Onboarding từ Trưởng phòng Nhân sự (SP-7).
/// </summary>
public class CreateOnboardingChecklistInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Mã hồ sơ ứng tuyển (ApplicationId) là bắt buộc.")]
    public Guid ApplicationId { get; set; }

    [Required(ErrorMessage = "Phòng ban tiếp nhận là bắt buộc.")]
    public Guid DepartmentId { get; set; }

    [Required(ErrorMessage = "Ngày đi làm đầu tiên là bắt buộc.")]
    public DateOnly FirstWorkingDate { get; set; }

    public List<CreateOnboardingChecklistItemInputModel> Items { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ApplicationId == Guid.Empty)
        {
            yield return new ValidationResult("Mã hồ sơ ứng tuyển không hợp lệ.", [nameof(ApplicationId)]);
        }

        if (DepartmentId == Guid.Empty)
        {
            yield return new ValidationResult("Mã phòng ban không hợp lệ.", [nameof(DepartmentId)]);
        }

        if (FirstWorkingDate == default)
        {
            yield return new ValidationResult("Ngày đi làm đầu tiên không hợp lệ.", [nameof(FirstWorkingDate)]);
        }

        if (Items == null || Items.Count == 0)
        {
            yield return new ValidationResult("Checklist phải có ít nhất một hạng mục công việc.", [nameof(Items)]);
            yield break;
        }

        for (int i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            if (string.IsNullOrWhiteSpace(item.Title))
            {
                yield return new ValidationResult($"Mục thứ {i + 1}: Tên hạng mục không được để trống.", [$"Items[{i}].Title"]);
            }

            if (item.AssignedToUserId == Guid.Empty)
            {
                yield return new ValidationResult($"Mục thứ {i + 1} ({item.Title}): Bắt buộc phải chọn người phụ trách.", [$"Items[{i}].AssignedToUserId"]);
            }

            if (item.DueDate == default)
            {
                yield return new ValidationResult($"Mục thứ {i + 1} ({item.Title}): Bắt buộc phải chọn hạn hoàn thành.", [$"Items[{i}].DueDate"]);
            }
            else if (item.DueDate > FirstWorkingDate)
            {
                yield return new ValidationResult(
                    $"Mục thứ {i + 1} ({item.Title}): Hạn hoàn thành ({item.DueDate:dd/MM/yyyy}) không được sau ngày đi làm đầu tiên ({FirstWorkingDate:dd/MM/yyyy}).",
                    [$"Items[{i}].DueDate"]);
            }
        }
    }
}

/// <summary>
/// Model tiếp nhận thông tin từng mục khi khởi tạo checklist.
/// </summary>
public class CreateOnboardingChecklistItemInputModel
{
    [Required(ErrorMessage = "Tên hạng mục không được để trống.")]
    [StringLength(250, ErrorMessage = "Tên hạng mục tối đa 250 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhóm hạng mục là bắt buộc.")]
    public OnboardingCategory Category { get; set; }

    [Required(ErrorMessage = "Người phụ trách là bắt buộc.")]
    public Guid AssignedToUserId { get; set; }

    [Required(ErrorMessage = "Hạn hoàn thành là bắt buộc.")]
    public DateOnly DueDate { get; set; }
}

/// <summary>
/// Model tiếp nhận thao tác đánh dấu hoàn thành / bỏ hoàn thành 1 mục.
/// </summary>
public class ToggleOnboardingItemInputModel
{
    [Required(ErrorMessage = "Mã mục checklist là bắt buộc.")]
    public Guid ItemId { get; set; }

    public bool IsCompleted { get; set; }
}

/// <summary>
/// ViewModel nạp dữ liệu modal khởi tạo checklist Onboarding (SP-7).
/// </summary>
public class PrepareCreateOnboardingChecklistModalViewModel
{
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public DateOnly FirstWorkingDate { get; set; }
    public OnboardingTemplateLookupDto Template { get; set; } = new();
    public List<UserLookupDto> AvailableUsers { get; set; } = [];
}

/// <summary>
/// DTO người dùng phục vụ phân công công việc trong modal checklist.
/// </summary>
public class UserLookupDto
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
}
