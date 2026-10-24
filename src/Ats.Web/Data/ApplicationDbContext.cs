using Ats.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<UserRole> UserRoles { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }

    public DbSet<AuthAuditLog> AuthAuditLogs { get; set; }
    public DbSet<UserSession> UserSessions { get; set; }

    public DbSet<Department> Departments { get; set; }
    public DbSet<JobPosition> JobPositions { get; set; }
    public DbSet<JobRequisition> JobRequisitions { get; set; }
    public DbSet<RequisitionApproval> RequisitionApprovals { get; set; }
    public DbSet<JobPosting> JobPostings { get; set; }

    public DbSet<Candidate> Candidates { get; set; }
    public DbSet<Resume> Resumes { get; set; }
    public DbSet<CandidateTag> CandidateTags { get; set; }
    public DbSet<CandidateNote> CandidateNotes { get; set; }
    public DbSet<TalentPoolCandidate> TalentPoolCandidates { get; set; }
    public DbSet<PipelineStage> PipelineStages { get; set; }
    public DbSet<Application> Applications { get; set; }
    public DbSet<ApplicationStageHistory> ApplicationStageHistories { get; set; }

    public DbSet<Interview> Interviews { get; set; }
    public DbSet<InterviewPanelist> InterviewPanelists { get; set; }
    public DbSet<EvaluationCriteria> EvaluationCriterias { get; set; }
    public DbSet<InterviewEvaluation> InterviewEvaluations { get; set; }
    public DbSet<EvaluationScore> EvaluationScores { get; set; }
    public DbSet<EvaluationForm> EvaluationForms { get; set; }
    public DbSet<EvaluationItem> EvaluationItems { get; set; }

    public DbSet<JobOffer> JobOffers { get; set; }
    public DbSet<OfferApproval> OfferApprovals { get; set; }

    public DbSet<EmailLog> EmailLogs { get; set; }
    public DbSet<EmailTemplate> EmailTemplates { get; set; }
    public DbSet<Notification> Notifications { get; set; }

    public DbSet<CompetencyFramework> CompetencyFrameworks { get; set; }
    public DbSet<CompetencyCriterion> CompetencyCriteria { get; set; }
    public DbSet<InterviewQuestionBank> InterviewQuestionBanks { get; set; }
    public DbSet<RecruitmentCatalog> RecruitmentCatalogs { get; set; }
    public DbSet<CandidateMergeHistory> CandidateMergeHistories { get; set; }
    public DbSet<CandidateInteraction> CandidateInteractions { get; set; }
    public DbSet<CompanyProfile> CompanyProfiles { get; set; }
    public DbSet<ApprovalRule> ApprovalRules { get; set; }
    public DbSet<ApprovalRuleStep> ApprovalRuleSteps { get; set; }

    // SCRUM-42: Quản lý giai đoạn ứng viên & Danh mục lý do loại
    public DbSet<RejectionReason> RejectionReasons { get; set; }
    public DbSet<CandidateStageHistory> CandidateStageHistories { get; set; }
    public DbSet<DepartmentHeadcountBudget> DepartmentHeadcountBudgets { get; set; }
    public DbSet<RequisitionRecruiter> RequisitionRecruiters { get; set; }
    public DbSet<RequisitionHandoverHistory> RequisitionHandoverHistories { get; set; }
    public DbSet<SavedCandidateFilter> SavedCandidateFilters { get; set; }

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

        // Approval rules relationships
        modelBuilder.Entity<ApprovalRule>()
            .HasOne(ar => ar.Department)
            .WithMany()
            .HasForeignKey(ar => ar.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ApprovalRuleStep>()
            .HasOne(s => s.ApprovalRule)
            .WithMany(r => r.Steps)
            .HasForeignKey(s => s.ApprovalRuleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ApprovalRuleStep>()
            .HasOne(s => s.ApproverRole)
            .WithMany()
            .HasForeignKey(s => s.ApproverRoleId)
            .OnDelete(DeleteBehavior.Restrict);
            
        modelBuilder.Entity<ApprovalRuleStep>()
            .HasOne(s => s.ApproverUser)
            .WithMany()
            .HasForeignKey(s => s.ApproverUserId)
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

        modelBuilder.Entity<JobRequisition>()
            .HasOne(jr => jr.OverQuotaApprovedBy)
            .WithMany()
            .HasForeignKey(jr => jr.OverQuotaApprovedById)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<DepartmentHeadcountBudget>(entity =>
        {
            entity.HasOne(b => b.Department)
                .WithMany()
                .HasForeignKey(b => b.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(b => new { b.DepartmentId, b.Year })
                .IsUnique();
        });

        // RequisitionRecruiter (Scrum 26)
        modelBuilder.Entity<RequisitionRecruiter>(entity =>
        {
            entity.HasKey(rr => rr.Id);
            entity.Property(rr => rr.Id).ValueGeneratedNever();

            entity.HasOne(rr => rr.Requisition)
                .WithMany(r => r.RequisitionRecruiters)
                .HasForeignKey(rr => rr.RequisitionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(rr => rr.Recruiter)
                .WithMany()
                .HasForeignKey(rr => rr.RecruiterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(rr => rr.AssignedBy)
                .WithMany()
                .HasForeignKey(rr => rr.AssignedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(rr => new { rr.RequisitionId, rr.RecruiterId });
        });

        // RequisitionHandoverHistory (Scrum 26)
        modelBuilder.Entity<RequisitionHandoverHistory>(entity =>
        {
            entity.HasKey(rh => rh.Id);
            entity.Property(rh => rh.Id).ValueGeneratedNever();

            entity.HasOne(rh => rh.Requisition)
                .WithMany(r => r.HandoverHistories)
                .HasForeignKey(rh => rh.RequisitionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(rh => rh.FromRecruiter)
                .WithMany()
                .HasForeignKey(rh => rh.FromRecruiterId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(rh => rh.ToRecruiter)
                .WithMany()
                .HasForeignKey(rh => rh.ToRecruiterId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(rh => rh.TransferredBy)
                .WithMany()
                .HasForeignKey(rh => rh.TransferredById)
                .OnDelete(DeleteBehavior.Restrict);
        });

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

        modelBuilder.Entity<CandidateTag>()
            .HasOne(t => t.Candidate)
            .WithMany(c => c.Tags)
            .HasForeignKey(t => t.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CandidateTag>()
            .HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CandidateNote>()
            .HasOne(n => n.Candidate)
            .WithMany(c => c.Notes)
            .HasForeignKey(n => n.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CandidateNote>()
            .HasOne(n => n.AuthorUser)
            .WithMany()
            .HasForeignKey(n => n.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

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

        modelBuilder.Entity<InterviewEvaluation>()
            .HasOne(ie => ie.ReopenedByUser)
            .WithMany()
            .HasForeignKey(ie => ie.ReopenedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

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

        // AuthAuditLog relationship
        modelBuilder.Entity<AuthAuditLog>(entity =>
        {
            entity.HasOne<User>()
                  .WithMany()
                  .HasForeignKey(a => a.UserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // UserSession relationship
        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasOne<User>()
                  .WithMany()
                  .HasForeignKey(s => s.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // User indexes & relationships
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.FullName);
            entity.HasIndex(e => e.Department);

            entity.HasOne(e => e.RoleEntity)
                  .WithMany()
                  .HasForeignKey(e => e.RoleId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.DepartmentEntity)
                  .WithMany()
                  .HasForeignKey(e => e.DepartmentId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.JobPosition)
                  .WithMany()
                  .HasForeignKey(e => e.JobPositionId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // UserRole relationship
        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(ur => new { ur.UserId, ur.RoleId });

            entity.HasOne(ur => ur.User)
                  .WithMany(u => u.UserRoles)
                  .HasForeignKey(ur => ur.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ur => ur.Role)
                  .WithMany(r => r.UserRoles)
                  .HasForeignKey(ur => ur.RoleId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OldValues).HasColumnType("jsonb");
            entity.Property(e => e.NewValues).HasColumnType("jsonb");
        });
        // Other Indexes
        modelBuilder.Entity<Application>().HasIndex(a => a.TrackingCode);
        modelBuilder.Entity<Application>().HasIndex(a => a.ReferrerUserId);
        modelBuilder.Entity<Application>()
            .HasOne(a => a.ReferrerUser)
            .WithMany()
            .HasForeignKey(a => a.ReferrerUserId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Candidate>().HasIndex(c => c.Email);
        modelBuilder.Entity<Candidate>().HasIndex(c => c.Phone);
        modelBuilder.Entity<Candidate>().HasIndex(c => c.IsMerged);

        // Candidate self-referencing merge relationship (Scrum #40)
        modelBuilder.Entity<Candidate>()
            .HasOne(c => c.MergedIntoCandidate)
            .WithMany(c => c.MergedSubCandidates)
            .HasForeignKey(c => c.MergedIntoCandidateId)
            .OnDelete(DeleteBehavior.SetNull);

        // CandidateMergeHistory relationships & indexes (Scrum #40)
        modelBuilder.Entity<CandidateMergeHistory>(entity =>
        {
            entity.HasOne(m => m.PrimaryCandidate)
                .WithMany()
                .HasForeignKey(m => m.PrimaryCandidateId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.SecondaryCandidate)
                .WithMany()
                .HasForeignKey(m => m.SecondaryCandidateId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.MergedByUser)
                .WithMany()
                .HasForeignKey(m => m.MergedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.UndoneByUser)
                .WithMany()
                .HasForeignKey(m => m.UndoneByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(m => m.PrimaryCandidateId);
            entity.HasIndex(m => m.SecondaryCandidateId);
            entity.HasIndex(m => m.MergedAt);
        });

        modelBuilder.Entity<JobRequisition>().HasIndex(r => r.Code).IsUnique();
        modelBuilder.Entity<JobPosting>().HasIndex(p => p.Slug).IsUnique();
        modelBuilder.Entity<Department>().HasIndex(d => d.Code).IsUnique();
        modelBuilder.Entity<JobPosition>().HasIndex(p => p.Code).IsUnique();
        modelBuilder.Entity<Role>().HasIndex(r => r.Code).IsUnique();
        modelBuilder.Entity<Permission>().HasIndex(p => p.Code).IsUnique();

        // CompetencyFramework: unique code + relationship to JobPosition (SCRUM-222)
        modelBuilder.Entity<CompetencyFramework>().HasIndex(cf => cf.Code).IsUnique();

        modelBuilder.Entity<JobPosition>()
            .HasOne(jp => jp.CompetencyFramework)
            .WithMany(cf => cf.AssignedPositions)
            .HasForeignKey(jp => jp.CompetencyFrameworkId)
            .OnDelete(DeleteBehavior.SetNull);

        // SCRUM-42: Candidate stage management & rejection reasons
        modelBuilder.Entity<CandidateStageHistory>(entity =>
        {
            entity.HasOne(h => h.Application)
                .WithMany(a => a.CandidateStageHistories)
                .HasForeignKey(h => h.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(h => h.RejectionReason)
                .WithMany(r => r.StageHistories)
                .HasForeignKey(h => h.RejectionReasonId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(h => h.ChangedByUser)
                .WithMany()
                .HasForeignKey(h => h.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(h => h.ApplicationId);
            entity.HasIndex(h => h.ToStage);
            entity.HasIndex(h => h.CreatedAt);
        });

        modelBuilder.Entity<RejectionReason>(entity =>
        {
            entity.HasIndex(r => r.Code).IsUnique();
            entity.HasIndex(r => r.IsActive);
            entity.HasIndex(r => r.DisplayOrder);
        });

        // CandidateInteraction relationships & indexes (Scrum #46)
        modelBuilder.Entity<CandidateInteraction>(entity =>
        {
            entity.HasOne(ci => ci.Candidate)
                .WithMany(c => c.Interactions)
                .HasForeignKey(ci => ci.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ci => ci.Application)
                .WithMany(a => a.Interactions)
                .HasForeignKey(ci => ci.ApplicationId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(ci => ci.CreatedByUser)
                .WithMany()
                .HasForeignKey(ci => ci.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(ci => ci.CandidateId);
            entity.HasIndex(ci => ci.ApplicationId);
            entity.HasIndex(ci => ci.InteractionDate);
        });

        // TalentPoolCandidate mapping (SCRUM-45)
        modelBuilder.Entity<TalentPoolCandidate>(entity =>
        {
            entity.ToTable("talent_pool_candidates");
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Candidate)
                  .WithMany(c => c.TalentPoolEntries)
                  .HasForeignKey(e => e.CandidateId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceApplication)
                  .WithMany()
                  .HasForeignKey(e => e.SourceApplicationId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.AddedByUser)
                  .WithMany()
                  .HasForeignKey(e => e.AddedByUserId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.TargetJobTitle);
            entity.HasIndex(e => e.Status);
        });

        // SavedCandidateFilter mapping (SCRUM-44)
        modelBuilder.Entity<SavedCandidateFilter>(entity =>
        {
            entity.ToTable("saved_candidate_filters");
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.Name });
        });

        // SCRUM-56: EvaluationForm & EvaluationItem mapping
        modelBuilder.Entity<EvaluationForm>(entity =>
        {
            entity.ToTable("evaluation_forms");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TotalScore).HasPrecision(5, 2);
            entity.Property(e => e.Status).HasConversion<string>();

            entity.HasOne(e => e.Application)
                .WithMany()
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Interview)
                .WithMany()
                .HasForeignKey(e => e.InterviewId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Evaluator)
                .WithMany()
                .HasForeignKey(e => e.EvaluatorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Items)
                .WithOne(i => i.EvaluationForm)
                .HasForeignKey(i => i.EvaluationFormId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ApplicationId);
            entity.HasIndex(e => e.InterviewId);
            entity.HasIndex(e => e.EvaluatorId);
            entity.HasIndex(e => new { e.InterviewId, e.EvaluatorId });
        });

        modelBuilder.Entity<EvaluationItem>(entity =>
        {
            entity.ToTable("evaluation_items");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Weight).HasPrecision(18, 4);
            entity.HasIndex(e => e.EvaluationFormId);
        });
    }
}