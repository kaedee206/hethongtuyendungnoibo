namespace Ats.Web.Models.Enums;

/// <summary>
/// Các phân loại sự kiện thông báo nội bộ cho nhân sự (SCRUM-349).
/// </summary>
public enum NotificationType
{
    /// <summary>Nhận hồ sơ ứng tuyển mới từ ứng viên</summary>
    NEW_APPLICATION = 1,

    /// <summary>Lịch phỏng vấn mới được xếp hoặc thay đổi giờ</summary>
    INTERVIEW_SCHEDULED = 2,

    /// <summary>Thành viên hội đồng hoàn tất gửi đánh giá phỏng vấn</summary>
    INTERVIEW_EVALUATION_SUBMITTED = 3,

    /// <summary>Yêu cầu phê duyệt đề xuất tuyển dụng hoặc yêu cầu tuyển dụng (BẮT BUỘC, KHÔNG THỂ TẮT)</summary>
    APPROVAL_REQUEST = 4,

    /// <summary>Cảnh báo hồ sơ ứng viên vượt quá hạn cam kết SLA</summary>
    SLA_OVERDUE_WARNING = 5,

    /// <summary>Trạng thái hoặc giai đoạn của ứng viên có sự thay đổi</summary>
    STAGE_CHANGED = 6
}
