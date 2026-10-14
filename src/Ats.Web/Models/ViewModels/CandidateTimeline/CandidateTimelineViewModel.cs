namespace Ats.Web.Models.ViewModels.CandidateTimeline;

public class CandidateTimelineViewModel
{
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string? CandidatePhone { get; set; }
    public string? CurrentCompany { get; set; }
    public string? CurrentTitle { get; set; }
    public string CandidateAvatarLetter => string.IsNullOrWhiteSpace(CandidateName) ? "U" : CandidateName.Trim()[0].ToString().ToUpperInvariant();

    public Guid? SelectedApplicationId { get; set; }
    public string? SelectedJobTitle { get; set; }
    public string FilterType { get; set; } = "ALL";
    public string? SearchQuery { get; set; }

    // Thong ke tuong tac
    public int TotalEventsCount { get; set; }
    public int ApplicationsCount { get; set; }
    public int StageChangesCount { get; set; }
    public int EmailsSentCount { get; set; }
    public int InterviewsCount { get; set; }
    public int EvaluationsCount { get; set; }
    public int CallsAndNotesCount { get; set; }

    public List<CandidateTimelineEventDto> Events { get; set; } = new();
    public List<CandidateApplicationOptionDto> Applications { get; set; } = new();
}

public class CandidateApplicationOptionDto
{
    public Guid ApplicationId { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public DateTimeOffset? AppliedAt { get; set; }
    public string CurrentStageName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
