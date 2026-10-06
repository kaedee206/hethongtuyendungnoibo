using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Controllers;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Requisitions;
using Ats.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ats.Web.Tests;

public class RequisitionListTests
{
    private readonly Mock<ILogger<RequisitionService>> _serviceLoggerMock = new();
    private readonly Mock<ILogger<RequisitionsController>> _controllerLoggerMock = new();

    private static (User Manager1, User Manager2, User Admin, Department Dept, JobPosition Pos1, JobPosition Pos2) SeedCommonData(Data.ApplicationDbContext context)
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "TECH",
            Name = "Khối Công nghệ & Kỹ thuật",
            Level = 1,
            Path = "TECH"
        };
        context.Departments.Add(dept);

        var manager1 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Văn Trưởng Bộ Phận",
            Email = "manager1@noveratech.digital",
            PasswordHash = "hash",
            Role = UserRoles.HiringManager,
            DepartmentId = dept.Id
        };
        var manager2 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Trần Thị Quản Lý Khác",
            Email = "manager2@noveratech.digital",
            PasswordHash = "hash",
            Role = UserRoles.HiringManager,
            DepartmentId = dept.Id
        };
        var admin = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Quản Trị Viên Hệ Thống",
            Email = "admin@noveratech.digital",
            PasswordHash = "hash",
            Role = UserRoles.Admin,
            DepartmentId = dept.Id
        };
        context.Users.AddRange(manager1, manager2, admin);

        var pos1 = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "BACKEND-SR",
            Title = "Senior Backend Engineer (.NET 9)",
            DepartmentId = dept.Id,
            JobLevel = "Senior",
            IsActive = true
        };
        var pos2 = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "FRONTEND-MID",
            Title = "Frontend Engineer (React 19)",
            DepartmentId = dept.Id,
            JobLevel = "Mid",
            IsActive = true
        };
        context.JobPositions.AddRange(pos1, pos2);

        context.SaveChanges();

        return (manager1, manager2, admin, dept, pos1, pos2);
    }

    [Fact]
    public async Task GetRequisitionsListAsync_AsHiringManager_ShouldOnlyReturnOwnRequisitions()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (m1, m2, _, dept, pos1, pos2) = SeedCommonData(context);

        // M1 có 2 yêu cầu
        context.JobRequisitions.Add(new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-M1-001",
            HiringManagerId = m1.Id,
            DepartmentId = dept.Id,
            JobPositionId = pos1.Id,
            Quantity = 2,
            Status = RequisitionStatus.DRAFT,
            CreatedAt = DateTimeOffset.UtcNow
        });
        context.JobRequisitions.Add(new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-M1-002",
            HiringManagerId = m1.Id,
            DepartmentId = dept.Id,
            JobPositionId = pos2.Id,
            Quantity = 1,
            Status = RequisitionStatus.APPROVED,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // M2 có 1 yêu cầu
        context.JobRequisitions.Add(new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-M2-001",
            HiringManagerId = m2.Id,
            DepartmentId = dept.Id,
            JobPositionId = pos1.Id,
            Quantity = 3,
            Status = RequisitionStatus.PENDING_APPROVAL,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _serviceLoggerMock.Object);

        // Act: M1 truy vấn danh sách
        var result = await service.GetRequisitionsListAsync(new RequisitionListFilterInputModel(), m1.Id);

        // Assert: M1 chỉ thấy 2 yêu cầu của mình
        result.Should().NotBeNull();
        result.TotalItems.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(r => r.Code.StartsWith("REQ-M1"));
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetRequisitionsListAsync_WithStatusFilter_ShouldFilterCorrectly()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (m1, _, _, dept, pos1, _) = SeedCommonData(context);

        context.JobRequisitions.AddRange(
            new JobRequisition { Id = Guid.NewGuid(), Code = "REQ-DRAFT", HiringManagerId = m1.Id, DepartmentId = dept.Id, JobPositionId = pos1.Id, Status = RequisitionStatus.DRAFT, CreatedAt = DateTimeOffset.UtcNow },
            new JobRequisition { Id = Guid.NewGuid(), Code = "REQ-PENDING", HiringManagerId = m1.Id, DepartmentId = dept.Id, JobPositionId = pos1.Id, Status = RequisitionStatus.PENDING_APPROVAL, CreatedAt = DateTimeOffset.UtcNow },
            new JobRequisition { Id = Guid.NewGuid(), Code = "REQ-APPROVED", HiringManagerId = m1.Id, DepartmentId = dept.Id, JobPositionId = pos1.Id, Status = RequisitionStatus.APPROVED, CreatedAt = DateTimeOffset.UtcNow },
            new JobRequisition { Id = Guid.NewGuid(), Code = "REQ-REJECTED", HiringManagerId = m1.Id, DepartmentId = dept.Id, JobPositionId = pos1.Id, Status = RequisitionStatus.REJECTED, CreatedAt = DateTimeOffset.UtcNow }
        );
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _serviceLoggerMock.Object);

        // Lọc Approved
        var approvedResult = await service.GetRequisitionsListAsync(new RequisitionListFilterInputModel { Status = RequisitionStatus.APPROVED }, m1.Id);
        approvedResult.Items.Should().ContainSingle();
        approvedResult.Items[0].Code.Should().Be("REQ-APPROVED");
        approvedResult.ApprovedCount.Should().Be(1);
        approvedResult.DraftCount.Should().Be(1);
        approvedResult.TotalCount.Should().Be(4);

        // Lọc Draft
        var draftResult = await service.GetRequisitionsListAsync(new RequisitionListFilterInputModel { Status = RequisitionStatus.DRAFT }, m1.Id);
        draftResult.Items.Should().ContainSingle();
        draftResult.Items[0].Code.Should().Be("REQ-DRAFT");
    }

    [Fact]
    public async Task GetRequisitionsListAsync_WithJobPositionFilter_ShouldFilterCorrectly()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (m1, _, _, dept, pos1, pos2) = SeedCommonData(context);

        context.JobRequisitions.AddRange(
            new JobRequisition { Id = Guid.NewGuid(), Code = "REQ-POS1", HiringManagerId = m1.Id, DepartmentId = dept.Id, JobPositionId = pos1.Id, Status = RequisitionStatus.DRAFT, CreatedAt = DateTimeOffset.UtcNow },
            new JobRequisition { Id = Guid.NewGuid(), Code = "REQ-POS2", HiringManagerId = m1.Id, DepartmentId = dept.Id, JobPositionId = pos2.Id, Status = RequisitionStatus.DRAFT, CreatedAt = DateTimeOffset.UtcNow }
        );
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _serviceLoggerMock.Object);

        var result = await service.GetRequisitionsListAsync(new RequisitionListFilterInputModel { JobPositionId = pos1.Id }, m1.Id);

        result.Items.Should().ContainSingle();
        result.Items[0].Code.Should().Be("REQ-POS1");
        result.Items[0].JobPositionTitle.Should().Be(pos1.Title);
    }

    [Fact]
    public async Task GetRequisitionsListAsync_WithSearchTerm_ShouldMatchCodeOrTitle()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (m1, _, _, dept, pos1, pos2) = SeedCommonData(context);

        context.JobRequisitions.AddRange(
            new JobRequisition { Id = Guid.NewGuid(), Code = "REQ-SPECIAL-99", HiringManagerId = m1.Id, DepartmentId = dept.Id, JobPositionId = pos1.Id, Status = RequisitionStatus.DRAFT, CreatedAt = DateTimeOffset.UtcNow },
            new JobRequisition { Id = Guid.NewGuid(), Code = "REQ-NORMAL-01", HiringManagerId = m1.Id, DepartmentId = dept.Id, JobPositionId = pos2.Id, Status = RequisitionStatus.DRAFT, CreatedAt = DateTimeOffset.UtcNow }
        );
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _serviceLoggerMock.Object);

        // Search by Code
        var searchCodeResult = await service.GetRequisitionsListAsync(new RequisitionListFilterInputModel { Search = "SPECIAL-99" }, m1.Id);
        searchCodeResult.Items.Should().ContainSingle();
        searchCodeResult.Items[0].Code.Should().Be("REQ-SPECIAL-99");

        // Search by Title
        var searchTitleResult = await service.GetRequisitionsListAsync(new RequisitionListFilterInputModel { Search = "React 19" }, m1.Id);
        searchTitleResult.Items.Should().ContainSingle();
        searchTitleResult.Items[0].Code.Should().Be("REQ-NORMAL-01");
    }

    [Fact]
    public async Task GetRequisitionsListAsync_SortByCreatedAndTargetDate_ShouldOrderCorrectly()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (m1, _, _, dept, pos1, _) = SeedCommonData(context);

        var now = DateTimeOffset.UtcNow;
        var reqEarly = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-EARLY",
            HiringManagerId = m1.Id,
            DepartmentId = dept.Id,
            JobPositionId = pos1.Id,
            CreatedAt = now.AddDays(-10),
            TargetHireDate = new DateOnly(2026, 11, 1)
        };
        var reqLate = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-LATE",
            HiringManagerId = m1.Id,
            DepartmentId = dept.Id,
            JobPositionId = pos1.Id,
            CreatedAt = now.AddDays(-1),
            TargetHireDate = new DateOnly(2026, 10, 15)
        };

        context.JobRequisitions.AddRange(reqEarly, reqLate);
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _serviceLoggerMock.Object);

        // Sort created_desc (default)
        var sortCreatedDesc = await service.GetRequisitionsListAsync(new RequisitionListFilterInputModel { SortBy = "created_desc" }, m1.Id);
        sortCreatedDesc.Items[0].Code.Should().Be("REQ-LATE");

        // Sort created_asc
        var sortCreatedAsc = await service.GetRequisitionsListAsync(new RequisitionListFilterInputModel { SortBy = "created_asc" }, m1.Id);
        sortCreatedAsc.Items[0].Code.Should().Be("REQ-EARLY");

        // Sort target_date_asc (gần nhất trước: 15/10 trước 01/11)
        var sortTargetAsc = await service.GetRequisitionsListAsync(new RequisitionListFilterInputModel { SortBy = "target_date_asc" }, m1.Id);
        sortTargetAsc.Items[0].Code.Should().Be("REQ-LATE");
    }

    [Fact]
    public async Task GetRequisitionDetailAsync_WhenRequested_ChecksAuthorizationCorrectly()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (m1, m2, admin, dept, pos1, _) = SeedCommonData(context);

        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-DETAIL-001",
            HiringManagerId = m1.Id,
            DepartmentId = dept.Id,
            JobPositionId = pos1.Id,
            Quantity = 3,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            Reason = "Mở rộng dự án mới",
            MinSalary = 20_000_000,
            MaxSalary = 35_000_000,
            Status = RequisitionStatus.APPROVED,
            JobDescription = "<h3>Trách nhiệm công việc</h3><p>Phát triển API microservices.</p>",
            Requirements = "<ul><li>3+ năm kinh nghiệm .NET</li></ul>",
            TargetHireDate = new DateOnly(2026, 12, 1),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        context.JobRequisitions.Add(req);
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _serviceLoggerMock.Object);

        // 1. M1 (chủ sở hữu) xem chi tiết -> thành công
        var detailM1 = await service.GetRequisitionDetailAsync(req.Id, m1.Id);
        detailM1.Should().NotBeNull();
        detailM1!.Code.Should().Be("REQ-DETAIL-001");
        detailM1.JobPositionTitle.Should().Be(pos1.Title);
        detailM1.HiringManagerName.Should().Be(m1.FullName);
        detailM1.Quantity.Should().Be(3);
        detailM1.CanEdit.Should().BeFalse(); // Đã Approved không được sửa nháp
        detailM1.JobDescription.Should().Contain("microservices");

        // 2. M2 (Trưởng bộ phận khác) cố xem yêu cầu của M1 -> bị từ chối (null)
        var detailM2 = await service.GetRequisitionDetailAsync(req.Id, m2.Id);
        detailM2.Should().BeNull();

        // 3. Admin xem chi tiết yêu cầu của M1 -> được phép
        var detailAdmin = await service.GetRequisitionDetailAsync(req.Id, admin.Id);
        detailAdmin.Should().NotBeNull();
        detailAdmin!.Code.Should().Be("REQ-DETAIL-001");
    }

    [Fact]
    public async Task Controller_IndexAndDetails_ReturnsExpectedViews()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (m1, _, _, dept, pos1, _) = SeedCommonData(context);

        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-CTRL-01",
            HiringManagerId = m1.Id,
            DepartmentId = dept.Id,
            JobPositionId = pos1.Id,
            Quantity = 1,
            Status = RequisitionStatus.DRAFT,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.JobRequisitions.Add(req);
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _serviceLoggerMock.Object);
        var controller = new RequisitionsController(service, _controllerLoggerMock.Object);

        // Mock User Claims
        var userClaims = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, m1.Id.ToString()),
            new Claim(ClaimTypes.Role, UserRoles.HiringManager)
        ], "mock"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = userClaims }
        };
        controller.TempData = new TempDataDictionary(controller.ControllerContext.HttpContext, Mock.Of<ITempDataProvider>());

        // 1. Test Index
        var indexResult = await controller.Index(new RequisitionListFilterInputModel());
        var indexView = indexResult.Should().BeOfType<ViewResult>().Subject;
        var indexModel = indexView.Model.Should().BeOfType<RequisitionListViewModel>().Subject;
        indexModel.Items.Should().ContainSingle();

        // 2. Test Details (tồn tại)
        var detailResult = await controller.Details(req.Id);
        var detailView = detailResult.Should().BeOfType<ViewResult>().Subject;
        var detailModel = detailView.Model.Should().BeOfType<RequisitionDetailViewModel>().Subject;
        detailModel.Code.Should().Be("REQ-CTRL-01");

        // 3. Test Details (không tồn tại) -> Redirect Index
        var notFoundResult = await controller.Details(Guid.NewGuid());
        var redirect = notFoundResult.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(RequisitionsController.Index));
    }
}
