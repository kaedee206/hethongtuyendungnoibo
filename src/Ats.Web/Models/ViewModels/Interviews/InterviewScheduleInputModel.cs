using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.Interviews;

/// <summary>
/// InputModel đặt lịch hoặc cập nhật lịch phỏng vấn kèm cơ chế ghi đè trùng lịch (SCRUM-53).
/// Tuân thủ quy chuẩn: PascalCase, InputModel cho form/API request.
/// </summary>
public class InterviewScheduleInputModel
{
    /// <summary>
    /// Mã ID của buổi phỏng vấn (dùng khi cập nhật lịch hiện có).
    /// </summary>
    public Guid? InterviewId { get; set; }

    /// <summary>
    /// Mã ID hồ sơ ứng tuyển của ứng viên.
    /// </summary>
    [Required(ErrorMessage = "Mã hồ sơ ứng tuyển là bắt buộc.")]
    public Guid ApplicationId { get; set; }

    /// <summary>
    /// Thứ tự vòng phỏng vấn (1, 2, 3...).
    /// </summary>
    [Range(1, 10, ErrorMessage = "Vòng phỏng vấn phải từ 1 đến 10.")]
    public int RoundNumber { get; set; } = 1;

    /// <summary>
    /// Tiêu đề hoặc tên vòng phỏng vấn.
    /// </summary>
    public string? RoundTitle { get; set; }

    /// <summary>
    /// Thời gian bắt đầu phỏng vấn.
    /// </summary>
    [Required(ErrorMessage = "Vui lòng chọn ngày giờ bắt đầu phỏng vấn.")]
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>
    /// Thời lượng phỏng vấn (phút). Mặc định là 60 phút.
    /// </summary>
    [Range(15, 480, ErrorMessage = "Thời lượng phỏng vấn phải từ 15 đến 480 phút.")]
    public int DurationMinutes { get; set; } = 60;

    /// <summary>
    /// Thời gian kết thúc phỏng vấn.
    /// </summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>
    /// Hình thức phỏng vấn ("ONLINE_MEET" hoặc "OFFLINE_OFFICE").
    /// </summary>
    public string InterviewType { get; set; } = "ONLINE_MEET";

    /// <summary>
    /// Địa điểm hoặc liên kết phòng họp trực tuyến.
    /// </summary>
    [Required(ErrorMessage = "Vui lòng cung cấp phòng họp hoặc liên kết họp trực tuyến.")]
    public string LocationOrLink { get; set; } = string.Empty;

    /// <summary>
    /// Mã phòng họp được chọn từ danh mục (nếu có).
    /// </summary>
    public Guid? MeetingRoomId { get; set; }

    /// <summary>
    /// Danh sách mã người phỏng vấn được phân công vào Hội đồng.
    /// </summary>
    public List<Guid> InterviewerIds { get; set; } = new();

    /// <summary>
    /// Ghi chú cho buổi phỏng vấn hoặc dặn dò ứng viên.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Đánh dấu người dùng đã chủ động xác nhận ghi đè khi có cảnh báo trùng lịch (Hard Block with Override).
    /// </summary>
    public bool IsOverridden { get; set; } = false;

    /// <summary>
    /// Lý do ghi đè bắt buộc nhập khi IsOverridden = true (tối thiểu 10 ký tự để kiểm toán).
    /// </summary>
    public string? OverrideReason { get; set; }
}
