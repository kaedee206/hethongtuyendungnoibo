using Ats.Web.Controllers;
using Ats.Web.Models.Entities;
using Ats.Web.Models.ViewModels.JobPositions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;

namespace Ats.Web.Tests;

public class JobPositionSearchPaginationTests
{
    private readonly Mock<ILogger<JobPositionsController>> _loggerMock = new();

    private static ClaimsPrincipal CreatePrincipal(string role)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static async Task SeedPositionsAsync(Data.ApplicationDbContext context, Department dept, int count = 5)
    {
        var levels = new[] { "JUNIOR", "MIDDLE", "SENIOR", "LEAD", "MANAGER" };
        var titles = new[]
        {
            "Lập trình viên Backend .NET",
            "Kỹ sư Frontend React",
            "Chuyên viên DevOps Cloud",
            "Kỹ sư AI và Dữ liệu",
            "Trưởng nhóm Kiến trúc sư Phần mềm"
        };

        for (int i = 0; i < count; i++)
        {
            context.JobPositions.Add(new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = $"POS-00{i + 1}",
                Title = titles[i % titles.Length],
                DepartmentId = dept.Id,
                JobLevel = levels[i % levels.Length],
                MinSalary = 10000000m + (i * 5000000m),
                MaxSalary = 20000000m + (i * 5000000m),
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(i),
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(i)
            });
        }

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Index_SearchByCode_ReturnsMatchingPosition()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "ENG",
            Name = "Phòng Kỹ thuật",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Departments.Add(dept);
        await SeedPositionsAsync(context, dept, count: 5);

        var controller = new JobPositionsController(context, _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("HRManager") }
            }
        };

        var actionResult = await controller.Index(keyword: "POS-002", level: null, departmentId: null);

        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<JobPositionListViewModel>().Subject;
        model.TotalRecords.Should().Be(1);
        model.Positions.Should().ContainSingle(p => p.Code == "POS-002");
    }

    [Fact]
    public async Task Index_SearchByTitle_CaseInsensitive_ReturnsMatches()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "ENG",
            Name = "Phòng Kỹ thuật",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Departments.Add(dept);
        await SeedPositionsAsync(context, dept, count: 5);

        var controller = new JobPositionsController(context, _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("HRManager") }
            }
        };

        var actionResult = await controller.Index(keyword: "frontend", level: null, departmentId: null);

        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<JobPositionListViewModel>().Subject;
        model.TotalRecords.Should().Be(1);
        model.Positions.Should().ContainSingle(p => p.Title.Contains("Frontend"));
    }

    [Fact]
    public async Task Index_FilterByJobLevel_ReturnsOnlyMatchingLevel()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "ENG",
            Name = "Phòng Kỹ thuật",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Departments.Add(dept);
        await SeedPositionsAsync(context, dept, count: 5);

        var controller = new JobPositionsController(context, _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("HRManager") }
            }
        };

        var actionResult = await controller.Index(keyword: null, level: "senior", departmentId: null);

        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<JobPositionListViewModel>().Subject;
        model.TotalRecords.Should().Be(1);
        model.Positions.Should().OnlyContain(p => p.JobLevel == "SENIOR");
    }

    [Fact]
    public async Task Index_Pagination_FirstPage_ReturnsSlicesCorrectly()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "ENG",
            Name = "Phòng Kỹ thuật",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Departments.Add(dept);
        await SeedPositionsAsync(context, dept, count: 5);

        var controller = new JobPositionsController(context, _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("HRManager") }
            }
        };

        var actionResult = await controller.Index(keyword: null, level: null, departmentId: null, page: 1, pageSize: 2);

        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<JobPositionListViewModel>().Subject;
        model.TotalRecords.Should().Be(5);
        model.CurrentPage.Should().Be(1);
        model.PageSize.Should().Be(2);
        model.TotalPages.Should().Be(3);
        model.HasPreviousPage.Should().BeFalse();
        model.HasNextPage.Should().BeTrue();
        model.Positions.Should().HaveCount(2);
    }

    [Fact]
    public async Task Index_Pagination_SecondPage_ReturnsNextSlice()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "ENG",
            Name = "Phòng Kỹ thuật",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Departments.Add(dept);
        await SeedPositionsAsync(context, dept, count: 5);

        var controller = new JobPositionsController(context, _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("HRManager") }
            }
        };

        var actionResult = await controller.Index(keyword: null, level: null, departmentId: null, page: 2, pageSize: 2);

        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<JobPositionListViewModel>().Subject;
        model.TotalRecords.Should().Be(5);
        model.CurrentPage.Should().Be(2);
        model.PageSize.Should().Be(2);
        model.HasPreviousPage.Should().BeTrue();
        model.HasNextPage.Should().BeTrue();
        model.Positions.Should().HaveCount(2);
    }

    [Fact]
    public async Task Index_Pagination_InvalidPageAndPageSize_NormalizesDefaults()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "ENG",
            Name = "Phòng Kỹ thuật",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Departments.Add(dept);
        await SeedPositionsAsync(context, dept, count: 3);

        var controller = new JobPositionsController(context, _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("HRManager") }
            }
        };

        var actionResult = await controller.Index(keyword: null, level: null, departmentId: null, page: -5, pageSize: -10);

        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<JobPositionListViewModel>().Subject;
        model.CurrentPage.Should().Be(1);
        model.PageSize.Should().Be(10);
        model.TotalRecords.Should().Be(3);
        model.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task Index_PaginationWithInterviewer_MasksSalariesOnPage()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "ENG",
            Name = "Phòng Kỹ thuật",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Departments.Add(dept);
        await SeedPositionsAsync(context, dept, count: 4);

        var controller = new JobPositionsController(context, _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("Interviewer") }
            }
        };

        var actionResult = await controller.Index(keyword: null, level: null, departmentId: null, page: 1, pageSize: 2);

        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<JobPositionListViewModel>().Subject;
        model.CanViewSalary.Should().BeFalse();
        model.Positions.Should().HaveCount(2);
        model.Positions.Should().OnlyContain(p => p.MinSalary == null && p.MaxSalary == null && p.MinSalaryFormatted == "---");
    }
}
