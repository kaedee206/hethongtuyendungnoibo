namespace Ats.Web.Models.Entities;

/// <summary>
/// Checklist tiếp nhận nhân sự (Onboarding) thực tế của một ứng viên đã nhận việc (SP-7).
/// Mỗi hồ sơ ứng tuyển (Application) chỉ có tối đa 1 checklist đang hoạt động.
/// </summary>
public class CandidateOnboardingChecklist : BaseEntity
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public Guid DepartmentId { get; set; }

    /// <summary>
    /// Ngày đi làm đầu tiên của ứng viên mới.
    /// </summary>
    public DateOnly FirstWorkingDate { get; set; }

    /// <summary>
    /// Tiến độ hoàn thành tính theo phần trăm (0 - 100%).
    /// </summary>
    public int ProgressPercentage { get; set; } = 0;

    /// <summary>
    /// Cờ đánh dấu đã hoàn thành toàn bộ 100% checklist.
    /// </summary>
    public bool IsCompleted { get; set; } = false;

    /// <summary>
    /// Thời điểm hoàn tất 100% checklist onboarding.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public Application Application { get; set; } = null!;
    public Candidate Candidate { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public ICollection<CandidateOnboardingChecklistItem> Items { get; set; } = new List<CandidateOnboardingChecklistItem>();
}
