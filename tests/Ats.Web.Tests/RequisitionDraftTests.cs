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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ats.Web.Tests;

public class RequisitionDraftTests
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
    public void DraftModel_WithCompletelyEmptyFields_ShouldPassValidation()
    {
        // Cho phép lưu nháp ở bất kỳ bước nào, kể cả khi form chưa điền đầy đủ
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = null,
            DepartmentId = null,
            Quantity = 1,
            MinSalary = null,
            MaxSalary = null,
            TargetHireDate = null,
            JobDescription = null,
            Requirements = null,
            IsDraft = true
        };

        var results = ValidateModel(model);

        results.Should().BeEmpty();
    }

    [Fact]
    public void SubmitApprovalModel_WithMissingJobPositionOrDepartment_ShouldFailValidation()
    {
        // Khi gửi duyệt (IsDraft = false), bắt buộc phải có đầy đủ chức danh, phòng ban, mô tả và yêu cầu
        var model = new RequisitionCreateViewModel
        {
            JobPositionId = null,
            DepartmentId = null,
            Quantity = 1,
            TargetHireDate = null,
            JobDescription = null,
            Requirements = null,
            IsDraft = false
        };

        var results = ValidateModel(model);

        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("chức danh"));
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("phòng ban"));
        results.Should().Contain(r => r.ErrorMessage != null && r.ErrorMessage.Contains("ngày"));
    }

    [Fact]
    public async Task Service_SaveOrUpdateRequisitionAsync_NewDraft_CreatesDraftSuccessfully()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new RequisitionService(context, _serviceLoggerMock.Object);
        var managerId = Guid.NewGuid();

        var model = new RequisitionCreateViewModel
        {
            Id = null,
            JobPositionId = null, // Chưa chọn chức danh
            DepartmentId = null,   // Chưa chọn phòng ban
            Quantity = 2,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            ReasonDetail = "Nháp lưu ban đầu",
            MinSalary = 15_000_000,
            MaxSalary = 25_000_000,
            JobDescription = "<p>Mô tả đang viết dở...</p>",
            Requirements = null,
            IsDraft = true
        };

        var (success, message, requisitionId, code) = await service.SaveOrUpdateRequisitionAsync(model, managerId);

        success.Should().BeTrue();
        requisitionId.Should().NotBeNull();
        code.Should().StartWith("REQ-");
        message.Should().Contain("đã được lưu thành công");

        var savedEntity = await context.JobRequisitions.FirstOrDefaultAsync(r => r.Id == requisitionId);
        savedEntity.Should().NotBeNull();
        savedEntity!.Status.Should().Be(RequisitionStatus.DRAFT);
        savedEntity.HiringManagerId.Should().Be(managerId);
        savedEntity.JobDescription.Should().Be("<p>Mô tả đang viết dở...</p>");
        savedEntity.Requirements.Should().BeNull();
        savedEntity.JobPositionId.Should().BeNull();
        savedEntity.DepartmentId.Should().BeNull();
    }

    [Fact]
    public async Task Service_SaveOrUpdateRequisitionAsync_ExistingDraft_UpdatesInPlaceWithoutDuplication()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new RequisitionService(context, _serviceLoggerMock.Object);
        var managerId = Guid.NewGuid();

        // 1. Tạo bản nháp lần 1
        var model1 = new RequisitionCreateViewModel
        {
            ReasonDetail = "Lần lưu 1",
            Quantity = 1,
            IsDraft = true
        };
        var (s1, _, id1, code1) = await service.SaveOrUpdateRequisitionAsync(model1, managerId);
        s1.Should().BeTrue();
        id1.Should().NotBeNull();

        // 2. Auto-save lần 2 với ID cũ
        var model2 = new RequisitionCreateViewModel
        {
            Id = id1,
            Code = code1,
            ReasonDetail = "Lần lưu 2 (đã cập nhật)",
            Quantity = 3,
            JobDescription = "<h3>Cập nhật nhiệm vụ</h3>",
            IsDraft = true
        };
        var (s2, _, id2, code2) = await service.SaveOrUpdateRequisitionAsync(model2, managerId);

        s2.Should().BeTrue();
        id2.Should().Be(id1);
        code2.Should().Be(code1);

        // Kiểm tra cơ sở dữ liệu chỉ có duy nhất 1 bản ghi
        var totalRequisitions = await context.JobRequisitions.CountAsync();
        totalRequisitions.Should().Be(1);

        var updated = await context.JobRequisitions.FindAsync(id1);
        updated!.Reason.Should().Be("Lần lưu 2 (đã cập nhật)");
        updated.Quantity.Should().Be(3);
        updated.JobDescription.Should().Be("<h3>Cập nhật nhiệm vụ</h3>");
    }

    [Fact]
    public async Task Service_GetDraftsByManagerAsync_FiltersCorrectly()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new RequisitionService(context, _serviceLoggerMock.Object);

        var manager1 = Guid.NewGuid();
        var manager2 = Guid.NewGuid();

        // Manager 1 có 2 bản nháp và 1 phiếu đã gửi duyệt
        context.JobRequisitions.AddRange(
            new JobRequisition
            {
                Id = Guid.NewGuid(),
                Code = "REQ-M1-D1",
                HiringManagerId = manager1,
                Status = RequisitionStatus.DRAFT,
                Quantity = 1,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            },
            new JobRequisition
            {
                Id = Guid.NewGuid(),
                Code = "REQ-M1-D2",
                HiringManagerId = manager1,
                Status = RequisitionStatus.DRAFT,
                Quantity = 2,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            },
            new JobRequisition
            {
                Id = Guid.NewGuid(),
                Code = "REQ-M1-SUBMITTED",
                HiringManagerId = manager1,
                Status = RequisitionStatus.PENDING_APPROVAL,
                Quantity = 1
            },
            // Manager 2 có 1 bản nháp
            new JobRequisition
            {
                Id = Guid.NewGuid(),
                Code = "REQ-M2-D1",
                HiringManagerId = manager2,
                Status = RequisitionStatus.DRAFT,
                Quantity = 1
            }
        );
        await context.SaveChangesAsync();

        var drafts = await service.GetDraftsByManagerAsync(manager1);

        drafts.Should().HaveCount(2);
        drafts.Select(d => d.Code).Should().Contain(new[] { "REQ-M1-D1", "REQ-M1-D2" });
        drafts.Select(d => d.Code).Should().NotContain("REQ-M1-SUBMITTED");
        drafts.Select(d => d.Code).Should().NotContain("REQ-M2-D1");
    }

    [Fact]
    public async Task Service_DeleteDraftAsync_SoftDeletesDraft_OnlyIfInDraftStatus()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new RequisitionService(context, _serviceLoggerMock.Object);
        var managerId = Guid.NewGuid();

        var draft = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-DRAFT-DEL",
            HiringManagerId = managerId,
            Status = RequisitionStatus.DRAFT,
            Quantity = 1
        };

        var approved = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-APPROVED",
            HiringManagerId = managerId,
            Status = RequisitionStatus.APPROVED,
            Quantity = 1
        };

        context.JobRequisitions.AddRange(draft, approved);
        await context.SaveChangesAsync();

        // 1. Xóa bản nháp -> Thành công
        var (s1, msg1) = await service.DeleteDraftAsync(draft.Id, managerId);
        s1.Should().BeTrue();
        msg1.Should().Contain("thành công");

        var deletedDraft = await context.JobRequisitions.FindAsync(draft.Id);
        deletedDraft!.IsDeleted.Should().BeTrue();

        // 2. Cố gắng xóa phiếu đã duyệt -> Thất bại
        var (s2, msg2) = await service.DeleteDraftAsync(approved.Id, managerId);
        s2.Should().BeFalse();
        msg2.Should().Contain("trạng thái Bản nháp");
    }

    [Fact]
    public async Task Service_DeleteDraftAsync_UnauthorizedUser_Fails()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new RequisitionService(context, _serviceLoggerMock.Object);
        var ownerManagerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var draft = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-SECURE-DRAFT",
            HiringManagerId = ownerManagerId,
            Status = RequisitionStatus.DRAFT,
            Quantity = 1
        };
        context.JobRequisitions.Add(draft);
        await context.SaveChangesAsync();

        var (success, message) = await service.DeleteDraftAsync(draft.Id, otherUserId);

        success.Should().BeFalse();
        message.Should().Contain("quyền");
    }

    [Fact]
    public async Task Controller_AutoSaveDraft_ReturnsOkWithIdAndCode()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new RequisitionService(context, _serviceLoggerMock.Object);
        var controller = new RequisitionsController(service, context, _controllerLoggerMock.Object);

        var managerId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, managerId.ToString()),
            new Claim(ClaimTypes.Name, "AutoSave Test User"),
            new Claim(ClaimTypes.Role, "HiringManager")
        ], "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var model = new RequisitionCreateViewModel
        {
            Quantity = 1,
            ReasonDetail = "AutoSave payload test",
            JobDescription = "<p>Nội dung auto-save</p>"
        };

        var result = await controller.AutoSaveDraft(model);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var value = okResult.Value;
        value.Should().NotBeNull();

        // Kiểm tra phản hồi có id và code
        var jsonPropSuccess = value?.GetType().GetProperty("success")?.GetValue(value);
        var jsonPropCode = value?.GetType().GetProperty("code")?.GetValue(value);
        var jsonPropId = value?.GetType().GetProperty("requisitionId")?.GetValue(value);

        jsonPropSuccess.Should().Be(true);
        jsonPropCode.Should().NotBeNull();
        jsonPropId.Should().NotBeNull();
    }
}
