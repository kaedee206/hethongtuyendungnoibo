using System.Text.Json;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Requisitions;
using Ats.Web.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ats.Web.Tests;

public class RequisitionApprovalTrackingTests
{
    private readonly Mock<ILogger<RequisitionService>> _loggerMock = new();

    private static (ApplicationDbContext Context, User HrUser, User BodUser, User HmUser, JobRequisition Requisition) SetupTestData()
    {
        var context = TestDbContextFactory.CreateInMemoryDbContext();

        var hrRole = context.Roles.First(r => r.Code == RoleCode.HR_MANAGER);
        var approverRole = context.Roles.First(r => r.Code == RoleCode.APPROVER);
        var hmRole = context.Roles.First(r => r.Code == RoleCode.HIRING_MANAGER);

        var hrUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "phuong.nguyen@noveratech.digital",
            FullName = "Nguyễn Mai Phương",
            Role = UserRoles.HRManager,
            RoleId = hrRole.Id,
            Status = "ACTIVE"
        };

        var bodUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "minh.tran@noveratech.digital",
            FullName = "Trần Đức Minh",
            Role = UserRoles.Approver,
            RoleId = approverRole.Id,
            Status = "ACTIVE"
        };

        var hmUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "it@noveratech.vn",
            FullName = "Vũ Thành Long",
            Role = UserRoles.HiringManager,
            RoleId = hmRole.Id,
            Status = "ACTIVE"
        };

        context.Users.AddRange(hrUser, bodUser, hmUser);
        context.UserRoles.AddRange(
            new UserRole { UserId = hrUser.Id, RoleId = hrRole.Id, Role = hrRole },
            new UserRole { UserId = bodUser.Id, RoleId = approverRole.Id, Role = approverRole },
            new UserRole { UserId = hmUser.Id, RoleId = hmRole.Id, Role = hmRole }
        );

        var dept = new Department { Id = Guid.NewGuid(), Code = "IT", Name = "Công nghệ thông tin", IsActive = true };
        var pos = new JobPosition { Id = Guid.NewGuid(), Code = "DEV-LEAD", Title = "Technical Lead", DepartmentId = dept.Id, IsActive = true };
        context.Departments.Add(dept);
        context.JobPositions.Add(pos);

        var requisition = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-20261009-TRACK",
            JobPositionId = pos.Id,
            DepartmentId = dept.Id,
            HiringManagerId = hmUser.Id,
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 30_000_000,
            MaxSalary = 50_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(45)),
            JobDescription = "<p>Mô tả công việc vị trí Technical Lead</p>",
            Requirements = "<p>Yêu cầu ít nhất 7 năm kinh nghiệm</p>",
            Status = RequisitionStatus.PENDING_APPROVAL,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        var step1 = new RequisitionApproval
        {
            Id = Guid.NewGuid(),
            RequisitionId = requisition.Id,
            ApproverId = hrUser.Id,
            Approver = hrUser,
            StepOrder = 1,
            Status = ApprovalStatus.PENDING,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        var step2 = new RequisitionApproval
        {
            Id = Guid.NewGuid(),
            RequisitionId = requisition.Id,
            ApproverId = bodUser.Id,
            Approver = bodUser,
            StepOrder = 2,
            Status = ApprovalStatus.PENDING,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        requisition.Approvals = new List<RequisitionApproval> { step1, step2 };

        context.JobRequisitions.Add(requisition);
        context.RequisitionApprovals.AddRange(step1, step2);
        context.SaveChanges();

        return (context, hrUser, bodUser, hmUser, requisition);
    }

    [Fact]
    public async Task GetRequisitionDetailsAsync_ShouldDisplayCurrentApproverDeskAndChainInfo_ForPendingRequisition()
    {
        // Arrange
        var (context, hrUser, bodUser, hmUser, requisition) = SetupTestData();
        var service = new RequisitionService(context, _loggerMock.Object);

        // Act - Trưởng bộ phận hmUser xem chi tiết yêu cầu
        var details = await service.GetRequisitionDetailsAsync(requisition.Id, hmUser.Id);

        // Assert
        details.Should().NotBeNull();
        details!.IsCurrentUserHiringManager.Should().BeTrue();
        details.CurrentApproverName.Should().Be("Nguyễn Mai Phương");
        details.CurrentApproverRole.Should().Contain("HR Manager");
        details.CurrentApproverEmail.Should().Be("phuong.nguyen@noveratech.digital");
        details.CurrentStageSummary.Should().Contain("Cấp 1");
        details.CurrentWaitingDurationDisplay.Should().NotBeNullOrWhiteSpace();
        details.ApprovalSteps.Should().HaveCount(2);

        // Kiểm tra bước 1 (đang chờ)
        var firstStep = details.ApprovalSteps[0];
        firstStep.ApproverName.Should().Be("Nguyễn Mai Phương");
        firstStep.ApproverEmail.Should().Be("phuong.nguyen@noveratech.digital");
        firstStep.ApproverRole.Should().Contain("HR Manager");
        firstStep.IsCurrent.Should().BeTrue();
        firstStep.IsImmutable.Should().BeTrue();
        firstStep.StepCreatedAtDisplay.Should().NotBeNullOrWhiteSpace();

        // Kiểm tra bước 2 (chờ cấp 1 xong)
        var secondStep = details.ApprovalSteps[1];
        secondStep.ApproverName.Should().Be("Trần Đức Minh");
        secondStep.ApproverEmail.Should().Be("minh.tran@noveratech.digital");
        secondStep.ApproverRole.Should().Contain("BOD");
        secondStep.IsCurrent.Should().BeFalse();
        secondStep.IsImmutable.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessApprovalDecisionAsync_ShouldSaveAuditLog_WhenApproverMakesDecision()
    {
        // Arrange
        var (context, hrUser, _, _, requisition) = SetupTestData();
        var service = new RequisitionService(context, _loggerMock.Object);

        var decision = new RequisitionApprovalDecisionInputModel
        {
            RequisitionId = requisition.Id,
            Action = "APPROVE",
            Comment = "HR đồng ý định biên và mức lương này."
        };

        // Act - Cấp 1 HR duyệt
        var result = await service.ProcessApprovalDecisionAsync(requisition.Id, hrUser.Id, decision);

        // Assert
        result.Success.Should().BeTrue();

        // Kiểm tra bảng AuditLogs có bản ghi bất biến được ghi nhận tự động
        var auditLog = await context.AuditLogs
            .FirstOrDefaultAsync(a => a.EntityName == "JobRequisition" && a.EntityId == requisition.Id.ToString());

        auditLog.Should().NotBeNull();
        auditLog!.Action.Should().Be("REQUISITION_APPROVAL_APPROVE");
        auditLog.UserId.Should().Be(hrUser.Id);
        auditLog.NewValues.Should().NotBeNull();
        auditLog.NewValues.Should().Contain("HR đồng ý định biên và mức lương này.");
        auditLog.NewValues.Should().Contain("APPROVE");
    }

    [Fact]
    public async Task GetRequisitionsForApprovalAsync_MyRequisitionsTab_ShouldOnlyIncludeOwnRequisitions()
    {
        // Arrange
        var (context, _, _, hmUser, requisition) = SetupTestData();
        var otherDept = new Department { Id = Guid.NewGuid(), Code = "MKT", Name = "Marketing", IsActive = true };
        var otherPos = new JobPosition { Id = Guid.NewGuid(), Code = "MKT-LEAD", Title = "Marketing Lead", DepartmentId = otherDept.Id, IsActive = true };
        var otherHmUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "mkt@noveratech.vn",
            FullName = "Hoàng Hải Yến",
            Role = UserRoles.HiringManager,
            RoleId = context.Roles.First(r => r.Code == RoleCode.HIRING_MANAGER).Id,
            Status = "ACTIVE"
        };
        context.Departments.Add(otherDept);
        context.JobPositions.Add(otherPos);
        context.Users.Add(otherHmUser);

        var otherReq = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-20261009-OTHER",
            JobPositionId = otherPos.Id,
            DepartmentId = otherDept.Id,
            HiringManagerId = otherHmUser.Id,
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 25_000_000,
            MaxSalary = 35_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            Status = RequisitionStatus.PENDING_APPROVAL,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        context.JobRequisitions.Add(otherReq);
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _loggerMock.Object);

        // Act - hmUser lọc tab "my-requisitions"
        var list = await service.GetRequisitionsForApprovalAsync(hmUser.Id, tab: "my-requisitions");

        // Assert
        list.Items.Should().HaveCount(1);
        list.Items.Should().ContainSingle(i => i.Id == requisition.Id);
        var item = list.Items.First();
        item.IsCreatedByCurrentUser.Should().BeTrue();
        item.CurrentApproverName.Should().Be("Nguyễn Mai Phương");
        item.CurrentApproverRole.Should().Contain("HR Manager");
        item.ApprovalChainProgress.Should().Contain("Cấp 1");
    }

    [Fact]
    public async Task GetMyRequisitionsCountAsync_ShouldReturnAccurateCount()
    {
        // Arrange
        var (context, hrUser, _, hmUser, _) = SetupTestData();
        var service = new RequisitionService(context, _loggerMock.Object);

        // Act
        var countForHm = await service.GetMyRequisitionsCountAsync(hmUser.Id);
        var countForHr = await service.GetMyRequisitionsCountAsync(hrUser.Id);

        // Assert
        countForHm.Should().Be(1);
        countForHr.Should().Be(0);
    }

    [Fact]
    public async Task MultiRoundApprovalHistory_ShouldRetainPreviousRoundsAndOrderCorrectly()
    {
        // Arrange
        var (context, hrUser, _, hmUser, requisition) = SetupTestData();
        var service = new RequisitionService(context, _loggerMock.Object);

        // 1. Cấp 1 HR yêu cầu bổ sung
        var changeRequestDecision = new RequisitionApprovalDecisionInputModel
        {
            RequisitionId = requisition.Id,
            Action = "REQUEST_CHANGES",
            Comment = "Vui lòng giải trình rõ hơn về lý do mở thêm định biên mới."
        };
        var res1 = await service.ProcessApprovalDecisionAsync(requisition.Id, hrUser.Id, changeRequestDecision);
        res1.Success.Should().BeTrue();

        // 2. Trưởng bộ phận cập nhật hồ sơ và gửi lại phê duyệt
        var draftModel = await service.GetDraftByIdAsync(requisition.Id, hmUser.Id);
        draftModel.Should().NotBeNull();
        draftModel!.JobDescription = "<p>Đã cập nhật bản phân tích khối lượng công việc theo yêu cầu của HR.</p>";
        draftModel.IsDraft = false; // Gửi phê duyệt lại

        var resUpdate = await service.SaveOrUpdateRequisitionAsync(draftModel, hmUser.Id);
        resUpdate.Success.Should().BeTrue();

        // Act - Xem lại chi tiết để kiểm tra lịch sử phê duyệt đa vòng
        var details = await service.GetRequisitionDetailsAsync(requisition.Id, hmUser.Id);

        // Assert
        details.Should().NotBeNull();
        details!.ApprovalSteps.Should().HaveCount(3); // 1 bước ở vòng 1 (CHANGES_REQUESTED) + 2 bước ở vòng 2 (PENDING)
        
        // Vòng 1: Cấp 1 HR yêu cầu bổ sung
        var round1Step = details.ApprovalSteps.First(s => s.Status == ApprovalStatus.CHANGES_REQUESTED);
        round1Step.RoundNumber.Should().Be(1);
        round1Step.Comment.Should().Be("Vui lòng giải trình rõ hơn về lý do mở thêm định biên mới.");
        round1Step.IsImmutable.Should().BeTrue();

        // Vòng 2: Bước đang chờ duyệt lại
        var round2Step = details.ApprovalSteps.First(s => s.RoundNumber == 2 && s.IsCurrent);
        round2Step.ApproverName.Should().Be("Nguyễn Mai Phương");
        round2Step.IsCurrent.Should().BeTrue();
    }
}
