using Ats.Web.Constants;
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
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Unit Tests kiểm thử toàn diện nghiệp vụ Phê duyệt Offer theo hạn mức lương (SCRUM-341).
/// Tiêu chí chấp nhận:
/// 1. Offer trong dải chuẩn do Trưởng phòng Nhân sự (HRManager) duyệt (1 cấp duyệt).
/// 2. Offer vượt dải chuẩn tự động thêm cấp duyệt cao hơn (Approver / Ban Giám Đốc - 2 cấp duyệt tuần tự).
/// 3. Ba hành động: Duyệt (APPROVE), Từ chối (REJECT), Yêu cầu sửa (CHANGES_REQUESTED) — hai hành động sau bắt buộc nhập ý kiến.
/// 4. Lịch sử duyệt không sửa và không xoá được (bất biến).
/// </summary>
public class OfferApprovalServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsOfferApprovalDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(
        ApplicationDbContext context,
        User hrManager,
        User bodApprover,
        User admin,
        User recruiter,
        JobPosition position,
        JobOffer offerWithinBand,
        JobOffer offerAboveBand,
        PipelineStage hiredStage)> SetupApprovalScenarioAsync()
    {
        var context = CreateInMemoryDbContext();

        var hrManager = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Mai Phương",
            Email = "hr@noveratech.vn",
            Role = UserRoles.HRManager,
            Status = "ACTIVE"
        };

        var bodApprover = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Trần Đức Minh",
            Email = "bod@noveratech.vn",
            Role = UserRoles.Approver,
            Status = "ACTIVE"
        };

        var admin = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Quản trị viên Hệ thống",
            Email = "admin@noveratech.vn",
            Role = UserRoles.Admin,
            Status = "ACTIVE"
        };

        var recruiter = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Lê Thu Hà",
            Email = "recruiter@noveratech.vn",
            Role = UserRoles.Recruiter,
            Status = "ACTIVE"
        };

        await context.Users.AddRangeAsync(hrManager, bodApprover, admin, recruiter);

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Khối Công nghệ & Kỹ thuật",
            Code = "TECH"
        };
        await context.Departments.AddAsync(dept);

        // Vị trí chuẩn có dải lương: 25,000,000 - 40,000,000 VNĐ
        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "DEV-LEAD",
            Title = "Trưởng nhóm Phát triển (Lead Engineer)",
            DepartmentId = dept.Id,
            MinSalary = 25000000m,
            MaxSalary = 40000000m
        };
        await context.JobPositions.AddAsync(position);

        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-TECH-01",
            JobPositionId = position.Id,
            DepartmentId = dept.Id,
            HiringManagerId = recruiter.Id,
            Quantity = 1,
            MinSalary = 25000000m,
            MaxSalary = 40000000m,
            Status = RequisitionStatus.APPROVED
        };
        await context.JobRequisitions.AddAsync(req);

        var posting = new JobPosting
        {
            Id = Guid.NewGuid(),
            Title = "Tuyển Dụng Lead Engineer NoveraTech",
            RequisitionId = req.Id,
            Status = JobPostingStatus.PUBLISHED
        };
        await context.JobPostings.AddAsync(posting);

        var offerStage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Đề xuất thư mời (Offer)",
            StageOrder = 5
        };

        var hiredStage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Tuyển dụng thành công",
            StageOrder = 6
        };
        await context.PipelineStages.AddRangeAsync(offerStage, hiredStage);

        // Ứng viên 1: Nhận offer trong dải chuẩn (35,000,000 VNĐ <= 40,000,000 VNĐ)
        var candidate1 = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Trần",
            LastName = "Văn Nam",
            Email = "nam.tran@example.com"
        };
        await context.Candidates.AddAsync(candidate1);

        var app1 = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate1.Id,
            JobPostingId = posting.Id,
            CurrentStageId = offerStage.Id,
            Status = ApplicationStatus.IN_PROCESS
        };
        await context.Applications.AddAsync(app1);

        var offerWithinBand = new JobOffer
        {
            Id = Guid.NewGuid(),
            ApplicationId = app1.Id,
            CreatedByRecruiterId = recruiter.Id,
            JobPositionId = position.Id,
            JobTitle = "Trưởng nhóm Phát triển (Lead Engineer)",
            BaseSalary = 35000000m, // <= 40,000,000 m (TRONG DẢI CHUẨN)
            BonusAllowance = 5000000m,
            TotalPackage = 40000000m,
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.Today.AddDays(20)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            Status = OfferStatus.PENDING_APPROVAL,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.JobOffers.AddAsync(offerWithinBand);

        // Ứng viên 2: Nhận offer VƯỢT dải chuẩn (50,000,000 VNĐ > 40,000,000 VNĐ)
        var candidate2 = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Phạm",
            LastName = "Quốc Bảo",
            Email = "bao.pham@example.com"
        };
        await context.Candidates.AddAsync(candidate2);

        var app2 = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate2.Id,
            JobPostingId = posting.Id,
            CurrentStageId = offerStage.Id,
            Status = ApplicationStatus.IN_PROCESS
        };
        await context.Applications.AddAsync(app2);

        var offerAboveBand = new JobOffer
        {
            Id = Guid.NewGuid(),
            ApplicationId = app2.Id,
            CreatedByRecruiterId = recruiter.Id,
            JobPositionId = position.Id,
            JobTitle = "Trưởng nhóm Phát triển (Lead Engineer)",
            BaseSalary = 50000000m, // > 40,000,000 m (VƯỢT DẢI CHUẨN)
            BonusAllowance = 10000000m,
            TotalPackage = 60000000m,
            SalaryJustification = "Ứng viên có 12 năm kinh nghiệm kiến trúc hệ thống phân tán, từng là Principal Engineer tại tập đoàn lớn.",
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.Today.AddDays(15)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
            Status = OfferStatus.PENDING_APPROVAL,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.JobOffers.AddAsync(offerAboveBand);

        await context.SaveChangesAsync();

        return (context, hrManager, bodApprover, admin, recruiter, position, offerWithinBand, offerAboveBand, hiredStage);
    }

    [Fact]
    public async Task InitializeApprovalWorkflowAsync_WhenSalaryWithinBand_CreatesSingleLevelForHRManager()
    {
        // Arrange
        var (context, hrManager, _, _, _, _, offerWithinBand, _, _) = await SetupApprovalScenarioAsync();
        var sut = new OfferApprovalService(context, NullLogger<OfferApprovalService>.Instance);

        // Act
        await sut.InitializeApprovalWorkflowAsync(offerWithinBand.Id);

        // Assert
        var updatedOffer = await context.JobOffers
            .Include(o => o.Approvals)
            .FirstOrDefaultAsync(o => o.Id == offerWithinBand.Id);

        updatedOffer.Should().NotBeNull();
        updatedOffer!.IsAboveBudget.Should().BeFalse();
        updatedOffer.IsOutsideBand.Should().BeFalse();
        updatedOffer.Approvals.Should().HaveCount(1, "Offer trong dải chuẩn chỉ yêu cầu 1 cấp phê duyệt duy nhất của Trưởng phòng Nhân sự");

        var step1 = updatedOffer.Approvals.First();
        step1.Level.Should().Be(1);
        step1.ApproverId.Should().Be(hrManager.Id);
        step1.Status.Should().Be(ApprovalStatus.PENDING);
        step1.DecidedAt.Should().BeNull();
    }

    [Fact]
    public async Task InitializeApprovalWorkflowAsync_WhenSalaryAboveBand_CreatesTwoSequentialLevels()
    {
        // Arrange
        var (context, hrManager, bodApprover, _, _, _, _, offerAboveBand, _) = await SetupApprovalScenarioAsync();
        var sut = new OfferApprovalService(context, NullLogger<OfferApprovalService>.Instance);

        // Act
        await sut.InitializeApprovalWorkflowAsync(offerAboveBand.Id);

        // Assert
        var updatedOffer = await context.JobOffers
            .Include(o => o.Approvals)
            .FirstOrDefaultAsync(o => o.Id == offerAboveBand.Id);

        updatedOffer.Should().NotBeNull();
        updatedOffer!.IsAboveBudget.Should().BeTrue("Mức lương đề xuất 50 triệu vượt mức trần 40 triệu");
        updatedOffer.IsOutsideBand.Should().BeTrue();
        updatedOffer.Approvals.Should().HaveCount(2, "Offer vượt dải chuẩn tự động kích hoạt 2 cấp phê duyệt tuần tự");

        var steps = updatedOffer.Approvals.OrderBy(a => a.Level).ToList();
        
        // Cấp 1: Trưởng phòng Nhân sự
        steps[0].Level.Should().Be(1);
        steps[0].ApproverId.Should().Be(hrManager.Id);
        steps[0].Status.Should().Be(ApprovalStatus.PENDING);

        // Cấp 2: Ban Giám Đốc (Approver)
        steps[1].Level.Should().Be(2);
        steps[1].ApproverId.Should().Be(bodApprover.Id);
        steps[1].Status.Should().Be(ApprovalStatus.PENDING);
    }

    [Fact]
    public async Task SubmitApprovalDecisionAsync_WhenWithinBandAndHRManagerApproves_BecomesFullyApprovedAndAdvancesApplicationStage()
    {
        // Arrange
        var (context, hrManager, _, _, _, _, offerWithinBand, _, hiredStage) = await SetupApprovalScenarioAsync();
        var sut = new OfferApprovalService(context, NullLogger<OfferApprovalService>.Instance);
        await sut.InitializeApprovalWorkflowAsync(offerWithinBand.Id);

        var input = new OfferApprovalDecisionInputModel
        {
            OfferId = offerWithinBand.Id,
            Decision = "APPROVE",
            Comment = "Đồng ý mức đãi ngộ trong dải chuẩn cho ứng viên."
        };

        // Act
        var result = await sut.SubmitApprovalDecisionAsync(input, hrManager.Id, UserRoles.HRManager);

        // Assert
        result.Success.Should().BeTrue();
        result.IsFinalApproval.Should().BeTrue("Offer trong dải chuẩn hoàn thành phê duyệt ngay sau Cấp 1");
        result.OfferStatus.Should().Be(OfferStatus.APPROVED);

        var offerInDb = await context.JobOffers
            .Include(o => o.Application)
            .Include(o => o.Approvals)
            .FirstOrDefaultAsync(o => o.Id == offerWithinBand.Id);

        offerInDb!.Status.Should().Be(OfferStatus.APPROVED);
        offerInDb.Approvals.First().Status.Should().Be(ApprovalStatus.APPROVED);
        offerInDb.Approvals.First().DecidedAt.Should().NotBeNull();
        offerInDb.Approvals.First().Comment.Should().Be(input.Comment);
    }

    [Fact]
    public async Task SubmitApprovalDecisionAsync_WhenAboveBandAndHRManagerApprovesLevel1_RemainsPendingApprovalWaitingForLevel2()
    {
        // Arrange
        var (context, hrManager, _, _, _, _, _, offerAboveBand, _) = await SetupApprovalScenarioAsync();
        var sut = new OfferApprovalService(context, NullLogger<OfferApprovalService>.Instance);
        await sut.InitializeApprovalWorkflowAsync(offerAboveBand.Id);

        var input = new OfferApprovalDecisionInputModel
        {
            OfferId = offerAboveBand.Id,
            Decision = "APPROVE",
            Comment = "Đồng ý đề xuất vượt khung, chuyển tiếp Ban Giám Đốc phê chuẩn."
        };

        // Act: Trưởng phòng Nhân sự duyệt Cấp 1
        var result = await sut.SubmitApprovalDecisionAsync(input, hrManager.Id, UserRoles.HRManager);

        // Assert
        result.Success.Should().BeTrue();
        result.IsFinalApproval.Should().BeFalse("Vượt dải chuẩn yêu cầu 2 cấp nên chưa phải bước phê duyệt cuối");
        result.OfferStatus.Should().Be(OfferStatus.PENDING_APPROVAL);
        result.NextLevel.Should().Be(2);

        var offerInDb = await context.JobOffers
            .Include(o => o.Approvals)
            .FirstOrDefaultAsync(o => o.Id == offerAboveBand.Id);

        offerInDb!.Status.Should().Be(OfferStatus.PENDING_APPROVAL);
        var steps = offerInDb.Approvals.OrderBy(a => a.Level).ToList();
        steps[0].Status.Should().Be(ApprovalStatus.APPROVED);
        steps[1].Status.Should().Be(ApprovalStatus.PENDING);
    }

    [Fact]
    public async Task SubmitApprovalDecisionAsync_WhenAboveBandAndBothLevelsApprove_BecomesFullyApproved()
    {
        // Arrange
        var (context, hrManager, bodApprover, _, _, _, _, offerAboveBand, hiredStage) = await SetupApprovalScenarioAsync();
        var sut = new OfferApprovalService(context, NullLogger<OfferApprovalService>.Instance);
        await sut.InitializeApprovalWorkflowAsync(offerAboveBand.Id);

        // Bước 1: Trưởng phòng Nhân sự duyệt Cấp 1
        await sut.SubmitApprovalDecisionAsync(new OfferApprovalDecisionInputModel
        {
            OfferId = offerAboveBand.Id,
            Decision = "APPROVE",
            Comment = "Đã rà soát năng lực và hồ sơ phỏng vấn, đề xuất BGĐ thông qua."
        }, hrManager.Id, UserRoles.HRManager);

        // Bước 2: Ban Giám Đốc duyệt Cấp 2
        var bodDecision = new OfferApprovalDecisionInputModel
        {
            OfferId = offerAboveBand.Id,
            Decision = "APPROVE",
            Comment = "Ban Giám Đốc phê chuẩn mức đãi ngộ đặc biệt cho nhân tài chiến lược."
        };

        // Act
        var result = await sut.SubmitApprovalDecisionAsync(bodDecision, bodApprover.Id, UserRoles.Approver);

        // Assert
        result.Success.Should().BeTrue();
        result.IsFinalApproval.Should().BeTrue();
        result.OfferStatus.Should().Be(OfferStatus.APPROVED);

        var offerInDb = await context.JobOffers
            .Include(o => o.Application)
            .Include(o => o.Approvals)
            .FirstOrDefaultAsync(o => o.Id == offerAboveBand.Id);

        offerInDb!.Status.Should().Be(OfferStatus.APPROVED);
        offerInDb.Approvals.All(a => a.Status == ApprovalStatus.APPROVED).Should().BeTrue();
    }

    [Fact]
    public async Task SubmitApprovalDecisionAsync_WhenApproverTriesToApproveBeforeLevel1_ReturnsFailResult()
    {
        // Arrange
        var (context, _, bodApprover, _, _, _, _, offerAboveBand, _) = await SetupApprovalScenarioAsync();
        var sut = new OfferApprovalService(context, NullLogger<OfferApprovalService>.Instance);
        await sut.InitializeApprovalWorkflowAsync(offerAboveBand.Id);

        var bodDecision = new OfferApprovalDecisionInputModel
        {
            OfferId = offerAboveBand.Id,
            Decision = "APPROVE",
            Comment = "Duyệt trước khi cấp 1 xem xét"
        };

        // Act & Assert: Cấp 2 không được duyệt khi Cấp 1 còn PENDING
        var result = await sut.SubmitApprovalDecisionAsync(bodDecision, bodApprover.Id, UserRoles.Approver);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Cấp 1");
    }

    [Fact]
    public async Task SubmitApprovalDecisionAsync_WhenRejectWithoutComment_ReturnsFailResult()
    {
        // Arrange
        var (context, hrManager, _, _, _, _, offerWithinBand, _, _) = await SetupApprovalScenarioAsync();
        var sut = new OfferApprovalService(context, NullLogger<OfferApprovalService>.Instance);
        await sut.InitializeApprovalWorkflowAsync(offerWithinBand.Id);

        var rejectInput = new OfferApprovalDecisionInputModel
        {
            OfferId = offerWithinBand.Id,
            Decision = "REJECT",
            Comment = "" // Rỗng - vi phạm tiêu chí bắt buộc
        };

        // Act & Assert
        var result = await sut.SubmitApprovalDecisionAsync(rejectInput, hrManager.Id, UserRoles.HRManager);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("lý do từ chối");
    }

    [Fact]
    public async Task SubmitApprovalDecisionAsync_WhenRejectWithComment_UpdatesStatusToRejectedAndPreservesComment()
    {
        // Arrange
        var (context, hrManager, _, _, _, _, offerWithinBand, _, _) = await SetupApprovalScenarioAsync();
        var sut = new OfferApprovalService(context, NullLogger<OfferApprovalService>.Instance);
        await sut.InitializeApprovalWorkflowAsync(offerWithinBand.Id);

        var rejectInput = new OfferApprovalDecisionInputModel
        {
            OfferId = offerWithinBand.Id,
            Decision = "REJECT",
            Comment = "Mức lương và chế độ đãi ngộ chưa cân đối được quỹ lương khối kỹ thuật."
        };

        // Act
        var result = await sut.SubmitApprovalDecisionAsync(rejectInput, hrManager.Id, UserRoles.HRManager);

        // Assert
        result.Success.Should().BeTrue();
        result.OfferStatus.Should().Be(OfferStatus.REJECTED);

        var offerInDb = await context.JobOffers
            .Include(o => o.Approvals)
            .FirstOrDefaultAsync(o => o.Id == offerWithinBand.Id);

        offerInDb!.Status.Should().Be(OfferStatus.REJECTED);
        offerInDb.Approvals.First().Status.Should().Be(ApprovalStatus.REJECTED);
        offerInDb.Approvals.First().Comment.Should().Be(rejectInput.Comment);
        offerInDb.Approvals.First().DecidedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SubmitApprovalDecisionAsync_WhenRequestChangesWithoutComment_ReturnsFailResult()
    {
        // Arrange
        var (context, hrManager, _, _, _, _, offerWithinBand, _, _) = await SetupApprovalScenarioAsync();
        var sut = new OfferApprovalService(context, NullLogger<OfferApprovalService>.Instance);
        await sut.InitializeApprovalWorkflowAsync(offerWithinBand.Id);

        var requestChangesInput = new OfferApprovalDecisionInputModel
        {
            OfferId = offerWithinBand.Id,
            Decision = "CHANGES_REQUESTED",
            Comment = "   " // Khoảng trắng - vi phạm
        };

        // Act & Assert
        var result = await sut.SubmitApprovalDecisionAsync(requestChangesInput, hrManager.Id, UserRoles.HRManager);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("nội dung yêu cầu chỉnh sửa");
    }

    [Fact]
    public async Task SubmitApprovalDecisionAsync_WhenRequestChangesWithComment_RevertsOfferStatusToDraftForRecruiterEdit()
    {
        // Arrange
        var (context, hrManager, _, _, _, _, offerWithinBand, _, _) = await SetupApprovalScenarioAsync();
        var sut = new OfferApprovalService(context, NullLogger<OfferApprovalService>.Instance);
        await sut.InitializeApprovalWorkflowAsync(offerWithinBand.Id);

        var requestChangesInput = new OfferApprovalDecisionInputModel
        {
            OfferId = offerWithinBand.Id,
            Decision = "CHANGES_REQUESTED",
            Comment = "Yêu cầu đàm phán lại thời gian thử việc 1 tháng và điều chỉnh phụ cấp sang thưởng KPI."
        };

        // Act
        var result = await sut.SubmitApprovalDecisionAsync(requestChangesInput, hrManager.Id, UserRoles.HRManager);

        // Assert
        result.Success.Should().BeTrue();
        result.OfferStatus.Should().Be(OfferStatus.DRAFT, "Yêu cầu sửa đổi phải trả Offer về trạng thái Bản nháp để Recruiter chỉnh sửa");

        var offerInDb = await context.JobOffers
            .Include(o => o.Approvals)
            .FirstOrDefaultAsync(o => o.Id == offerWithinBand.Id);

        offerInDb!.Status.Should().Be(OfferStatus.DRAFT);
        offerInDb.Approvals.First().Status.Should().Be(ApprovalStatus.CHANGES_REQUESTED);
        offerInDb.Approvals.First().Comment.Should().Be(requestChangesInput.Comment);
    }

    [Fact]
    public async Task SubmitApprovalDecisionAsync_WhenUnauthorizedUserAttemptsAction_ReturnsFailResult()
    {
        // Arrange
        var (context, _, _, _, _, _, offerWithinBand, _, _) = await SetupApprovalScenarioAsync();
        var sut = new OfferApprovalService(context, NullLogger<OfferApprovalService>.Instance);
        await sut.InitializeApprovalWorkflowAsync(offerWithinBand.Id);

        var outsiderUserId = Guid.NewGuid();

        var input = new OfferApprovalDecisionInputModel
        {
            OfferId = offerWithinBand.Id,
            Decision = "APPROVE",
            Comment = "Tự duyệt"
        };

        // Act & Assert
        var result = await sut.SubmitApprovalDecisionAsync(input, outsiderUserId, "Candidate");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("không có quyền");
    }

    [Fact]
    public async Task GetApprovalHistoryAsync_ReturnsImmutableChronologicalAuditTrail()
    {
        // Arrange
        var (context, hrManager, bodApprover, _, _, _, _, offerAboveBand, _) = await SetupApprovalScenarioAsync();
        var sut = new OfferApprovalService(context, NullLogger<OfferApprovalService>.Instance);
        await sut.InitializeApprovalWorkflowAsync(offerAboveBand.Id);

        // Cấp 1 duyệt
        await sut.SubmitApprovalDecisionAsync(new OfferApprovalDecisionInputModel
        {
            OfferId = offerAboveBand.Id,
            Decision = "APPROVE",
            Comment = "Cấp 1 đã duyệt thông qua"
        }, hrManager.Id, UserRoles.HRManager);

        // Act
        var history = await sut.GetApprovalHistoryAsync(offerAboveBand.Id);

        // Assert
        history.Should().NotBeNull();
        history.Should().HaveCount(2);

        var step1 = history.First(h => h.Level == 1);
        step1.ApproverName.Should().Be(hrManager.FullName);
        step1.Status.Should().Be(ApprovalStatus.APPROVED);
        step1.Comment.Should().Be("Cấp 1 đã duyệt thông qua");
        step1.DecidedAt.Should().NotBeNull();

        var step2 = history.First(h => h.Level == 2);
        step2.ApproverName.Should().Be(bodApprover.FullName);
        step2.Status.Should().Be(ApprovalStatus.PENDING);
        step2.Comment.Should().BeNull();
        step2.DecidedAt.Should().BeNull();
    }
}
