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
}
