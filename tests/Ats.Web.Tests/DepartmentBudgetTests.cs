using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Controllers;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.DepartmentBudgets;
using Ats.Web.Models.ViewModels.Requisitions;
using Ats.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ats.Web.Tests;

public class DepartmentBudgetTests
{
    private readonly NullLogger<DepartmentBudgetService> _budgetLogger = NullLogger<DepartmentBudgetService>.Instance;
    private readonly NullLogger<DepartmentBudgetsController> _controllerLogger = NullLogger<DepartmentBudgetsController>.Instance;
    private readonly NullLogger<RequisitionService> _reqServiceLogger = NullLogger<RequisitionService>.Instance;

    [Fact]
    public async Task GetBudgetsAsync_CalculatesCorrectStaffAndRemainingQuota()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Phòng Kỹ thuật",
            Code = "TECH",
            IsActive = true
        };
        db.Departments.Add(dept);

        var budget = new DepartmentHeadcountBudget
        {
            Id = Guid.NewGuid(),
            DepartmentId = dept.Id,
            Year = DateTime.UtcNow.Year,
            TargetHeadcount = 10,
            SalaryBudget = 2_000_000_000m,
            IsActive = true
        };
        db.DepartmentHeadcountBudgets.Add(budget);

        // 2 active staff
        db.Users.AddRange(
            new User { Id = Guid.NewGuid(), Email = "tech1@ats.test", FullName = "Tech 1", DepartmentId = dept.Id, Status = "ACTIVE" },
            new User { Id = Guid.NewGuid(), Email = "tech2@ats.test", FullName = "Tech 2", DepartmentId = dept.Id, Status = "ACTIVE" }
        );

        // 1 requisition in progress with quantity 3
        db.JobRequisitions.Add(new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-TECH-01",
            DepartmentId = dept.Id,
            HiringManagerId = Guid.NewGuid(),
            Quantity = 3,
            Status = RequisitionStatus.IN_PROGRESS,
            TargetHireDate = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        await db.SaveChangesAsync();

        var service = new DepartmentBudgetService(db, _budgetLogger);

        // Act
        var result = await service.GetBudgetsAsync(DateTime.UtcNow.Year);

        // Assert
        result.Should().NotBeNull();
        result.SelectedYear.Should().Be(DateTime.UtcNow.Year);
        var deptBudget = result.DepartmentBudgets.FirstOrDefault(b => b.DepartmentId == dept.Id);
        deptBudget.Should().NotBeNull();
        deptBudget!.TargetHeadcount.Should().Be(10);
        deptBudget.CurrentStaffCount.Should().Be(2);
        deptBudget.RecruitingHeadcount.Should().Be(3);
        deptBudget.UsedHeadcount.Should().Be(5);
        deptBudget.RemainingHeadcount.Should().Be(5);
        deptBudget.UsagePercentage.Should().Be(50.0);
    }

    [Fact]
    public async Task SaveBudgetAsync_ValidModel_InsertsAndUpdatesCorrectly()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department { Id = Guid.NewGuid(), Name = "Phòng Nhân sự", Code = "HR", IsActive = true };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();

        var service = new DepartmentBudgetService(db, _budgetLogger);
        var userId = Guid.NewGuid();

        // Act 1: Insert new budget
        var createModel = new DepartmentBudgetViewModel
        {
            DepartmentId = dept.Id,
            Year = 2026,
            TargetHeadcount = 8,
            SalaryBudget = 1_500_000_000m,
            Currency = "VND",
            Note = "Khai báo mới đầu năm"
        };
        var (success1, message1, id1) = await service.SaveBudgetAsync(createModel, userId);

        // Assert 1
        success1.Should().BeTrue();
        id1.Should().NotBeNull();
        message1.Should().Contain("thành công");

        // Act 2: Update existing budget
        var updateModel = new DepartmentBudgetViewModel
        {
            DepartmentId = dept.Id,
            Year = 2026,
            TargetHeadcount = 12,
            SalaryBudget = 2_200_000_000m,
            Currency = "VND",
            Note = "Điều chỉnh tăng chỉ tiêu"
        };
        var (success2, message2, id2) = await service.SaveBudgetAsync(updateModel, userId);

        // Assert 2
        success2.Should().BeTrue();
        id2.Should().Be(id1);
        var saved = db.DepartmentHeadcountBudgets.First(b => b.DepartmentId == dept.Id && b.Year == 2026);
        saved.TargetHeadcount.Should().Be(12);
        saved.SalaryBudget.Should().Be(2_200_000_000m);
        saved.Note.Should().Be("Điều chỉnh tăng chỉ tiêu");
    }

    [Fact]
    public async Task SaveBudgetAsync_NegativeTargetOrSalary_ReturnsError()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department { Id = Guid.NewGuid(), Name = "Phòng Kinh doanh", Code = "SALES", IsActive = true };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();

        var service = new DepartmentBudgetService(db, _budgetLogger);

        // Act & Assert negative headcount
        var model1 = new DepartmentBudgetViewModel
        {
            DepartmentId = dept.Id,
            Year = 2026,
            TargetHeadcount = -5,
            SalaryBudget = 1_000_000_000m
        };
        var (s1, m1, _) = await service.SaveBudgetAsync(model1, Guid.NewGuid());
        s1.Should().BeFalse();
        m1.Should().Contain("âm");

        // Act & Assert negative salary
        var model2 = new DepartmentBudgetViewModel
        {
            DepartmentId = dept.Id,
            Year = 2026,
            TargetHeadcount = 5,
            SalaryBudget = -100_000_000m
        };
        var (s2, m2, _) = await service.SaveBudgetAsync(model2, Guid.NewGuid());
        s2.Should().BeFalse();
        m2.Should().Contain("âm");
    }

    [Fact]
    public async Task CheckHeadcountQuotaAsync_DetectsWithinAndOverQuotaCorrectly()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department { Id = Guid.NewGuid(), Name = "Khối Vận hành", Code = "OPS", IsActive = true };
        db.Departments.Add(dept);

        var currentYear = DateTime.UtcNow.Year;
        db.DepartmentHeadcountBudgets.Add(new DepartmentHeadcountBudget
        {
            Id = Guid.NewGuid(),
            DepartmentId = dept.Id,
            Year = currentYear,
            TargetHeadcount = 5,
            SalaryBudget = 1_000_000_000m,
            IsActive = true
        });

        // 3 active staff
        db.Users.AddRange(
            new User { Id = Guid.NewGuid(), Email = "ops1@ats.test", FullName = "Ops 1", DepartmentId = dept.Id, Status = "ACTIVE" },
            new User { Id = Guid.NewGuid(), Email = "ops2@ats.test", FullName = "Ops 2", DepartmentId = dept.Id, Status = "ACTIVE" },
            new User { Id = Guid.NewGuid(), Email = "ops3@ats.test", FullName = "Ops 3", DepartmentId = dept.Id, Status = "ACTIVE" }
        );
        await db.SaveChangesAsync();

        var service = new DepartmentBudgetService(db, _budgetLogger);

        // Act 1: Request 2 -> used(3) + 2 = 5 <= 5 => Not over quota
        var check1 = await service.CheckHeadcountQuotaAsync(dept.Id, quantity: 2);
        check1.IsOverQuota.Should().BeFalse();
        check1.RemainingHeadcount.Should().Be(2);

        // Act 2: Request 3 -> used(3) + 3 = 6 > 5 => Over quota
        var check2 = await service.CheckHeadcountQuotaAsync(dept.Id, quantity: 3);
        check2.IsOverQuota.Should().BeTrue();
        check2.WarningMessage.Should().Contain("vượt quá chỉ tiêu headcount");
    }

    [Fact]
    public async Task Requisition_WhenOverQuota_HiringManagerIsBlocked()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department { Id = Guid.NewGuid(), Name = "Phòng Kế toán", Code = "ACC", IsActive = true };
        db.Departments.Add(dept);

        var currentYear = DateTime.UtcNow.Year;
        db.DepartmentHeadcountBudgets.Add(new DepartmentHeadcountBudget
        {
            Id = Guid.NewGuid(),
            DepartmentId = dept.Id,
            Year = currentYear,
            TargetHeadcount = 2,
            SalaryBudget = 500_000_000m,
            IsActive = true
        });

        var pos = new JobPosition
        {
            Id = Guid.NewGuid(),
            DepartmentId = dept.Id,
            Title = "Kế toán viên",
            Code = "ACC-01",
            MinSalary = 10_000_000m,
            MaxSalary = 20_000_000m,
            IsActive = true
        };
        db.JobPositions.Add(pos);

        var hmUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "hm.acc@ats.test",
            FullName = "Hiring Manager ACC",
            Role = UserRoles.HiringManager,
            DepartmentId = dept.Id,
            Status = "ACTIVE"
        };
        db.Users.Add(hmUser);
        await db.SaveChangesAsync();

        var budgetService = new DepartmentBudgetService(db, _budgetLogger);
        var reqService = new RequisitionService(db, budgetService, _reqServiceLogger);

        var model = new RequisitionCreateViewModel
        {
            JobPositionId = pos.Id,
            DepartmentId = dept.Id,
            Quantity = 5, // Target is 2, requesting 5 -> OVER QUOTA!
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 12_000_000m,
            MaxSalary = 18_000_000m,
            TargetHireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            JobDescription = "Mô tả công việc kế toán tổng hợp",
            Requirements = "Yêu cầu bằng cử nhân kế toán, 2 năm kinh nghiệm",
            IsDraft = false // Submit for approval
        };

        // Act
        var (success, message, reqId, _) = await reqService.SaveOrUpdateRequisitionAsync(model, hmUser.Id);

        // Assert: Hiring Manager must be BLOCKED
        success.Should().BeFalse();
        reqId.Should().BeNull();
        message.Should().Contain("vượt quá chỉ tiêu headcount");
    }

    [Fact]
    public async Task Requisition_WhenOverQuota_HRManagerWithOverrideAndReason_Succeeds()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department { Id = Guid.NewGuid(), Name = "Phòng Kế toán", Code = "ACC", IsActive = true };
        db.Departments.Add(dept);

        var currentYear = DateTime.UtcNow.Year;
        db.DepartmentHeadcountBudgets.Add(new DepartmentHeadcountBudget
        {
            Id = Guid.NewGuid(),
            DepartmentId = dept.Id,
            Year = currentYear,
            TargetHeadcount = 2,
            SalaryBudget = 500_000_000m,
            IsActive = true
        });

        var pos = new JobPosition
        {
            Id = Guid.NewGuid(),
            DepartmentId = dept.Id,
            Title = "Kế toán viên",
            Code = "ACC-01",
            MinSalary = 10_000_000m,
            MaxSalary = 20_000_000m,
            IsActive = true
        };
        db.JobPositions.Add(pos);

        var hrUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "hr.lead@ats.test",
            FullName = "Trưởng phòng Nhân sự",
            Role = UserRoles.HRManager,
            DepartmentId = dept.Id,
            Status = "ACTIVE"
        };
        db.Users.Add(hrUser);
        await db.SaveChangesAsync();

        var budgetService = new DepartmentBudgetService(db, _budgetLogger);
        var reqService = new RequisitionService(db, budgetService, _reqServiceLogger);

        // 1. HR Manager without override -> Blocked
        var modelNoOverride = new RequisitionCreateViewModel
        {
            JobPositionId = pos.Id,
            DepartmentId = dept.Id,
            Quantity = 4,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 12_000_000m,
            MaxSalary = 18_000_000m,
            TargetHireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            JobDescription = "Mô tả công việc",
            Requirements = "Yêu cầu ứng viên",
            IsDraft = false,
            IsOverQuotaOverride = false
        };
        var (s1, m1, _, _) = await reqService.SaveOrUpdateRequisitionAsync(modelNoOverride, hrUser.Id);
        s1.Should().BeFalse();
        m1.Should().Contain("ghi đè");

        // 2. HR Manager with override and valid reason -> Allowed!
        var modelWithOverride = new RequisitionCreateViewModel
        {
            JobPositionId = pos.Id,
            DepartmentId = dept.Id,
            Quantity = 4,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 12_000_000m,
            MaxSalary = 18_000_000m,
            TargetHireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            JobDescription = "Mô tả công việc",
            Requirements = "Yêu cầu ứng viên",
            IsDraft = false,
            IsOverQuotaOverride = true,
            OverQuotaReason = "Ban Giám Đốc đã phê duyệt bổ sung ngoại lệ theo Biên bản họp số 12/BB-BOD"
        };
        var (s2, m2, reqId2, _) = await reqService.SaveOrUpdateRequisitionAsync(modelWithOverride, hrUser.Id);

        // Assert
        s2.Should().BeTrue();
        reqId2.Should().NotBeNull();
        var createdReq = db.JobRequisitions.First(r => r.Id == reqId2);
        createdReq.IsOverQuota.Should().BeTrue();
        createdReq.OverQuotaReason.Should().Be("Ban Giám Đốc đã phê duyệt bổ sung ngoại lệ theo Biên bản họp số 12/BB-BOD");
        createdReq.OverQuotaApprovedById.Should().Be(hrUser.Id);
        createdReq.OverQuotaApprovedAt.Should().NotBeNull();
        createdReq.Status.Should().Be(RequisitionStatus.PENDING_APPROVAL);
    }

    [Fact]
    public async Task Requisition_WhenOverQuota_DraftCanAlwaysBeSaved()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department { Id = Guid.NewGuid(), Name = "Phòng Tiếp thị", Code = "MKT", IsActive = true };
        db.Departments.Add(dept);

        var currentYear = DateTime.UtcNow.Year;
        db.DepartmentHeadcountBudgets.Add(new DepartmentHeadcountBudget
        {
            Id = Guid.NewGuid(),
            DepartmentId = dept.Id,
            Year = currentYear,
            TargetHeadcount = 1,
            SalaryBudget = 200_000_000m,
            IsActive = true
        });

        var hmUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "hm.mkt@ats.test",
            FullName = "Hiring Manager MKT",
            Role = UserRoles.HiringManager,
            DepartmentId = dept.Id,
            Status = "ACTIVE"
        };
        db.Users.Add(hmUser);
        await db.SaveChangesAsync();

        var budgetService = new DepartmentBudgetService(db, _budgetLogger);
        var reqService = new RequisitionService(db, budgetService, _reqServiceLogger);

        // Request 10 people (over quota of 1), but saved as DRAFT
        var draftModel = new RequisitionCreateViewModel
        {
            DepartmentId = dept.Id,
            Quantity = 10,
            ReasonDetail = "Kế hoạch nháp mở rộng tiếp thị",
            IsDraft = true
        };

        // Act
        var (success, message, reqId, _) = await reqService.SaveOrUpdateRequisitionAsync(draftModel, hmUser.Id);

        // Assert: Drafts are saved successfully so user does not lose work in progress
        success.Should().BeTrue();
        reqId.Should().NotBeNull();
        var draft = db.JobRequisitions.First(r => r.Id == reqId);
        draft.Status.Should().Be(RequisitionStatus.DRAFT);
        draft.IsOverQuota.Should().BeTrue();
    }

    [Fact]
    public async Task DepartmentBudgetsController_IndexAndGetDetail_ReturnExpectedResults()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department { Id = Guid.NewGuid(), Name = "Ban Giám Đốc", Code = "BOD", IsActive = true };
        db.Departments.Add(dept);

        var currentYear = DateTime.UtcNow.Year;
        db.DepartmentHeadcountBudgets.Add(new DepartmentHeadcountBudget
        {
            Id = Guid.NewGuid(),
            DepartmentId = dept.Id,
            Year = currentYear,
            TargetHeadcount = 6,
            SalaryBudget = 5_000_000_000m,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var budgetService = new DepartmentBudgetService(db, _budgetLogger);
        var controller = new DepartmentBudgetsController(budgetService, _controllerLogger);

        var adminUser = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "Admin User"),
            new Claim(ClaimTypes.Role, UserRoles.Admin)
        ], "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = adminUser }
        };

        // Act 1: Index
        var indexResult = await controller.Index(currentYear);
        var viewResult = indexResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<DepartmentBudgetListViewModel>().Subject;
        model.SelectedYear.Should().Be(currentYear);
        model.DepartmentBudgets.Should().Contain(b => b.DepartmentId == dept.Id);

        // Act 2: GetDetail
        var detailResult = await controller.GetDetail(dept.Id, currentYear);
        var okResult = detailResult.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }
}
