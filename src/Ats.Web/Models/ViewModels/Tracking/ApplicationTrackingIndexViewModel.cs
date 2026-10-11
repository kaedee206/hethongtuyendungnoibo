namespace Ats.Web.Models.ViewModels.Tracking;

/// <summary>
/// Composite ViewModel cho trang tra cứu /tra-cuu (Scrum #35)
/// </summary>
public class ApplicationTrackingIndexViewModel
{
    public ApplicationTrackingInputModel Input { get; set; } = new();
    public ApplicationTrackingResultViewModel? Result { get; set; }
    public bool HasSearched { get; set; }
}
