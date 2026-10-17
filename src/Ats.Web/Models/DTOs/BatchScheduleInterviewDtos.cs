using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.DTOs;

/// <summary>
/// Model nhận yêu cầu tính toán và xem trước các khe giờ phỏng vấn hàng loạt (Scrum - Số 52).
/// Hỗ trợ chia khe liên tiếp dựa trên mốc bắt đầu, thời lượng ca và thời gian nghỉ giữa các ca.
/// </summary>
public class BatchSchedulePreviewInputModel
{
    [Required(ErrorMessage = "Vui lòng chọn ít nhất một ứng viên để đặt lịch hàng loạt.")]
    public List<Guid> ApplicationIds { get; set; } = new();

    [Required(ErrorMessage = "Vui lòng chọn ngày giờ bắt đầu ca phỏng vấn đầu tiên.")]
    public DateTime? StartTime { get; set; }

    [Range(15, 240, ErrorMessage = "Thời lượng mỗi ca phỏng vấn phải từ 15 đến 240 phút.")]
    public int SlotDurationMinutes { get; set; } = 30;

    [Range(0, 60, ErrorMessage = "Thời gian nghỉ giữa các ca phải từ 0 đến 60 phút.")]
    public int BreakDurationMinutes { get; set; } = 5;

    [Range(1, 10, ErrorMessage = "Vòng phỏng vấn phải từ 1 đến 10.")]
    public int RoundNumber { get; set; } = 1;

    public string? RoundTitle { get; set; }

    public string InterviewType { get; set; } = "ONLINE_MEET";

    public string? LocationOrLink { get; set; }

    public List<Guid>? InterviewerIds { get; set; } = new();

    public string? Notes { get; set; }

    public List<Guid> GetNormalizedInterviewerIds()
    {
        return InterviewerIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();
    }
}

/// <summary>
/// Chi tiết một khe giờ được xem trước trước khi nhân viên tuyển dụng xác nhận (Scrum - Số 52).
/// </summary>
public class BatchScheduleSlotPreviewItemDto
{
    public int SlotOrder { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string CandidatePhone { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string StartTimeDisplay { get; set; } = string.Empty;
    public string EndTimeDisplay { get; set; } = string.Empty;
    public string TimeSlotDisplay => $"{StartTimeDisplay} - {EndTimeDisplay}";
    public int DurationMinutes { get; set; }
    public string? LocationOrLink { get; set; }
}

/// <summary>
/// Kết quả sinh danh sách khe giờ xem trước (Preview) cho toàn bộ ứng viên (Scrum - Số 52).
/// </summary>
public class BatchSchedulePreviewResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCandidates { get; set; }
    public DateTime? OverallStartTime { get; set; }
    public DateTime? OverallEndTime { get; set; }
    public string? OverallTimeDisplay { get; set; }
    public int TotalDurationMinutes { get; set; }
    public List<BatchScheduleSlotPreviewItemDto> Slots { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Model gửi lên khi nhân viên tuyển dụng chốt toàn bộ các khe giờ phỏng vấn hàng loạt (Scrum - Số 52).
/// </summary>
public class BatchScheduleConfirmInputModel
{
    [Range(1, 10, ErrorMessage = "Vòng phỏng vấn phải từ 1 đến 10.")]
    public int RoundNumber { get; set; } = 1;

    public string? RoundTitle { get; set; }

    public string InterviewType { get; set; } = "ONLINE_MEET";

    [Required(ErrorMessage = "Vui lòng cung cấp phòng họp hoặc liên kết họp trực tuyến.")]
    public string LocationOrLink { get; set; } = string.Empty;

    public List<Guid>? InterviewerIds { get; set; } = new();

    public string? Notes { get; set; }

    [Required(ErrorMessage = "Danh sách các khe giờ xác nhận không được để trống.")]
    public List<BatchScheduleSlotConfirmItemModel> Slots { get; set; } = new();

    public List<Guid> GetNormalizedInterviewerIds()
    {
        return InterviewerIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();
    }
}

/// <summary>
/// Chi tiết khe giờ cụ thể của từng ứng viên khi xác nhận đặt lịch (Scrum - Số 52).
/// </summary>
public class BatchScheduleSlotConfirmItemModel
{
    [Required]
    public Guid ApplicationId { get; set; }

    [Required]
    public DateTime StartTime { get; set; }

    [Required]
    public DateTime EndTime { get; set; }

    public int SlotOrder { get; set; }

    public string? CustomLocationOrLink { get; set; }
}

/// <summary>
/// Kết quả phản hồi sau khi hoàn tất đặt lịch phỏng vấn hàng loạt (Scrum - Số 52).
/// </summary>
public class BatchScheduleResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<InterviewScheduleResultDto> ScheduledInterviews { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
