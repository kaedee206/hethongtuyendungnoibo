namespace Ats.Web.Models.Entities;

/// <summary>
/// Quản lý phân công chuyên viên tuyển dụng phụ trách một yêu cầu tuyển dụng (Scrum 26).
/// Bao gồm 1 Recruiter chính (IsPrimary = true) và nhiều Recruiter hỗ trợ (IsPrimary = false).
/// </summary>
public class RequisitionRecruiter : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequisitionId { get; set; }
    public Guid RecruiterId { get; set; }
    
    /// <summary>
    /// true: Recruiter chính (chịu trách nhiệm chạy tới cùng), false: Recruiter hỗ trợ
    /// </summary>
    public bool IsPrimary { get; set; } = false;
    
    /// <summary>
    /// Người thực hiện phân công (Trưởng phòng Nhân sự / Admin)
    /// </summary>
    public Guid AssignedById { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Note { get; set; }
    
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? Notes { get => Note; set => Note = value; }

    public JobRequisition Requisition { get; set; } = null!;
    public User Recruiter { get; set; } = null!;
    public User AssignedBy { get; set; } = null!;
}
