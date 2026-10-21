using Ats.Web.Data;
using Ats.Web.Models.DTOs.Offers;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Unit Tests kiểm thử User Story SP-7: Xử lý Ứng viên Từ chối Offer, Thương lượng lại & Chuyển vào Kho tiềm năng.
/// Kiểm thử các tiêu chí nghiệm thu (AC):
/// - AC1: Ghi nhận lý do từ chối offer theo danh mục chuẩn (Compensation, Location, Accepted Another Offer, Personal, Other), lương kỳ vọng, trạng thái REJECTED.
/// - AC2: Luồng đàm phán & tạo offer sửa đổi: Offer cũ chuyển sang REPLACED/SUPERSEDED, offer mới tăng Version và liên kết ParentOfferId.
/// - AC3: Gợi ý và tự động đưa vào Kho tiềm năng (Talent Pool) khi ứng viên từ chối offer.
/// - AC4: Ràng buộc nghiệp vụ: Không cho phép sửa đổi hoặc từ chối offer đã ACCEPTED hoặc REPLACED.
/// </summary>
public class OfferServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsOfferServiceDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static (OfferService service, TalentPoolService talentPoolService, ApplicationDbContext context) CreateService(ApplicationDbContext context)
    {
        var auditService = new SecurityAuditService(NullLogger<SecurityAuditService>.Instance);
        var talentPoolService = new TalentPoolService(context, auditService, NullLogger<TalentPoolService>.Instance);
        var offerService = new OfferService(context, talentPoolService, NullLogger<OfferService>.Instance);
        return (offerService, talentPoolService, context);
    }

    private static async Task<(JobOffer offer, Application app, Candidate candidate, User recruiter, JobPosting job)> SeedOfferScenarioAsync(
        ApplicationDbContext context,
        OfferStatus status = OfferStatus.PENDING_APPROVAL,
        decimal baseSalary = 30_000_000m,
        int version = 1)
    {
        var recruiter = new User
        {
            Id = Guid.NewGuid(),
            Email = $"recruiter_{Guid.NewGuid():N}@noveratech.vn",
            FullName = "Nguyễn Văn Tuyển Dụng",
            Role = "RECRUITER",
            PasswordHash = "hash"
        };
        await context.Users.AddAsync(recruiter);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Hoàng",
            LastName = "Văn Nam",
            Email = $"nam.hoang_{Guid.NewGuid():N}@example.com",
            Phone = "0987654321",
            IsInTalentPool = false
        };
        await context.Candidates.AddAsync(candidate);

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Trung tâm Phát triển Phần mềm",
            Code = $"SWD_{Guid.NewGuid():N}".Substring(0, 8)
        };
        await context.Departments.AddAsync(dept);

        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = $"REQ_{Guid.NewGuid():N}".Substring(0, 8),
            DepartmentId = dept.Id,
            HiringManagerId = recruiter.Id,
            Status = RequisitionStatus.APPROVED,
            Quantity = 2
        };
        await context.JobRequisitions.AddAsync(req);

        var job = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Title = "Senior .NET Core Engineer",
            Status = JobPostingStatus.PUBLISHED,
            Requirements = "C#, .NET 9, PostgreSQL, Docker",
            PublishedAt = DateTimeOffset.UtcNow
        };
        await context.JobPostings.AddAsync(job);

        var stageOffer = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Đề nghị tuyển dụng (Offer)",
            StageOrder = 5,
            ColorCode = "#7C3AED"
        };
        await context.PipelineStages.AddAsync(stageOffer);

        var app = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            JobPostingId = job.Id,
            CurrentStageId = stageOffer.Id,
            Status = ApplicationStatus.IN_PROCESS,
            AppliedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        await context.Applications.AddAsync(app);

        var offer = new JobOffer
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            CreatedByRecruiterId = recruiter.Id,
            BaseSalary = baseSalary,
            BonusAllowance = 2_000_000m,
            TotalPackage = baseSalary + 2_000_000m,
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            ProbationPeriodMonths = 2,
            ContractType = "Chính thức (Toàn thời gian)",
            Status = status,
            Version = version,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        await context.JobOffers.AddAsync(offer);
        await context.SaveChangesAsync();

        return (offer, app, candidate, recruiter, job);
    }

    // ─────────────────────────────────────────────────────────────
    // AC1: GHI NHẬN LÝ DO TỪ CHỐI OFFER & KỲ VỌNG THU NHẬP
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectOfferAsync_WithSalaryReasonAndExpectedSalary_UpdatesStatusToRejected()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (service, _, _) = CreateService(context);
        var (offer, _, _, recruiter, _) = await SeedOfferScenarioAsync(context, OfferStatus.PENDING_APPROVAL);

        var dto = new RejectOfferDto
        {
            OfferId = offer.Id,
            ReasonCategory = OfferRejectionReasonCategory.Salary,
            RejectionNotes = "Ứng viên mong muốn mức lương cơ bản tối thiểu 40 triệu do có offer cạnh tranh.",
            ExpectedSalary = 40_000_000m,
            AddToTalentPool = false
        };

        // Act
        var result = await service.RejectOfferAsync(dto, recruiter.Id);

        // Assert
        Assert.True(result.Success);
        var updatedOffer = await context.JobOffers.FindAsync(offer.Id);
        Assert.NotNull(updatedOffer);
        Assert.Equal(OfferStatus.REJECTED, updatedOffer.Status);
        Assert.Equal(OfferRejectionReasonCategory.Salary, updatedOffer.RejectionReasonCategory);
        Assert.Equal(40_000_000m, updatedOffer.ExpectedSalary);
        Assert.Contains("40 triệu", updatedOffer.RejectionNotes);
        Assert.NotNull(updatedOffer.RejectedAt);

        // Kiểm tra audit trail trong ApplicationStageHistories
        var audit = await context.ApplicationStageHistories
            .Where(h => h.ApplicationId == offer.ApplicationId)
            .OrderByDescending(h => h.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Contains("từ chối", audit.Comment);
        Assert.Contains("Lương / Đãi ngộ", audit.Comment);
        Assert.Contains("40.000.000", audit.Comment);
    }

    [Theory]
    [InlineData(OfferRejectionReasonCategory.Location, "Nhà quá xa công ty, thời gian di chuyển hơn 2 tiếng")]
    [InlineData(OfferRejectionReasonCategory.CompetitorOffer, "Đã chấp thuận offer từ một tập đoàn công nghệ khác")]
    [InlineData(OfferRejectionReasonCategory.Personal, "Gia đình có kế hoạch chuyển nơi cư trú")]
    [InlineData(OfferRejectionReasonCategory.Other, "Dự án mới không đúng kỳ vọng chuyên môn")]
    public async Task RejectOfferAsync_WithVariousReasonCategories_Succeeds(
        OfferRejectionReasonCategory category,
        string notes)
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (service, _, _) = CreateService(context);
        var (offer, _, _, recruiter, _) = await SeedOfferScenarioAsync(context);

        var dto = new RejectOfferDto
        {
            OfferId = offer.Id,
            ReasonCategory = category,
            RejectionNotes = notes,
            AddToTalentPool = false
        };

        // Act
        var result = await service.RejectOfferAsync(dto, recruiter.Id);

        // Assert
        Assert.True(result.Success);
        var dbOffer = await context.JobOffers.FindAsync(offer.Id);
        Assert.NotNull(dbOffer);
        Assert.Equal(OfferStatus.REJECTED, dbOffer.Status);
        Assert.Equal(category, dbOffer.RejectionReasonCategory);
        Assert.Equal(notes, dbOffer.RejectionNotes);
    }

    // ─────────────────────────────────────────────────────────────
    // AC3: GỢI Ý ĐƯA ỨNG VIÊN TỪ CHỐI VÀO KHO TIỀM NĂNG (TALENT POOL)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectOfferAsync_WithAddToTalentPoolTrue_UpdatesCandidateAndCreatesPoolEntry()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (service, _, _) = CreateService(context);
        var (offer, _, candidate, recruiter, _) = await SeedOfferScenarioAsync(context);

        var dto = new RejectOfferDto
        {
            OfferId = offer.Id,
            ReasonCategory = OfferRejectionReasonCategory.Salary,
            RejectionNotes = "Ứng viên rất xuất sắc về kỹ năng hệ thống nhưng kỳ vọng lương cao hơn budget hiện tại.",
            ExpectedSalary = 45_000_000m,
            AddToTalentPool = true,
            TalentPoolTargetJobTitle = "Tech Lead / Solution Architect",
            TalentPoolReason = "Năng lực chuyên môn xuất sắc, lưu trữ để liên hệ lại khi có dự án ngân sách cao hơn",
            TalentPoolRating = 5,
            TalentPoolNotes = "Kỹ năng .NET và Cloud rất mạnh, thái độ tích cực"
        };

        // Act
        var result = await service.RejectOfferAsync(dto, recruiter.Id);

        // Assert
        Assert.True(result.Success);

        // 1. Kiểm tra cờ IsInTalentPool trên Candidate
        var updatedCandidate = await context.Candidates.FindAsync(candidate.Id);
        Assert.NotNull(updatedCandidate);
        Assert.True(updatedCandidate.IsInTalentPool);
        Assert.NotNull(updatedCandidate.TalentPoolAddedAt);
        Assert.Contains("Lương / Đãi ngộ", updatedCandidate.TalentPoolNotes);

        // 2. Kiểm tra bản ghi trong bảng TalentPoolCandidates
        var poolEntry = await context.TalentPoolCandidates
            .FirstOrDefaultAsync(tp => tp.CandidateId == candidate.Id && !tp.IsDeleted);
        Assert.NotNull(poolEntry);
        Assert.Equal("AVAILABLE", poolEntry.Status);
        Assert.Equal("Tech Lead / Solution Architect", poolEntry.TargetJobTitle);
        Assert.Equal(5, poolEntry.Rating);
        Assert.Equal("Đề nghị tuyển dụng (Offer)", poolEntry.LastStageReached);
        Assert.Contains("Năng lực chuyên môn xuất sắc", poolEntry.Reason);
    }

    // ─────────────────────────────────────────────────────────────
    // AC2: LUỒNG ĐÀM PHÁN & TẠO OFFER SỬA ĐỔI (REVISED OFFER FLOW)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReviseOfferAsync_ValidInput_SupersedesOldOfferAndCreatesNewVersion()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (service, _, _) = CreateService(context);
        var (oldOffer, _, _, recruiter, _) = await SeedOfferScenarioAsync(
            context,
            status: OfferStatus.PENDING_APPROVAL,
            baseSalary: 30_000_000m,
            version: 1);

        var newJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21));
        var dto = new ReviseOfferDto
        {
            OriginalOfferId = oldOffer.Id,
            BaseSalary = 36_000_000m,
            BonusAllowance = 4_000_000m,
            TotalPackage = 40_000_000m,
            ProposedJoinDate = newJoinDate,
            ProbationPeriodMonths = 2,
            ContractType = "Chính thức (Toàn thời gian)",
            RevisionNotes = "Nâng lương theo kết quả thương lượng với ứng viên ngày 21/10"
        };

        // Act
        var result = await service.ReviseOfferAsync(dto, recruiter.Id);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.NewOffer);

        // 1. Offer cũ phải chuyển sang trạng thái REPLACED
        var reloadedOldOffer = await context.JobOffers.FindAsync(oldOffer.Id);
        Assert.NotNull(reloadedOldOffer);
        Assert.Equal(OfferStatus.REPLACED, reloadedOldOffer.Status);

        // 2. Offer mới phải có Version tăng lên và trỏ ParentOfferId
        var newOffer = await context.JobOffers.FindAsync(result.NewOffer.Id);
        Assert.NotNull(newOffer);
        Assert.Equal(2, newOffer.Version);
        Assert.Equal(oldOffer.Id, newOffer.ParentOfferId);
        Assert.Equal(36_000_000m, newOffer.BaseSalary);
        Assert.Equal(4_000_000m, newOffer.BonusAllowance);
        Assert.Equal(40_000_000m, newOffer.TotalPackage);
        Assert.Equal(newJoinDate, newOffer.ProposedJoinDate);
        Assert.Equal(OfferStatus.PENDING_APPROVAL, newOffer.Status);

        // 3. Kiểm tra nhật ký Audit Trail ghi nhận phiên bản sửa đổi
        var audit = await context.ApplicationStageHistories
            .Where(h => h.ApplicationId == oldOffer.ApplicationId)
            .OrderByDescending(h => h.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Contains("v2", audit.Comment);
        Assert.Contains("v1", audit.Comment);
        Assert.Contains("36.000.000", audit.Comment);
    }

    [Fact]
    public async Task ReviseOfferAsync_ChainMultipleRevisions_IncrementsVersionProgressively()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (service, _, _) = CreateService(context);
        var (offerV1, _, _, recruiter, _) = await SeedOfferScenarioAsync(context, OfferStatus.PENDING_APPROVAL, 30_000_000m, 1);

        // Revise v1 -> v2
        var resV2 = await service.ReviseOfferAsync(new ReviseOfferDto
        {
            OriginalOfferId = offerV1.Id,
            BaseSalary = 33_000_000m,
            RevisionNotes = "Đàm phán vòng 1"
        }, recruiter.Id);
        Assert.True(resV2.Success);
        Assert.Equal(2, resV2.NewOffer!.Version);

        // Revise v2 -> v3
        var resV3 = await service.ReviseOfferAsync(new ReviseOfferDto
        {
            OriginalOfferId = resV2.NewOffer.Id,
            BaseSalary = 35_000_000m,
            RevisionNotes = "Đàm phán vòng 2 (chốt mức lương cuối cùng)"
        }, recruiter.Id);

        // Assert
        Assert.True(resV3.Success);
        Assert.Equal(3, resV3.NewOffer!.Version);
        Assert.Equal(resV2.NewOffer.Id, resV3.NewOffer.ParentOfferId);

        // V1 và V2 đều phải ở trạng thái REPLACED, V3 ở trạng thái PENDING_APPROVAL
        var v1 = await context.JobOffers.FindAsync(offerV1.Id);
        var v2 = await context.JobOffers.FindAsync(resV2.NewOffer.Id);
        var v3 = await context.JobOffers.FindAsync(resV3.NewOffer.Id);

        Assert.Equal(OfferStatus.REPLACED, v1!.Status);
        Assert.Equal(OfferStatus.REPLACED, v2!.Status);
        Assert.Equal(OfferStatus.PENDING_APPROVAL, v3!.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // AC4: RÀNG BUỘC NGHIỆP VỤ & VALIDATION
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectOfferAsync_WhenOfferAlreadyAccepted_ReturnsFalse()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (service, _, _) = CreateService(context);
        var (offer, _, _, recruiter, _) = await SeedOfferScenarioAsync(context, OfferStatus.ACCEPTED);

        var dto = new RejectOfferDto
        {
            OfferId = offer.Id,
            ReasonCategory = OfferRejectionReasonCategory.Personal
        };

        // Act
        var result = await service.RejectOfferAsync(dto, recruiter.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("ACCEPTED", result.Message);
    }

    [Fact]
    public async Task RejectOfferAsync_WhenOfferAlreadyReplaced_ReturnsFalse()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (service, _, _) = CreateService(context);
        var (offer, _, _, recruiter, _) = await SeedOfferScenarioAsync(context, OfferStatus.REPLACED);

        var dto = new RejectOfferDto
        {
            OfferId = offer.Id,
            ReasonCategory = OfferRejectionReasonCategory.Other
        };

        // Act
        var result = await service.RejectOfferAsync(dto, recruiter.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("REPLACED", result.Message);
    }

    [Fact]
    public async Task ReviseOfferAsync_WhenOfferAlreadyAccepted_ReturnsFalse()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (service, _, _) = CreateService(context);
        var (offer, _, _, recruiter, _) = await SeedOfferScenarioAsync(context, OfferStatus.ACCEPTED);

        var dto = new ReviseOfferDto
        {
            OriginalOfferId = offer.Id,
            BaseSalary = 35_000_000m
        };

        // Act
        var result = await service.ReviseOfferAsync(dto, recruiter.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("ACCEPTED", result.Message);
    }

    [Fact]
    public async Task ReviseOfferAsync_WhenOfferAlreadyReplaced_ReturnsFalse()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (service, _, _) = CreateService(context);
        var (offer, _, _, recruiter, _) = await SeedOfferScenarioAsync(context, OfferStatus.REPLACED);

        var dto = new ReviseOfferDto
        {
            OriginalOfferId = offer.Id,
            BaseSalary = 35_000_000m
        };

        // Act
        var result = await service.ReviseOfferAsync(dto, recruiter.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("thay thế", result.Message);
    }

    [Fact]
    public async Task ReviseOfferAsync_WithZeroSalary_ReturnsFalse()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (service, _, _) = CreateService(context);
        var (offer, _, _, recruiter, _) = await SeedOfferScenarioAsync(context);

        var dto = new ReviseOfferDto
        {
            OriginalOfferId = offer.Id,
            BaseSalary = 0
        };

        // Act
        var result = await service.ReviseOfferAsync(dto, recruiter.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("lớn hơn 0", result.Message);
    }

    [Fact]
    public async Task GetRejectionReasonsAsync_ReturnsFiveStandardCategoriesWithVietnameseNames()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (service, _, _) = CreateService(context);

        // Act
        var reasons = await service.GetRejectionReasonsAsync();

        // Assert
        Assert.NotNull(reasons);
        Assert.Equal(5, reasons.Count);

        var salaryCat = reasons.FirstOrDefault(r => r.Code == OfferRejectionReasonCategory.Salary.ToString());
        Assert.NotNull(salaryCat);
        Assert.True(salaryCat.RequiresExpectedSalary);
        Assert.Contains("Lương", salaryCat.DisplayName);

        var locationCat = reasons.FirstOrDefault(r => r.Code == OfferRejectionReasonCategory.Location.ToString());
        Assert.NotNull(locationCat);
        Assert.False(locationCat.RequiresExpectedSalary);
        Assert.Contains("Địa điểm", locationCat.DisplayName);

        var competitorCat = reasons.FirstOrDefault(r => r.Code == OfferRejectionReasonCategory.CompetitorOffer.ToString());
        Assert.NotNull(competitorCat);
        Assert.Contains("nơi khác", competitorCat.DisplayName);
    }

    [Fact]
    public async Task GetOfferHistoryByApplicationAsync_ReturnsOrderedVersions()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (service, _, _) = CreateService(context);
        var (v1, app, _, recruiter, _) = await SeedOfferScenarioAsync(context, OfferStatus.PENDING_APPROVAL, 30_000_000m, 1);

        // Revise to create v2
        await service.ReviseOfferAsync(new ReviseOfferDto
        {
            OriginalOfferId = v1.Id,
            BaseSalary = 35_000_000m
        }, recruiter.Id);

        // Act
        var history = await service.GetOfferHistoryByApplicationAsync(app.Id);

        // Assert
        Assert.Equal(2, history.Count);
        Assert.Equal(1, history[0].Version);
        Assert.Equal(OfferStatus.REPLACED, history[0].Status);
        Assert.Equal(2, history[1].Version);
        Assert.Equal(OfferStatus.PENDING_APPROVAL, history[1].Status);
    }
}
