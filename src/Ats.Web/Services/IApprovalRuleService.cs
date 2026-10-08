using Ats.Web.Models.DTOs;
using Ats.Web.Models.DTOs.ApprovalRule;

namespace Ats.Web.Services;

public interface IApprovalRuleService
{
    Task<BaseResponse<List<ApprovalRuleDto>>> GetAllRulesAsync(Guid? departmentId, CancellationToken cancellationToken = default);
    Task<BaseResponse<ApprovalRuleDto>> GetRuleByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BaseResponse<ApprovalRuleDto>> CreateRuleAsync(CreateApprovalRuleDto request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<BaseResponse<ApprovalRuleDto>> UpdateRuleAsync(Guid id, UpdateApprovalRuleDto request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<BaseResponse<bool>> DeleteRuleAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BaseResponse<List<ApprovalRuleStepDto>>> GetApplicableStepsAsync(Guid? departmentId, decimal proposedSalary, CancellationToken cancellationToken = default);
}
