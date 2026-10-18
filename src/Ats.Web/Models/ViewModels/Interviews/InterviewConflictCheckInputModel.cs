using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.Interviews;

/// <summary>
/// InputModel nhận dữ liệu kiểm tra xung đột lịch phỏng vấn (SCRUM-53).
/// Tuân thủ quy chuẩn: PascalCase, InputModel cho request/form submission.
/// </summary>
public class InterviewConflictCheckInputModel
{
    /// <summary>
    /// Thời gian bắt đầu phỏng vấn dự kiến (hỗ trợ cả UTC và Local DateTime/DateTimeOffset).
    /// </summary>
    [Required(ErrorMessage = "Thời gian bắt đầu phỏng vấn là bắt buộc.")]
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>
    /// Thời gian kết thúc phỏng vấn dự kiến.
    /// </summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>
    /// Thời lượng phỏng vấn tính bằng phút (dùng tính EndTime nếu EndTime không được truyền).
    /// </summary>
    public int? DurationMinutes { get; set; }

    /// <summary>
    /// Danh sách mã người dùng của các thành viên trong Hội đồng phỏng vấn cần kiểm tra trùng.
    /// </summary>
    public List<Guid> InterviewerIds { get; set; } = new();

    /// <summary>
    /// Mã phòng họp được chọn từ danh mục (nếu có).
    /// </summary>
    public Guid? MeetingRoomId { get; set; }

    /// <summary>
    /// Địa điểm hoặc tên phòng họp nhập thủ công (nếu không chọn từ danh mục).
    /// </summary>
    public string? LocationOrLink { get; set; }

    /// <summary>
    /// Mã ID buổi phỏng vấn cần loại trừ (dùng khi cập nhật/đổi lịch cho buổi phỏng vấn hiện có).
    /// </summary>
    public Guid? ExcludeInterviewId { get; set; }
}
