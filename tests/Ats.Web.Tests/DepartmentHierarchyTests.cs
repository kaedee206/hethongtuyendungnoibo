using Ats.Web.Controllers;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Ats.Web.Tests;

public class DepartmentHierarchyTests
{
    [Fact]
    public async Task Delete_WithActiveRequisitions_Returns409Conflict()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Khối Kỹ thuật Phần mềm",
            Code = "SWE",
            Path = $"/{Guid.NewGuid()}/",
            Level = 1,
            IsActive = true
        };
        db.Departments.Add(dept);

        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-001",
            DepartmentId = dept.Id,
            Department = dept,
            Status = Ats.Web.Models.Enums.RequisitionStatus.APPROVED
        };
        db.JobRequisitions.Add(req);
        await db.SaveChangesAsync();

        var controller = new DepartmentsController(db);

        // Act
        var result = await controller.Delete(dept.Id);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, statusResult.StatusCode);
    }

    [Fact]
    public async Task Deactivate_CascadesToChildren()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var parent = new Department
        {
            Id = parentId,
            Name = "Phòng Vận hành",
            Code = "OPS",
            Path = $"/{parentId}/",
            Level = 1,
            IsActive = true
        };
        var child = new Department
        {
            Id = childId,
            ParentId = parentId,
            Name = "Đội Bảo trì",
            Code = "MAINT",
            Path = $"/{parentId}/{childId}/",
            Level = 2,
            IsActive = true
        };
        db.Departments.AddRange(parent, child);
        await db.SaveChangesAsync();

        var controller = new DepartmentsController(db);

        // Act: Deactivate parent
        var result = await controller.Deactivate(parent.Id);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        var updatedParent = await db.Departments.FindAsync(parentId);
        var updatedChild = await db.Departments.FindAsync(childId);
        Assert.NotNull(updatedParent);
        Assert.NotNull(updatedChild);
        Assert.False(updatedParent.IsActive);
        Assert.False(updatedChild.IsActive);
    }

    [Fact]
    public async Task Reactivate_WhenParentInactive_ReturnsBadRequest()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var parent = new Department
        {
            Id = parentId,
            Name = "Phòng Vận hành",
            Code = "OPS",
            Path = $"/{parentId}/",
            Level = 1,
            IsActive = false
        };
        var child = new Department
        {
            Id = childId,
            ParentId = parentId,
            Name = "Đội Bảo trì",
            Code = "MAINT",
            Path = $"/{parentId}/{childId}/",
            Level = 2,
            IsActive = false
        };
        db.Departments.AddRange(parent, child);
        await db.SaveChangesAsync();

        var controller = new DepartmentsController(db);

        // Act: Try reactivating child when parent is inactive
        var result = await controller.Reactivate(childId);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }
}
