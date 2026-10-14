namespace Ats.Web.Models.Entities;

/// <summary>
/// Thực thể Ứng viên trong Kho Tiềm năng (Scrum 45 - Talent Pool Candidate).
/// Lưu giữ ứng viên giỏi từng trượt vòng cuối hoặc có năng lực nổi bật để tái tuyển dụng.
/// </summary>
public class TalentPoolCandidate : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// ID ứng viên trong hệ thống
    /// </summary>
    public Guid CandidateId { get; set; }

    /// <summary>
    /// Đơn ứng tuyển gốc mà ứng viên từng tham gia (nếu có)
    /// </summary>
    public Guid? SourceApplicationId { get; set; }

    /// <summary>
    /// Vòng tuyển dụng cuối mà ứng viên đã đạt được (vd: "Phỏng vấn chuyên môn", "Đề nghị tuyển dụng (Offer)")
    /// </summary>
    public string? LastStageReached { get; set; }

    /// <summary>
    /// Lý do đưa vào kho tiềm năng (Bắt buộc theo Scrum 45)
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Chức danh quan tâm / phù hợp để tái tuyển dụng (Target Job Title)
    /// </summary>
    public string TargetJobTitle { get; set; } = string.Empty;

    /// <summary>
    /// Kỹ năng chính của ứng viên (cách nhau dấu phẩy hoặc từ khóa chuyên môn)
    /// </summary>
    public string? Skills { get; set; }

    /// <summary>
    /// Số năm kinh nghiệm ước tính
    /// </summary>
    public int? ExperienceYears { get; set; }

    /// <summary>
    /// Đánh giá năng lực của hội đồng / Trưởng phòng nhân sự (1 - 5 sao)
    /// </summary>
    public int? Rating { get; set; }

    /// <summary>
    /// Ghi chú chuyên môn bổ sung
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Trạng thái trong kho: AVAILABLE (Sẵn sàng tiếp cận), CONTACTED (Đã liên hệ), REAPPLIED (Đã ứng tuyển lại), HIRED (Đã tuyển dụng), ARCHIVED (Lưu trữ)
    /// </summary>
    public string Status { get; set; } = "AVAILABLE";

    /// <summary>
    /// ID người đưa vào kho tiềm năng (Trưởng phòng nhân sự / Recruiter)
    /// </summary>
    public Guid AddedByUserId { get; set; }

    /// <summary>
    /// Thời điểm đưa vào kho
    /// </summary>
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Candidate Candidate { get; set; } = null!;
    public Application? SourceApplication { get; set; }
    public User AddedByUser { get; set; } = null!;
}
