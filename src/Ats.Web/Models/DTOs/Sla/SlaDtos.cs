namespace Ats.Web.Models.DTOs.Sla;

/// <summary>
/// DTO thông tin cấu hình SLA của một giai đoạn tuyển dụng (SCRUM-327).
/// </summary>
public class PipelineStageSlaDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int StageOrder { get; set; }
    public string? ColorCode { get; set; }
    public int SlaDays { get; set; }
    public bool IsSlaEnabled { get; set; }
    public int ActiveCandidateCount { get; set; }
    public int OverdueCandidateCount { get; set; }
}

/// <summary>
/// DTO cập nhật cấu hình SLA cho một giai đoạn (SCRUM-327).
/// </summary>
public class UpdateStageSlaDto
{
    public Guid Id { get; set; }
    public int SlaDays { get; set; }
    public bool IsSlaEnabled { get; set; }
}

/// <summary>
/// DTO chứa kết quả tác vụ quét kiểm tra quá hạn SLA (SCRUM-327).
/// </summary>
public class SlaScanResultDto
{
    public int TotalScanned { get; set; }
    public int OverdueCount { get; set; }
    public int NotificationsCreated { get; set; }
    public int EmailsSent { get; set; }
    public DateTimeOffset ScannedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<OverdueApplicationAlertDto> OverdueItems { get; set; } = new();
}

/// <summary>
/// DTO chi tiết từng hồ sơ bị quá hạn SLA và đã được nhắc việc (SCRUM-327).
/// </summary>
public class OverdueApplicationAlertDto
{
    public Guid ApplicationId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public int SlaDays { get; set; }
    public int DaysInStage { get; set; }
    public int OverdueDays { get; set; }
    public string RecruiterEmail { get; set; } = string.Empty;
    public bool IsAlreadyAlertedToday { get; set; }
}
