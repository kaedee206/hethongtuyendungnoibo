using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class Candidate : BaseEntity
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; } // Liên kết đến User nếu ứng viên tạo account trên portal
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? CurrentCompany { get; set; }
    public string? CurrentTitle { get; set; }
    public string? Address { get; set; }
    public string? LinkedinUrl { get; set; }
    
    public CandidateSource Source { get; set; } = CandidateSource.PORTAL;
    public Guid? ReferrerUserId { get; set; }
    
    public bool IsBlacklisted { get; set; } = false;
    public string? BlacklistReason { get; set; }

    public User? User { get; set; }
    public User? ReferrerUser { get; set; }
    public ICollection<Resume> Resumes { get; set; } = new List<Resume>();
    public ICollection<Application> Applications { get; set; } = new List<Application>();
}
