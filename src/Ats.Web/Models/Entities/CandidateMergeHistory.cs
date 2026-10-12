namespace Ats.Web.Models.Entities;

/// <summary>
/// Lưu vết các phiên gộp hồ sơ ứng viên trùng lặp (Scrum #40)
/// Cho phép theo dõi chi tiết và hoàn tác an toàn trong vòng 24 giờ
/// </summary>
public class CandidateMergeHistory : BaseEntity
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// ID hồ sơ ứng viên chính được giữ lại (Target/Primary Candidate)
    /// </summary>
    public Guid PrimaryCandidateId { get; set; }
    
    /// <summary>
    /// ID hồ sơ ứng viên phụ bị gộp vào (Source/Secondary Candidate)
    /// </summary>
    public Guid SecondaryCandidateId { get; set; }
    
    /// <summary>
    /// ID nhân sự/recruiter thực hiện gộp hồ sơ
    /// </summary>
    public Guid MergedByUserId { get; set; }
    
    /// <summary>
    /// Thời điểm thực hiện gộp hồ sơ
    /// </summary>
    public DateTimeOffset MergedAt { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// Lý do phát hiện trùng lặp & quyết định gộp (ví dụ: Trùng SĐT 0912345678, Trùng Email)
    /// </summary>
    public string? MergeReason { get; set; }
    
    /// <summary>
    /// Danh sách JSON các Application Id đã chuyển từ Secondary sang Primary
    /// </summary>
    public string MovedApplicationIdsJson { get; set; } = "[]";
    
    /// <summary>
    /// Danh sách JSON các Resume Id đã chuyển từ Secondary sang Primary
    /// </summary>
    public string MovedResumeIdsJson { get; set; } = "[]";
    
    /// <summary>
    /// Snapshot JSON thông tin của Secondary Candidate trước khi gộp
    /// </summary>
    public string? SnapshotSecondaryDataJson { get; set; }
    
    /// <summary>
    /// Đánh dấu phiên gộp đã bị hoàn tác hay chưa
    /// </summary>
    public bool IsUndone { get; set; } = false;
    
    /// <summary>
    /// Thời điểm hoàn tác (bắt buộc trong vòng 24 giờ kể từ MergedAt)
    /// </summary>
    public DateTimeOffset? UndoneAt { get; set; }
    
    /// <summary>
    /// ID người thực hiện hoàn tác
    /// </summary>
    public Guid? UndoneByUserId { get; set; }
    
    /// <summary>
    /// Ghi chú bổ sung khi hoàn tác hoặc xử lý
    /// </summary>
    public string? Notes { get; set; }

    // Navigation properties
    public Candidate PrimaryCandidate { get; set; } = null!;
    public Candidate SecondaryCandidate { get; set; } = null!;
    public User MergedByUser { get; set; } = null!;
    public User? UndoneByUser { get; set; }
}
