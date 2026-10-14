namespace Ats.Web.Models.Enums;

/// <summary>
/// Trạng thái bài đăng tuyển dụng trên cổng việc làm (Job Posting Lifecycle).
/// Tuân thủ quy trình Scrum-38: Gỡ tin thủ công (CLOSED) & Tự động hết hạn (EXPIRED).
/// </summary>
public enum JobPostingStatus
{
    /// <summary>
    /// Bản nháp, chưa công bố ngoài cổng.
    /// </summary>
    DRAFT = 0,

    /// <summary>
    /// Đang đăng tuyển, hiển thị công khai trên Cổng việc làm.
    /// </summary>
    PUBLISHED = 1,

    /// <summary>
    /// Đã gỡ tin thủ công bởi Recruiter/HR/Admin. Tin biến mất khỏi cổng công khai ngay lập tức.
    /// </summary>
    CLOSED = 2,

    /// <summary>
    /// Đã quá hạn nhận hồ sơ, tự động ẩn bởi tác vụ định kỳ hằng ngày của hệ thống.
    /// </summary>
    EXPIRED = 3
}
