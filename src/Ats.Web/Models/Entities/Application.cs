using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class Application : BaseEntity
{
    public Guid Id { get; set; }
    public Guid JobPostingId { get; set; }
    public Guid CandidateId { get; set; }
    public Guid ResumeId { get; set; }
    public Guid CurrentStageId { get; set; }
    
    public ApplicationStatus Status { get; set; } = ApplicationStatus.IN_PROCESS;
    public Guid? RejectionReasonId { get; set; }
    public string? RejectionNote { get; set; }
    public int? Rating { get; set; } // 1-5
    
    public DateTimeOffset? AppliedAt { get; set; }

    /// <summary>
    /// Mã token bảo mật dùng để rút hồ sơ qua liên kết email xác nhận (SCRUM-291).
    /// </summary>
    public string? WithdrawToken { get; set; }

    /// <summary>
    /// Thời điểm ứng viên thực hiện rút hồ sơ.
    /// </summary>
    public DateTimeOffset? WithdrawnAt { get; set; }

    /// <summary>
    /// Lý do ứng viên rút hồ sơ ứng tuyển.
    /// </summary>
    public string? WithdrawReason { get; set; }

    /// <summary>
    /// Cờ đánh dấu hồ sơ đang bị quá hạn ngưỡng SLA của giai đoạn hiện tại (SCRUM-327).
    /// </summary>
    public bool IsSlaOverdue { get; set; } = false;

    /// <summary>
    /// Thời điểm gửi thông báo/email nhắc việc quá hạn SLA gần nhất cho Recruiter (dùng chống nhắc trùng trong cùng ngày - SCRUM-327).
    /// </summary>
    public DateTimeOffset? SlaOverdueAlertedAt { get; set; }

    /// <summary>
    /// Thời điểm hồ sơ bắt đầu chuyển vào giai đoạn hiện tại (dùng tính số ngày nằm im tại giai đoạn - SCRUM-327).
    /// </summary>
    public DateTimeOffset? StageEnteredAt { get; set; }

    public JobPosting JobPosting { get; set; } = null!;
    public Candidate Candidate { get; set; } = null!;
    public Resume Resume { get; set; } = null!;
    public PipelineStage CurrentStage { get; set; } = null!;
    public ICollection<ApplicationStageHistory> StageHistories { get; set; } = new List<ApplicationStageHistory>();
    public ICollection<Interview> Interviews { get; set; } = new List<Interview>();
    public ICollection<JobOffer> JobOffers { get; set; } = new List<JobOffer>();
}
