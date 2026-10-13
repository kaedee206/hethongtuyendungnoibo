using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

/// <summary>
/// Thực thể ghi nhận lịch sử mỗi lần chuyển giai đoạn ứng viên (SCRUM-42).
/// Đảm bảo tính toàn vẹn dữ liệu (Audit Trail) để phân tích tỷ lệ rơi rụng (drop-off rate).
/// </summary>
public class CandidateStageHistory : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Mã hồ sơ ứng tuyển
    /// </summary>
    public Guid ApplicationId { get; set; }

    /// <summary>
    /// Giai đoạn trước khi chuyển (null nếu là giai đoạn khởi tạo ban đầu)
    /// </summary>
    public CandidateStage? FromStage { get; set; }

    /// <summary>
    /// Giai đoạn đích sau khi chuyển
    /// </summary>
    public CandidateStage ToStage { get; set; }

    /// <summary>
    /// Mã lý do loại từ danh mục (Bắt buộc khi ToStage == REJECTED)
    /// </summary>
    public Guid? RejectionReasonId { get; set; }

    /// <summary>
    /// Ghi chú, phản hồi hoặc nhận xét bổ sung của người thực hiện
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Mã người dùng thực hiện thao tác chuyển giai đoạn
    /// </summary>
    public Guid ChangedByUserId { get; set; }

    // Navigation properties
    public Application Application { get; set; } = null!;
    public RejectionReason? RejectionReason { get; set; }
    public User ChangedByUser { get; set; } = null!;
}
