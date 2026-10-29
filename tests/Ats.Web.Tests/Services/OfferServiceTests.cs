using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Offers;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Unit Tests kiểm thử toàn diện nghiệp vụ Soạn đề xuất Offer cho ứng viên (SCRUM-340).
/// Tiêu chí chấp nhận:
/// 1. Khai báo chức danh, mức lương, phụ cấp, ngày dự kiến nhận việc, thời hạn phản hồi.
/// 2. Mức lương ngoài dải chuẩn của chức danh bắt buộc nhập giải trình chi tiết.
/// 3. Cho phép Lưu nháp (DRAFT) và Xem trước thư offer trước khi gửi duyệt (PENDING_APPROVAL).
/// 4. Một ứng viên tại một thời điểm chỉ có duy nhất một offer đang hiệu lực.
/// </summary>
public class OfferServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsOfferDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(
        Department dept,
        JobPosition position,
        JobRequisition req,
        JobPosting posting,
        PipelineStage interviewStage,
        PipelineStage offerStage,
        Candidate candidate,
        Application app,
        User recruiter)> SeedBaseDataAsync(ApplicationDbContext context)
    {
        var recruiter = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Chuyên Viên HR",
            Email = $"hr_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash_sample",
            Status = "ACTIVE"
        };
        await context.Users.AddAsync(recruiter);

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Khối Kỹ thuật Phần mềm",
            Code = "SWE"
        };
        await context.Departments.AddAsync(dept);

        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "SE-02",
            Title = "Kỹ sư Phần mềm Cao cấp (Senior Software Engineer)",
            DepartmentId = dept.Id,
            MinSalary = 30_000_000,
            MaxSalary = 50_000_000,
            IsActive = true
        };
        await context.JobPositions.AddAsync(position);

        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-SWE-01",
            DepartmentId = dept.Id,
            JobPositionId = position.Id,
            HiringManagerId = recruiter.Id,
            Quantity = 1,
            MinSalary = 30_000_000,
            MaxSalary = 50_000_000,
            Status = RequisitionStatus.APPROVED
        };
        await context.JobRequisitions.AddAsync(req);

        var posting = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Title = "Senior .NET Backend Engineer",
            WorkLocation = "Tòa nhà Novera Tower, Cầu Giấy, Hà Nội",
            Status = JobPostingStatus.PUBLISHED
        };
        await context.JobPostings.AddAsync(posting);

        var interviewStage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Phỏng vấn Chuyên môn",
            StageOrder = 3
        };
        var offerStage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Đề xuất Thư mời (Offer)",
            StageOrder = 5
        };
        await context.PipelineStages.AddRangeAsync(interviewStage, offerStage);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Trần",
            LastName = "Văn Nam",
            Email = "nam.tran@example.com",
            Phone = "0987654321"
        };
        await context.Candidates.AddAsync(candidate);

        var app = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            JobPostingId = posting.Id,
            CurrentStageId = interviewStage.Id,
            Status = ApplicationStatus.IN_PROCESS,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Applications.AddAsync(app);

        await context.SaveChangesAsync();

        return (dept, position, req, posting, interviewStage, offerStage, candidate, app, recruiter);
    }

    [Fact]
    public async Task GetOfferFormInitDataAsync_WhenCalled_ReturnsCandidateAndPositionSalaryRange()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, position, _, _, _, _, candidate, app, recruiter) = await SeedBaseDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new OfferService(context, mockEmail.Object, NullLogger<OfferService>.Instance);

        // Act
        var result = await service.GetOfferFormInitDataAsync(app.Id, recruiter.Id);

        // Assert
        result.Should().NotBeNull();
        result.ApplicationId.Should().Be(app.Id);
        result.CandidateName.Should().Be("Trần Văn Nam");
        result.CandidateEmail.Should().Be("nam.tran@example.com");
        result.CandidatePhone.Should().Be("0987654321");
        result.StandardMinSalary.Should().Be(position.MinSalary);
        result.StandardMaxSalary.Should().Be(position.MaxSalary);
        result.HasOtherActiveOffer.Should().BeFalse();
        result.AvailablePositions.Should().Contain(p => p.Id == position.Id);
    }

    [Fact]
    public async Task SaveOfferAsync_WhenCandidateHasAnotherActiveOffer_ReturnsFailWithRuleMessage()
    {
        // Arrange (SCRUM-340: Một ứng viên tại một thời điểm chỉ có 1 offer đang hiệu lực)
        using var context = CreateInMemoryDbContext();
        var (_, position, _, _, _, _, candidate, app1, recruiter) = await SeedBaseDataAsync(context);

        // Tạo thêm application 2 cho cùng ứng viên đó
        var app2 = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            JobPostingId = app1.JobPostingId,
            CurrentStageId = app1.CurrentStageId,
            ResumeId = Guid.NewGuid(),
            Status = ApplicationStatus.IN_PROCESS,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Applications.AddAsync(app2);

        // Đã có 1 offer hiệu lực ở app1 (trạng thái PENDING_APPROVAL)
        var existingActiveOffer = new JobOffer
        {
            Id = Guid.NewGuid(),
            ApplicationId = app1.Id,
            CreatedByRecruiterId = recruiter.Id,
            JobPositionId = position.Id,
            BaseSalary = 35_000_000,
            Status = OfferStatus.PENDING_APPROVAL,
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))
        };
        await context.JobOffers.AddAsync(existingActiveOffer);
        await context.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var service = new OfferService(context, mockEmail.Object, NullLogger<OfferService>.Instance);

        var input = new JobOfferInputModel
        {
            ApplicationId = app2.Id,
            JobPositionId = position.Id,
            JobTitle = "Tech Lead",
            BaseSalary = 40_000_000,
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            IsDraft = false
        };

        // Act
        var result = await service.SaveOfferAsync(input, recruiter.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("chỉ được phép có tối đa 1 offer có hiệu lực");
    }

    [Fact]
    public async Task SaveOfferAsync_WhenPreviousOfferIsWithdrawn_AllowsCreatingNewOffer()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, position, _, _, _, _, candidate, app1, recruiter) = await SeedBaseDataAsync(context);

        var app2 = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            JobPostingId = app1.JobPostingId,
            CurrentStageId = app1.CurrentStageId,
            ResumeId = Guid.NewGuid(),
            Status = ApplicationStatus.IN_PROCESS,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Applications.AddAsync(app2);

        // Offer cũ đã bị WITHDRAWN (hết hiệu lực)
        var withdrawnOffer = new JobOffer
        {
            Id = Guid.NewGuid(),
            ApplicationId = app1.Id,
            CreatedByRecruiterId = recruiter.Id,
            JobPositionId = position.Id,
            BaseSalary = 35_000_000,
            Status = OfferStatus.WITHDRAWN,
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))
        };
        await context.JobOffers.AddAsync(withdrawnOffer);
        await context.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var service = new OfferService(context, mockEmail.Object, NullLogger<OfferService>.Instance);

        var input = new JobOfferInputModel
        {
            ApplicationId = app2.Id,
            JobPositionId = position.Id,
            JobTitle = position.Title,
            BaseSalary = 40_000_000, // trong dải 30-50m
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            IsDraft = true
        };

        // Act
        var result = await service.SaveOfferAsync(input, recruiter.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.OfferId.Should().NotBeNull();
        var saved = await context.JobOffers.FirstOrDefaultAsync(o => o.Id == result.OfferId);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(OfferStatus.DRAFT);
    }

    [Fact]
    public async Task SaveOfferAsync_WhenUpdatingSameApplicationOffer_AllowsModification()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, position, _, _, _, _, _, app, recruiter) = await SeedBaseDataAsync(context);

        var draftOffer = new JobOffer
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            CreatedByRecruiterId = recruiter.Id,
            JobPositionId = position.Id,
            BaseSalary = 35_000_000,
            Status = OfferStatus.DRAFT,
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))
        };
        await context.JobOffers.AddAsync(draftOffer);
        await context.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var service = new OfferService(context, mockEmail.Object, NullLogger<OfferService>.Instance);

        var input = new JobOfferInputModel
        {
            ApplicationId = app.Id,
            OfferId = draftOffer.Id,
            JobPositionId = position.Id,
            JobTitle = "Senior Engineer",
            BaseSalary = 42_000_000,
            BonusAllowance = 3_000_000,
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(25)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            IsDraft = true
        };

        // Act
        var result = await service.SaveOfferAsync(input, recruiter.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.OfferId.Should().Be(draftOffer.Id);
        draftOffer.BaseSalary.Should().Be(42_000_000);
        draftOffer.BonusAllowance.Should().Be(3_000_000);
    }

    [Fact]
    public async Task SaveOfferAsync_WhenSalaryOutsideBandWithoutJustification_ReturnsFail()
    {
        // Arrange (Dải chuẩn 30M - 50M. Đề xuất 55M vượt trần và không có giải trình)
        using var context = CreateInMemoryDbContext();
        var (_, position, _, _, _, _, _, app, recruiter) = await SeedBaseDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new OfferService(context, mockEmail.Object, NullLogger<OfferService>.Instance);

        var input = new JobOfferInputModel
        {
            ApplicationId = app.Id,
            JobPositionId = position.Id,
            JobTitle = position.Title,
            BaseSalary = 55_000_000, // Vượt max 50m
            SalaryJustification = null, // Thiếu giải trình
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            IsDraft = true
        };

        // Act
        var result = await service.SaveOfferAsync(input, recruiter.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("nằm ngoài dải chuẩn");
        result.Message.Should().Contain("Bắt buộc phải nhập lý do giải trình");
    }

    [Fact]
    public async Task SaveOfferAsync_WhenSalaryBelowMinWithoutJustification_ReturnsFail()
    {
        // Arrange (Dải chuẩn 30M - 50M. Đề xuất 25M dưới sàn và không có giải trình)
        using var context = CreateInMemoryDbContext();
        var (_, position, _, _, _, _, _, app, recruiter) = await SeedBaseDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new OfferService(context, mockEmail.Object, NullLogger<OfferService>.Instance);

        var input = new JobOfferInputModel
        {
            ApplicationId = app.Id,
            JobPositionId = position.Id,
            JobTitle = position.Title,
            BaseSalary = 25_000_000, // Dưới min 30m
            SalaryJustification = "   ", // Khoảng trắng
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            IsDraft = true
        };

        // Act
        var result = await service.SaveOfferAsync(input, recruiter.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("nằm ngoài dải chuẩn");
    }

    [Fact]
    public async Task SaveOfferAsync_WhenSalaryOutsideBandWithJustification_SucceedsAndFlagsOutsideBand()
    {
        // Arrange (Mức 55M kèm lý do giải trình năng lực xuất sắc)
        using var context = CreateInMemoryDbContext();
        var (_, position, _, _, _, _, _, app, recruiter) = await SeedBaseDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new OfferService(context, mockEmail.Object, NullLogger<OfferService>.Instance);

        var input = new JobOfferInputModel
        {
            ApplicationId = app.Id,
            JobPositionId = position.Id,
            JobTitle = position.Title,
            BaseSalary = 55_000_000,
            BonusAllowance = 5_000_000,
            SalaryJustification = "Ứng viên có 8 năm kinh nghiệm System Architect, vượt trội so với yêu cầu tiêu chuẩn.",
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            IsDraft = true
        };

        // Act
        var result = await service.SaveOfferAsync(input, recruiter.Id);

        // Assert
        result.Success.Should().BeTrue();
        var offer = await context.JobOffers.FirstOrDefaultAsync(o => o.Id == result.OfferId);
        offer.Should().NotBeNull();
        offer!.IsOutsideBand.Should().BeTrue();
        offer.IsAboveBudget.Should().BeTrue();
        offer.SalaryJustification.Should().Contain("System Architect");
        offer.TotalPackage.Should().Be(60_000_000);
    }

    [Fact]
    public async Task SaveOfferAsync_WhenSalaryWithinBand_SucceedsWithoutJustification()
    {
        // Arrange (Mức 45M trong dải 30M - 50M)
        using var context = CreateInMemoryDbContext();
        var (_, position, _, _, _, _, _, app, recruiter) = await SeedBaseDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new OfferService(context, mockEmail.Object, NullLogger<OfferService>.Instance);

        var input = new JobOfferInputModel
        {
            ApplicationId = app.Id,
            JobPositionId = position.Id,
            JobTitle = position.Title,
            BaseSalary = 45_000_000,
            BonusAllowance = 2_000_000,
            SalaryJustification = null,
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            IsDraft = true
        };

        // Act
        var result = await service.SaveOfferAsync(input, recruiter.Id);

        // Assert
        result.Success.Should().BeTrue();
        var offer = await context.JobOffers.FirstOrDefaultAsync(o => o.Id == result.OfferId);
        offer.Should().NotBeNull();
        offer!.IsOutsideBand.Should().BeFalse();
        offer.IsAboveBudget.Should().BeFalse();
        offer.SalaryJustification.Should().BeNull();
    }

    [Fact]
    public async Task SaveOfferAsync_WhenSavedAsDraft_RetainsDraftStatusAndDoesNotAdvanceStage()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, position, _, _, interviewStage, offerStage, _, app, recruiter) = await SeedBaseDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new OfferService(context, mockEmail.Object, NullLogger<OfferService>.Instance);

        var input = new JobOfferInputModel
        {
            ApplicationId = app.Id,
            JobPositionId = position.Id,
            JobTitle = position.Title,
            BaseSalary = 40_000_000,
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            IsDraft = true
        };

        // Act
        var result = await service.SaveOfferAsync(input, recruiter.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.IsDraft.Should().BeTrue();

        var offer = await context.JobOffers.FirstOrDefaultAsync(o => o.Id == result.OfferId);
        offer!.Status.Should().Be(OfferStatus.DRAFT);

        var updatedApp = await context.Applications.FirstOrDefaultAsync(a => a.Id == app.Id);
        updatedApp!.CurrentStageId.Should().Be(interviewStage.Id); // Vẫn giữ nguyên stage phỏng vấn, chưa chuyển Offer
    }

    [Fact]
    public async Task SaveOfferAsync_WhenSubmittedForApproval_SetsPendingApprovalAndAdvancesToOfferStage()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, position, _, _, _, offerStage, _, app, recruiter) = await SeedBaseDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new OfferService(context, mockEmail.Object, NullLogger<OfferService>.Instance);

        var input = new JobOfferInputModel
        {
            ApplicationId = app.Id,
            JobPositionId = position.Id,
            JobTitle = position.Title,
            BaseSalary = 40_000_000,
            BonusAllowance = 2_000_000,
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            IsDraft = false // Gửi phê duyệt
        };

        // Act
        var result = await service.SaveOfferAsync(input, recruiter.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.IsDraft.Should().BeFalse();

        var offer = await context.JobOffers.FirstOrDefaultAsync(o => o.Id == result.OfferId);
        offer!.Status.Should().Be(OfferStatus.PENDING_APPROVAL);

        var updatedApp = await context.Applications.FirstOrDefaultAsync(a => a.Id == app.Id);
        updatedApp!.CurrentStageId.Should().Be(offerStage.Id); // Đã chuyển sang stage Offer

        var history = await context.ApplicationStageHistories
            .FirstOrDefaultAsync(h => h.ApplicationId == app.Id && h.ToStageId == offerStage.Id);
        history.Should().NotBeNull();
        history!.Comment.Should().Contain("Chuyển phê duyệt Offer");
    }

    [Fact]
    public async Task SaveOfferAsync_WhenDatesAreInPastOrInvalid_ReturnsFail()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, position, _, _, _, _, _, app, recruiter) = await SeedBaseDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new OfferService(context, mockEmail.Object, NullLogger<OfferService>.Instance);

        // Trường hợp 1: Ngày nhận việc trong quá khứ
        var inputPastJoinDate = new JobOfferInputModel
        {
            ApplicationId = app.Id,
            JobPositionId = position.Id,
            BaseSalary = 35_000_000,
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            IsDraft = true
        };
        var res1 = await service.SaveOfferAsync(inputPastJoinDate, recruiter.Id);
        res1.Success.Should().BeFalse();
        res1.Message.Should().Contain("không được ở trong quá khứ");

        // Trường hợp 2: Hạn phản hồi sau Ngày nhận việc
        var inputExpAfterJoin = new JobOfferInputModel
        {
            ApplicationId = app.Id,
            JobPositionId = position.Id,
            BaseSalary = 35_000_000,
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
            IsDraft = true
        };
        var res2 = await service.SaveOfferAsync(inputExpAfterJoin, recruiter.Id);
        res2.Success.Should().BeFalse();
        res2.Message.Should().Contain("phải trước hoặc cùng ngày với Ngày dự kiến nhận việc");
    }

    [Fact]
    public async Task GenerateOfferPreviewAsync_WhenCalled_ReturnsCalculatedPreviewModel()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (dept, position, _, _, _, _, _, app, recruiter) = await SeedBaseDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new OfferService(context, mockEmail.Object, NullLogger<OfferService>.Instance);

        var input = new JobOfferInputModel
        {
            ApplicationId = app.Id,
            JobPositionId = position.Id,
            JobTitle = "Chuyên gia Kỹ thuật Phần mềm",
            BaseSalary = 45_000_000,
            BonusAllowance = 5_000_000,
            ProbationPeriodMonths = 2,
            ContractType = "Toàn thời gian Không xác định thời hạn",
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))
        };

        // Act
        var preview = await service.GenerateOfferPreviewAsync(input, recruiter.Id);

        // Assert
        preview.Should().NotBeNull();
        preview.CandidateName.Should().Be("Trần Văn Nam");
        preview.JobTitle.Should().Be("Chuyên gia Kỹ thuật Phần mềm");
        preview.DepartmentName.Should().Be(dept.Name);
        preview.BaseSalary.Should().Be(45_000_000);
        preview.BonusAllowance.Should().Be(5_000_000);
        preview.TotalPackage.Should().Be(50_000_000);
        preview.ProbationSalaryPercentage.Should().Be(85);
        preview.RecruiterName.Should().Be(recruiter.FullName);
    }

    [Fact]
    public async Task WithdrawOfferAsync_WhenCalledForActiveOffer_SetsStatusWithdrawnAndAppendsReason()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, position, _, _, _, _, _, app, recruiter) = await SeedBaseDataAsync(context);

        var activeOffer = new JobOffer
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            CreatedByRecruiterId = recruiter.Id,
            JobPositionId = position.Id,
            BaseSalary = 40_000_000,
            Status = OfferStatus.PENDING_APPROVAL,
            Notes = "Ghi chú ban đầu"
        };
        await context.JobOffers.AddAsync(activeOffer);
        await context.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var service = new OfferService(context, mockEmail.Object, NullLogger<OfferService>.Instance);

        // Act
        var result = await service.WithdrawOfferAsync(activeOffer.Id, recruiter.Id, "Ứng viên đổi ý muốn sang vị trí khác.");

        // Assert
        result.Success.Should().BeTrue();
        var updated = await context.JobOffers.FirstOrDefaultAsync(o => o.Id == activeOffer.Id);
        updated!.Status.Should().Be(OfferStatus.WITHDRAWN);
        updated.Notes.Should().Contain("Ứng viên đổi ý muốn sang vị trí khác");
    }
}
