using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

/// <summary>
/// Mẫu checklist tiếp nhận nhân sự mới (Onboarding Template) theo phòng ban (SP-7).
/// </summary>
public class OnboardingTemplate : BaseEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// Phòng ban áp dụng mẫu này. Nếu null, đây là Mẫu chuẩn dùng chung toàn công ty (Default/General template).
    /// </summary>
    public Guid? DepartmentId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public Department? Department { get; set; }
    public ICollection<OnboardingTemplateItem> Items { get; set; } = new List<OnboardingTemplateItem>();
}
