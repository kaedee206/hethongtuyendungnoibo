using Ats.Web.Controllers;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.ViewModels.CompetencyFrameworks;
using Ats.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace Ats.Web.Tests;

public class CompetencyFrameworkServiceTests
{
    private readonly Mock<ILogger<CompetencyFrameworksController>> _controllerLoggerMock = new();

    private static async Task SeedSampleJobPositionsAsync(ApplicationDbContext context)
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Phòng Kỹ thuật Phần mềm",
            Code = "TECH",
            IsActive = true
        };
        context.Departments.Add(dept);

        context.JobPositions.AddRange(
            new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = "POS-001",
                Title = "Kỹ sư Lập trình Backend C# .NET",
                DepartmentId = dept.Id,
                Department = dept,
                JobLevel = "SENIOR",
                MinSalary = 25000000m,
                MaxSalary = 45000000m,
                IsActive = true,
                IsDeleted = false
            },
            new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = "POS-002",
                Title = "Chuyên viên Phân tích Dữ liệu Data Analyst",
                DepartmentId = dept.Id,
                Department = dept,
                JobLevel = "MIDDLE",
                MinSalary = 18000000m,
                MaxSalary = 30000000m,
                IsActive = true,
                IsDeleted = false
            }
        );

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetFrameworksAsync_DefaultParameters_ReturnsMoreThan200SeededFrameworks()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedSampleJobPositionsAsync(context);

        var service = new CompetencyFrameworkService(context);

        var result = await service.GetFrameworksAsync(null, null, 1, 10);

        result.Should().NotBeNull();
        result.TotalRecords.Should().BeGreaterThanOrEqualTo(200);
        result.Frameworks.Should().HaveCount(10);
        result.CurrentPage.Should().Be(1);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().BeGreaterThan(20);
        result.TotalActiveFrameworks.Should().BeGreaterThan(0);
        result.TotalRecords.Should().Be(result.TotalActiveFrameworks + result.TotalInactiveFrameworks);
    }

    [Fact]
    public async Task GetFrameworksAsync_FilterByActiveStatus_ReturnsOnlyActiveFrameworks()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new CompetencyFrameworkService(context);

        var result = await service.GetFrameworksAsync(null, "ACTIVE", 1, 50);

        result.Frameworks.Should().NotBeEmpty();
        result.Frameworks.Should().OnlyContain(f => f.IsActive);
        result.StatusFilter.Should().Be("ACTIVE");
    }

    [Fact]
    public async Task GetFrameworksAsync_FilterByInactiveStatus_ReturnsOnlyInactiveFrameworks()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new CompetencyFrameworkService(context);

        var result = await service.GetFrameworksAsync(null, "INACTIVE", 1, 50);

        result.Frameworks.Should().NotBeEmpty();
        result.Frameworks.Should().OnlyContain(f => !f.IsActive);
        result.StatusFilter.Should().Be("INACTIVE");
    }

    [Fact]
    public async Task GetFrameworksAsync_SearchByCode_ReturnsMatchingFramework()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new CompetencyFrameworkService(context);

        var result = await service.GetFrameworksAsync("CF-TECH-001", null, 1, 10);

        result.Frameworks.Should().NotBeEmpty();
        result.Frameworks.Should().Contain(f => f.Code.Contains("CF-TECH-001", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetFrameworksAsync_SearchByName_ReturnsMatchingFrameworks()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new CompetencyFrameworkService(context);

        var result = await service.GetFrameworksAsync("Backend", null, 1, 10);

        result.Frameworks.Should().NotBeEmpty();
        result.Frameworks.Should().OnlyContain(f => 
            f.Name.Contains("Backend", StringComparison.OrdinalIgnoreCase) || 
            (f.Description != null && f.Description.Contains("Backend", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GetFrameworksAsync_Pagination_CalculatesCorrectOffsetsAndPages()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new CompetencyFrameworkService(context);

        var page1 = await service.GetFrameworksAsync(null, null, 1, 15);
        var page2 = await service.GetFrameworksAsync(null, null, 2, 15);

        page1.Frameworks.Should().HaveCount(15);
        page2.Frameworks.Should().HaveCount(15);
        page1.Frameworks.Select(x => x.Id).Should().NotIntersectWith(page2.Frameworks.Select(x => x.Id));
        page2.CurrentPage.Should().Be(2);
        page2.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task GetAssociatedPositionsAsync_ExistingFramework_ReturnsAssociatedPositions()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedSampleJobPositionsAsync(context);

        var service = new CompetencyFrameworkService(context);
        var frameworksResult = await service.GetFrameworksAsync(null, null, 1, 10);
        var targetFramework = frameworksResult.Frameworks.First(f => f.AssociatedJobPositionsCount > 0);

        var positions = await service.GetAssociatedPositionsAsync(targetFramework.Id);

        positions.Should().NotBeEmpty();
        positions.First().Code.Should().NotBeNullOrEmpty();
        positions.First().Title.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CompetencyFrameworksController_Index_ReturnsViewWithPopulatedModel()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedSampleJobPositionsAsync(context);

        var service = new CompetencyFrameworkService(context);
        var controller = new CompetencyFrameworksController(service, _controllerLoggerMock.Object);

        var actionResult = await controller.Index(keyword: "Frontend", status: "ACTIVE", page: 1, pageSize: 10);

        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<CompetencyFrameworkListViewModel>().Subject;
        model.Keyword.Should().Be("Frontend");
        model.StatusFilter.Should().Be("ACTIVE");
    }

    [Fact]
    public async Task CompetencyFrameworksController_GetPositions_ReturnsJsonResultWithData()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedSampleJobPositionsAsync(context);

        var service = new CompetencyFrameworkService(context);
        var controller = new CompetencyFrameworksController(service, _controllerLoggerMock.Object);
        var frameworksResult = await service.GetFrameworksAsync(null, null, 1, 10);
        var targetFramework = frameworksResult.Frameworks.First(f => f.AssociatedJobPositionsCount > 0);

        var actionResult = await controller.GetPositions(targetFramework.Id);

        var jsonResult = actionResult.Should().BeOfType<JsonResult>().Subject;
        jsonResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByPositionAsync_ValidPositionWithFramework_ReturnsFrameworkDetail()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedSampleJobPositionsAsync(context);

        var service = new CompetencyFrameworkService(context);
        await service.GetFrameworksAsync(null, null, 1, 10); // Ensure seed

        var position = context.JobPositions.First(p => p.CompetencyFrameworkId != null);
        var detail = await service.GetByPositionAsync(position.Id);

        detail.Should().NotBeNull();
        detail!.Id.Should().Be(position.CompetencyFrameworkId!.Value);
        detail.Criteria.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CloneAsync_ValidFramework_CreatesIndependentCopy()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedSampleJobPositionsAsync(context);

        var service = new CompetencyFrameworkService(context);
        var list = await service.GetFrameworksAsync(null, null, 1, 10);
        var source = list.Frameworks.First();

        var (success, message, newId) = await service.CloneAsync(source.Id);

        success.Should().BeTrue();
        newId.Should().NotBeNull();
        newId.Should().NotBe(source.Id);

        var clonedDetail = await service.GetDetailAsync(newId!.Value);
        clonedDetail.Should().NotBeNull();
        clonedDetail!.Name.Should().Contain(source.Name);
        clonedDetail.Code.Should().NotBe(source.Code);
        clonedDetail.Criteria.Should().HaveCount(source.CompetenciesCount);
    }

    [Fact]
    public async Task CompetencyFrameworksController_GetByPosition_ReturnsOkWithData()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedSampleJobPositionsAsync(context);

        var service = new CompetencyFrameworkService(context);
        var controller = new CompetencyFrameworksController(service, _controllerLoggerMock.Object);
        await service.GetFrameworksAsync(null, null, 1, 10);

        var position = context.JobPositions.First(p => p.CompetencyFrameworkId != null);
        var actionResult = await controller.GetByPosition(position.Id);

        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
    }
}
