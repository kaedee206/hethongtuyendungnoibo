namespace Ats.Web.Models.ViewModels.Interviews;

/// <summary>
/// Kết quả tổng hợp kiểm tra xung đột lịch phỏng vấn (SCRUM-53).
/// Tuân thủ quy chuẩn: PascalCase, phân tách rõ ràng ViewModel hiển thị.
/// </summary>
public class InterviewConflictCheckResultViewModel
{
    /// <summary>
    /// Đánh dấu có phát hiện xung đột lịch (người phỏng vấn hoặc phòng họp) hay không.
    /// </summary>
    public bool HasConflict { get; set; }

    /// <summary>
    /// Danh sách chi tiết các buổi phỏng vấn bị xung đột giờ.
    /// </summary>
    public List<InterviewConflictItemViewModel> Conflicts { get; set; } = new();

    /// <summary>
    /// Thông điệp tổng quan hoặc cảnh báo trả về cho người dùng.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Số lượng xung đột liên quan đến người phỏng vấn.
    /// </summary>
    public int InterviewerConflictCount => Conflicts.Count(c => c.ConflictType == "INTERVIEWER");

    /// <summary>
    /// Số lượng xung đột liên quan đến phòng họp.
    /// </summary>
    public int RoomConflictCount => Conflicts.Count(c => c.ConflictType == "ROOM");
}
