using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.JobPostings;

/// <summary>
/// ViewModel chứa dữ liệu chi tiết của yêu cầu tuyển dụng (Requisition) đã duyệt để truyền sang form soạn tin tuyển dụng (SCRUM-29).
/// </summary>
public class JobPostingPreFillViewModel
{
    // Thông tin gốc của Requisition
    public Guid RequisitionId { get; set; }
    public string RequisitionCode { get; set; } = string.Empty;
    public Guid? JobPositionId { get; set; }
    public string JobPositionTitle { get; set; } = string.Empty;
    public string? JobPositionCode { get; set; }
    public Guid? DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public Guid HiringManagerId { get; set; }
    public string HiringManagerName { get; set; } = string.Empty;
    public string HiringManagerEmail { get; set; } = string.Empty;
    public Guid? AssignedRecruiterId { get; set; }
    public string? AssignedRecruiterName { get; set; }
    public int Quantity { get; set; }
    public HeadcountType HeadcountType { get; set; }
    public string HeadcountTypeName { get; set; } = string.Empty;
    public DateOnly? TargetHireDate { get; set; }
    public RequisitionStatus RequisitionStatus { get; set; }
    public string RequisitionStatusName { get; set; } = string.Empty;
    public DateTimeOffset? ApprovedAt { get; set; }
    public int ExistingJobPostingsCount { get; set; }

    // Dữ liệu kế thừa (sẵn sàng đổ vào input form)
    public string Title { get; set; } = string.Empty;
    public string? JobDescription { get; set; }
    public string? Requirements { get; set; }
    public string? Benefits { get; set; }
    public decimal? MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
    public string Currency { get; set; } = "VND";
    public string SalaryDisplay { get; set; } = string.Empty;

    // Gợi ý mặc định cho các trường khai báo mới
    public string SuggestedWorkLocation { get; set; } = "Hà Nội (Hybrid 2 ngày WFH)";
    public EmploymentType SuggestedEmploymentType { get; set; } = EmploymentType.FULL_TIME;
    public DateTime? SuggestedDeadline { get; set; }
    public bool IsSalaryDisclosed { get; set; } = true;

    /// <summary>
    /// Chuyển đổi sang InputModel ban đầu phục vụ binding lên giao diện soạn thảo.
    /// </summary>
    public JobPostingCreateInputModel ToCreateInputModel()
    {
        return new JobPostingCreateInputModel
        {
            RequisitionId = RequisitionId,
            RequisitionCode = RequisitionCode,
            DepartmentName = DepartmentName,
            JobPositionTitle = JobPositionTitle,
            Quantity = Quantity,
            TargetHireDate = TargetHireDate,
            Title = Title,
            JobDescription = JobDescription,
            Requirements = Requirements,
            Benefits = Benefits,
            MinSalary = MinSalary,
            MaxSalary = MaxSalary,
            Currency = Currency,
            WorkLocation = SuggestedWorkLocation,
            EmploymentType = SuggestedEmploymentType,
            ApplicationDeadline = SuggestedDeadline,
            IsSalaryDisclosed = IsSalaryDisclosed,
            SaveAsDraft = false
        };
    }
}
