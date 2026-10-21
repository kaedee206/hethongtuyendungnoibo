using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

/// <summary>
/// Từng hạng mục công việc trong checklist Onboarding của ứng viên (SP-7).
/// </summary>
public class CandidateOnboardingChecklistItem : BaseEntity
{
    public Guid Id { get; set; }
    public Guid ChecklistId { get; set; }
    public string Title { get; set; } = string.Empty;
    public OnboardingCategory Category { get; set; }

    /// <summary>
    /// Người phụ trách thực hiện hạng mục này (chọn từ danh sách nhân viên nội bộ).
    /// </summary>
    public Guid AssignedToUserId { get; set; }

    /// <summary>
    /// Hạn hoàn thành mục (Không được sau ngày đi làm đầu tiên của ứng viên).
    /// </summary>
    public DateOnly DueDate { get; set; }

    /// <summary>
    /// Trạng thái hoàn thành (chưa làm / đã xong).
    /// </summary>
    public bool IsCompleted { get; set; } = false;

    /// <summary>
    /// Thời điểm đánh dấu hoàn thành mục.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Người dùng thực hiện đánh dấu hoàn thành mục.
    /// </summary>
    public Guid? CompletedByUserId { get; set; }

    public CandidateOnboardingChecklist Checklist { get; set; } = null!;
    public User AssignedToUser { get; set; } = null!;
    public User? CompletedByUser { get; set; }
}
