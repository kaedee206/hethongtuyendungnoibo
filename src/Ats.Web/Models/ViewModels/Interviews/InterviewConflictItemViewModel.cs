namespace Ats.Web.Models.ViewModels.Interviews;

/// <summary>
/// ViewModel chứa thông tin chi tiết một xung đột lịch phỏng vấn (SCRUM-53).
/// Tuân thủ quy chuẩn: PascalCase, múi giờ Việt Nam UTC+7 culture vi-VN.
/// </summary>
public class InterviewConflictItemViewModel
{
    /// <summary>
    /// Loại xung đột: "INTERVIEWER" (Trùng người phỏng vấn) hoặc "ROOM" (Trùng phòng họp).
    /// </summary>
    public string ConflictType { get; set; } = string.Empty;

    /// <summary>
    /// Tên của đối tượng bị xung đột (Họ tên người phỏng vấn hoặc Tên phòng họp).
    /// </summary>
    public string ConflictingEntityName { get; set; } = string.Empty;

    /// <summary>
    /// Mã ID của đối tượng bị xung đột (InterviewerId hoặc MeetingRoomId).
    /// </summary>
    public Guid? ConflictingEntityId { get; set; }

    /// <summary>
    /// Họ tên của ứng viên trong buổi phỏng vấn bị trùng lịch.
    /// </summary>
    public string ConflictingCandidateName { get; set; } = string.Empty;

    /// <summary>
    /// Vị trí tuyển dụng tương ứng của buổi phỏng vấn bị trùng lịch.
    /// </summary>
    public string JobTitle { get; set; } = string.Empty;

    /// <summary>
    /// Mã ID của buổi phỏng vấn hiện có bị trùng lịch.
    /// </summary>
    public Guid ConflictingInterviewId { get; set; }

    /// <summary>
    /// Thời gian bắt đầu của buổi phỏng vấn bị trùng (định dạng múi giờ Việt Nam UTC+7: dd/MM/yyyy HH:mm).
    /// </summary>
    public string ConflictingStartTime { get; set; } = string.Empty;

    /// <summary>
    /// Thời gian kết thúc của buổi phỏng vấn bị trùng (định dạng múi giờ Việt Nam UTC+7: dd/MM/yyyy HH:mm).
    /// </summary>
    public string ConflictingEndTime { get; set; } = string.Empty;

    /// <summary>
    /// Khung thời gian hiển thị thân thiện (ví dụ: "14:00 - 15:30 ngày 20/10/2026").
    /// </summary>
    public string TimeRangeDisplay { get; set; } = string.Empty;

    /// <summary>
    /// Thời gian bắt đầu dạng UTC thô phục vụ so khớp logic.
    /// </summary>
    public DateTimeOffset? StartTimeUtc { get; set; }

    /// <summary>
    /// Thời gian kết thúc dạng UTC thô phục vụ so khớp logic.
    /// </summary>
    public DateTimeOffset? EndTimeUtc { get; set; }
}
