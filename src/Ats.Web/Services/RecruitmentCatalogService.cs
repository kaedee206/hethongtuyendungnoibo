using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ats.Web.Services;

public class RecruitmentCatalogService : IRecruitmentCatalogService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<RecruitmentCatalogService> _logger;

    private const string CacheKeyPrefix = "RecruitmentCatalog_";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public RecruitmentCatalogService(
        ApplicationDbContext dbContext,
        IMemoryCache memoryCache,
        ILogger<RecruitmentCatalogService> logger)
    {
        _dbContext = dbContext;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<List<RecruitmentCatalog>> GetByTypeAsync(
        string catalogType,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(catalogType))
        {
            return new List<RecruitmentCatalog>();
        }

        var normalizedType = catalogType.Trim().ToUpperInvariant();
        var cacheKey = $"{CacheKeyPrefix}{normalizedType}_{(activeOnly ? "Active" : "All")}";

        if (_memoryCache.TryGetValue(cacheKey, out List<RecruitmentCatalog>? cachedList) && cachedList != null)
        {
            return cachedList;
        }

        var query = _dbContext.RecruitmentCatalogs
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.CatalogType.ToUpper() == normalizedType);

        if (activeOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        var list = await query
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        _memoryCache.Set(cacheKey, list, CacheDuration);
        return list;
    }

    public async Task<List<SelectListItem>> GetSelectListByTypeAsync(
        string catalogType,
        string? selectedValue = null,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var items = await GetByTypeAsync(catalogType, activeOnly, cancellationToken);
        var selectList = new List<SelectListItem>();

        foreach (var item in items)
        {
            var isSelected = !string.IsNullOrWhiteSpace(selectedValue) &&
                             (string.Equals(selectedValue, item.Name, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(selectedValue, item.Code, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(selectedValue, item.Id.ToString(), StringComparison.OrdinalIgnoreCase));

            selectList.Add(new SelectListItem
            {
                Value = item.Name, // Lưu trữ tên chuẩn hoặc code
                Text = item.Name,
                Selected = isSelected
            });
        }

        return selectList;
    }

    public async Task<RecruitmentCatalog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.RecruitmentCatalogs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken);
    }

    public async Task<RecruitmentCatalog?> GetByCodeAsync(
        string catalogType,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(catalogType) || string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var items = await GetByTypeAsync(catalogType, true, cancellationToken);
        return items.FirstOrDefault(i => string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public void InvalidateCache(string? catalogType = null)
    {
        if (!string.IsNullOrWhiteSpace(catalogType))
        {
            var normalizedType = catalogType.Trim().ToUpperInvariant();
            _memoryCache.Remove($"{CacheKeyPrefix}{normalizedType}_Active");
            _memoryCache.Remove($"{CacheKeyPrefix}{normalizedType}_All");
            _logger.LogInformation("Đã xóa cache danh mục tuyển dụng loại: {Type}", normalizedType);
        }
        else
        {
            // Xóa cache các loại phổ biến
            var knownTypes = new[] { "LOCATION", "WORK_TYPE", "REJECTION_REASON", "SOURCE", "EDUCATION_LEVEL", "EXPERIENCE_LEVEL" };
            foreach (var t in knownTypes)
            {
                _memoryCache.Remove($"{CacheKeyPrefix}{t}_Active");
                _memoryCache.Remove($"{CacheKeyPrefix}{t}_All");
            }
            _logger.LogInformation("Đã xóa toàn bộ cache danh mục tuyển dụng.");
        }
    }
}
