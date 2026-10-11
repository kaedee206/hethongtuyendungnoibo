namespace Ats.Web.Models.Enums;

/// <summary>
/// 4 mức trạng thái tổng quát hiển thị công khai cho ứng viên tra cứu (Scrum #35)
/// Tuyệt đối không để lộ trạng thái chi tiết, điểm đánh giá hay ghi chú nội bộ.
/// </summary>
public enum PublicApplicationStatus
{
    Received = 1,        // Đã nhận
    UnderReview = 2,     // Đang xem xét
    InterviewInvited = 3,// Mời phỏng vấn
    Concluded = 4        // Kết thúc
}
