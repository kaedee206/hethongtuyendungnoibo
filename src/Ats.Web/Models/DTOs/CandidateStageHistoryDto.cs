using Ats.Web.Models.Enums;

namespace Ats.Web.Models.DTOs;

/// <summary>
/// DTO chứa thông tin lịch sử chuyển giai đoạn của ứng viên (SCRUM-42).
/// </summary>
public class CandidateStageHistoryDto
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }

    public CandidateStage? FromStage { get; set; }
    public string? FromStageDisplayName { get; set; }

    public CandidateStage ToStage { get; set; }
    public string ToStageDisplayName { get; set; } = string.Empty;

    public Guid? RejectionReasonId { get; set; }
    public string? RejectionReasonCode { get; set; }
    public string? RejectionReasonName { get; set; }

    public string? Note { get; set; }

    public Guid ChangedByUserId { get; set; }
    public string? ChangedByUserName { get; set; }
    public string? ChangedByUserEmail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
