using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Controllers;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.RequisitionAssignments;
using Ats.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ats.Web.Tests;

public class RequisitionAssignmentTests
{
    private readonly NullLogger<RequisitionAssignmentService> _serviceLogger = NullLogger<RequisitionAssignmentService>.Instance;
    private readonly NullLogger<RequisitionAssignmentsController> _controllerLogger = NullLogger<RequisitionAssignmentsController>.Instance;

    private static (Department dept, JobPosition pos, JobRequisition req, User leadUser, User supportUser, User hrManager) CreateBasicTestData(Data.ApplicationDbContext db)
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Phòng Công nghệ",
            Code = "IT",
            IsActive = true
        };
        db.Departments.Add(dept);

        var pos = new JobPosition
        {
            Id = Guid.NewGuid(),
            Title = "Senior Backend Engineer",
            Code = "SWE-01",
            DepartmentId = dept.Id,
            JobLevel = "Senior",
            IsActive = true
        };
        db.JobPositions.Add(pos);

        var hrManager = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Mai Phương",
            Email = "hrm@ats.test",
            Role = RoleCode.HR_MANAGER.ToString(),
            Status = "ACTIVE",
            DepartmentId = dept.Id
        };

        var leadUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Lê Thùy Dung",
            Email = "recruiter.lead@ats.test",
            Role = RoleCode.RECRUITER.ToString(),
            Status = "ACTIVE",
            DepartmentId = dept.Id
        };

        var supportUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Phạm Quốc Anh",
            Email = "recruiter.support@ats.test",
            Role = RoleCode.RECRUITER.ToString(),
            Status = "ACTIVE",
            DepartmentId = dept.Id
        };

        db.Users.AddRange(hrManager, leadUser, supportUser);

        // Assign Recruiter role in UserRoles table
        var recRole = db.Roles.First(r => r.Code == RoleCode.RECRUITER);
        db.UserRoles.AddRange(
            new UserRole { UserId = leadUser.Id, RoleId = recRole.Id },
            new UserRole { UserId = supportUser.Id, RoleId = recRole.Id }
        );

        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-IT-001",
            JobPositionId = pos.Id,
            DepartmentId = dept.Id,
            HiringManagerId = hrManager.Id,
            Quantity = 2,
            Status = RequisitionStatus.APPROVED,
            Currency = "VND",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.JobRequisitions.Add(req);

        db.SaveChanges();

        return (dept, pos, req, leadUser, supportUser, hrManager);
    }

    [Fact]
    public async Task AssignRecruitersAsync_AssignsLeadAndSupportingRecruitersSuccessfully()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, _, req, leadUser, supportUser, hrManager) = CreateBasicTestData(db);
        var service = new RequisitionAssignmentService(db, _serviceLogger);

        var model = new RequisitionAssignRequestModel
        {
            RequisitionId = req.Id,
            LeadRecruiterId = leadUser.Id,
            SupportingRecruiterIds = new List<Guid> { supportUser.Id },
            Notes = "Phân công ban đầu cho dự án trọng điểm."
        };

        // Act
        var result = await service.AssignRecruitersAsync(model, hrManager.Id);

        // Assert
        result.Success.Should().BeTrue();

        // Kiểm tra Requisition.AssignedRecruiterId được cập nhật
        var updatedReq = await db.JobRequisitions.FindAsync(req.Id);
        updatedReq!.AssignedRecruiterId.Should().Be(leadUser.Id);

        // Kiểm tra RequisitionRecruiters có 1 primary và 1 supporting
        var assignments = db.RequisitionRecruiters.Where(r => r.RequisitionId == req.Id).ToList();
        assignments.Should().HaveCount(2);

        var primary = assignments.FirstOrDefault(r => r.IsPrimary);
        primary.Should().NotBeNull();
        primary!.RecruiterId.Should().Be(leadUser.Id);

        var supporting = assignments.FirstOrDefault(r => !r.IsPrimary);
        supporting.Should().NotBeNull();
        supporting!.RecruiterId.Should().Be(supportUser.Id);

        // Kiểm tra lịch sử chuyển giao ban đầu được ghi nhận
        var histories = db.RequisitionHandoverHistories.Where(h => h.RequisitionId == req.Id).ToList();
        histories.Should().HaveCount(1);
        histories[0].HandoverType.Should().Be("INITIAL_ASSIGNMENT");
        histories[0].ToRecruiterId.Should().Be(leadUser.Id);
    }

    [Fact]
    public async Task AssignRecruitersAsync_WhenLeadChanges_RequiresReason()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, _, req, leadUser, supportUser, hrManager) = CreateBasicTestData(db);
        var service = new RequisitionAssignmentService(db, _serviceLogger);

        // Đã phân công leadUser trước đó
        req.AssignedRecruiterId = leadUser.Id;
        db.RequisitionRecruiters.Add(new RequisitionRecruiter
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            RecruiterId = leadUser.Id,
            IsPrimary = true,
            AssignedById = hrManager.Id,
            AssignedAt = DateTimeOffset.UtcNow.AddDays(-5)
        });
        await db.SaveChangesAsync();

        // Chuyển giao sang supportUser làm lead nhưng KHÔNG cung cấp Reason
        var model = new RequisitionAssignRequestModel
        {
            RequisitionId = req.Id,
            LeadRecruiterId = supportUser.Id, // Đổi sang supportUser
            Reason = "   ", // Lý do trống
            Notes = "Bàn giao gấp"
        };

        // Act
        var result = await service.AssignRecruitersAsync(model, hrManager.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("lý do chuyển giao");

        // Lead Recruiter không bị đổi sai
        var unchangedReq = await db.JobRequisitions.FindAsync(req.Id);
        unchangedReq!.AssignedRecruiterId.Should().Be(leadUser.Id);
    }

    [Fact]
    public async Task AssignRecruitersAsync_WhenLeadChangesWithReason_RecordsHandoverHistory()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, _, req, leadUser, supportUser, hrManager) = CreateBasicTestData(db);
        var service = new RequisitionAssignmentService(db, _serviceLogger);

        // Phân công leadUser ban đầu
        req.AssignedRecruiterId = leadUser.Id;
        db.RequisitionRecruiters.Add(new RequisitionRecruiter
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            RecruiterId = leadUser.Id,
            IsPrimary = true,
            AssignedById = hrManager.Id,
            AssignedAt = DateTimeOffset.UtcNow.AddDays(-5)
        });
        await db.SaveChangesAsync();

        // Đổi người phụ trách chính sang supportUser kèm lý do đầy đủ
        var model = new RequisitionAssignRequestModel
        {
            RequisitionId = req.Id,
            LeadRecruiterId = supportUser.Id,
            SupportingRecruiterIds = new List<Guid> { leadUser.Id }, // leadUser chuyển thành hỗ trợ
            Reason = "Điều chuyển khối lượng công việc theo định biên mới.",
            Notes = "Đã bàn giao tài liệu phỏng vấn."
        };

        // Act
        var result = await service.AssignRecruitersAsync(model, hrManager.Id);

        // Assert
        result.Success.Should().BeTrue();

        var updatedReq = await db.JobRequisitions.FindAsync(req.Id);
        updatedReq!.AssignedRecruiterId.Should().Be(supportUser.Id);

        // Kiểm tra lịch sử chuyển giao
        var histories = db.RequisitionHandoverHistories
            .Where(h => h.RequisitionId == req.Id && h.HandoverType == "LEAD_HANDOVER")
            .ToList();

        histories.Should().HaveCount(1);
        var handover = histories[0];
        handover.FromRecruiterId.Should().Be(leadUser.Id);
        handover.ToRecruiterId.Should().Be(supportUser.Id);
        handover.Reason.Should().Be("Điều chuyển khối lượng công việc theo định biên mới.");
        handover.TransferredById.Should().Be(hrManager.Id);
    }

    [Fact]
    public async Task GetAssignedRequisitionIdsForRecruiterAsync_ReturnsBothPrimaryAndSupportingRequisitions()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, pos, req1, leadUser, supportUser, hrManager) = CreateBasicTestData(db);
        var service = new RequisitionAssignmentService(db, _serviceLogger);

        // Tạo thêm requisition 2 và 3
        var req2 = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-IT-002",
            JobPositionId = pos.Id,
            DepartmentId = dept.Id,
            HiringManagerId = hrManager.Id,
            Quantity = 1,
            Status = RequisitionStatus.APPROVED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var req3 = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-IT-003",
            JobPositionId = pos.Id,
            DepartmentId = dept.Id,
            HiringManagerId = hrManager.Id,
            Quantity = 1,
            Status = RequisitionStatus.APPROVED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.JobRequisitions.AddRange(req2, req3);

        // leadUser:
        // - Req 1: Lead (Primary)
        // - Req 2: Supporting
        // - Req 3: Not assigned
        db.RequisitionRecruiters.AddRange(
            new RequisitionRecruiter { Id = Guid.NewGuid(), RequisitionId = req1.Id, RecruiterId = leadUser.Id, IsPrimary = true },
            new RequisitionRecruiter { Id = Guid.NewGuid(), RequisitionId = req2.Id, RecruiterId = leadUser.Id, IsPrimary = false }
        );
        await db.SaveChangesAsync();

        // Act
        var assignedIds = await service.GetAssignedRequisitionIdsForRecruiterAsync(leadUser.Id);

        // Assert
        assignedIds.Should().Contain(req1.Id);
        assignedIds.Should().Contain(req2.Id);
        assignedIds.Should().NotContain(req3.Id);
        assignedIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task CanRecruiterAccessApplicationAsync_AllowsAccessOnlyIfAssignedToRequisition()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, pos, req, leadUser, supportUser, hrManager) = CreateBasicTestData(db);
        var service = new RequisitionAssignmentService(db, _serviceLogger);

        // Tạo tin tuyển dụng (JobPosting) liên kết với Requisition
        var posting = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Title = "Senior Backend Engineer",
            Slug = "senior-backend-engineer",
            Status = JobPostingStatus.PUBLISHED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.JobPostings.Add(posting);

        // Tạo ứng viên và đơn ứng tuyển
        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Hoàng",
            LastName = "Văn Nam",
            Email = "nam.hoang@ats.test"
        };
        db.Candidates.Add(candidate);

        var firstStage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Sơ loại CV",
            StageOrder = 1,
            ColorCode = "#059669"
        };
        db.PipelineStages.Add(firstStage);

        var app = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            JobPostingId = posting.Id,
            CurrentStageId = firstStage.Id,
            Status = ApplicationStatus.IN_PROCESS,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Applications.Add(app);

        // Chỉ gán leadUser vào Requisition, supportUser không được gán
        db.RequisitionRecruiters.Add(new RequisitionRecruiter
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            RecruiterId = leadUser.Id,
            IsPrimary = true
        });
        await db.SaveChangesAsync();

        // Act & Assert
        var leadCanAccess = await service.CanRecruiterAccessApplicationAsync(leadUser.Id, app.Id);
        var otherCanAccess = await service.CanRecruiterAccessApplicationAsync(supportUser.Id, app.Id);

        leadCanAccess.Should().BeTrue("Recruiter được phân công phụ trách vị trí phải có quyền truy cập ứng viên");
        otherCanAccess.Should().BeFalse("Recruiter không được phân công phụ trách vị trí không được phép truy cập ứng viên");
    }

    [Fact]
    public async Task GetAvailableRecruitersAsync_CalculatesActiveWorkloadsCorrectly()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, pos, req1, leadUser, supportUser, hrManager) = CreateBasicTestData(db);
        var service = new RequisitionAssignmentService(db, _serviceLogger);

        var req2 = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-IT-002",
            JobPositionId = pos.Id,
            DepartmentId = dept.Id,
            HiringManagerId = hrManager.Id,
            Quantity = 1,
            Status = RequisitionStatus.APPROVED
        };
        db.JobRequisitions.Add(req2);

        // leadUser: 1 Lead, 1 Support
        // supportUser: 1 Lead
        db.RequisitionRecruiters.AddRange(
            new RequisitionRecruiter { Id = Guid.NewGuid(), RequisitionId = req1.Id, RecruiterId = leadUser.Id, IsPrimary = true },
            new RequisitionRecruiter { Id = Guid.NewGuid(), RequisitionId = req2.Id, RecruiterId = leadUser.Id, IsPrimary = false },
            new RequisitionRecruiter { Id = Guid.NewGuid(), RequisitionId = req2.Id, RecruiterId = supportUser.Id, IsPrimary = true }
        );
        await db.SaveChangesAsync();

        // Act
        var recruiters = await service.GetAvailableRecruitersAsync();

        // Assert
        recruiters.Should().NotBeEmpty();
        var leadRec = recruiters.FirstOrDefault(r => r.UserId == leadUser.Id);
        leadRec.Should().NotBeNull();
        leadRec!.ActiveLeadCount.Should().Be(1);
        leadRec.ActiveSupportingCount.Should().Be(1);

        var suppRec = recruiters.FirstOrDefault(r => r.UserId == supportUser.Id);
        suppRec.Should().NotBeNull();
        suppRec!.ActiveLeadCount.Should().Be(1);
        suppRec.ActiveSupportingCount.Should().Be(0);
    }

    [Fact]
    public async Task Controller_Assign_ValidatesModelAndRedirectsWithSuccess()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, _, req, leadUser, _, hrManager) = CreateBasicTestData(db);
        var service = new RequisitionAssignmentService(db, _serviceLogger);
        var controller = new RequisitionAssignmentsController(service, _controllerLogger);

        // Setup controller HttpContext with User Claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, hrManager.Id.ToString()),
            new(ClaimTypes.Name, hrManager.FullName),
            new(ClaimTypes.Role, UserRoles.HRManager)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = claimsPrincipal };
        var tempDataProvider = new Mock<ITempDataProvider>();
        var tempData = new TempDataDictionary(httpContext, tempDataProvider.Object);

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = tempData;

        var model = new RequisitionAssignRequestModel
        {
            RequisitionId = req.Id,
            LeadRecruiterId = leadUser.Id,
            Notes = "Kiểm tra phân công qua Controller"
        };

        // Act
        var actionResult = await controller.Assign(model);

        // Assert
        actionResult.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)actionResult;
        redirect.ActionName.Should().Be("Index");

        controller.TempData["SuccessMessage"].Should().NotBeNull();
        controller.TempData["SuccessMessage"]!.ToString().Should().Contain("thành công");
    }
}
