namespace Ats.Web.Models.Entities;

/// <summary>
/// Thực thể lưu bộ lọc tìm kiếm hồ sơ ứng viên hay dùng của Recruiter (SCRUM-44).
/// </summary>
public class SavedCandidateFilter : BaseEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CriteriaJson { get; set; } = string.Empty;
    public bool IsDefault { get; set; } = false;

    public User User { get; set; } = null!;
}
