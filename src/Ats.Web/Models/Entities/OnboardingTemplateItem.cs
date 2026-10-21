using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

/// <summary>
/// Từng hạng mục thuộc mẫu Onboarding theo phòng ban (SP-7).
/// </summary>
public class OnboardingTemplateItem : BaseEntity
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public string Title { get; set; } = string.Empty;
    public OnboardingCategory Category { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// Người phụ trách mặc định theo mẫu (nếu có thể xác định trước).
    /// </summary>
    public Guid? DefaultAssignedUserId { get; set; }

    /// <summary>
    /// Số ngày cần hoàn thành trước ngày đi làm đầu tiên (VD: 1 = trước 1 ngày, 0 = đúng ngày đi làm).
    /// </summary>
    public int DaysBeforeJoinDateOffset { get; set; } = 1;

    public int DisplayOrder { get; set; } = 1;

    public OnboardingTemplate Template { get; set; } = null!;
    public User? DefaultAssignedUser { get; set; }
}
