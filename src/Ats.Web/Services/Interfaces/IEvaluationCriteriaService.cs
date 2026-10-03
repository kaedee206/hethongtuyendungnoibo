using Ats.Web.Models.ViewModels.EvaluationCriteria;

namespace Ats.Web.Services.Interfaces;

public interface IEvaluationCriteriaService
{
    Task<EvaluationCriteriaListViewModel> GetAllCriteriaAsync(CancellationToken cancellationToken = default);

    Task<CriteriaRubricViewModel?> GetRubricAsync(Guid criteriaId, CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> SaveRubricAsync(CriteriaRubricSaveInputModel input, CancellationToken cancellationToken = default);

    Task<InterviewEvaluationSheetViewModel> GetInterviewSheetRubricsAsync(CancellationToken cancellationToken = default);

    Task SeedStandardCriteriaIfEmptyAsync(CancellationToken cancellationToken = default);
}
