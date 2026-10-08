using System.ComponentModel.DataAnnotations;
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

public class RequisitionWizardTests
{
    private readonly Mock<ILogger<RequisitionService>> _serviceLoggerMock = new();

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
    public void WizardSubmission_WithCompleteDataAcrossAllSteps_ShouldPassValidation()
    {
        // Khi người dùng hoàn thành Bước 1 và Bước 2, chuyển sang Bước 3 và bấm "Gửi đề xuất tuyển dụng"
        var model = new RequisitionCreateViewModel
        {
            // Dữ liệu Bước 1: Thông tin cơ bản
            JobPositionId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            Quantity = 3,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            ReasonDetail = "Mở rộng nhóm nghiên cứu AI/ML",
            MinSalary = 30_000_000,
            MaxSalary = 50_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(45)),

            // Dữ liệu Bước 2: Mô tả & Tiêu chuẩn
            JobDescription = "<h2>Trách nhiệm chính</h2><ul><li>Phát triển mô hình Deep Learning</li></ul>",
            Requirements = "<h2>Yêu cầu chuyên môn</h2><ul><li>Tối thiểu 3 năm kinh nghiệm Python/PyTorch</li></ul>",

            // Bước 3: Gửi duyệt chính thức
            IsDraft = false
        };

        var results = ValidateModel(model);

        results.Should().BeEmpty();
    }

    [Fact]
    public void WizardSubmission_WhenStep1HasMissingFields_ShouldFailWithStep1Errors()
    {
        // Khi người dùng bấm gửi duyệt nhưng Bước 1 chưa điền chức danh hoặc phòng ban
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = null,
            DepartmentId = null,
            Quantity = 1,
            TargetHireDate = null,
            JobDescription = "<p>Mô tả hợp lệ</p>",
            Requirements = "<p>Yêu cầu hợp lệ</p>",
            IsDraft = false
        };

        var results = ValidateModel(model);

        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("chức danh"));
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("phòng ban"));
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("ngày"));
    }

    [Fact]
    public void WizardSubmission_WhenStep2HasEmptyEditors_ShouldFailWithStep2Errors()
    {
        // Khi người dùng điền đủ Bước 1 nhưng để trống Mô tả hoặc Yêu cầu ở Bước 2
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            Quantity = 1,
            MinSalary = 20_000_000,
            MaxSalary = 35_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            JobDescription = "   ", // Trống
            Requirements = null,    // Null
            IsDraft = false
        };

        var results = ValidateModel(model);

        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("mô tả công việc"));
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("yêu cầu ứng viên"));
    }

    [Fact]
    public async Task WizardDraftSave_FromStep1_ShouldSucceedEvenWithIncompleteData()
    {
        // Lưu nháp ngay tại Bước 1 khi chưa nhập Bước 2
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new RequisitionService(context, _serviceLoggerMock.Object);
        var managerId = Guid.NewGuid();

        var model = new RequisitionCreateViewModel
        {
            JobPositionId = null,
            DepartmentId = null,
            Quantity = 2,
            ReasonDetail = "Lưu nháp tại Bước 1",
            JobDescription = null,
            Requirements = null,
            IsDraft = true
        };

        var (success, message, requisitionId, code) = await service.SaveOrUpdateRequisitionAsync(model, managerId);

        success.Should().BeTrue();
        requisitionId.Should().NotBeNull();
        code.Should().StartWith("REQ-");

        var entity = await context.JobRequisitions.FindAsync(requisitionId);
        entity.Should().NotBeNull();
        entity!.Status.Should().Be(RequisitionStatus.DRAFT);
        entity.Reason.Should().Be("Lưu nháp tại Bước 1");
    }

    [Fact]
    public async Task WizardDraftSave_FromStep2_ShouldRetainBothStep1AndStep2Data()
    {
        // Lưu nháp tại Bước 2 khi đã có thông tin Bước 1 và nội dung soạn thảo Bước 2
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new RequisitionService(context, _serviceLoggerMock.Object);
        var managerId = Guid.NewGuid();

        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "DEV-LEAD",
            Title = "Tech Lead Backend",
            MinSalary = 40_000_000,
            MaxSalary = 70_000_000,
            IsActive = true
        };
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "ENG",
            Name = "Phòng Kỹ thuật Phần mềm",
            IsActive = true
        };
        context.JobPositions.Add(position);
        context.Departments.Add(dept);
        await context.SaveChangesAsync();

        var model = new RequisitionCreateViewModel
        {
            JobPositionId = position.Id,
            DepartmentId = dept.Id,
            Quantity = 1,
            MinSalary = 45_000_000,
            MaxSalary = 65_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            JobDescription = "<h3>Nhiệm vụ Tech Lead</h3><ul><li>Kiến trúc hệ thống</li></ul>",
            Requirements = "<h3>Yêu cầu</h3><ul><li>5+ năm kinh nghiệm</li></ul>",
            IsDraft = true
        };

        var (success, message, requisitionId, code) = await service.SaveOrUpdateRequisitionAsync(model, managerId);

        success.Should().BeTrue();
        var entity = await context.JobRequisitions.FindAsync(requisitionId);
        entity.Should().NotBeNull();
        entity!.Status.Should().Be(RequisitionStatus.DRAFT);
        entity.JobPositionId.Should().Be(position.Id);
        entity.DepartmentId.Should().Be(dept.Id);
        entity.JobDescription.Should().Contain("Kiến trúc hệ thống");
        entity.Requirements.Should().Contain("5+ năm kinh nghiệm");
    }

    [Fact]
    public async Task WizardSubmission_FromStep3_TransitionsToPendingApproval()
    {
        // Khi bấm "Gửi đề xuất tuyển dụng" tại Bước 3 -> Chuyển trạng thái sang PENDING_APPROVAL
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new RequisitionService(context, _serviceLoggerMock.Object);
        var managerId = Guid.NewGuid();

        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "QA-SENIOR",
            Title = "Senior QA Engineer",
            MinSalary = 20_000_000,
            MaxSalary = 35_000_000,
            IsActive = true
        };
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "QC",
            Name = "Phòng Đảm bảo Chất lượng",
            IsActive = true
        };
        context.JobPositions.Add(position);
        context.Departments.Add(dept);
        await context.SaveChangesAsync();

        var model = new RequisitionCreateViewModel
        {
            JobPositionId = position.Id,
            DepartmentId = dept.Id,
            Quantity = 2,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            ReasonDetail = "Bổ sung kiểm thử tự động cho dự án Fintech",
            MinSalary = 25_000_000,
            MaxSalary = 35_000_000,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(20)),
            JobDescription = "<p>Xây dựng automation framework với Playwright</p>",
            Requirements = "<p>Kinh nghiệm viết test tự động và CI/CD</p>",
            IsDraft = false // Gửi duyệt
        };

        var (success, message, requisitionId, code) = await service.SaveOrUpdateRequisitionAsync(model, managerId);

        success.Should().BeTrue();
        message.Should().Contain("thành công");

        var submittedEntity = await context.JobRequisitions.FindAsync(requisitionId);
        submittedEntity.Should().NotBeNull();
        submittedEntity!.Status.Should().Be(RequisitionStatus.PENDING_APPROVAL);
    }
}
