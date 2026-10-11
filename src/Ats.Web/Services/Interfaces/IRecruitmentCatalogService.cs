using Ats.Web.Models.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Services.Interfaces;

public interface IRecruitmentCatalogService
{
    Task<List<RecruitmentCatalog>> GetByTypeAsync(string catalogType, bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<List<SelectListItem>> GetSelectListByTypeAsync(string catalogType, string? selectedValue = null, bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<RecruitmentCatalog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RecruitmentCatalog?> GetByCodeAsync(string catalogType, string code, CancellationToken cancellationToken = default);
    void InvalidateCache(string? catalogType = null);
}
