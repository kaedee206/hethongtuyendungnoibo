using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.DTOs;

/// <summary>
/// Model nhận dữ liệu từ form/request đặt lịch phỏng vấn (Scrum - Số 51).
/// Hỗ trợ cả FromForm và FromBody JSON, tương thích các alias tên trường.
/// </summary>
public class ScheduleInterviewInputModel
{
    [Required(ErrorMessage = "Mã hồ sơ ứng tuyển là bắt buộc.")]
    public Guid ApplicationId { get; set; }

    [Range(1, 10, ErrorMessage = "Vòng phỏng vấn phải từ 1 đến 10.")]
    public int RoundNumber { get; set; } = 1;

    public string? RoundTitle { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày giờ bắt đầu phỏng vấn.")]
    public DateTime? StartTime { get; set; }

    [Range(15, 480, ErrorMessage = "Thời lượng phỏng vấn phải từ 15 đến 480 phút.")]
    public int DurationMinutes { get; set; } = 60;

    public DateTime? EndTime { get; set; }

    public string InterviewType { get; set; } = "ONLINE_MEET";

    [Required(ErrorMessage = "Vui lòng cung cấp phòng họp hoặc liên kết họp trực tuyến.")]
    public string LocationOrLink { get; set; } = string.Empty;

    public List<Guid>? InterviewerIds { get; set; } = new();

    public List<Guid>? PanelistIds { get; set; }

    public string? Note { get; set; }

    public string? Notes { get; set; }

    public DateTime? ScheduledDate { get; set; }

    /// <summary>
    /// Chuẩn hóa danh sách ID người phỏng vấn được chọn từ cả 2 alias (InterviewerIds / PanelistIds).
    /// </summary>
    public List<Guid> GetNormalizedInterviewerIds()
    {
        var list = new List<Guid>();
        if (InterviewerIds != null && InterviewerIds.Any())
        {
            list.AddRange(InterviewerIds.Where(id => id != Guid.Empty));
        }
        if (PanelistIds != null && PanelistIds.Any())
        {
            list.AddRange(PanelistIds.Where(id => id != Guid.Empty));
        }
        return list.Distinct().ToList();
    }

    /// <summary>
    /// Lấy thời điểm bắt đầu chuẩn hóa từ StartTime hoặc ScheduledDate.
    /// </summary>
    public DateTime? GetNormalizedStartTime()
    {
        return StartTime ?? ScheduledDate;
    }

    /// <summary>
    /// Lấy ghi chú chuẩn hóa từ Note hoặc Notes.
    /// </summary>
    public string? GetNormalizedNotes()
    {
        return !string.IsNullOrWhiteSpace(Note) ? Note.Trim() : Notes?.Trim();
    }
}

/// <summary>
/// Kết quả phản hồi sau khi hoàn tất đặt lịch phỏng vấn (Scrum - Số 51).
/// </summary>
public class InterviewScheduleResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? InterviewId { get; set; }
    public int RoundNumber { get; set; }
    public string? Title { get; set; }
    public string? StartTimeDisplay { get; set; }
    public string? EndTimeDisplay { get; set; }
    public string? InterviewTypeDisplay { get; set; }
    public string? LocationOrLink { get; set; }
    public string? Panelists { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// DTO chứa thông tin nhân sự nội bộ có thể phân công phỏng vấn.
/// </summary>
public class InterviewerLookupDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string DisplayText => $"{FullName} ({Role}) - {Email}";
}

/// <summary>
/// Model nhận dữ liệu từ form/request dời lịch phỏng vấn (Scrum - Số 55).
/// Cho phép đổi ngày giờ, hình thức, liên kết và danh sách người phỏng vấn.
/// </summary>
public class RescheduleInterviewInputModel
{
    [Required(ErrorMessage = "Mã buổi phỏng vấn là bắt buộc.")]
    public Guid InterviewId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày giờ bắt đầu mới.")]
    public DateTime? NewStartTime { get; set; }

    public DateTime? StartTime { get; set; }

    [Range(15, 480, ErrorMessage = "Thời lượng phỏng vấn phải từ 15 đến 480 phút.")]
    public int DurationMinutes { get; set; } = 60;

    public DateTime? NewEndTime { get; set; }

    public DateTime? EndTime { get; set; }

    public string InterviewType { get; set; } = "ONLINE_MEET";

    [Required(ErrorMessage = "Vui lòng cung cấp phòng họp hoặc liên kết họp mới.")]
    public string LocationOrLink { get; set; } = string.Empty;

    public List<Guid>? InterviewerIds { get; set; } = new();

    public List<Guid>? PanelistIds { get; set; }

    public string? Reason { get; set; }

    public string? RescheduleReason { get; set; }

    public string? Notes { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Lấy thời gian bắt đầu mới chuẩn hóa từ NewStartTime hoặc StartTime.
    /// </summary>
    public DateTime? GetNormalizedStartTime()
    {
        return NewStartTime ?? StartTime;
    }

    /// <summary>
    /// Chuẩn hóa danh sách ID người phỏng vấn được chọn từ cả 2 alias (InterviewerIds / PanelistIds).
    /// </summary>
    public List<Guid> GetNormalizedInterviewerIds()
    {
        var list = new List<Guid>();
        if (InterviewerIds != null && InterviewerIds.Any())
        {
            list.AddRange(InterviewerIds.Where(id => id != Guid.Empty));
        }
        if (PanelistIds != null && PanelistIds.Any())
        {
            list.AddRange(PanelistIds.Where(id => id != Guid.Empty));
        }
        return list.Distinct().ToList();
    }

    /// <summary>
    /// Lấy lý do dời lịch chuẩn hóa từ Reason hoặc RescheduleReason.
    /// </summary>
    public string? GetNormalizedReason()
    {
        return !string.IsNullOrWhiteSpace(Reason) ? Reason.Trim() : RescheduleReason?.Trim();
    }

    /// <summary>
    /// Lấy ghi chú chuẩn hóa từ Notes hoặc Note.
    /// </summary>
    public string? GetNormalizedNotes()
    {
        return !string.IsNullOrWhiteSpace(Notes) ? Notes.Trim() : Note?.Trim();
    }
}

/// <summary>
/// Model nhận dữ liệu huỷ buổi phỏng vấn (Scrum - Số 55).
/// Bắt buộc nhập lý do huỷ rõ ràng để thông báo cho ứng viên và hội đồng.
/// </summary>
public class CancelInterviewInputModel
{
    [Required(ErrorMessage = "Mã buổi phỏng vấn là bắt buộc.")]
    public Guid InterviewId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập lý do huỷ buổi phỏng vấn.")]
    [MinLength(5, ErrorMessage = "Lý do huỷ phải có ít nhất 5 ký tự để các bên nắm rõ thông tin.")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Kết quả phản hồi sau khi huỷ buổi phỏng vấn (Scrum - Số 55).
/// </summary>
public class InterviewCancelResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? InterviewId { get; set; }
    public string? CancellationReason { get; set; }
    public string? Reason { get => CancellationReason; set => CancellationReason = value; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// DTO chứa thông tin chi tiết một buổi phỏng vấn phục vụ nạp dữ liệu lên modal dời lịch (Scrum - Số 55).
/// </summary>
public class InterviewDetailForEditDto
{
    public Guid InterviewId { get; set; }
    public Guid ApplicationId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public int RoundNumber { get; set; } = 1;
    public string Title { get; set; } = string.Empty;
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public string InterviewType { get; set; } = "ONLINE_MEET";
    public string LocationOrLink { get; set; } = string.Empty;
    public List<Guid> InterviewerIds { get; set; } = new();
    public string PanelistNames { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool HasSubmittedEvaluation { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CancellationReason { get; set; }
    public string? RescheduleReason { get; set; }
}

