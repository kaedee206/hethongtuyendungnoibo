using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.DTOs.ApprovalRule;
using Ats.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

public class ApprovalRuleService : IApprovalRuleService
{
    private readonly ApplicationDbContext _dbContext;

    public ApprovalRuleService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BaseResponse<List<ApprovalRuleDto>>> GetAllRulesAsync(Guid? departmentId, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ApprovalRules
            .Include(r => r.Department)
            .Include(r => r.Steps)
                .ThenInclude(s => s.ApproverRole)
            .Include(r => r.Steps)
                .ThenInclude(s => s.ApproverUser)
            .AsNoTracking()
            .Where(r => !r.IsDeleted);

        if (departmentId.HasValue)
        {
            query = query.Where(r => r.DepartmentId == departmentId.Value || r.DepartmentId == null);
        }

        var rules = await query.OrderBy(r => r.DepartmentId).ThenBy(r => r.MinSalary).ToListAsync(cancellationToken);

        var dtos = rules.Select(MapToDto).ToList();
        return BaseResponse<List<ApprovalRuleDto>>.SuccessResponse(dtos);
    }

    public async Task<BaseResponse<ApprovalRuleDto>> GetRuleByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await _dbContext.ApprovalRules
            .Include(r => r.Department)
            .Include(r => r.Steps)
                .ThenInclude(s => s.ApproverRole)
            .Include(r => r.Steps)
                .ThenInclude(s => s.ApproverUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken);

        if (rule == null)
        {
            return BaseResponse<ApprovalRuleDto>.ErrorResponse("Không tìm thấy quy trình duyệt này.");
        }

        return BaseResponse<ApprovalRuleDto>.SuccessResponse(MapToDto(rule));
    }

    public async Task<BaseResponse<ApprovalRuleDto>> CreateRuleAsync(CreateApprovalRuleDto request, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        if (request.Steps == null || !request.Steps.Any())
        {
            return BaseResponse<ApprovalRuleDto>.ErrorResponse("Phải cấu hình ít nhất 1 cấp duyệt.");
        }

        foreach (var step in request.Steps)
        {
            if (step.ApproverRoleId == null && step.ApproverUserId == null)
            {
                return BaseResponse<ApprovalRuleDto>.ErrorResponse($"Cấp duyệt {step.StepOrder} phải chọn Role hoặc User phê duyệt.");
            }
        }

        var newRule = new ApprovalRule
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DepartmentId = request.DepartmentId,
            MinSalary = request.MinSalary,
            MaxSalary = request.MaxSalary,
            IsActive = request.IsActive,
            CreatedById = currentUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            Steps = request.Steps.Select(s => new ApprovalRuleStep
            {
                Id = Guid.NewGuid(),
                StepOrder = s.StepOrder,
                ApproverRoleId = s.ApproverRoleId,
                ApproverUserId = s.ApproverUserId,
                CreatedById = currentUserId,
                CreatedAt = DateTimeOffset.UtcNow
            }).ToList()
        };

        _dbContext.ApprovalRules.Add(newRule);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetRuleByIdAsync(newRule.Id, cancellationToken);
    }

    public async Task<BaseResponse<ApprovalRuleDto>> UpdateRuleAsync(Guid id, UpdateApprovalRuleDto request, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var rule = await _dbContext.ApprovalRules
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken);

        if (rule == null)
        {
            return BaseResponse<ApprovalRuleDto>.ErrorResponse("Không tìm thấy quy trình duyệt này.");
        }
        
        if (request.Steps == null || !request.Steps.Any())
        {
            return BaseResponse<ApprovalRuleDto>.ErrorResponse("Phải cấu hình ít nhất 1 cấp duyệt.");
        }

        foreach (var step in request.Steps)
        {
            if (step.ApproverRoleId == null && step.ApproverUserId == null)
            {
                return BaseResponse<ApprovalRuleDto>.ErrorResponse($"Cấp duyệt {step.StepOrder} phải chọn Role hoặc User phê duyệt.");
            }
        }

        rule.Name = request.Name;
        rule.DepartmentId = request.DepartmentId;
        rule.MinSalary = request.MinSalary;
        rule.MaxSalary = request.MaxSalary;
        rule.IsActive = request.IsActive;
        rule.UpdatedById = currentUserId;
        rule.UpdatedAt = DateTimeOffset.UtcNow;

        _dbContext.ApprovalRuleSteps.RemoveRange(rule.Steps);

        rule.Steps = request.Steps.Select(s => new ApprovalRuleStep
            {
                Id = Guid.NewGuid(),
                ApprovalRuleId = rule.Id,
                StepOrder = s.StepOrder,
                ApproverRoleId = s.ApproverRoleId,
                ApproverUserId = s.ApproverUserId,
                CreatedById = currentUserId,
                CreatedAt = DateTimeOffset.UtcNow
            }).ToList();

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetRuleByIdAsync(rule.Id, cancellationToken);
    }

    public async Task<BaseResponse<bool>> DeleteRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await _dbContext.ApprovalRules.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken);
        if (rule == null)
        {
            return BaseResponse<bool>.ErrorResponse("Không tìm thấy quy trình duyệt này.");
        }

        rule.IsDeleted = true;
        rule.DeletedAt = DateTimeOffset.UtcNow;
        
        await _dbContext.SaveChangesAsync(cancellationToken);
        return BaseResponse<bool>.SuccessResponse(true, "Xóa quy trình duyệt thành công.");
    }

    public async Task<BaseResponse<List<ApprovalRuleStepDto>>> GetApplicableStepsAsync(Guid? departmentId, decimal proposedSalary, CancellationToken cancellationToken = default)
    {
        // Find rules matching department and salary
        // Fallback to global rules (DepartmentId == null) if no specific department rule is found for the salary
        
        var query = _dbContext.ApprovalRules
            .Include(r => r.Steps)
                .ThenInclude(s => s.ApproverRole)
            .Include(r => r.Steps)
                .ThenInclude(s => s.ApproverUser)
            .AsNoTracking()
            .Where(r => !r.IsDeleted && r.IsActive)
            .Where(r => r.MinSalary <= proposedSalary && (r.MaxSalary == null || proposedSalary <= r.MaxSalary));

        var possibleRules = await query.ToListAsync(cancellationToken);
        
        // Priority 1: Exact Department Match
        var applicableRule = possibleRules.FirstOrDefault(r => r.DepartmentId == departmentId) 
                          ?? possibleRules.FirstOrDefault(r => r.DepartmentId == null);

        if (applicableRule == null)
        {
            return BaseResponse<List<ApprovalRuleStepDto>>.ErrorResponse("Không tìm thấy quy trình duyệt phù hợp cho mức lương và phòng ban này.");
        }

        var stepDtos = applicableRule.Steps.OrderBy(s => s.StepOrder).Select(MapToStepDto).ToList();
        return BaseResponse<List<ApprovalRuleStepDto>>.SuccessResponse(stepDtos);
    }

    private ApprovalRuleDto MapToDto(ApprovalRule rule)
    {
        return new ApprovalRuleDto
        {
            Id = rule.Id,
            Name = rule.Name,
            DepartmentId = rule.DepartmentId,
            DepartmentName = rule.Department?.Name,
            MinSalary = rule.MinSalary,
            MaxSalary = rule.MaxSalary,
            IsActive = rule.IsActive,
            Steps = rule.Steps.OrderBy(s => s.StepOrder).Select(MapToStepDto).ToList()
        };
    }

    private ApprovalRuleStepDto MapToStepDto(ApprovalRuleStep step)
    {
        return new ApprovalRuleStepDto
        {
            Id = step.Id,
            StepOrder = step.StepOrder,
            ApproverRoleId = step.ApproverRoleId,
            ApproverRoleName = step.ApproverRole?.Name,
            ApproverUserId = step.ApproverUserId,
            ApproverUserName = step.ApproverUser?.FullName
        };
    }
}
