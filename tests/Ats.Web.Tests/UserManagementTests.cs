using Ats.Web.Constants;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.ViewModels.Users;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Ats.Web.Tests;

public class UserManagementTests
{
    private readonly Mock<IEmailService> _emailServiceMock = new();

    [Fact]
    public async Task CreateUserAsync_WithValidNoveraTechEmail_SucceedsAndAssignsMultipleRoles()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var adminId = Guid.NewGuid();
        var admin = new User
        {
            Id = adminId,
            Email = "nam.dang@noveratech.digital",
            FullName = "Lường Minh Hiếu",
            Role = UserRoles.Admin,
            Status = "ACTIVE",
            PasswordHash = "dummy",
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Users.Add(admin);
        await context.SaveChangesAsync();

        var service = new UserService(context, _emailServiceMock.Object);

        var roles = await context.Roles.ToListAsync();
        var hmRole = roles.First(r => r.Name == "HiringManager");
        var interviewerRole = roles.First(r => r.Name == "Interviewer");

        var model = new UserCreateViewModel
        {
            FullName = "Nguyễn Văn Test",
            Email = "van.nguyen@noveratech.digital",
            Department = "Engineering",
            AvailableRoles = new List<RoleCheckboxItem>
            {
                new() { RoleId = hmRole.Id, RoleName = hmRole.Name, DisplayName = "Quản lý tuyển dụng", IsSelected = true },
                new() { RoleId = interviewerRole.Id, RoleName = interviewerRole.Name, DisplayName = "Người phỏng vấn", IsSelected = true }
            }
        };

        // Act
        var result = await service.CreateUserAsync(model, adminId, "http://localhost");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.UserId.Should().NotBeNull();

        var createdUser = await context.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Email == "van.nguyen@noveratech.digital");

        createdUser.Should().NotBeNull();
        createdUser!.FullName.Should().Be("Nguyễn Văn Test");
        createdUser.Department.Should().Be("Engineering");
        createdUser.Status.Should().Be("ACTIVE");
        createdUser.UserRoles.Should().HaveCount(2);

        // Verify email was sent
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            It.Is<SendEmailRequestDto>(r => r.ToEmail == "van.nguyen@noveratech.digital"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateUserAsync_WithInvalidDomain_FailsValidation()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new UserService(context, _emailServiceMock.Object);

        var model = new UserCreateViewModel
        {
            FullName = "Nguyễn Văn Hacker",
            Email = "hacker@gmail.com", // Sai tên miền công ty
            Department = "Security",
            AvailableRoles = new List<RoleCheckboxItem>()
        };

        // Act
        var result = await service.CreateUserAsync(model, Guid.NewGuid(), "http://localhost");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("@noveratech.digital");
    }

    [Fact]
    public async Task UpdateUserAsync_SelfAdminRevocation_IsPrevented()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var adminId = Guid.NewGuid();
        var adminRole = await context.Roles.FirstAsync(r => r.Name == "Admin");

        var admin = new User
        {
            Id = adminId,
            Email = "admin@noveratech.digital",
            FullName = "Super Admin",
            Role = UserRoles.Admin,
            Status = "ACTIVE",
            PasswordHash = "dummy",
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Users.Add(admin);
        context.UserRoles.Add(new UserRole { UserId = adminId, RoleId = adminRole.Id });
        await context.SaveChangesAsync();

        var service = new UserService(context, _emailServiceMock.Object);

        var editModel = new UserEditViewModel
        {
            Id = adminId,
            FullName = "Super Admin Renamed",
            Status = "ACTIVE",
            IsSelf = true,
            AvailableRoles = new List<RoleCheckboxItem>
            {
                // Bỏ chọn vai trò Admin
                new() { RoleId = adminRole.Id, RoleName = "Admin", DisplayName = "Quản trị viên", IsSelected = false }
            }
        };

        // Act
        var result = await service.UpdateUserAsync(editModel, adminId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("tự thu hồi quyền Quản trị viên");
    }

    [Fact]
    public async Task LockUserAsync_RevokesAllSessions_AndRecordsReason()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        var targetUser = new User
        {
            Id = targetUserId,
            Email = "user@noveratech.digital",
            FullName = "Test Employee",
            Role = "Employee",
            Status = "ACTIVE",
            PasswordHash = "dummy",
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Users.Add(targetUser);

        // Thêm 2 phiên làm việc đang active của user
        context.UserSessions.AddRange(
            new UserSession { Id = Guid.NewGuid(), UserId = targetUserId, SessionId = "sess-1", CreatedAt = DateTimeOffset.UtcNow, LastActiveAt = DateTimeOffset.UtcNow, IsRevoked = false },
            new UserSession { Id = Guid.NewGuid(), UserId = targetUserId, SessionId = "sess-2", CreatedAt = DateTimeOffset.UtcNow, LastActiveAt = DateTimeOffset.UtcNow, IsRevoked = false }
        );
        await context.SaveChangesAsync();

        var service = new UserService(context, _emailServiceMock.Object);

        // Act
        var result = await service.LockUserAsync(targetUserId, "Vi phạm bảo mật nghiêm trọng", adminId);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updatedUser = await context.Users.FindAsync(targetUserId);
        updatedUser!.Status.Should().Be("LOCKED");
        updatedUser.LockReason.Should().Be("Vi phạm bảo mật nghiêm trọng");
        updatedUser.LockedBy.Should().Be(adminId);
        updatedUser.LockedAt.Should().NotBeNull();

        // Mọi phiên làm việc phải bị thu hồi ngay lập tức (S1-10)
        var sessions = await context.UserSessions.Where(s => s.UserId == targetUserId).ToListAsync();
        sessions.Should().AllSatisfy(s => s.IsRevoked.Should().BeTrue());
    }

    [Fact]
    public async Task LockUserAsync_ForRecruiterOrHiringManager_ReturnsHandoverWarning()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var adminId = Guid.NewGuid();
        var recruiterId = Guid.NewGuid();

        var recruiter = new User
        {
            Id = recruiterId,
            Email = "recruiter@noveratech.digital",
            FullName = "Lê Thùy Dung",
            Role = UserRoles.Recruiter,
            Status = "ACTIVE",
            PasswordHash = "dummy",
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Users.Add(recruiter);
        await context.SaveChangesAsync();

        var service = new UserService(context, _emailServiceMock.Object);

        // Act
        var result = await service.LockUserAsync(recruiterId, "Nghỉ việc theo nguyện vọng", adminId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.HandoverWarning.Should().NotBeNullOrEmpty();
        result.HandoverWarning.Should().Contain("bàn giao");
    }
}
