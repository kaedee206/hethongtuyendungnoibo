namespace Ats.Web.Models.Entities;

/// <summary>
/// Lưu vết lịch sử chuyển giao khi thay đổi người phụ trách yêu cầu tuyển dụng (Scrum 26).
/// Ghi nhận rõ người cũ, người mới, lý do chuyển giao và ghi chú bàn giao công việc.
/// </summary>
public class RequisitionHandoverHistory : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequisitionId { get; set; }
    
    /// <summary>
    /// Người phụ trách cũ (null nếu là lần phân công đầu tiên)
    /// </summary>
    public Guid? FromRecruiterId { get; set; }
    
    /// <summary>
    /// Người phụ trách mới tiếp nhận
    /// </summary>
    public Guid ToRecruiterId { get; set; }
    
    /// <summary>
    /// Loại chuyển giao: PRIMARY_TRANSFER (Đổi người phụ trách chính), SUPPORT_ADDED (Thêm hỗ trợ), SUPPORT_REMOVED (Bỏ hỗ trợ)
    /// </summary>
    public string HandoverType { get; set; } = "PRIMARY_TRANSFER";
    
    /// <summary>
    /// Lý do chuyển giao khi đổi người phụ trách (bắt buộc theo quy định nghiệp vụ)
    /// </summary>
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// Ghi chú bàn giao công việc / tiến độ ứng viên
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Người thực hiện chuyển giao (Trưởng phòng Nhân sự / Admin)
    /// </summary>
    public Guid TransferredById { get; set; }
    public DateTimeOffset TransferredAt { get; set; } = DateTimeOffset.UtcNow;

    public JobRequisition Requisition { get; set; } = null!;
    public User? FromRecruiter { get; set; }
    public User ToRecruiter { get; set; } = null!;
    public User TransferredBy { get; set; } = null!;
}
