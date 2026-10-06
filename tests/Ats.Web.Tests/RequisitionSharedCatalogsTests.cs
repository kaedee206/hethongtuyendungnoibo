using System.ComponentModel.DataAnnotations;
using Ats.Web.Constants;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Requisitions;
using Ats.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ats.Web.Tests;

public class RequisitionSharedCatalogsTests
{
    private readonly Mock<ILogger<RequisitionService>> _loggerMock = new();

    private static IList<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var ctx = new ValidationContext(model, null, null);
        Validator.TryValidateObject(model, ctx, validationResults, true);

        if (model is IValidatableObject validatable)
        {
            var customResults = validatable.Validate(ctx);
            validationResults.AddRange(customResults);
        }

        return validationResults;
    }

    [Fact]
    public async Task PopulateOptionsAsync_LoadsActiveLocationsAndWorkTypes_InDisplayOrder()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var managerId = Guid.NewGuid();
        var user = new User
        {
            Id = managerId,
            FullName = "Nguyen Van A",
            Email = "manager@example.com",
            Role = UserRoles.HiringManager,
            Status = "ACTIVE"
        };
        context.Users.Add(user);

        // Locations: 3 active (orders: 2, 1, 3) and 1 inactive (order: 0)
        var locInactive = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "LOCATION", Code = "OLD_LOC", Name = "Địa điểm cũ", DisplayOrder = 0, IsActive = false };
        var loc2 = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "LOCATION", Code = "HCM_BRANCH", Name = "TP. Hồ Chí Minh", DisplayOrder = 2, IsActive = true };
        var loc1 = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "LOCATION", Code = "HN_HQ", Name = "Hà Nội - Trụ sở chính", DisplayOrder = 1, IsActive = true };
        var loc3 = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "LOCATION", Code = "DN_HUB", Name = "Đà Nẵng", DisplayOrder = 3, IsActive = true };

        // WorkTypes: 2 active (orders: 2, 1) and 1 inactive
        var wtInactive = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "WORK_TYPE", Code = "OLD_WT", Name = "Hình thức cũ", DisplayOrder = 0, IsActive = false };
        var wt2 = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "WORK_TYPE", Code = "HYBRID", Name = "Linh hoạt (Hybrid)", DisplayOrder = 2, IsActive = true };
        var wt1 = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "WORK_TYPE", Code = "FULL_TIME", Name = "Toàn thời gian (Full-time)", DisplayOrder = 1, IsActive = true };

        context.RecruitmentCatalogs.AddRange(locInactive, loc2, loc1, loc3, wtInactive, wt2, wt1);
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _loggerMock.Object);
        var model = new RequisitionCreateViewModel();

        // Act
        await service.PopulateOptionsAsync(model, managerId);

        // Assert
        model.LocationOptions.Should().NotBeNull();
        model.LocationOptions.Should().HaveCount(3);
        model.LocationOptions.Select(l => l.Name).Should().ContainInOrder("Hà Nội - Trụ sở chính", "TP. Hồ Chí Minh", "Đà Nẵng");
        model.LocationOptions.Any(l => l.Name == "Địa điểm cũ").Should().BeFalse();

        model.WorkTypeOptions.Should().NotBeNull();
        model.WorkTypeOptions.Should().HaveCount(2);
        model.WorkTypeOptions.Select(w => w.Name).Should().ContainInOrder("Toàn thời gian (Full-time)", "Linh hoạt (Hybrid)");
        model.WorkTypeOptions.Any(w => w.Name == "Hình thức cũ").Should().BeFalse();
    }

    [Fact]
    public void Submit_WithoutLocation_ReturnsValidationError()
    {
        // Arrange
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = null, // Thiếu địa điểm
            WorkTypeId = Guid.NewGuid(),
            Quantity = 2,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            ReasonDetail = "Mở rộng team",
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            JobDescription = "<p>Mô tả công việc</p>",
            Requirements = "<p>Yêu cầu công việc</p>",
            IsDraft = false
        };

        // Act
        var results = ValidateModel(model);

        // Assert
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("địa điểm làm việc"));
    }

    [Fact]
    public void Submit_WithoutWorkType_ReturnsValidationError()
    {
        // Arrange
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            WorkTypeId = null, // Thiếu hình thức làm việc
            Quantity = 2,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            ReasonDetail = "Mở rộng team",
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            JobDescription = "<p>Mô tả công việc</p>",
            Requirements = "<p>Yêu cầu công việc</p>",
            IsDraft = false
        };

        // Act
        var results = ValidateModel(model);

        // Assert
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("hình thức làm việc"));
    }

    [Fact]
    public void Draft_AllowsNullLocationAndWorkType()
    {
        // Arrange
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = null,
            WorkTypeId = null,
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            IsDraft = true
        };

        // Act
        var results = ValidateModel(model);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveOrUpdateRequisitionAsync_PersistsLocationAndWorkType()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var managerId = Guid.NewGuid();

        var department = new Department { Id = Guid.NewGuid(), Code = "TECH", Name = "Khối Công nghệ", IsActive = true };
        var position = new JobPosition { Id = Guid.NewGuid(), Code = "FE-DEV", Title = "Frontend Developer", IsActive = true };
        var loc = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "LOCATION", Code = "HN_HQ", Name = "Hà Nội", IsActive = true };
        var wt = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "WORK_TYPE", Code = "HYBRID", Name = "Hybrid", IsActive = true };

        context.Departments.Add(department);
        context.JobPositions.Add(position);
        context.RecruitmentCatalogs.AddRange(loc, wt);
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _loggerMock.Object);

        var model = new RequisitionCreateViewModel
        {
            JobPositionId = position.Id,
            DepartmentId = department.Id,
            LocationId = loc.Id,
            WorkTypeId = wt.Id,
            Quantity = 2,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 20_000_000,
            MaxSalary = 35_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            JobDescription = "<p>Xây dựng giao diện Web</p>",
            Requirements = "<p>Kinh nghiệm React/TypeScript</p>",
            IsDraft = false
        };

        // Act
        var (success, message, requisitionId, code) = await service.SaveOrUpdateRequisitionAsync(model, managerId);

        // Assert
        success.Should().BeTrue();
        requisitionId.Should().NotBeNull();

        var entity = await context.JobRequisitions.FindAsync(requisitionId);
        entity.Should().NotBeNull();
        entity!.LocationId.Should().Be(loc.Id);
        entity.WorkTypeId.Should().Be(wt.Id);
        entity.Status.Should().Be(RequisitionStatus.PENDING_APPROVAL);
    }

    [Fact]
    public async Task GetDraftByIdAsync_MapsLocationAndWorkTypeCorrectly()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var managerId = Guid.NewGuid();
        var user = new User
        {
            Id = managerId,
            FullName = "Nguyen Van A",
            Email = "manager@example.com",
            Role = UserRoles.HiringManager,
            Status = "ACTIVE"
        };
        context.Users.Add(user);

        var loc = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "LOCATION", Code = "HCM_BRANCH", Name = "TP. Hồ Chí Minh", IsActive = true };
        var wt = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "WORK_TYPE", Code = "FULL_TIME", Name = "Full-time", IsActive = true };

        var requisition = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-20261006-TEST",
            HiringManagerId = managerId,
            LocationId = loc.Id,
            WorkTypeId = wt.Id,
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            Status = RequisitionStatus.DRAFT,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        context.RecruitmentCatalogs.AddRange(loc, wt);
        context.JobRequisitions.Add(requisition);
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _loggerMock.Object);

        // Act
        var draft = await service.GetDraftByIdAsync(requisition.Id, managerId);

        // Assert
        draft.Should().NotBeNull();
        draft!.LocationId.Should().Be(loc.Id);
        draft.WorkTypeId.Should().Be(wt.Id);
    }

    [Fact]
    public async Task GetRequisitionDetailAsync_ReturnsLocationAndWorkTypeNames()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var managerId = Guid.NewGuid();
        var user = new User
        {
            Id = managerId,
            FullName = "Nguyen Van A",
            Email = "manager@example.com",
            Role = UserRoles.HiringManager,
            Status = "ACTIVE"
        };
        context.Users.Add(user);

        var loc = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "LOCATION", Code = "DN_HUB", Name = "Đà Nẵng", IsActive = true };
        var wt = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "WORK_TYPE", Code = "REMOTE", Name = "Từ xa (Remote)", IsActive = true };
        var dept = new Department { Id = Guid.NewGuid(), Code = "RND", Name = "Khối R&D", IsActive = true };
        var pos = new JobPosition { Id = Guid.NewGuid(), Code = "AI-RES", Title = "AI Researcher", IsActive = true };

        var requisition = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-20261006-DETAIL",
            HiringManagerId = managerId,
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            LocationId = loc.Id,
            WorkTypeId = wt.Id,
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            Status = RequisitionStatus.PENDING_APPROVAL,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        context.RecruitmentCatalogs.AddRange(loc, wt);
        context.Departments.Add(dept);
        context.JobPositions.Add(pos);
        context.JobRequisitions.Add(requisition);
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _loggerMock.Object);

        // Act
        var detail = await service.GetRequisitionDetailAsync(requisition.Id, managerId);

        // Assert
        detail.Should().NotBeNull();
        detail!.LocationId.Should().Be(loc.Id);
        detail.LocationName.Should().Be("Đà Nẵng");
        detail.WorkTypeId.Should().Be(wt.Id);
        detail.WorkTypeName.Should().Be("Từ xa (Remote)");
    }
}
