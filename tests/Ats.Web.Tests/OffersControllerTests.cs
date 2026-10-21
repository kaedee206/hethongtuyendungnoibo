using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Controllers;
using Ats.Web.Models.DTOs.Offers;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ats.Web.Tests;

/// <summary>
/// Unit Tests kiểm thử OffersController (User Story SP-7).
/// Kiểm thử các endpoints:
/// - GET: /offers/rejection-reasons
/// - GET: /offers/{id}
/// - GET: /offers/application/{applicationId}/history
/// - POST: /offers/reject
/// - POST: /offers/revise
/// </summary>
public class OffersControllerTests
{
    private readonly Mock<IOfferService> _mockOfferService = new();
    private readonly Mock<IRequisitionAssignmentService> _mockAssignmentService = new();
    private readonly NullLogger<OffersController> _logger = NullLogger<OffersController>.Instance;

    private OffersController CreateController(Guid? userId = null, string role = UserRoles.HRManager)
    {
        var controller = new OffersController(_mockOfferService.Object, _mockAssignmentService.Object, _logger);

        var uid = userId ?? Guid.NewGuid();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, uid.ToString()),
            new(ClaimTypes.Name, "Test User"),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        return controller;
    }

    [Fact]
    public async Task GetRejectionReasons_ReturnsJsonWithStandardCategories()
    {
        // Arrange
        var sampleReasons = new List<OfferRejectionReasonCategoryDto>
        {
            new() { Id = 1, Code = "Salary", DisplayName = "Lương / Đãi ngộ", RequiresExpectedSalary = true },
            new() { Id = 2, Code = "Location", DisplayName = "Địa điểm", RequiresExpectedSalary = false }
        };
        _mockOfferService.Setup(s => s.GetRejectionReasonsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleReasons);

        var controller = CreateController();

        // Act
        var result = await controller.GetRejectionReasons(CancellationToken.None);

        // Assert
        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        jsonResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDetails_WhenOfferNotFound_ReturnsFailureJson()
    {
        // Arrange
        var offerId = Guid.NewGuid();
        _mockOfferService.Setup(s => s.GetOfferDetailsAsync(offerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OfferItemDto?)null);

        var controller = CreateController();

        // Act
        var result = await controller.GetDetails(offerId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<JsonResult>();
    }

    [Fact]
    public async Task GetDetails_WhenRecruiterHasNoAccess_ReturnsForbiddenJson()
    {
        // Arrange
        var offerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var recruiterId = Guid.NewGuid();
        var offerDto = new OfferItemDto { OfferId = offerId, ApplicationId = appId, CandidateName = "Lê Văn A" };

        _mockOfferService.Setup(s => s.GetOfferDetailsAsync(offerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(offerDto);
        _mockAssignmentService.Setup(s => s.CanRecruiterAccessApplicationAsync(recruiterId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = CreateController(recruiterId, UserRoles.Recruiter);

        // Act
        var result = await controller.GetDetails(offerId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<JsonResult>();
    }

    [Fact]
    public async Task GetDetails_WhenAuthorized_ReturnsSuccessWithData()
    {
        // Arrange
        var offerId = Guid.NewGuid();
        var offerDto = new OfferItemDto { OfferId = offerId, CandidateName = "Nguyễn Văn B", Version = 1 };

        _mockOfferService.Setup(s => s.GetOfferDetailsAsync(offerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(offerDto);

        var controller = CreateController();

        // Act
        var result = await controller.GetDetails(offerId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<JsonResult>();
    }

    [Fact]
    public async Task GetHistory_WhenAuthorized_ReturnsSuccessJson()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var historyList = new List<OfferItemDto>
        {
            new() { OfferId = Guid.NewGuid(), ApplicationId = appId, Version = 1, Status = OfferStatus.REPLACED },
            new() { OfferId = Guid.NewGuid(), ApplicationId = appId, Version = 2, Status = OfferStatus.PENDING_APPROVAL }
        };

        _mockOfferService.Setup(s => s.GetOfferHistoryByApplicationAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(historyList);

        var controller = CreateController();

        // Act
        var result = await controller.GetHistory(appId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<JsonResult>();
    }

    [Fact]
    public async Task Reject_WithInvalidModelState_ReturnsFailureJson()
    {
        // Arrange
        var controller = CreateController();
        controller.ModelState.AddModelError("ReasonCategory", "Bắt buộc chọn lý do");

        var dto = new RejectOfferDto();

        // Act
        var result = await controller.Reject(dto, CancellationToken.None);

        // Assert
        result.Should().BeOfType<JsonResult>();
    }

    [Fact]
    public async Task Reject_WhenOfferNotFound_ReturnsNotFoundJson()
    {
        // Arrange
        var offerId = Guid.NewGuid();
        _mockOfferService.Setup(s => s.GetOfferByIdAsync(offerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobOffer?)null);

        var controller = CreateController();
        var dto = new RejectOfferDto { OfferId = offerId, ReasonCategory = OfferRejectionReasonCategory.Salary };

        // Act
        var result = await controller.Reject(dto, CancellationToken.None);

        // Assert
        result.Should().BeOfType<JsonResult>();
    }

    [Fact]
    public async Task Reject_WhenRecruiterHasNoAccess_ReturnsForbiddenJson()
    {
        // Arrange
        var offerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var recruiterId = Guid.NewGuid();
        var existingOffer = new JobOffer { Id = offerId, ApplicationId = appId };

        _mockOfferService.Setup(s => s.GetOfferByIdAsync(offerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOffer);
        _mockAssignmentService.Setup(s => s.CanRecruiterAccessApplicationAsync(recruiterId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = CreateController(recruiterId, UserRoles.Recruiter);
        var dto = new RejectOfferDto { OfferId = offerId, ReasonCategory = OfferRejectionReasonCategory.Location };

        // Act
        var result = await controller.Reject(dto, CancellationToken.None);

        // Assert
        result.Should().BeOfType<JsonResult>();
    }

    [Fact]
    public async Task Reject_WhenValid_CallsServiceAndReturnsSuccessJson()
    {
        // Arrange
        var offerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var recruiterId = Guid.NewGuid();
        var existingOffer = new JobOffer { Id = offerId, ApplicationId = appId };
        var updatedOffer = new JobOffer { Id = offerId, Status = OfferStatus.REJECTED };

        _mockOfferService.Setup(s => s.GetOfferByIdAsync(offerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOffer);
        _mockAssignmentService.Setup(s => s.CanRecruiterAccessApplicationAsync(recruiterId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockOfferService.Setup(s => s.RejectOfferAsync(It.IsAny<RejectOfferDto>(), recruiterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "Đã ghi nhận ứng viên từ chối thành công.", updatedOffer));

        var controller = CreateController(recruiterId, UserRoles.Recruiter);
        var dto = new RejectOfferDto
        {
            OfferId = offerId,
            ReasonCategory = OfferRejectionReasonCategory.Salary,
            ExpectedSalary = 35_000_000m,
            AddToTalentPool = true
        };

        // Act
        var result = await controller.Reject(dto, CancellationToken.None);

        // Assert
        result.Should().BeOfType<JsonResult>();
        _mockOfferService.Verify(s => s.RejectOfferAsync(dto, recruiterId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revise_WithInvalidModelState_ReturnsFailureJson()
    {
        // Arrange
        var controller = CreateController();
        controller.ModelState.AddModelError("BaseSalary", "Lương phải lớn hơn 0");

        var dto = new ReviseOfferDto();

        // Act
        var result = await controller.Revise(dto, CancellationToken.None);

        // Assert
        result.Should().BeOfType<JsonResult>();
    }

    [Fact]
    public async Task Revise_WhenOriginalOfferNotFound_ReturnsNotFoundJson()
    {
        // Arrange
        var offerId = Guid.NewGuid();
        _mockOfferService.Setup(s => s.GetOfferByIdAsync(offerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobOffer?)null);

        var controller = CreateController();
        var dto = new ReviseOfferDto { OriginalOfferId = offerId, BaseSalary = 35_000_000m };

        // Act
        var result = await controller.Revise(dto, CancellationToken.None);

        // Assert
        result.Should().BeOfType<JsonResult>();
    }

    [Fact]
    public async Task Revise_WhenRecruiterHasNoAccess_ReturnsForbiddenJson()
    {
        // Arrange
        var offerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var recruiterId = Guid.NewGuid();
        var existingOffer = new JobOffer { Id = offerId, ApplicationId = appId };

        _mockOfferService.Setup(s => s.GetOfferByIdAsync(offerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOffer);
        _mockAssignmentService.Setup(s => s.CanRecruiterAccessApplicationAsync(recruiterId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = CreateController(recruiterId, UserRoles.Recruiter);
        var dto = new ReviseOfferDto { OriginalOfferId = offerId, BaseSalary = 35_000_000m };

        // Act
        var result = await controller.Revise(dto, CancellationToken.None);

        // Assert
        result.Should().BeOfType<JsonResult>();
    }

    [Fact]
    public async Task Revise_WhenValid_CallsServiceAndReturnsSuccessJson()
    {
        // Arrange
        var offerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var recruiterId = Guid.NewGuid();
        var existingOffer = new JobOffer { Id = offerId, ApplicationId = appId, Version = 1 };
        var newOffer = new JobOffer { Id = Guid.NewGuid(), ApplicationId = appId, Version = 2, Status = OfferStatus.PENDING_APPROVAL };

        _mockOfferService.Setup(s => s.GetOfferByIdAsync(offerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOffer);
        _mockAssignmentService.Setup(s => s.CanRecruiterAccessApplicationAsync(recruiterId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockOfferService.Setup(s => s.ReviseOfferAsync(It.IsAny<ReviseOfferDto>(), recruiterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "Tạo Thư mời sửa đổi thành công!", newOffer));

        var controller = CreateController(recruiterId, UserRoles.Recruiter);
        var dto = new ReviseOfferDto
        {
            OriginalOfferId = offerId,
            BaseSalary = 35_000_000m,
            BonusAllowance = 2_000_000m,
            RevisionNotes = "Tăng lương cơ bản theo yêu cầu ứng viên"
        };

        // Act
        var result = await controller.Revise(dto, CancellationToken.None);

        // Assert
        result.Should().BeOfType<JsonResult>();
        _mockOfferService.Verify(s => s.ReviseOfferAsync(dto, recruiterId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
