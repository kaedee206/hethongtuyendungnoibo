using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Requisitions;

public class RequisitionApprovalListItemViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public HeadcountType HeadcountType { get; set; }
    public string SalaryDisplay { get; set; } = string.Empty;
    public string TargetHireDateDisplay { get; set; } = string.Empty;
    public string HiringManagerName { get; set; } = string.Empty;
    public RequisitionStatus Status { get; set; }
    public int CurrentStepOrder { get; set; }
    public string CurrentApproverName { get; set; } = string.Empty;
    public string CurrentApproverRole { get; set; } = string.Empty;
    public string CurrentApproverEmail { get; set; } = string.Empty;
    public bool CanCurrentUserApprove { get; set; }
    public string CreatedAtDisplay { get; set; } = string.Empty;
    public string? LatestComment { get; set; }
    public string WaitingSinceDisplay { get; set; } = string.Empty;
    public string WaitingDurationDisplay { get; set; } = string.Empty;
    public string ApprovalChainProgress { get; set; } = string.Empty;
    public bool IsCreatedByCurrentUser { get; set; }
    
    // Thuộc tính mới cho SCRUM-283
    public int OpenDays { get; set; }
    public int? RemainingDays { get; set; }
    public bool IsOverdue { get; set; }
    public string? AssignedRecruiterName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? AssignedRecruiterId { get; set; }
}

public class RequisitionDetailsViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string JobPositionTitle { get; set; } = string.Empty;
    public string? JobLevel { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string HiringManagerName { get; set; } = string.Empty;
    public string HiringManagerEmail { get; set; } = string.Empty;
    public string? AssignedRecruiterName { get; set; }
    public int Quantity { get; set; }
    public HeadcountType HeadcountType { get; set; }
    public string? Reason { get; set; }
    public string SalaryDisplay { get; set; } = string.Empty;
    public string? SalaryBandExplanation { get; set; }
    public string TargetHireDateDisplay { get; set; } = string.Empty;
    public RequisitionStatus Status { get; set; }
    public string? JobDescription { get; set; }
    public string? Requirements { get; set; }
    public string CreatedAtDisplay { get; set; } = string.Empty;
    public bool CanCurrentUserApprove { get; set; }
    public int? CurrentPendingStepOrder { get; set; }

    // Thông tin vị trí bàn làm việc hiện tại (Scrum #23)
    public string CurrentApproverName { get; set; } = string.Empty;
    public string CurrentApproverRole { get; set; } = string.Empty;
    public string CurrentApproverEmail { get; set; } = string.Empty;
    public string CurrentPendingSinceDisplay { get; set; } = string.Empty;
    public string CurrentWaitingDurationDisplay { get; set; } = string.Empty;
    public string CurrentStageSummary { get; set; } = string.Empty;
    public bool IsCurrentUserHiringManager { get; set; }

    public List<RequisitionApprovalStepDto> ApprovalSteps { get; set; } = new();
}

public class RequisitionApprovalStepDto
{
    public int StepOrder { get; set; }
    public string StepTitle { get; set; } = string.Empty;
    public Guid ApproverId { get; set; }
    public string ApproverName { get; set; } = string.Empty;
    public string ApproverRole { get; set; } = string.Empty;
    public string ApproverEmail { get; set; } = string.Empty;
    public ApprovalStatus Status { get; set; } = ApprovalStatus.PENDING;
    public string? Comment { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string DecidedAtDisplay { get; set; } = string.Empty;
    public string StepCreatedAtDisplay { get; set; } = string.Empty;
    public string DurationDisplay { get; set; } = string.Empty;
    public int RoundNumber { get; set; } = 1;
    public bool IsCurrent { get; set; }
    public bool IsImmutable { get; set; } = true;
}

public class RequisitionApprovalDecisionInputModel
{
    public Guid RequisitionId { get; set; }
    public string Action { get; set; } = string.Empty; // APPROVE, REJECT, REQUEST_CHANGES
    public string? Comment { get; set; }
}
