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

public class RequisitionApprovalTests
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
        var pos = new JobPosition { Id = Guid.NewGuid(), Code = "DEV-SR", Title = "Senior Developer", DepartmentId = dept.Id, IsActive = true };
        context.Departments.Add(dept);
        context.JobPositions.Add(pos);

        var requisition = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-20261009-TEST",
            JobPositionId = pos.Id,
            DepartmentId = dept.Id,
            HiringManagerId = hmUser.Id,
            Quantity = 2,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 20_000_000,
            MaxSalary = 35_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            JobDescription = "<p>Mô tả công việc vị trí Senior Developer</p>",
            Requirements = "<p>Yêu cầu ít nhất 5 năm kinh nghiệm C# và React</p>",
            Status = RequisitionStatus.PENDING_APPROVAL,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var step1 = new RequisitionApproval
        {
            Id = Guid.NewGuid(),
            RequisitionId = requisition.Id,
            ApproverId = hrUser.Id,
            Approver = hrUser,
            StepOrder = 1,
            Status = ApprovalStatus.PENDING,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var step2 = new RequisitionApproval
        {
            Id = Guid.NewGuid(),
            RequisitionId = requisition.Id,
            ApproverId = bodUser.Id,
            Approver = bodUser,
            StepOrder = 2,
            Status = ApprovalStatus.PENDING,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        context.JobRequisitions.Add(requisition);
        context.RequisitionApprovals.AddRange(step1, step2);
        context.SaveChanges();

        return (context, hrUser, bodUser, hmUser, requisition);
    }

    [Fact]
    public async Task ProcessApprovalDecisionAsync_ApproveStep1_AdvancesToStep2_RequisitionRemainsPendingApproval()
    {
        var (context, hrUser, _, _, requisition) = SetupTestData();
        var service = new RequisitionService(context, _loggerMock.Object);

        var input = new RequisitionApprovalDecisionInputModel
        {
            RequisitionId = requisition.Id,
            Action = "APPROVE",
            Comment = "Đã rà soát ngân sách lương hợp lý, đồng ý cấp 1."
        };

        var result = await service.ProcessApprovalDecisionAsync(requisition.Id, hrUser.Id, input);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Cấp 1");

        var updatedReq = await context.JobRequisitions.Include(r => r.Approvals).FirstAsync(r => r.Id == requisition.Id);
        updatedReq.Status.Should().Be(RequisitionStatus.PENDING_APPROVAL);

        var step1 = updatedReq.Approvals.First(a => a.StepOrder == 1);
        step1.Status.Should().Be(ApprovalStatus.APPROVED);
        step1.Comment.Should().Be("Đã rà soát ngân sách lương hợp lý, đồng ý cấp 1.");
        step1.DecidedAt.Should().NotBeNull();

        var step2 = updatedReq.Approvals.First(a => a.StepOrder == 2);
        step2.Status.Should().Be(ApprovalStatus.PENDING);
    }

    [Fact]
    public async Task ProcessApprovalDecisionAsync_ApproveFinalStep_ChangesRequisitionStatusToApproved()
    {
        var (context, hrUser, bodUser, _, requisition) = SetupTestData();
        var service = new RequisitionService(context, _loggerMock.Object);

        // Bước 1: HR duyệt
        await service.ProcessApprovalDecisionAsync(requisition.Id, hrUser.Id, new RequisitionApprovalDecisionInputModel
        {
            RequisitionId = requisition.Id,
            Action = "APPROVE"
        });

        // Bước 2: BOD duyệt (cấp cuối)
        var result = await service.ProcessApprovalDecisionAsync(requisition.Id, bodUser.Id, new RequisitionApprovalDecisionInputModel
        {
            RequisitionId = requisition.Id,
            Action = "APPROVE",
            Comment = "Ban Giám Đốc đồng ý phê duyệt tuyển dụng."
        });

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("hoàn tất");

        var updatedReq = await context.JobRequisitions.Include(r => r.Approvals).FirstAsync(r => r.Id == requisition.Id);
        updatedReq.Status.Should().Be(RequisitionStatus.APPROVED);

        var step2 = updatedReq.Approvals.First(a => a.StepOrder == 2);
        step2.Status.Should().Be(ApprovalStatus.APPROVED);
        step2.DecidedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessApprovalDecisionAsync_Reject_RequiresComment_FailsIfEmpty()
    {
        var (context, hrUser, _, _, requisition) = SetupTestData();
        var service = new RequisitionService(context, _loggerMock.Object);

        var input = new RequisitionApprovalDecisionInputModel
        {
            RequisitionId = requisition.Id,
            Action = "REJECT",
            Comment = "   " // Rỗng
        };

        var result = await service.ProcessApprovalDecisionAsync(requisition.Id, hrUser.Id, input);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("lý do từ chối");

        var reqInDb = await context.JobRequisitions.FindAsync(requisition.Id);
        reqInDb!.Status.Should().Be(RequisitionStatus.PENDING_APPROVAL);
    }

    [Fact]
    public async Task ProcessApprovalDecisionAsync_Reject_WithComment_ChangesStatusToRejected()
    {
        var (context, hrUser, _, _, requisition) = SetupTestData();
        var service = new RequisitionService(context, _loggerMock.Object);

        var input = new RequisitionApprovalDecisionInputModel
        {
            RequisitionId = requisition.Id,
            Action = "REJECT",
            Comment = "Kế hoạch kinh doanh quý này cắt giảm định biên, không duyệt tăng mới."
        };

        var result = await service.ProcessApprovalDecisionAsync(requisition.Id, hrUser.Id, input);

        result.Success.Should().BeTrue();

        var updatedReq = await context.JobRequisitions.Include(r => r.Approvals).FirstAsync(r => r.Id == requisition.Id);
        updatedReq.Status.Should().Be(RequisitionStatus.REJECTED);

        var step1 = updatedReq.Approvals.First(a => a.StepOrder == 1);
        step1.Status.Should().Be(ApprovalStatus.REJECTED);
        step1.Comment.Should().Be("Kế hoạch kinh doanh quý này cắt giảm định biên, không duyệt tăng mới.");
    }

    [Fact]
    public async Task ProcessApprovalDecisionAsync_RequestChanges_RequiresComment_FailsIfEmpty()
    {
        var (context, hrUser, _, _, requisition) = SetupTestData();
        var service = new RequisitionService(context, _loggerMock.Object);

        var input = new RequisitionApprovalDecisionInputModel
        {
            RequisitionId = requisition.Id,
            Action = "REQUEST_CHANGES",
            Comment = ""
        };

        var result = await service.ProcessApprovalDecisionAsync(requisition.Id, hrUser.Id, input);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("ý kiến yêu cầu bổ sung");
    }

    [Fact]
    public async Task ProcessApprovalDecisionAsync_RequestChanges_ReturnsToCreator_PreservesHistory()
    {
        var (context, hrUser, _, hmUser, requisition) = SetupTestData();
        var service = new RequisitionService(context, _loggerMock.Object);

        var feedbackComment = "Cần bổ sung chi tiết KPIs kỹ năng React và điều chỉnh lại dải lương chuẩn.";

        // Người duyệt yêu cầu bổ sung
        var result = await service.ProcessApprovalDecisionAsync(requisition.Id, hrUser.Id, new RequisitionApprovalDecisionInputModel
        {
            RequisitionId = requisition.Id,
            Action = "REQUEST_CHANGES",
            Comment = feedbackComment
        });

        result.Success.Should().BeTrue();

        // 1. Kiểm tra trạng thái yêu cầu chuyển sang CHANGES_REQUESTED
        var reqAfterFeedback = await context.JobRequisitions.Include(r => r.Approvals).FirstAsync(r => r.Id == requisition.Id);
        reqAfterFeedback.Status.Should().Be(RequisitionStatus.CHANGES_REQUESTED);

        // 2. Người tạo mở lại để sửa: Phải đọc được phản hồi của người duyệt
        var draftModel = await service.GetDraftByIdAsync(requisition.Id, hmUser.Id);
        draftModel.Should().NotBeNull();
        draftModel!.Status.Should().Be(RequisitionStatus.CHANGES_REQUESTED);
        draftModel.ReviewerFeedback.Should().Be(feedbackComment);

        // 3. Người tạo cập nhật và gửi duyệt lại
        draftModel.JobDescription = "<p>Mô tả công việc đã bổ sung rõ ràng chi tiết KPI.</p>";
        draftModel.Requirements = "<p>Yêu cầu ứng viên đã cập nhật chứng chỉ và React 19.</p>";
        draftModel.IsDraft = false; // Gửi duyệt lại

        var updateResult = await service.SaveOrUpdateRequisitionAsync(draftModel, hmUser.Id);
        updateResult.Success.Should().BeTrue();

        // 4. Kiểm tra LỊCH SỬ DUYỆT ĐƯỢC GIỮ NGUYÊN
        var allApprovals = await context.RequisitionApprovals
            .Where(a => a.RequisitionId == requisition.Id)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();

        // Phải có ít nhất bản ghi CHANGES_REQUESTED cũ và các bước pending mới
        allApprovals.Should().Contain(a => a.Status == ApprovalStatus.CHANGES_REQUESTED && a.Comment == feedbackComment);
        allApprovals.Should().Contain(a => a.Status == ApprovalStatus.PENDING && a.StepOrder == 1);
        allApprovals.Should().Contain(a => a.Status == ApprovalStatus.PENDING && a.StepOrder == 2);

        var reqResubmitted = await context.JobRequisitions.FindAsync(requisition.Id);
        reqResubmitted!.Status.Should().Be(RequisitionStatus.PENDING_APPROVAL);
    }

    [Fact]
    public async Task ProcessApprovalDecisionAsync_NonApprover_CannotApprove()
    {
        var (context, _, _, hmUser, requisition) = SetupTestData();
        var service = new RequisitionService(context, _loggerMock.Object);

        // Hiring Manager không thể tự duyệt yêu cầu của mình ở Step 1 (thuộc quyền HR Manager)
        var result = await service.ProcessApprovalDecisionAsync(requisition.Id, hmUser.Id, new RequisitionApprovalDecisionInputModel
        {
            RequisitionId = requisition.Id,
            Action = "APPROVE"
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("không có quyền");
    }
}
