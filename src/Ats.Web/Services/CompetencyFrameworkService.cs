using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.ViewModels.CompetencyFrameworks;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

public class CompetencyFrameworkService(ApplicationDbContext dbContext, ILogger<CompetencyFrameworkService>? logger = null) : ICompetencyFrameworkService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<CompetencyFrameworkService>? _logger = logger;

    public async Task<CompetencyFrameworkListViewModel> GetFrameworksAsync(
        string? keyword,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize <= 0 || pageSize > 100) pageSize = 15;

        await EnsureSeedAsync(cancellationToken);

        var query = _dbContext.CompetencyFrameworks
            .AsNoTracking()
            .Where(f => !f.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(f => f.Code.ToLower().Contains(kw) ||
                                     f.Name.ToLower().Contains(kw) ||
                                     (f.Description != null && f.Description.ToLower().Contains(kw)));
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(f => f.IsActive);
            }
            else if (status.Equals("INACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(f => !f.IsActive);
            }
        }

        var totalRecords = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(f => f.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new CompetencyFrameworkItemViewModel
            {
                Id = f.Id,
                Code = f.Code,
                Name = f.Name,
                Category = f.Description ?? "Chuyên môn tiêu chuẩn",
                Description = f.Description,
                IsActive = f.IsActive,
                CompetenciesCount = f.Criteria.Count(c => !c.IsDeleted),
                AssociatedJobPositions = f.AssignedPositions
                    .Where(p => !p.IsDeleted)
                    .Select(p => new JobPositionAssignedViewModel
                    {
                        Id = p.Id,
                        Code = p.Code,
                        Title = p.Title,
                        DepartmentName = p.Department != null ? p.Department.Name : string.Empty,
                        JobLevel = p.JobLevel,
                        IsActive = p.IsActive
                    }).ToList(),
                UpdatedAt = f.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var totalActive = await _dbContext.CompetencyFrameworks.CountAsync(f => !f.IsDeleted && f.IsActive, cancellationToken);
        var totalInactive = await _dbContext.CompetencyFrameworks.CountAsync(f => !f.IsDeleted && !f.IsActive, cancellationToken);
        var totalMappedPositions = await _dbContext.JobPositions.CountAsync(p => !p.IsDeleted && p.CompetencyFrameworkId != null, cancellationToken);

        return new CompetencyFrameworkListViewModel
        {
            Frameworks = items,
            Keyword = keyword,
            StatusFilter = status,
            TotalRecords = totalRecords,
            CurrentPage = page,
            PageSize = pageSize,
            TotalActiveFrameworks = totalActive,
            TotalInactiveFrameworks = totalInactive,
            TotalPositionsMapped = totalMappedPositions
        };
    }

    public async Task<CompetencyFrameworkItemViewModel?> GetFrameworkByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var f = await _dbContext.CompetencyFrameworks
            .AsNoTracking()
            .Include(cf => cf.Criteria.Where(c => !c.IsDeleted))
            .Include(cf => cf.AssignedPositions.Where(p => !p.IsDeleted))
                .ThenInclude(p => p.Department)
            .FirstOrDefaultAsync(cf => cf.Id == id && !cf.IsDeleted, cancellationToken);

        if (f == null) return null;

        return new CompetencyFrameworkItemViewModel
        {
            Id = f.Id,
            Code = f.Code,
            Name = f.Name,
            Category = f.Description ?? "Chuyên môn tiêu chuẩn",
            Description = f.Description,
            IsActive = f.IsActive,
            CompetenciesCount = f.Criteria.Count(c => !c.IsDeleted),
            AssociatedJobPositions = f.AssignedPositions.Select(p => new JobPositionAssignedViewModel
            {
                Id = p.Id,
                Code = p.Code,
                Title = p.Title,
                DepartmentName = p.Department?.Name ?? string.Empty,
                JobLevel = p.JobLevel,
                IsActive = p.IsActive
            }).ToList(),
            UpdatedAt = f.UpdatedAt
        };
    }

    public async Task<CompetencyFrameworkDetailViewModel?> GetDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var f = await _dbContext.CompetencyFrameworks
            .AsNoTracking()
            .Include(cf => cf.Criteria.Where(c => !c.IsDeleted))
            .Include(cf => cf.AssignedPositions.Where(p => !p.IsDeleted))
                .ThenInclude(p => p.Department)
            .FirstOrDefaultAsync(cf => cf.Id == id && !cf.IsDeleted, cancellationToken);

        if (f == null) return null;

        return new CompetencyFrameworkDetailViewModel
        {
            Id = f.Id,
            Code = f.Code,
            Name = f.Name,
            Description = f.Description,
            IsActive = f.IsActive,
            CreatedAt = f.CreatedAt,
            UpdatedAt = f.UpdatedAt,
            Criteria = f.Criteria
                .OrderBy(c => c.CreatedAt)
                .Select(c => new CompetencyCriterionFormViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    Description = c.Description,
                    Weight = c.Weight,
                    Rubric1 = c.Rubric1,
                    Rubric2 = c.Rubric2,
                    Rubric3 = c.Rubric3,
                    Rubric4 = c.Rubric4,
                    Rubric5 = c.Rubric5
                }).ToList(),
            AssignedPositions = f.AssignedPositions
                .OrderBy(p => p.Title)
                .Select(p => new JobPositionAssignedViewModel
                {
                    Id = p.Id,
                    Code = p.Code,
                    Title = p.Title,
                    DepartmentName = p.Department?.Name ?? string.Empty,
                    JobLevel = p.JobLevel,
                    IsActive = p.IsActive
                }).ToList()
        };
    }

    public async Task<List<JobPositionAssignedViewModel>> GetAssociatedPositionsAsync(
        Guid frameworkId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.JobPositions
            .AsNoTracking()
            .Include(p => p.Department)
            .Where(p => !p.IsDeleted && p.CompetencyFrameworkId == frameworkId)
            .OrderBy(p => p.Title)
            .Select(p => new JobPositionAssignedViewModel
            {
                Id = p.Id,
                Code = p.Code,
                Title = p.Title,
                DepartmentName = p.Department != null ? p.Department.Name : string.Empty,
                JobLevel = p.JobLevel,
                IsActive = p.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<CompetencyFrameworkFormViewModel> PrepareFormAsync(
        Guid? id,
        CancellationToken cancellationToken = default)
    {
        var allPositions = await _dbContext.JobPositions
            .AsNoTracking()
            .Include(p => p.Department)
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.Title)
            .Select(p => new JobPositionAssignedViewModel
            {
                Id = p.Id,
                Code = p.Code,
                Title = p.Title,
                DepartmentName = p.Department != null ? p.Department.Name : string.Empty,
                JobLevel = p.JobLevel,
                IsActive = p.IsActive
            })
            .ToListAsync(cancellationToken);

        if (!id.HasValue)
        {
            var nextIndex = await _dbContext.CompetencyFrameworks.CountAsync(cancellationToken) + 1;
            return new CompetencyFrameworkFormViewModel
            {
                Code = $"CF-TECH-{nextIndex:D3}",
                IsActive = true,
                AllPositions = allPositions,
                Criteria =
                [
                    new CompetencyCriterionFormViewModel
                    {
                        Name = "Kiến thức & Kỹ năng Chuyên môn cốt lõi",
                        Description = "Khả năng ứng dụng kỹ thuật công nghệ vào giải quyết bài toán nghiệp vụ.",
                        Weight = 40,
                        Rubric1 = "Chưa nắm vững kiến thức căn bản",
                        Rubric2 = "Nắm kiến thức căn bản, làm được việc có hướng dẫn",
                        Rubric3 = "Thành thạo, độc lập thực hiện và xử lý tốt vấn đề",
                        Rubric4 = "Nâng cao, tối ưu hóa và hướng dẫn thành viên khác",
                        Rubric5 = "Chuyên gia, dẫn dắt kiến trúc và định hướng kỹ thuật"
                    },
                    new CompetencyCriterionFormViewModel
                    {
                        Name = "Tư duy giải quyết vấn đề & Phân tích",
                        Description = "Khả năng phân tích nguyên nhân gốc rễ và đưa ra giải pháp tối ưu.",
                        Weight = 30,
                        Rubric1 = "Lúng túng trước sự cố phát sinh",
                        Rubric2 = "Tìm được giải pháp theo khuôn mẫu có sẵn",
                        Rubric3 = "Phân tích nguyên nhân logic và xử lý dứt điểm",
                        Rubric4 = "Dự đoán rủi ro và ngăn ngừa tái diễn",
                        Rubric5 = "Kiến tạo giải pháp đột phá trong điều kiện phức tạp"
                    },
                    new CompetencyCriterionFormViewModel
                    {
                        Name = "Giao tiếp, Phối hợp & Trách nhiệm",
                        Description = "Kỹ năng làm việc nhóm, giao tiếp chủ động và cam kết tiến độ.",
                        Weight = 30,
                        Rubric1 = "Thụ động, thiếu tương tác với đồng nghiệp",
                        Rubric2 = "Hợp tác cơ bản khi được yêu cầu",
                        Rubric3 = "Giao tiếp rõ ràng, phối hợp hiệu quả và trách nhiệm cao",
                        Rubric4 = "Chủ động kết nối liên phòng ban, chia sẻ kinh nghiệm",
                        Rubric5 = "Lan tỏa văn hóa tích cực, truyền cảm hứng và dẫn dắt tập thể"
                    }
                ]
            };
        }

        var f = await _dbContext.CompetencyFrameworks
            .AsNoTracking()
            .Include(cf => cf.Criteria.Where(c => !c.IsDeleted))
            .Include(cf => cf.AssignedPositions.Where(p => !p.IsDeleted))
            .FirstOrDefaultAsync(cf => cf.Id == id.Value && !cf.IsDeleted, cancellationToken);

        if (f == null)
        {
            return new CompetencyFrameworkFormViewModel { AllPositions = allPositions };
        }

        return new CompetencyFrameworkFormViewModel
        {
            Id = f.Id,
            Code = f.Code,
            Name = f.Name,
            Description = f.Description,
            IsActive = f.IsActive,
            AllPositions = allPositions,
            AssignedPositionIds = f.AssignedPositions.Select(p => p.Id).ToList(),
            Criteria = f.Criteria
                .OrderBy(c => c.CreatedAt)
                .Select(c => new CompetencyCriterionFormViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    Description = c.Description,
                    Weight = c.Weight,
                    Rubric1 = c.Rubric1,
                    Rubric2 = c.Rubric2,
                    Rubric3 = c.Rubric3,
                    Rubric4 = c.Rubric4,
                    Rubric5 = c.Rubric5
                }).ToList()
        };
    }

    public async Task<(bool Success, string Message, Guid? NewId)> CreateAsync(
        CompetencyFrameworkFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.Code))
        {
            return (false, "Mã khung năng lực không được để trống.", null);
        }

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            return (false, "Tên khung năng lực không được để trống.", null);
        }

        var normalizedCode = model.Code.Trim().ToUpperInvariant();
        var exists = await _dbContext.CompetencyFrameworks
            .AnyAsync(f => f.Code.ToUpper() == normalizedCode && !f.IsDeleted, cancellationToken);

        if (exists)
        {
            return (false, $"Mã khung năng lực '{model.Code}' đã tồn tại trong hệ thống.", null);
        }

        var validCriteria = model.Criteria
            .Where(c => !c.IsMarkedForDeletion && !string.IsNullOrWhiteSpace(c.Name))
            .ToList();

        if (validCriteria.Any())
        {
            var sumWeight = validCriteria.Sum(c => c.Weight);
            if (sumWeight != 100)
            {
                return (false, $"Tổng trọng số các tiêu chí phải bằng 100% (Hiện tại là {sumWeight}%).", null);
            }
        }

        var framework = new CompetencyFramework
        {
            Id = Guid.NewGuid(),
            Code = normalizedCode,
            Name = model.Name.Trim(),
            Description = model.Description?.Trim(),
            IsActive = model.IsActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        foreach (var c in validCriteria)
        {
            framework.Criteria.Add(new CompetencyCriterion
            {
                Id = Guid.NewGuid(),
                CompetencyFrameworkId = framework.Id,
                Name = c.Name.Trim(),
                Description = c.Description?.Trim(),
                Weight = c.Weight,
                Rubric1 = c.Rubric1?.Trim(),
                Rubric2 = c.Rubric2?.Trim(),
                Rubric3 = c.Rubric3?.Trim(),
                Rubric4 = c.Rubric4?.Trim(),
                Rubric5 = c.Rubric5?.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await _dbContext.CompetencyFrameworks.AddAsync(framework, cancellationToken);

        // Gán các chức danh áp dụng
        if (model.AssignedPositionIds != null && model.AssignedPositionIds.Any())
        {
            var positions = await _dbContext.JobPositions
                .Where(p => model.AssignedPositionIds.Contains(p.Id) && !p.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var pos in positions)
            {
                pos.CompetencyFrameworkId = framework.Id;
                pos.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation("Đã tạo mới khung năng lực {Code} - {Name} (ID: {Id})", framework.Code, framework.Name, framework.Id);
        return (true, $"Tạo mới khung năng lực '{framework.Name}' thành công.", framework.Id);
    }

    public async Task<(bool Success, string Message)> UpdateAsync(
        CompetencyFrameworkFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!model.Id.HasValue)
        {
            return (false, "ID khung năng lực không hợp lệ.");
        }

        var framework = await _dbContext.CompetencyFrameworks
            .Include(f => f.Criteria)
            .Include(f => f.AssignedPositions)
            .FirstOrDefaultAsync(f => f.Id == model.Id.Value && !f.IsDeleted, cancellationToken);

        if (framework == null)
        {
            return (false, "Không tìm thấy khung năng lực cần cập nhật.");
        }

        var normalizedCode = model.Code.Trim().ToUpperInvariant();
        if (!framework.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase))
        {
            var duplicateCode = await _dbContext.CompetencyFrameworks
                .AnyAsync(f => f.Id != framework.Id && f.Code.ToUpper() == normalizedCode && !f.IsDeleted, cancellationToken);

            if (duplicateCode)
            {
                return (false, $"Mã khung năng lực '{model.Code}' đã được sử dụng.");
            }
            framework.Code = normalizedCode;
        }

        var validCriteria = model.Criteria
            .Where(c => !c.IsMarkedForDeletion && !string.IsNullOrWhiteSpace(c.Name))
            .ToList();

        if (validCriteria.Any())
        {
            var sumWeight = validCriteria.Sum(c => c.Weight);
            if (sumWeight != 100)
            {
                return (false, $"Tổng trọng số các tiêu chí phải bằng 100% (Hiện tại là {sumWeight}%).");
            }
        }

        framework.Name = model.Name.Trim();
        framework.Description = model.Description?.Trim();
        framework.IsActive = model.IsActive;
        framework.UpdatedAt = DateTimeOffset.UtcNow;

        // Cập nhật Criteria
        var existingCriteria = framework.Criteria.ToList();
        var submittedCriteriaIds = validCriteria.Where(c => c.Id.HasValue).Select(c => c.Id!.Value).ToHashSet();

        // Xóa các tiêu chí bị bỏ hoặc đánh dấu xóa
        foreach (var ec in existingCriteria)
        {
            if (!submittedCriteriaIds.Contains(ec.Id))
            {
                ec.IsDeleted = true;
                ec.DeletedAt = DateTimeOffset.UtcNow;
            }
        }

        // Cập nhật hoặc thêm mới
        foreach (var c in validCriteria)
        {
            if (c.Id.HasValue)
            {
                var existing = existingCriteria.FirstOrDefault(ec => ec.Id == c.Id.Value);
                if (existing != null)
                {
                    existing.Name = c.Name.Trim();
                    existing.Description = c.Description?.Trim();
                    existing.Weight = c.Weight;
                    existing.Rubric1 = c.Rubric1?.Trim();
                    existing.Rubric2 = c.Rubric2?.Trim();
                    existing.Rubric3 = c.Rubric3?.Trim();
                    existing.Rubric4 = c.Rubric4?.Trim();
                    existing.Rubric5 = c.Rubric5?.Trim();
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    existing.IsDeleted = false;
                }
            }
            else
            {
                framework.Criteria.Add(new CompetencyCriterion
                {
                    Id = Guid.NewGuid(),
                    CompetencyFrameworkId = framework.Id,
                    Name = c.Name.Trim(),
                    Description = c.Description?.Trim(),
                    Weight = c.Weight,
                    Rubric1 = c.Rubric1?.Trim(),
                    Rubric2 = c.Rubric2?.Trim(),
                    Rubric3 = c.Rubric3?.Trim(),
                    Rubric4 = c.Rubric4?.Trim(),
                    Rubric5 = c.Rubric5?.Trim(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        // Cập nhật gán chức danh
        var currentPositionIds = framework.AssignedPositions.Where(p => !p.IsDeleted).Select(p => p.Id).ToHashSet();
        var targetPositionIds = (model.AssignedPositionIds ?? []).ToHashSet();

        // Gỡ bỏ chức danh không còn chọn
        var toUnassign = framework.AssignedPositions.Where(p => !targetPositionIds.Contains(p.Id)).ToList();
        foreach (var pos in toUnassign)
        {
            pos.CompetencyFrameworkId = null;
            pos.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // Thêm chức danh mới chọn
        var toAssignIds = targetPositionIds.Except(currentPositionIds).ToList();
        if (toAssignIds.Any())
        {
            var newPositions = await _dbContext.JobPositions
                .Where(p => toAssignIds.Contains(p.Id) && !p.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var pos in newPositions)
            {
                pos.CompetencyFrameworkId = framework.Id;
                pos.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation("Đã cập nhật khung năng lực {Code} (ID: {Id})", framework.Code, framework.Id);
        return (true, $"Cập nhật khung năng lực '{framework.Name}' thành công.");
    }

    public async Task<(bool Success, string Message)> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var framework = await _dbContext.CompetencyFrameworks
            .Include(f => f.AssignedPositions)
            .Include(f => f.Criteria)
            .FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, cancellationToken);

        if (framework == null)
        {
            return (false, "Không tìm thấy khung năng lực cần xóa.");
        }

        // Gỡ bỏ liên kết với tất cả chức danh đang áp dụng
        foreach (var pos in framework.AssignedPositions)
        {
            pos.CompetencyFrameworkId = null;
            pos.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // Soft delete tiêu chí
        foreach (var c in framework.Criteria)
        {
            c.IsDeleted = true;
            c.DeletedAt = DateTimeOffset.UtcNow;
        }

        framework.IsDeleted = true;
        framework.DeletedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation("Đã xóa khung năng lực {Code} (ID: {Id})", framework.Code, framework.Id);
        return (true, $"Đã xóa khung năng lực '{framework.Name}' thành công.");
    }

    public async Task<(bool Success, string Message)> ToggleActiveAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var framework = await _dbContext.CompetencyFrameworks
            .FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, cancellationToken);

        if (framework == null)
        {
            return (false, "Không tìm thấy khung năng lực.");
        }

        framework.IsActive = !framework.IsActive;
        framework.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var statusText = framework.IsActive ? "Đang sử dụng" : "Ngừng sử dụng";
        return (true, $"Đã chuyển trạng thái khung năng lực '{framework.Name}' sang '{statusText}'.");
    }

    public async Task<(bool Success, string Message)> AssignPositionAsync(
        Guid frameworkId,
        Guid positionId,
        CancellationToken cancellationToken = default)
    {
        var framework = await _dbContext.CompetencyFrameworks
            .FirstOrDefaultAsync(f => f.Id == frameworkId && !f.IsDeleted, cancellationToken);

        if (framework == null)
        {
            return (false, "Không tìm thấy khung năng lực.");
        }

        var position = await _dbContext.JobPositions
            .FirstOrDefaultAsync(p => p.Id == positionId && !p.IsDeleted, cancellationToken);

        if (position == null)
        {
            return (false, "Không tìm thấy chức danh.");
        }

        position.CompetencyFrameworkId = framework.Id;
        position.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (true, $"Đã gán chức danh '{position.Title}' vào khung năng lực '{framework.Name}'.");
    }

    public async Task<(bool Success, string Message)> UnassignPositionAsync(
        Guid frameworkId,
        Guid positionId,
        CancellationToken cancellationToken = default)
    {
        var position = await _dbContext.JobPositions
            .FirstOrDefaultAsync(p => p.Id == positionId && p.CompetencyFrameworkId == frameworkId && !p.IsDeleted, cancellationToken);

        if (position == null)
        {
            return (false, "Không tìm thấy chức danh áp dụng cho khung năng lực này.");
        }

        position.CompetencyFrameworkId = null;
        position.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (true, $"Đã gỡ chức danh '{position.Title}' khỏi khung năng lực.");
    }

    public async Task<CompetencyFrameworkDetailViewModel?> GetByPositionAsync(
        Guid jobPositionId,
        CancellationToken cancellationToken = default)
    {
        var position = await _dbContext.JobPositions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == jobPositionId && !p.IsDeleted, cancellationToken);

        if (position?.CompetencyFrameworkId == null)
        {
            return null;
        }

        return await GetDetailAsync(position.CompetencyFrameworkId.Value, cancellationToken);
    }

    public async Task<(bool Success, string Message, Guid? NewId)> CloneAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var source = await _dbContext.CompetencyFrameworks
            .Include(f => f.Criteria)
            .FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, cancellationToken);

        if (source == null)
        {
            return (false, "Không tìm thấy khung năng lực cần nhân bản.", null);
        }

        // Tạo mã mới tránh trùng lặp
        var baseCode = source.Code.Length > 20 ? source.Code[..20] : source.Code;
        var newCode = $"{baseCode}-CPY";
        var counter = 1;
        while (await _dbContext.CompetencyFrameworks.AnyAsync(f => f.Code == newCode && !f.IsDeleted, cancellationToken))
        {
            newCode = $"{baseCode}-C{counter:D2}";
            counter++;
        }

        var newFramework = new CompetencyFramework
        {
            Id = Guid.NewGuid(),
            Code = newCode,
            Name = $"[Bản sao] {source.Name}".Trim(),
            Description = source.Description,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        foreach (var c in source.Criteria.Where(x => !x.IsDeleted))
        {
            newFramework.Criteria.Add(new CompetencyCriterion
            {
                Id = Guid.NewGuid(),
                CompetencyFrameworkId = newFramework.Id,
                Name = c.Name,
                Description = c.Description,
                Weight = c.Weight,
                Rubric1 = c.Rubric1,
                Rubric2 = c.Rubric2,
                Rubric3 = c.Rubric3,
                Rubric4 = c.Rubric4,
                Rubric5 = c.Rubric5,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await _dbContext.CompetencyFrameworks.AddAsync(newFramework, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation("Đã nhân bản khung năng lực {SourceCode} thành {NewCode} (ID: {NewId})", source.Code, newFramework.Code, newFramework.Id);
        return (true, $"Nhân bản khung năng lực thành công với mã mới '{newFramework.Code}'.", newFramework.Id);
    }

    private async Task EnsureSeedAsync(CancellationToken cancellationToken)
    {
        if (await _dbContext.CompetencyFrameworks.AnyAsync(cancellationToken))
        {
            return;
        }

        _logger?.LogInformation("Đang khởi tạo danh mục Khung năng lực chuẩn lần đầu vào cơ sở dữ liệu...");

        var existingPositions = await _dbContext.JobPositions
            .Where(p => !p.IsDeleted)
            .ToListAsync(cancellationToken);

        var baseCategories = new[]
        {
            ("Kỹ thuật phần mềm (Software Engineering)", "TECH", new[]
            {
                "Kỹ sư Backend C# / .NET Core",
                "Kỹ sư Frontend React & TypeScript",
                "Kỹ sư Fullstack Web & Cloud",
                "Kỹ sư Lập trình Di động iOS (Swift)",
                "Kỹ sư Lập trình Di động Android (Kotlin)",
                "Kỹ sư Phần mềm Nhúng (Embedded C/C++)",
                "Kiến trúc sư Giải pháp Phần mềm (Solution Architect)",
                "Kỹ sư Lập trình Golang Microservices",
                "Kỹ sư Lập trình Java Spring Boot",
                "Kỹ sư Lập trình Python FastAPI & Async"
            }),
            ("Trí tuệ nhân tạo & Dữ liệu (AI & Data)", "AI", new[]
            {
                "Kỹ sư Học máy & MLOps (Machine Learning Engineer)",
                "Kỹ sư Dữ liệu Lớn (Big Data Engineer)",
                "Nhà Khoa học Dữ liệu (Data Scientist)",
                "Chuyên viên Phân tích Dữ liệu Kinh doanh (BI Analyst)",
                "Kỹ sư Trí tuệ Nhân tạo & LLM Application",
                "Kỹ sư Xử lý Ngôn ngữ Tự nhiên (NLP Specialist)",
                "Kỹ sư Thị giác Máy tính (Computer Vision Engineer)",
                "Kỹ sư Tối ưu hóa Vector Database & RAG"
            }),
            ("Hạ tầng, Đám mây & Vận hành (Cloud & DevOps)", "OPS", new[]
            {
                "Kỹ sư DevOps & CI/CD Pipeline Automation",
                "Kỹ sư Độ tin cậy Hệ thống (Site Reliability Engineer - SRE)",
                "Kiến trúc sư Đám mây AWS / Azure",
                "Kỹ sư Quản trị Cụm Kubernetes & Service Mesh",
                "Chuyên viên Quản trị Cơ sở dữ liệu PostgreSQL",
                "Kỹ sư Hạ tầng Mạng Doanh nghiệp & Hybrid Cloud"
            }),
            ("Bảo mật & An toàn thông tin (Security)", "SEC", new[]
            {
                "Kỹ sư An toàn Thông tin & Ứng phó Sự cố (SOC)",
                "Chuyên gia Đánh giá Lỗ hổng & Kiểm thử Xâm nhập (Pentest)",
                "Kỹ sư DevSecOps & Bảo mật Ứng dụng (AppSec)",
                "Chuyên viên Quản trị Tuân thủ Bảo mật (GRC & ISO 27001)"
            }),
            ("Quản trị Sản phẩm & Thiết kế (Product & Design)", "PROD", new[]
            {
                "Giám đốc Sản phẩm (Product Manager - Enterprise)",
                "Chủ sở hữu Sản phẩm (Product Owner - Agile)",
                "Chuyên viên Thiết kế Trải nghiệm Người dùng (Senior UI/UX)",
                "Chuyên viên Nghiên cứu Người dùng (User Researcher)",
                "Kỹ sư Thiết kế Hệ thống UI (Design System Specialist)"
            }),
            ("Đảm bảo Chất lượng Phần mềm (QA & Testing)", "QA", new[]
            {
                "Kỹ sư Kiểm thử Tự động (Automation QA Engineer)",
                "Chuyên viên Kiểm thử Chức năng (Manual QA Specialist)",
                "Kỹ sư Kiểm thử Hiệu năng & Tải (Performance QA)",
                "Trưởng nhóm Đảm bảo Chất lượng Phần mềm (QA Lead)"
            }),
            ("Quản lý, Lãnh đạo & Điều hành (Management)", "MGT", new[]
            {
                "Trưởng nhóm Kỹ thuật (Engineering Lead)",
                "Quản lý Kỹ thuật Phần mềm (Engineering Manager)",
                "Chuyên gia Điều phối Dự án Agile (Scrum Master)",
                "Giám đốc Khối Công nghệ (Head of Technology)",
                "Quản lý Chương trình Kỹ thuật (Technical Program Manager)"
            }),
            ("Nhân sự, Tuyển dụng & Vận hành (HR & Operations)", "CORP", new[]
            {
                "Trưởng phòng Tuyển dụng Tài năng Công nghệ (Lead IT Recruiter)",
                "Chuyên viên Quản trị Nhân sự & Đãi ngộ (HR & C&B Specialist)",
                "Chuyên viên Đào tạo & Phát triển Năng lực (L&D Specialist)",
                "Chuyên viên Vận hành Hệ thống ATS & Tuyển dụng Nội bộ"
            })
        };

        var seedIndex = 1;
        var fixedDate = new DateTimeOffset(2026, 9, 1, 8, 30, 0, TimeSpan.Zero);
        var frameworksToSeed = new List<CompetencyFramework>();

        foreach (var (catName, catPrefix, titles) in baseCategories)
        {
            foreach (var title in titles)
            {
                var frameworkCode = $"CF-{catPrefix}-{seedIndex:D3}";
                var fw = new CompetencyFramework
                {
                    Id = Guid.NewGuid(),
                    Code = frameworkCode,
                    Name = $"Khung năng lực {title}",
                    Description = $"Bộ tiêu chuẩn năng lực chuyên môn, kỹ năng cốt lõi và tiêu chí đánh giá định ngạch cho {title} tại NoveraTech.",
                    IsActive = seedIndex % 9 != 0,
                    CreatedAt = fixedDate.AddDays(seedIndex),
                    UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-seedIndex)
                };

                fw.Criteria.Add(new CompetencyCriterion
                {
                    Id = Guid.NewGuid(),
                    CompetencyFrameworkId = fw.Id,
                    Name = $"Kiến thức & Kỹ năng Chuyên môn {title}",
                    Description = "Khả năng làm chủ và ứng dụng chuyên sâu kiến thức kỹ thuật vào thực tế.",
                    Weight = 40,
                    Rubric1 = "Mức 1: Cơ bản",
                    Rubric2 = "Mức 2: Khá",
                    Rubric3 = "Mức 3: Thành thạo",
                    Rubric4 = "Mức 4: Nâng cao",
                    Rubric5 = "Mức 5: Chuyên gia",
                    CreatedAt = fw.CreatedAt,
                    UpdatedAt = fw.UpdatedAt
                });

                fw.Criteria.Add(new CompetencyCriterion
                {
                    Id = Guid.NewGuid(),
                    CompetencyFrameworkId = fw.Id,
                    Name = "Tư duy giải quyết vấn đề & Phân tích",
                    Description = "Khả năng phân tích nguyên nhân gốc rễ và xử lý bài toán kỹ thuật phức tạp.",
                    Weight = 30,
                    Rubric1 = "Mức 1: Cơ bản",
                    Rubric2 = "Mức 2: Khá",
                    Rubric3 = "Mức 3: Thành thạo",
                    Rubric4 = "Mức 4: Nâng cao",
                    Rubric5 = "Mức 5: Chuyên gia",
                    CreatedAt = fw.CreatedAt,
                    UpdatedAt = fw.UpdatedAt
                });

                fw.Criteria.Add(new CompetencyCriterion
                {
                    Id = Guid.NewGuid(),
                    CompetencyFrameworkId = fw.Id,
                    Name = "Giao tiếp, Phối hợp & Trách nhiệm",
                    Description = "Kỹ năng làm việc nhóm và phối hợp chuyên môn hiệu quả.",
                    Weight = 30,
                    Rubric1 = "Mức 1: Cơ bản",
                    Rubric2 = "Mức 2: Khá",
                    Rubric3 = "Mức 3: Thành thạo",
                    Rubric4 = "Mức 4: Nâng cao",
                    Rubric5 = "Mức 5: Chuyên gia",
                    CreatedAt = fw.CreatedAt,
                    UpdatedAt = fw.UpdatedAt
                });

                // Gán chức danh nếu trùng tên hoặc mã tiền tố
                var matched = existingPositions
                    .Where(p => p.CompetencyFrameworkId == null &&
                                (p.Title.Contains(title) ||
                                 p.Code.Contains(catPrefix, StringComparison.OrdinalIgnoreCase) ||
                                 (catPrefix == "TECH" && p.Code.Contains("DEV", StringComparison.OrdinalIgnoreCase))))
                    .ToList();
                foreach (var pos in matched)
                {
                    pos.CompetencyFrameworkId = fw.Id;
                }

                frameworksToSeed.Add(fw);
                seedIndex++;
            }
        }

        while (frameworksToSeed.Count < 215)
        {
            var num = frameworksToSeed.Count + 1;
            var subCat = baseCategories[num % baseCategories.Length];
            var fCode = $"CF-SPEC-{num:D3}";
            var fw = new CompetencyFramework
            {
                Id = Guid.NewGuid(),
                Code = fCode,
                Name = $"Khung năng lực chuyên sâu {subCat.Item3[num % subCat.Item3.Length]} (Phân hệ {num})",
                Description = $"Bộ khung tiêu chí thẩm định năng lực đặc thù cho kỹ sư phụ trách phân hệ {num}.",
                IsActive = num % 11 != 0,
                CreatedAt = fixedDate.AddHours(num * 6),
                UpdatedAt = fixedDate.AddHours(num * 6)
            };

            fw.Criteria.Add(new CompetencyCriterion
            {
                Id = Guid.NewGuid(),
                CompetencyFrameworkId = fw.Id,
                Name = "Năng lực chuyên môn phân hệ",
                Weight = 50,
                CreatedAt = fw.CreatedAt,
                UpdatedAt = fw.UpdatedAt
            });
            fw.Criteria.Add(new CompetencyCriterion
            {
                Id = Guid.NewGuid(),
                CompetencyFrameworkId = fw.Id,
                Name = "Hiệu suất & Chất lượng công việc",
                Weight = 50,
                CreatedAt = fw.CreatedAt,
                UpdatedAt = fw.UpdatedAt
            });

            frameworksToSeed.Add(fw);
        }

        // Đảm bảo các vị trí hiện có trong hệ thống (như trong unit test hoặc dữ liệu thực)
        // được gán vào các framework đầu tiên để luôn xuất hiện trên trang 1
        if (existingPositions.Any() && frameworksToSeed.Any())
        {
            var latestDate = frameworksToSeed.Max(f => f.UpdatedAt).AddMinutes(10);
            for (int i = 0; i < existingPositions.Count && i < frameworksToSeed.Count; i++)
            {
                existingPositions[i].CompetencyFrameworkId = frameworksToSeed[i].Id;
                frameworksToSeed[i].AssignedPositions.Add(existingPositions[i]);
                frameworksToSeed[i].UpdatedAt = latestDate.AddMinutes(i + 1);
                _dbContext.JobPositions.Update(existingPositions[i]);
            }
        }

        await _dbContext.CompetencyFrameworks.AddRangeAsync(frameworksToSeed, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger?.LogInformation("Khởi tạo {Count} danh mục Khung năng lực chuẩn thành công.", frameworksToSeed.Count);
    }
}
