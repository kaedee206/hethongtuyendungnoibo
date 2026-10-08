using Ats.Web.Controllers;
using Ats.Web.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ats.Web.Tests;

public class RecruitmentCatalogTests
{
    private readonly Mock<ILogger<RecruitmentCatalogsController>> _mockLogger = new();

    [Fact]
    public async Task DeleteItem_SystemItem_Returns409Conflict()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var systemItem = new RecruitmentCatalog
        {
            Id = Guid.NewGuid(),
            CatalogType = "SOURCE",
            Code = "LINKEDIN",
            Name = "LinkedIn Recruiter",
            IsSystem = true,
            IsActive = true
        };
        db.RecruitmentCatalogs.Add(systemItem);
        await db.SaveChangesAsync();

        var controller = new RecruitmentCatalogsController(db, _mockLogger.Object);

        // Act
        var result = await controller.DeleteItem(systemItem.Id);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, statusResult.StatusCode);
    }

    [Fact]
    public async Task DeleteItem_NonSystemItem_Succeeds()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var customItem = new RecruitmentCatalog
        {
            Id = Guid.NewGuid(),
            CatalogType = "SOURCE",
            Code = "FACEBOOK_ADS",
            Name = "Quảng cáo Facebook",
            IsSystem = false,
            IsActive = true
        };
        db.RecruitmentCatalogs.Add(customItem);
        await db.SaveChangesAsync();

        var controller = new RecruitmentCatalogsController(db, _mockLogger.Object);

        // Act
        var result = await controller.DeleteItem(customItem.Id);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        var inDb = await db.RecruitmentCatalogs.FindAsync(customItem.Id);
        Assert.Null(inDb);
    }

    [Fact]
    public async Task CreateItem_DuplicateCodeInSameType_ReturnsConflict()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var existing = new RecruitmentCatalog
        {
            Id = Guid.NewGuid(),
            CatalogType = "LOCATION",
            Code = "HN_HQ",
            Name = "Hà Nội",
            IsSystem = true
        };
        db.RecruitmentCatalogs.Add(existing);
        await db.SaveChangesAsync();

        var controller = new RecruitmentCatalogsController(db, _mockLogger.Object);
        var dto = new CatalogItemDto
        {
            CatalogType = "LOCATION",
            Code = "HN_HQ",
            Name = "Hà Nội Trụ Sở Mới",
            DisplayOrder = 1,
            IsActive = true
        };

        // Act
        var result = await controller.CreateItem(dto);

        // Assert
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(conflictResult.Value);
    }
}
