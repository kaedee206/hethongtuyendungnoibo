namespace Ats.Web.Models.DTOs;

public class BulkRejectionCandidatePreviewDto
{
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string CurrentStageName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool HasSentRejectionEmail { get; set; }
    public DateTimeOffset? RejectionEmailSentAt { get; set; }
    public bool CanSend => !HasSentRejectionEmail;
    public string? SkipReason { get; set; }
    public string PreviewSubject { get; set; } = string.Empty;
    public string PreviewHtml { get; set; } = string.Empty;
}

public class BulkRejectionPreviewResponseDto
{
    public List<BulkRejectionCandidatePreviewDto> Candidates { get; set; } = [];
    public int TotalSelected { get; set; }
    public int ValidToSendCount { get; set; }
    public int BlockedDuplicateCount { get; set; }
    public string DefaultSubject { get; set; } = string.Empty;
}

public class BulkSendRejectionRequestDto
{
    public List<Guid> ApplicationIds { get; set; } = [];
    public Guid? RejectionReasonId { get; set; }
    public string? RejectionReasonTitle { get; set; }
    public string? CustomFeedback { get; set; }
}

public class BulkSendRejectionResponseDto
{
    public bool Success { get; set; }
    public int SentCount { get; set; }
    public int SkippedDuplicateCount { get; set; }
    public int FailedCount { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = [];
}
