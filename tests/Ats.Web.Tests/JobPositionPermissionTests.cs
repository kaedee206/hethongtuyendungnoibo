using Ats.Web.Constants;
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

public class JobPositionPermissionTests
{
    private readonly Mock<ILogger<JobPositionsController>> _loggerMock = new();

    private static ClaimsPrincipal CreatePrincipal(string role, bool isAuthenticated = true)
    {
        var identity = isAuthenticated
            ? new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "TestAuth")
            : new ClaimsIdentity();
        return new ClaimsPrincipal(identity);
    }

    [Theory]
    [InlineData("HRManager")]
    [InlineData("Trưởng phòng nhân sự")]
    [InlineData("Trưởng phòng Nhân sự")]
    [InlineData("Admin")]
    public void CanUserViewSalary_AuthorizedRoles_ReturnsTrue(string role)
    {
        var principal = CreatePrincipal(role);

        var result = JobPositionsController.CanUserViewSalary(principal);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("Interviewer")]
    [InlineData("Recruiter")]
    [InlineData("HiringManager")]
    [InlineData("Approver")]
    [InlineData("Candidate")]
    [InlineData("Guest")]
    public void CanUserViewSalary_UnauthorizedRoles_ReturnsFalse(string role)
    {
        var principal = CreatePrincipal(role);

        var result = JobPositionsController.CanUserViewSalary(principal);

        result.Should().BeFalse();
    }

    [Fact]
    public void CanUserViewSalary_UnauthenticatedUser_ReturnsFalse()
    {
        var principal = CreatePrincipal("HRManager", isAuthenticated: false);

        var result = JobPositionsController.CanUserViewSalary(principal);

        result.Should().BeFalse();
    }

    [Fact]
    public void CanUserViewSalary_NullUser_ReturnsFalse()
    {
        var result = JobPositionsController.CanUserViewSalary(null);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("HRManager", true)]
    [InlineData("Trưởng phòng Nhân sự", true)]
    [InlineData("Interviewer", false)]
    [InlineData("Recruiter", false)]
    [InlineData("HiringManager", false)]
    public void CanUserManage_VariousRoles_ReturnsExpected(string role, bool expected)
    {
        var principal = CreatePrincipal(role);

        var result = JobPositionsController.CanUserManage(principal);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task Index_WhenUserIsHRManager_IncludesSalaryInformation()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "HR",
            Name = "Phòng Nhân sự",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Departments.Add(dept);

        var pos = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "HR-SPECIALIST",
            Title = "Chuyên viên nhân sự",
            DepartmentId = dept.Id,
            JobLevel = "MIDDLE",
            MinSalary = 15000000m,
            MaxSalary = 25000000m,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        context.JobPositions.Add(pos);
        await context.SaveChangesAsync();

        var controller = new JobPositionsController(context, _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CreatePrincipal("HRManager")
                }
            }
        };

        var actionResult = await controller.Index(null, null, null);

        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<JobPositionListViewModel>().Subject;
        model.CanViewSalary.Should().BeTrue();
        model.Positions.Should().HaveCount(1);
        model.Positions[0].MinSalary.Should().Be(15000000m);
        model.Positions[0].MaxSalary.Should().Be(25000000m);
        model.Positions[0].MinSalaryFormatted.Should().Contain("15,000,000");
        model.Positions[0].MaxSalaryFormatted.Should().Contain("25,000,000");
    }

    [Fact]
    public async Task Index_WhenUserIsInterviewer_MasksSalaryToNull()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "IT",
            Name = "Phòng Công nghệ",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Departments.Add(dept);

        var pos = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "DEV-LEAD",
            Title = "Trưởng nhóm phát triển",
            DepartmentId = dept.Id,
            JobLevel = "LEAD",
            MinSalary = 45000000m,
            MaxSalary = 65000000m,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        context.JobPositions.Add(pos);
        await context.SaveChangesAsync();

        var controller = new JobPositionsController(context, _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CreatePrincipal("Interviewer")
                }
            }
        };

        var actionResult = await controller.Index(null, null, null);

        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<JobPositionListViewModel>().Subject;
        model.CanViewSalary.Should().BeFalse();
        model.Positions.Should().HaveCount(1);
        model.Positions[0].MinSalary.Should().BeNull();
        model.Positions[0].MaxSalary.Should().BeNull();
        model.Positions[0].MinSalaryFormatted.Should().Be("---");
        model.Positions[0].MaxSalaryFormatted.Should().Be("---");
    }

    [Fact]
    public async Task Create_Get_WhenUserIsInterviewer_ReturnsForbid()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var controller = new JobPositionsController(context, _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CreatePrincipal("Interviewer")
                }
            }
        };

        var result = await controller.Create();

        result.Should().BeOfType<ForbidResult>();
    }
}
