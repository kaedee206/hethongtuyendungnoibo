namespace Ats.Web.Models.Enums;

/// <summary>
/// 4 nhóm hạng mục chuẩn hóa trong quy trình Onboarding (SP-7).
/// </summary>
public enum OnboardingCategory
{
    /// <summary>
    /// Tài khoản (Email, VPN, Jira, Slack, Git, Phần mềm nghiệp vụ...)
    /// </summary>
    ACCOUNT = 1,

    /// <summary>
    /// Thiết bị (Laptop, Màn hình, Bàn phím, Chuột, Tai nghe...)
    /// </summary>
    EQUIPMENT = 2,

    /// <summary>
    /// Chỗ ngồi (Bàn làm việc, Thẻ nhân viên ra vào, Tủ đồ cá nhân...)
    /// </summary>
    SEATING = 3,

    /// <summary>
    /// Hồ sơ giấy tờ (Hợp đồng lao động, Bằng cấp công chứng, Sơ yếu lý lịch, Thỏa thuận bảo mật NDA...)
    /// </summary>
    DOCUMENT = 4
}

/// <summary>
/// Các phương thức mở rộng hỗ trợ hiển thị UI cho OnboardingCategory.
/// </summary>
public static class OnboardingCategoryExtensions
{
    public static string GetDisplayName(this OnboardingCategory category) => category switch
    {
        OnboardingCategory.ACCOUNT => "Tài khoản",
        OnboardingCategory.EQUIPMENT => "Thiết bị",
        OnboardingCategory.SEATING => "Chỗ ngồi",
        OnboardingCategory.DOCUMENT => "Hồ sơ giấy tờ",
        _ => category.ToString()
    };

    public static string GetIcon(this OnboardingCategory category) => category switch
    {
        OnboardingCategory.ACCOUNT => "bi-key",
        OnboardingCategory.EQUIPMENT => "bi-laptop",
        OnboardingCategory.SEATING => "bi-geo-alt",
        OnboardingCategory.DOCUMENT => "bi-file-earmark-text",
        _ => "bi-check2-square"
    };

    public static string GetBadgeClass(this OnboardingCategory category) => category switch
    {
        OnboardingCategory.ACCOUNT => "bg-primary-subtle text-primary border border-primary-subtle",
        OnboardingCategory.EQUIPMENT => "bg-info-subtle text-info border border-info-subtle",
        OnboardingCategory.SEATING => "bg-warning-subtle text-warning-emphasis border border-warning-subtle",
        OnboardingCategory.DOCUMENT => "bg-success-subtle text-success border border-success-subtle",
        _ => "bg-secondary-subtle text-secondary"
    };
}
