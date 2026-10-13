namespace Ats.Web.Models.Enums;

/// <summary>
/// Các giai đoạn tuyển dụng chuẩn hóa của ứng viên (SCRUM-42).
/// Phục vụ theo dõi tiến trình tuyển dụng và báo cáo phân tích tỷ lệ rơi rụng (drop-off rate).
/// </summary>
public enum CandidateStage
{
    /// <summary>
    /// Ứng tuyển mới (Hồ sơ mới nộp vào hệ thống)
    /// </summary>
    APPLIED = 1,

    /// <summary>
    /// Sàng lọc hồ sơ (HR / Recruiter đánh giá sự phù hợp của CV)
    /// </summary>
    SCREENING = 2,

    /// <summary>
    /// Phỏng vấn (Phỏng vấn chuyên môn, kỹ thuật hoặc văn hóa)
    /// </summary>
    INTERVIEW = 3,

    /// <summary>
    /// Đề xuất tuyển dụng (Gửi thư mời nhận việc Offer Letter)
    /// </summary>
    OFFER = 4,

    /// <summary>
    /// Tuyển dụng thành công (Ứng viên nhận offer và gia nhập công ty)
    /// </summary>
    HIRED = 5,

    /// <summary>
    /// Loại hồ sơ (Hồ sơ bị từ chối tại bất kỳ vòng nào kèm lý do chuẩn hóa)
    /// </summary>
    REJECTED = 6
}

/// <summary>
/// Tiện ích mở rộng hỗ trợ hiển thị tên tiếng Việt cho CandidateStage.
/// </summary>
public static class CandidateStageExtensions
{
    public static string GetDisplayName(this CandidateStage stage) => stage switch
    {
        CandidateStage.APPLIED => "Ứng tuyển mới",
        CandidateStage.SCREENING => "Sàng lọc hồ sơ",
        CandidateStage.INTERVIEW => "Phỏng vấn",
        CandidateStage.OFFER => "Đề xuất tuyển dụng (Offer)",
        CandidateStage.HIRED => "Tuyển dụng thành công",
        CandidateStage.REJECTED => "Loại hồ sơ",
        _ => stage.ToString()
    };
}
