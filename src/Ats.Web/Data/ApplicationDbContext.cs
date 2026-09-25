using Ats.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<UserRole> UserRoles { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }

    public DbSet<Department> Departments { get; set; }
    public DbSet<JobPosition> JobPositions { get; set; }
    public DbSet<JobRequisition> JobRequisitions { get; set; }
    public DbSet<RequisitionApproval> RequisitionApprovals { get; set; }
    public DbSet<JobPosting> JobPostings { get; set; }

    public DbSet<Candidate> Candidates { get; set; }
    public DbSet<Resume> Resumes { get; set; }
    public DbSet<PipelineStage> PipelineStages { get; set; }
    public DbSet<Application> Applications { get; set; }
    public DbSet<ApplicationStageHistory> ApplicationStageHistories { get; set; }

    public DbSet<Interview> Interviews { get; set; }
    public DbSet<InterviewPanelist> InterviewPanelists { get; set; }
    public DbSet<EvaluationCriteria> EvaluationCriterias { get; set; }
    public DbSet<InterviewEvaluation> InterviewEvaluations { get; set; }
    public DbSet<EvaluationScore> EvaluationScores { get; set; }

    public DbSet<JobOffer> JobOffers { get; set; }
    public DbSet<OfferApproval> OfferApprovals { get; set; }

    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<EmailLog> EmailLogs { get; set; }
    public DbSet<Notification> Notifications { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // RBAC Composite Keys
        modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
        modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });

        // Department Self-referencing
        modelBuilder.Entity<Department>()
            .HasOne(d => d.Parent)
            .WithMany(d => d.Children)
            .HasForeignKey(d => d.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Department>()
            .HasOne(d => d.Manager)
            .WithMany()
            .HasForeignKey(d => d.ManagerId)
            .OnDelete(DeleteBehavior.SetNull);

        // JobPosition relationships
        modelBuilder.Entity<JobPosition>()
            .HasOne(jp => jp.Department)
            .WithMany(d => d.JobPositions)
            .HasForeignKey(jp => jp.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // JobRequisition relationships
        modelBuilder.Entity<JobRequisition>()
            .HasOne(jr => jr.HiringManager)
            .WithMany()
            .HasForeignKey(jr => jr.HiringManagerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<JobRequisition>()
            .HasOne(jr => jr.AssignedRecruiter)
            .WithMany()
            .HasForeignKey(jr => jr.AssignedRecruiterId)
            .OnDelete(DeleteBehavior.SetNull);

        // Cascade delete behavior adjustments
        modelBuilder.Entity<RequisitionApproval>()
            .HasOne(ra => ra.Approver)
            .WithMany()
            .HasForeignKey(ra => ra.ApproverId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Candidate>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.SetNull);
            
        modelBuilder.Entity<Candidate>()
            .HasOne(c => c.ReferrerUser)
            .WithMany()
            .HasForeignKey(c => c.ReferrerUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ApplicationStageHistory>()
            .HasOne(ash => ash.ChangedByUser)
            .WithMany()
            .HasForeignKey(ash => ash.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<InterviewPanelist>()
            .HasOne(ip => ip.Interviewer)
            .WithMany()
            .HasForeignKey(ip => ip.InterviewerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<InterviewEvaluation>()
            .HasOne(ie => ie.Interviewer)
            .WithMany()
            .HasForeignKey(ie => ie.InterviewerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OfferApproval>()
            .HasOne(oa => oa.Approver)
            .WithMany()
            .HasForeignKey(oa => oa.ApproverId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AuditLog>()
            .HasOne(al => al.User)
            .WithMany()
            .HasForeignKey(al => al.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Indexes
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<Candidate>().HasIndex(c => c.Email);
        modelBuilder.Entity<JobRequisition>().HasIndex(r => r.Code).IsUnique();
        modelBuilder.Entity<JobPosting>().HasIndex(p => p.Slug).IsUnique();
        modelBuilder.Entity<Department>().HasIndex(d => d.Code).IsUnique();
        modelBuilder.Entity<JobPosition>().HasIndex(p => p.Code).IsUnique();
        modelBuilder.Entity<Role>().HasIndex(r => r.Code).IsUnique();
        modelBuilder.Entity<Permission>().HasIndex(p => p.Code).IsUnique();
    }
}