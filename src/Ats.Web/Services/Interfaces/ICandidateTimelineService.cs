using Ats.Web.Models.ViewModels.CandidateTimeline;

namespace Ats.Web.Services.Interfaces;

public interface ICandidateTimelineService
{
    Task<CandidateTimelineViewModel?> GetCandidateTimelineAsync(
        Guid candidateId,
        Guid? applicationId = null,
        string? filterType = null,
        string? searchQuery = null);

    Task<CandidateTimelineViewModel?> GetTimelineByApplicationIdAsync(
        Guid applicationId,
        string? filterType = null,
        string? searchQuery = null);

    Task<(bool Success, string Message, Guid? InteractionId)> AddInteractionAsync(
        AddCandidateInteractionInputModel model,
        Guid currentUserId);
}
