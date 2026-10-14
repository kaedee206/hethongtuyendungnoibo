namespace Ats.Web.Models.ViewModels.Applications;

public class WithdrawApplicationViewModel
{
    public string Token { get; set; } = string.Empty;
    public Guid ApplicationId { get; set; }
    public string ApplicationCode { get; set; } = string.Empty;
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string WorkLocation { get; set; } = string.Empty;
    public string CurrentStageName { get; set; } = string.Empty;
    public string AppliedDateDisplay { get; set; } = string.Empty;

    public string? ReasonCategory { get; set; }
    public string? ReasonDetail { get; set; }

    public bool IsAlreadyWithdrawn { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public string? ExistingReason { get; set; }
}
