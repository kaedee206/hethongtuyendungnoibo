using Ats.Web.Models.ViewModels.Requisitions;

namespace Ats.Web.Services.Interfaces;

public interface IRequisitionService
{
    Task<RequisitionCreateViewModel> PrepareCreateViewModelAsync(Guid currentUserId, CancellationToken cancellationToken = default);

    Task PopulateOptionsAsync(RequisitionCreateViewModel model, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<(bool Success, string Message, Guid? RequisitionId)> CreateRequisitionAsync(
        RequisitionCreateViewModel model, 
        Guid currentUserId, 
        CancellationToken cancellationToken = default);

    Task<(bool Success, string Message, Guid? RequisitionId, string? Code)> SaveOrUpdateRequisitionAsync(
        RequisitionCreateViewModel model,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<List<RequisitionDraftItemViewModel>> GetDraftsByManagerAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<RequisitionCreateViewModel?> GetDraftByIdAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> DeleteDraftAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<int> GetDraftCountAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string Message, Guid? NewRequisitionId, string? NewCode)> DuplicateRequisitionAsync(
        Guid sourceId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> ProcessApprovalDecisionAsync(
        Guid requisitionId,
        Guid currentUserId,
        RequisitionApprovalDecisionInputModel input,
        CancellationToken cancellationToken = default);

    Task<RequisitionDetailsViewModel?> GetRequisitionDetailsAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<List<RequisitionApprovalListItemViewModel>> GetRequisitionsForApprovalAsync(
        Guid currentUserId,
        string? tab = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<int> GetPendingApprovalCountForUserAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<int> GetMyRequisitionsCountAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default);
}
