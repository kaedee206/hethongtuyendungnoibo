using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Ats.Web.Controllers;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Requisitions;
using Ats.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ats.Web.Tests;

public class RequisitionValidationTests
{
    private readonly Mock<ILogger<RequisitionService>> _serviceLoggerMock = new();
    private readonly Mock<ILogger<RequisitionsController>> _controllerLoggerMock = new();

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
    public void ValidModel_WhenSubmittingApproval_WithRichText_ShouldPassValidation()
    {
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            WorkTypeId = Guid.NewGuid(),
            Quantity = 2,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            ReasonDetail = "Mở rộng team Backend cho dự án Core Banking",
            MinSalary = 25_000_000,
            MaxSalary = 40_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            JobDescription = "<h3>Mô tả công việc</h3><ul><li>Phát triển các module nghiệp vụ</li></ul>",
            Requirements = "<h3>Yêu cầu ứng viên</h3><p>Tối thiểu 3 năm kinh nghiệm .NET Core / C#</p>",
            IsDraft = false
        };

        var results = ValidateModel(model);

        results.Should().BeEmpty();
    }

    [Fact]
    public void DraftModel_WithEmptyJobDescriptionAndRequirements_ShouldPassValidation()
    {
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(20)),
            JobDescription = null,
            Requirements = string.Empty,
            IsDraft = true
        };

        var results = ValidateModel(model);

        results.Should().BeEmpty();
    }

    [Fact]
    public void SubmitApproval_WithMissingJobDescription_ShouldFailValidation()
    {
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(20)),
            JobDescription = null,
            Requirements = "<p>Có kinh nghiệm lập trình C#</p>",
            IsDraft = false
        };

        var results = ValidateModel(model);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(model.JobDescription)) &&
                                      r.ErrorMessage!.Contains("Vui lòng nhập mô tả công việc"));
    }

    [Fact]
    public void SubmitApproval_WithMissingRequirements_ShouldFailValidation()
    {
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(20)),
            JobDescription = "<p>Phát triển hệ thống microservices</p>",
            Requirements = null,
            IsDraft = false
        };

        var results = ValidateModel(model);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(model.Requirements)) &&
                                      r.ErrorMessage!.Contains("Vui lòng nhập yêu cầu ứng viên"));
    }

    [Theory]
    [InlineData("<p><br></p>")]
    [InlineData("   <div><br /></div>   ")]
    [InlineData("&nbsp;&nbsp;   <br>")]
    [InlineData("<p><span> </span></p>")]
    public void SubmitApproval_WithHtmlTagsOnly_ShouldFailValidation(string emptyHtml)
    {
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(20)),
            JobDescription = emptyHtml,
            Requirements = "<p>Yêu cầu hợp lệ</p>",
            IsDraft = false
        };

        var results = ValidateModel(model);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(model.JobDescription)));
    }

    [Fact]
    public void MissingJobPosition_ShouldFailValidation()
    {
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = null,
            DepartmentId = Guid.NewGuid(),
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(15))
        };

        var results = ValidateModel(model);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(model.JobPositionId)));
    }

    [Fact]
    public void MissingDepartment_ShouldFailValidation()
    {
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(),
            DepartmentId = null,
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(15))
        };

        var results = ValidateModel(model);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(model.DepartmentId)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Quantity_WhenOutOfRange_ShouldFailValidation(int quantity)
    {
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            Quantity = quantity,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(15))
        };

        var results = ValidateModel(model);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(model.Quantity)));
    }

    [Fact]
    public void Salaries_WhenMaxLessThanMin_ShouldFailCustomValidation()
    {
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 35_000_000,
            MaxSalary = 20_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(20))
        };

        var results = ValidateModel(model);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(model.MaxSalary)) &&
                                      r.ErrorMessage!.Contains("lớn hơn hoặc bằng"));
    }

    [Fact]
    public void TargetHireDate_WhenInPast_ShouldFailCustomValidation()
    {
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-2))
        };

        var results = ValidateModel(model);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(model.TargetHireDate)) &&
                                      r.ErrorMessage!.Contains("từ ngày hôm nay trở đi"));
    }

    [Fact]
    public async Task Service_PrepareCreateViewModel_AutoAssignsDepartmentOfManager()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var managerId = Guid.NewGuid();
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = "TECH-DEV",
            Name = "Phòng Phát triển Phần mềm",
            ManagerId = managerId,
            IsActive = true
        };
        var managerUser = new User
        {
            Id = managerId,
            Email = "manager@noveratech.digital",
            FullName = "Nguyễn Văn Trưởng Bộ Phận",
            Role = "HiringManager",
            DepartmentId = department.Id,
            Department = department.Name
        };

        context.Departments.Add(department);
        context.Users.Add(managerUser);
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _serviceLoggerMock.Object);

        var viewModel = await service.PrepareCreateViewModelAsync(managerId);

        viewModel.DepartmentId.Should().Be(department.Id);
        viewModel.CurrentUserDepartmentName.Should().Be(department.Name);
        viewModel.DepartmentOptions.Should().Contain(d => d.Id == department.Id);
    }

    [Fact]
    public async Task Service_CreateRequisitionAsync_ValidModel_PersistsRequisitionWithPendingApprovalStatus()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-IT",
            Name = "Khối Công nghệ & Dữ liệu",
            IsActive = true
        };
        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "DEV-GOLANG-01",
            Title = "Senior Golang Engineer",
            DepartmentId = department.Id,
            JobLevel = "SENIOR",
            IsActive = true
        };
        var managerId = Guid.NewGuid();

        var loc = new RecruitmentCatalog
        {
            Id = Guid.NewGuid(),
            CatalogType = "LOCATION",
            Code = "HN_HQ",
            Name = "Hà Nội - Trụ sở chính",
            IsActive = true
        };
        var wt = new RecruitmentCatalog
        {
            Id = Guid.NewGuid(),
            CatalogType = "WORK_TYPE",
            Code = "FULL_TIME",
            Name = "Toàn thời gian (Full-time)",
            IsActive = true
        };

        context.Departments.Add(department);
        context.JobPositions.Add(position);
        context.RecruitmentCatalogs.AddRange(loc, wt);
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _serviceLoggerMock.Object);

        var inputModel = new RequisitionCreateViewModel
        {
            JobPositionId = position.Id,
            DepartmentId = department.Id,
            LocationId = loc.Id,
            WorkTypeId = wt.Id,
            Quantity = 3,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            ReasonDetail = "Tuyển gấp cho dự án hạ tầng Microservices",
            MinSalary = 30_000_000,
            MaxSalary = 50_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(40)),
            JobDescription = "<h3>Mô tả công việc</h3><p>Thiết kế kiến trúc Go</p>",
            Requirements = "<h3>Yêu cầu</h3><p>3 năm kinh nghiệm Go</p>",
            IsDraft = false
        };

        var (success, message, requisitionId) = await service.CreateRequisitionAsync(inputModel, managerId);

        success.Should().BeTrue();
        requisitionId.Should().NotBeNull();
        message.Should().Contain("thành công");

        var created = context.JobRequisitions.FirstOrDefault(r => r.Id == requisitionId);
        created.Should().NotBeNull();
        created!.Code.Should().StartWith("REQ-");
        created.Status.Should().Be(RequisitionStatus.PENDING_APPROVAL);
        created.Quantity.Should().Be(3);
        created.MinSalary.Should().Be(30_000_000);
        created.MaxSalary.Should().Be(50_000_000);
        created.JobPositionId.Should().Be(position.Id);
        created.DepartmentId.Should().Be(department.Id);
        created.JobDescription.Should().Be("<h3>Mô tả công việc</h3><p>Thiết kế kiến trúc Go</p>");
        created.Requirements.Should().Be("<h3>Yêu cầu</h3><p>3 năm kinh nghiệm Go</p>");
    }

    [Fact]
    public async Task Service_CreateRequisitionAsync_WhenDraft_AllowsEmptyContentAndSetsStatusDraft()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-IT",
            Name = "Khối Công nghệ & Dữ liệu",
            IsActive = true
        };
        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "DEV-GOLANG-02",
            Title = "Junior Golang Engineer",
            DepartmentId = department.Id,
            JobLevel = "JUNIOR",
            IsActive = true
        };
        var managerId = Guid.NewGuid();

        context.Departments.Add(department);
        context.JobPositions.Add(position);
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _serviceLoggerMock.Object);

        var inputModel = new RequisitionCreateViewModel
        {
            JobPositionId = position.Id,
            DepartmentId = department.Id,
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            ReasonDetail = "Lưu nháp để chỉnh sửa sau",
            MinSalary = 15_000_000,
            MaxSalary = 20_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            JobDescription = null,
            Requirements = string.Empty,
            IsDraft = true
        };

        var (success, message, requisitionId) = await service.CreateRequisitionAsync(inputModel, managerId);

        success.Should().BeTrue();
        requisitionId.Should().NotBeNull();
        message.Should().Contain("Bản nháp").And.Contain("thành công");

        var created = context.JobRequisitions.FirstOrDefault(r => r.Id == requisitionId);
        created.Should().NotBeNull();
        created!.Status.Should().Be(RequisitionStatus.DRAFT);
        created.JobDescription.Should().BeNull();
        created.Requirements.Should().BeNull();
    }

    [Fact]
    public async Task Service_CreateRequisitionAsync_WhenSubmitApprovalWithoutContent_ReturnsError()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-IT",
            Name = "Khối Công nghệ & Dữ liệu",
            IsActive = true
        };
        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "DEV-GOLANG-03",
            Title = "Mid Golang Engineer",
            DepartmentId = department.Id,
            JobLevel = "MID",
            IsActive = true
        };
        var managerId = Guid.NewGuid();

        var loc = new RecruitmentCatalog
        {
            Id = Guid.NewGuid(),
            CatalogType = "LOCATION",
            Code = "HN_HQ",
            Name = "Hà Nội",
            IsActive = true
        };
        var wt = new RecruitmentCatalog
        {
            Id = Guid.NewGuid(),
            CatalogType = "WORK_TYPE",
            Code = "FULL_TIME",
            Name = "Toàn thời gian",
            IsActive = true
        };

        context.Departments.Add(department);
        context.JobPositions.Add(position);
        context.RecruitmentCatalogs.AddRange(loc, wt);
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _serviceLoggerMock.Object);

        var inputModel = new RequisitionCreateViewModel
        {
            JobPositionId = position.Id,
            DepartmentId = department.Id,
            LocationId = loc.Id,
            WorkTypeId = wt.Id,
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            JobDescription = "<p><br></p>", // Empty HTML
            Requirements = "<p>Có kinh nghiệm</p>",
            IsDraft = false
        };

        var (success, message, requisitionId) = await service.CreateRequisitionAsync(inputModel, managerId);

        success.Should().BeFalse();
        requisitionId.Should().BeNull();
        message.Should().Contain("mô tả công việc");
    }

    [Fact]
    public async Task Service_CreateRequisitionAsync_NonExistentPosition_ReturnsError()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-HR",
            Name = "Phòng Nhân sự",
            IsActive = true
        };
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        var service = new RequisitionService(context, _serviceLoggerMock.Object);

        var inputModel = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(), // ID không tồn tại
            DepartmentId = department.Id,
            Quantity = 1,
            HeadcountType = HeadcountType.REPLACEMENT,
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(25))
        };

        var (success, message, requisitionId) = await service.CreateRequisitionAsync(inputModel, Guid.NewGuid());

        success.Should().BeFalse();
        requisitionId.Should().BeNull();
        message.Should().Contain("Chức danh được chọn không tồn tại");
    }

    [Fact]
    public async Task Controller_Create_Get_ReturnsViewWithPopulatedModel()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new RequisitionService(context, _serviceLoggerMock.Object);
        var controller = new RequisitionsController(service, _controllerLoggerMock.Object);

        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "Hiring Manager Test"),
            new Claim(ClaimTypes.Role, "HiringManager")
        ], "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var result = await controller.Create();

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<RequisitionCreateViewModel>().Subject;
        model.Quantity.Should().Be(1);
    }
}
