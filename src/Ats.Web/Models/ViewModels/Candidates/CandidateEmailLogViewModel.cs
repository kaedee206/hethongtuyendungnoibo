namespace Ats.Web.Models.ViewModels.Candidates;

/// <summary>
/// ViewModel hiển thị từng dòng nhật ký email đã gửi cho ứng viên (SP-8).
/// </summary>
public class CandidateEmailLogItemViewModel
{
    public Guid Id { get; set; }
    public Guid? CandidateId { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string EmailType { get; set; } = string.Empty;
    public string? HtmlBody { get; set; }
    public string Status { get; set; } = string.Empty; // QUEUED, SENT, FAILED
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? TriggeredByUserName { get; set; }

    /// <summary>
    /// Chỉ cho phép gửi lại khi trạng thái là FAILED (AC2)
    /// </summary>
    public bool CanResend => Status == "FAILED";

    /// <summary>
    /// Thời điểm hiển thị theo định dạng giờ Việt Nam (UTC+7)
    /// </summary>
    public string FormattedTime => (SentAt ?? CreatedAt).ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm - dd/MM/yyyy");

    public string StatusBadgeClass => Status switch
    {
        "SENT" => "badge-subtle-emerald",
        "QUEUED" or "PENDING" => "badge-subtle-warning",
        "FAILED" => "badge-subtle-rose",
        _ => "badge-subtle-secondary"
    };

    public string StatusText => Status switch
    {
        "SENT" => "Thành công",
        "QUEUED" or "PENDING" => "Đang chờ gửi",
        "FAILED" => "Gửi thất bại",
        _ => Status
    };

    public string StatusIcon => Status switch
    {
        "SENT" => "bi-check-circle-fill text-success",
        "QUEUED" or "PENDING" => "bi-hourglass-split text-warning",
        "FAILED" => "bi-exclamation-triangle-fill text-danger",
        _ => "bi-info-circle text-secondary"
    };
}

/// <summary>
/// ViewModel bao bọc danh sách nhật ký email của ứng viên kèm bộ lọc trạng thái (SP-8).
/// </summary>
public class CandidateEmailLogListViewModel
{
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string CurrentFilter { get; set; } = "ALL";
    public List<CandidateEmailLogItemViewModel> Items { get; set; } = [];
    public int TotalCount => Items.Count;
    public int SentCount => Items.Count(i => i.Status == "SENT");
    public int FailedCount => Items.Count(i => i.Status == "FAILED");
    public int QueuedCount => Items.Count(i => i.Status == "QUEUED" || i.Status == "PENDING");
}
