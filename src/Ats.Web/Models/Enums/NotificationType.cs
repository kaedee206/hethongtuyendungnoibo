namespace Ats.Web.Models.Enums;

/// <summary>
/// Phân loại thông báo nội bộ (In-App Notification) theo User Story SP-8.
/// </summary>
public enum NotificationType
{
    /// <summary>
    /// Có yêu cầu chờ duyệt (Yêu cầu tuyển dụng / Đề xuất Offer).
    /// </summary>
    ApprovalPending,

    /// <summary>
    /// Được phân công phụ trách vị trí mới (Assign Recruiter/Manager).
    /// </summary>
    JobAssigned,

    /// <summary>
    /// Được mời tham gia hội đồng phỏng vấn (Interview Panelist invitation).
    /// </summary>
    InterviewInvitation,

    /// <summary>
    /// Ứng viên phản hồi Offer (Đồng ý, Từ chối, Thương lượng lại).
    /// </summary>
    OfferResponse,

    /// <summary>
    /// Thông báo hệ thống chung (SLA, nhắc việc, nhắc hạn).
    /// </summary>
    System
}
