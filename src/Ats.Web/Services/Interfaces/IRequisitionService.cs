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
}
