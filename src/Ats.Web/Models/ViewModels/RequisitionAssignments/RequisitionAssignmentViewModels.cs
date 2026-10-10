using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.RequisitionAssignments;

/// <summary>
/// ViewModel hiển thị một dòng yêu cầu tuyển dụng trong bảng phân công recruiter.
/// </summary>
public class RequisitionAssignmentItemViewModel
{
    public Guid RequisitionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string JobPositionTitle { get; set; } = string.Empty;
    public string JobTitle { get => JobPositionTitle; set => JobPositionTitle = value; }
    public string JobPositionCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public string? JobLevel { get; set; }
    public int Quantity { get; set; }
    public RequisitionStatus Status { get; set; }
    public DateOnly? TargetHireDate { get; set; }

    public string StatusDisplay => Status switch
    {
        RequisitionStatus.APPROVED => "Đang mở tuyển",
        RequisitionStatus.PENDING_APPROVAL => "Chờ duyệt",
        RequisitionStatus.REJECTED => "Từ chối",
        RequisitionStatus.FULFILLED => "Đã hoàn thành",
        RequisitionStatus.CANCELLED => "Đã hủy",
        _ => Status.ToString()
    };

    public string StatusBadgeClass => Status switch
    {
        RequisitionStatus.APPROVED => "bg-success-subtle text-success border border-success-subtle",
        RequisitionStatus.PENDING_APPROVAL => "bg-warning-subtle text-warning-emphasis border border-warning-subtle",
        RequisitionStatus.REJECTED => "bg-danger-subtle text-danger border border-danger-subtle",
        _ => "bg-secondary-subtle text-secondary"
    };

    public bool HasLeadRecruiter => LeadRecruiterId.HasValue;

    // Recruiter chính (Lead / Primary)
    public Guid? LeadRecruiterId { get; set; }
    public string? LeadRecruiterName { get; set; }
    public string? LeadRecruiterEmail { get; set; }
    public string? LeadRecruiterAvatar { get; set; }
    public string LeadRecruiterAvatarLetter => !string.IsNullOrWhiteSpace(LeadRecruiterName) ? LeadRecruiterName.Trim()[0].ToString().ToUpper() : "R";
    public DateTimeOffset? LeadAssignedAt { get; set; }

    // Danh sách các Recruiter hỗ trợ (Supporting)
    public List<SupportingRecruiterDto> SupportingRecruiters { get; set; } = new();

    // Thống kê hồ sơ ứng viên
    public int TotalCandidatesCount { get; set; }
    public int ApplicantCount { get => TotalCandidatesCount; set => TotalCandidatesCount = value; }
    public int InProcessCandidatesCount { get; set; }

    // Lịch sử chuyển giao
    public int HandoverCount { get; set; }
    public DateTimeOffset? LastHandoverAt { get; set; }
}

public class SupportingRecruiterDto
{
    public Guid RecruiterId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
}

/// <summary>
/// ViewModel chính cho trang Quản lý Phân công Recruiter.
/// </summary>
public class RequisitionAssignmentListViewModel
{
    public string? Search { get; set; }
    public Guid? SelectedDepartmentId { get; set; }
    public Guid? SelectedRecruiterId { get; set; }
    public string? AssignmentFilter { get; set; } // ALL, ASSIGNED, UNASSIGNED

    // KPI Cards
    public int TotalOpenRequisitions { get; set; }
    public int TotalRequisitions { get => TotalOpenRequisitions; set => TotalOpenRequisitions = value; }
    public int AssignedRequisitionsCount { get; set; }
    public int UnassignedRequisitionsCount { get; set; }
    public int TotalActiveRecruitersCount { get; set; }
    public int TotalRecruitersCount { get => TotalActiveRecruitersCount; set => TotalActiveRecruitersCount = value; }

    public List<string> Departments { get; set; } = new();
    public List<RequisitionAssignmentItemViewModel> Items { get; set; } = new();
    public List<RecruiterOptionViewModel> AvailableRecruiters { get; set; } = new();
}

/// <summary>
/// Model nhận request phân công / chuyển giao recruiter.
/// </summary>
public class RequisitionAssignRequestModel
{
    public Guid RequisitionId { get; set; }
    
    /// <summary>
    /// Recruiter chính (bắt buộc chọn 1 người)
    /// </summary>
    public Guid LeadRecruiterId { get; set; }
    
    /// <summary>
    /// Danh sách các Recruiter hỗ trợ (tùy chọn nhiều người)
    /// </summary>
    public List<Guid> SupportingRecruiterIds { get; set; } = new();

    /// <summary>
    /// Lý do chuyển giao (Bắt buộc nếu thay đổi Recruiter chính)
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Ghi chú / Dặn dò bàn giao công việc
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// ViewModel hiển thị từng dòng trong lịch sử chuyển giao.
/// </summary>
public class RequisitionHandoverHistoryViewModel
{
    public Guid Id { get; set; }
    public Guid RequisitionId { get; set; }
    public string RequisitionCode { get; set; } = string.Empty;
    public string JobPositionTitle { get; set; } = string.Empty;

    public Guid? FromRecruiterId { get; set; }
    public string? FromRecruiterName { get; set; }
    public string? FromRecruiterEmail { get; set; }

    public Guid ToRecruiterId { get; set; }
    public string ToRecruiterName { get; set; } = string.Empty;
    public string ToRecruiterEmail { get; set; } = string.Empty;

    public string HandoverType { get; set; } = "LEAD_HANDOVER";
    public string HandoverTypeDisplay => HandoverType switch
    {
        "INITIAL_ASSIGNMENT" => "Phân công ban đầu",
        "LEAD_HANDOVER" => "Chuyển giao người phụ trách chính",
        "PRIMARY_TRANSFER" => "Đổi người phụ trách chính",
        "SUPPORT_ADDED" => "Thêm người hỗ trợ",
        "SUPPORT_REMOVED" => "Gỡ người hỗ trợ",
        "SUPPORT_UPDATE" => "Cập nhật đội ngũ hỗ trợ",
        _ => "Chuyển giao phân công"
    };

    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public string TransferredByName { get; set; } = string.Empty;
    public DateTimeOffset TransferredAt { get; set; }
    public string TransferredAtDisplay => TransferredAt.ToString("dd/MM/yyyy HH:mm");
}

/// <summary>
/// DTO thông tin recruiter để chọn trong dropdown/checklist.
/// </summary>
public class RecruiterOptionViewModel
{
    public Guid Id { get; set; }
    public Guid UserId { get => Id; set => Id = value; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string? JobTitle { get; set; }
    public int ActiveLeadCount { get; set; }
    public int ActiveSupportCount { get; set; }
    public int ActiveSupportingCount { get => ActiveSupportCount; set => ActiveSupportCount = value; }
}
