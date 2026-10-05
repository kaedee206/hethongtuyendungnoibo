using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

[Authorize(Roles = $"{UserRoles.Admin},{UserRoles.HRManager}")]
[Route("recruitment-catalogs")]
[Route("danh-muc-tuyen-dung")]
public class RecruitmentCatalogsController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<RecruitmentCatalogsController> _logger;

    public RecruitmentCatalogsController(ApplicationDbContext dbContext, ILogger<RecruitmentCatalogsController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        await EnsureSeedCatalogsAsync();
        var catalogs = await _dbContext.RecruitmentCatalogs
            .OrderBy(c => c.CatalogType)
            .ThenBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

        return View(catalogs);
    }

    [HttpGet("api/items")]
    public async Task<IActionResult> GetItems(
        [FromQuery] string? catalogType,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        await EnsureSeedCatalogsAsync();
        var query = _dbContext.RecruitmentCatalogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(catalogType) && catalogType != "ALL")
        {
            query = query.Where(c => c.CatalogType == catalogType);
        }

        var totalRecords = await query.CountAsync();

        var orderedQuery = query
            .OrderBy(c => c.CatalogType)
            .ThenBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name);

        if (page.HasValue && pageSize.HasValue && page.Value > 0 && pageSize.Value > 0)
        {
            var p = page.Value;
            var ps = Math.Min(pageSize.Value, 100);
            var totalPages = (int)Math.Ceiling((double)totalRecords / ps);
            var pagedItems = await orderedQuery
                .Skip((p - 1) * ps)
                .Take(ps)
                .ToListAsync();

            return Ok(new
            {
                data = pagedItems,
                totalRecords,
                currentPage = p,
                pageSize = ps,
                totalPages
            });
        }

        var items = await orderedQuery.ToListAsync();
        return Ok(items);
    }

    [HttpPost("api/items")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateItem([FromBody] CatalogItemDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.CatalogType) || string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Code))
        {
            return BadRequest(new { message = "Loại danh mục, mã và tên danh mục là bắt buộc." });
        }

        var cleanCode = dto.Code.Trim().ToUpperInvariant();
        var exists = await _dbContext.RecruitmentCatalogs
            .AnyAsync(c => c.CatalogType == dto.CatalogType && c.Code == cleanCode);

        if (exists)
        {
            return Conflict(new { message = $"Mã danh mục '{cleanCode}' đã tồn tại trong nhóm này." });
        }

        var entity = new RecruitmentCatalog
        {
            Id = Guid.NewGuid(),
            CatalogType = dto.CatalogType.Trim().ToUpperInvariant(),
            Code = cleanCode,
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            DisplayOrder = dto.DisplayOrder,
            IsActive = dto.IsActive,
            IsSystem = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.RecruitmentCatalogs.Add(entity);
        await _dbContext.SaveChangesAsync();

        return Ok(entity);
    }

    [HttpPut("api/items/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateItem(Guid id, [FromBody] CatalogItemDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new { message = "Tên danh mục là bắt buộc." });
        }

        var item = await _dbContext.RecruitmentCatalogs.FirstOrDefaultAsync(c => c.Id == id);
        if (item == null)
        {
            return NotFound(new { message = "Không tìm thấy danh mục yêu cầu." });
        }

        // If system, cannot change Code or CatalogType
        if (!item.IsSystem && !string.IsNullOrWhiteSpace(dto.Code))
        {
            var cleanCode = dto.Code.Trim().ToUpperInvariant();
            var codeExists = await _dbContext.RecruitmentCatalogs
                .AnyAsync(c => c.Id != id && c.CatalogType == item.CatalogType && c.Code == cleanCode);
            if (codeExists)
            {
                return Conflict(new { message = $"Mã danh mục '{cleanCode}' đã tồn tại." });
            }
            item.Code = cleanCode;
        }

        item.Name = dto.Name.Trim();
        item.Description = dto.Description?.Trim();
        item.DisplayOrder = dto.DisplayOrder;
        item.IsActive = dto.IsActive;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("api/items/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteItem(Guid id)
    {
        var item = await _dbContext.RecruitmentCatalogs.FirstOrDefaultAsync(c => c.Id == id);
        if (item == null)
        {
            return NotFound(new { message = "Không tìm thấy mục danh mục cần xóa." });
        }

        if (item.IsSystem)
        {
            return StatusCode(409, new { message = "Mục danh mục hệ thống mặc định được bảo vệ và không thể xóa. Bạn có thể vô hiệu hóa trạng thái để ẩn." });
        }

        _dbContext.RecruitmentCatalogs.Remove(item);
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Đã xóa mục danh mục thành công." });
    }

    [HttpPost("api/items/{id:guid}/toggle-status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(Guid id)
    {
        var item = await _dbContext.RecruitmentCatalogs.FirstOrDefaultAsync(c => c.Id == id);
        if (item == null)
        {
            return NotFound(new { message = "Không tìm thấy mục danh mục." });
        }

        item.IsActive = !item.IsActive;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Ok(new { id = item.Id, isActive = item.IsActive });
    }

    private async Task EnsureSeedCatalogsAsync()
    {
        if (await _dbContext.RecruitmentCatalogs.AnyAsync()) return;

        var defaults = new List<RecruitmentCatalog>
        {
            // Nguồn ứng viên
            new() { CatalogType = "SOURCE", Code = "LINKEDIN", Name = "LinkedIn Recruiter", Description = "Mạng xã hội nghề nghiệp LinkedIn", DisplayOrder = 1, IsSystem = true },
            new() { CatalogType = "SOURCE", Code = "TOPCV", Name = "TopCV Platform", Description = "Cổng thông tin việc làm TopCV", DisplayOrder = 2, IsSystem = true },
            new() { CatalogType = "SOURCE", Code = "VIETNAMWORKS", Name = "VietnamWorks", Description = "Cổng việc làm VietnamWorks", DisplayOrder = 3, IsSystem = true },
            new() { CatalogType = "SOURCE", Code = "INTERNAL_REFERRAL", Name = "Nội bộ giới thiệu", Description = "Ứng viên do nhân sự NoveraTech giới thiệu", DisplayOrder = 4, IsSystem = true },
            new() { CatalogType = "SOURCE", Code = "DIRECT_PORTAL", Name = "Website tuyển dụng trực tiếp", Description = "Ứng viên nộp trực tiếp qua trang /gioi-thieu", DisplayOrder = 5, IsSystem = true },

            // Lý do từ chối
            new() { CatalogType = "REJECTION_REASON", Code = "SKILL_MISMATCH", Name = "Chuyên môn chưa phù hợp", Description = "Kỹ năng thực tế chưa đáp ứng yêu cầu vị trí", DisplayOrder = 1, IsSystem = true },
            new() { CatalogType = "REJECTION_REASON", Code = "SALARY_OVER_BAND", Name = "Kỳ vọng lương vượt khung", Description = "Mức lương mong muốn vượt quá dải lương quy định", DisplayOrder = 2, IsSystem = true },
            new() { CatalogType = "REJECTION_REASON", Code = "CULTURE_MISMATCH", Name = "Chưa tương thích văn hóa đội ngũ", Description = "Không đồng điệu với giá trị cốt lõi & văn hóa công ty", DisplayOrder = 3, IsSystem = true },
            new() { CatalogType = "REJECTION_REASON", Code = "CANDIDATE_DECLINED", Name = "Ứng viên từ chối Offer", Description = "Ứng viên quyết định chọn cơ hội khác", DisplayOrder = 4, IsSystem = true },
            new() { CatalogType = "REJECTION_REASON", Code = "NO_RESPONSE", Name = "Ứng viên không phản hồi", Description = "Không liên lạc được sau nhiều lần liên hệ", DisplayOrder = 5, IsSystem = true },

            // Địa điểm làm việc
            new() { CatalogType = "LOCATION", Code = "HN_HQ", Name = "Hà Nội — Trụ sở chính", Description = "Tòa nhà Novera Tower, Cầu Giấy, Hà Nội", DisplayOrder = 1, IsSystem = true },
            new() { CatalogType = "LOCATION", Code = "HCM_BRANCH", Name = "TP. Hồ Chí Minh — Chi nhánh", Description = "Tòa nhà Bitexco / Quận 1, TP. HCM", DisplayOrder = 2, IsSystem = true },
            new() { CatalogType = "LOCATION", Code = "DN_HUB", Name = "Đà Nẵng — R&D Hub", Description = "Công viên phần mềm Đà Nẵng", DisplayOrder = 3, IsSystem = true },
            new() { CatalogType = "LOCATION", Code = "REMOTE_VN", Name = "Remote toàn quốc (Việt Nam)", Description = "Làm việc từ xa mọi nơi tại Việt Nam", DisplayOrder = 4, IsSystem = true },

            // Hình thức làm việc
            new() { CatalogType = "WORK_TYPE", Code = "FULL_TIME", Name = "Toàn thời gian (Full-time)", Description = "40 giờ/tuần, hưởng đầy đủ chế độ", DisplayOrder = 1, IsSystem = true },
            new() { CatalogType = "WORK_TYPE", Code = "HYBRID", Name = "Hybrid (2 ngày WFH/tuần)", Description = "Linh hoạt giữa văn phòng và từ xa", DisplayOrder = 2, IsSystem = true },
            new() { CatalogType = "WORK_TYPE", Code = "PART_TIME", Name = "Bán thời gian (Part-time)", Description = "20-25 giờ/tuần", DisplayOrder = 3, IsSystem = true },
            new() { CatalogType = "WORK_TYPE", Code = "CONTRACT", Name = "Hợp đồng dự án (Contractor)", Description = "Theo tiến độ dự án 6-12 tháng", DisplayOrder = 4, IsSystem = true }
        };

        foreach (var d in defaults)
        {
            d.CreatedAt = DateTimeOffset.UtcNow;
            d.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.RecruitmentCatalogs.AddRangeAsync(defaults);
        await _dbContext.SaveChangesAsync();
    }
}

public class CatalogItemDto
{
    public string CatalogType { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;
}
