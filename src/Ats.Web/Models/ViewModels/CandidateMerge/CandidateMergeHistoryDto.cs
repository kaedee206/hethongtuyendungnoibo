namespace Ats.Web.Models.ViewModels.CandidateMerge;

/// <summary>
/// DTO hiển thị danh sách lịch sử các phiên gộp hồ sơ và trạng thái hoàn tác 24 giờ
/// </summary>
public class CandidateMergeHistoryDto
{
    public Guid HistoryId { get; set; }
    
    public Guid PrimaryCandidateId { get; set; }
    public string PrimaryCandidateName { get; set; } = string.Empty;
    public string PrimaryCandidateEmail { get; set; } = string.Empty;
    public string? PrimaryCandidatePhone { get; set; }

    public Guid SecondaryCandidateId { get; set; }
    public string SecondaryCandidateName { get; set; } = string.Empty;
    public string SecondaryCandidateEmail { get; set; } = string.Empty;
    public string? SecondaryCandidatePhone { get; set; }

    public Guid MergedByUserId { get; set; }
    public string MergedByUserName { get; set; } = string.Empty;
    public DateTimeOffset MergedAt { get; set; }
    public string MergedAtDisplay { get; set; } = string.Empty;
    public string? MergeReason { get; set; }

    public int MovedApplicationsCount { get; set; }
    public int MovedResumesCount { get; set; }

    public bool IsUndone { get; set; }
    public DateTimeOffset? UndoneAt { get; set; }
    public string? UndoneAtDisplay { get; set; }
    public string? UndoneByUserName { get; set; }

    // Kiểm tra giới hạn 24 giờ hoàn tác
    public bool CanUndo => !IsUndone && (DateTimeOffset.UtcNow - MergedAt) <= TimeSpan.FromHours(24);
    
    public TimeSpan RemainingUndoTime
    {
        get
        {
            if (IsUndone) return TimeSpan.Zero;
            var remaining = TimeSpan.FromHours(24) - (DateTimeOffset.UtcNow - MergedAt);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public string RemainingTimeDisplay
    {
        get
        {
            if (IsUndone) return "Đã hoàn tác";
            if (!CanUndo) return "Đã hết hạn 24h";
            var rem = RemainingUndoTime;
            return $"Còn {rem.Hours} giờ {rem.Minutes} phút";
        }
    }

    public string StatusBadgeClass
    {
        get
        {
            if (IsUndone) return "bg-secondary-subtle text-secondary border border-secondary-subtle";
            if (CanUndo) return "bg-success-subtle text-success border border-success-subtle";
            return "bg-dark-subtle text-dark border border-dark-subtle";
        }
    }
}
