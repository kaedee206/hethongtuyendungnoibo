using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ats.Web.Tests;

public class ProfileServiceTests
{
    private readonly Mock<ILogger<ProfileService>> _mockLogger = new();
    private readonly string _testWebRoot = AppDomain.CurrentDomain.BaseDirectory;

    [Fact]
    public async Task GetProfileAsync_ExistingUser_ReturnsCorrectViewModel()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Văn An",
            Email = "an.nguyen@noveratech.digital",
            PhoneNumber = "0912345678",
            Department = "Khối Công nghệ & Kỹ thuật",
            Role = "Admin",
            Status = "ACTIVE"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new ProfileService(db, _mockLogger.Object);

        // Act
        var result = await service.GetProfileAsync(user.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Nguyễn Văn An", result.FullName);
        Assert.Equal("an.nguyen@noveratech.digital", result.Email);
        Assert.Equal("0912345678", result.PhoneNumber);
        Assert.Equal("Khối Công nghệ & Kỹ thuật", result.DepartmentName);
    }

    [Theory]
    [InlineData("0912345678", true)]
    [InlineData("+84987654321", true)]
    [InlineData("84333444555", true)]
    [InlineData("0389998888", true)]
    [InlineData("123456789", false)]
    [InlineData("012345678999", false)]
    [InlineData("abcdefghij", false)]
    [InlineData("02439998888", false)] // Landline prefix 024 is not valid mobile prefix 3,5,7,8,9
    public async Task UpdateProfileAsync_PhoneValidation_BehavesCorrectly(string phone, bool expectedSuccess)
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Trần Thị Bình",
            Email = "binh.tran@noveratech.digital",
            PhoneNumber = "0900000000",
            Department = "Nhân sự",
            Role = "HRManager",
            Status = "ACTIVE"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new ProfileService(db, _mockLogger.Object);
        var dto = new UpdateProfileRequestDto
        {
            FullName = "Trần Thị Bình Cập Nhật",
            PhoneNumber = phone,
            JobTitle = "HR Lead"
        };

        // Act
        var (isSuccess, message) = await service.UpdateProfileAsync(user.Id, dto);

        // Assert
        Assert.Equal(expectedSuccess, isSuccess);
        if (!expectedSuccess)
        {
            Assert.Contains("Số điện thoại không hợp lệ", message);
        }
    }

    [Fact]
    public async Task UpdateProfileAsync_ReadOnlyFields_CannotBeTampered()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Lê Hoàng Long",
            Email = "long.le@noveratech.digital",
            Department = "Khối Công nghệ & Kỹ thuật",
            Role = "Recruiter",
            Status = "ACTIVE"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new ProfileService(db, _mockLogger.Object);
        var dto = new UpdateProfileRequestDto
        {
            FullName = "Lê Hoàng Long Mới",
            PhoneNumber = "0988776655",
            JobTitle = "Senior Recruiter",
            Email = "hacked@hacker.com", // should be ignored
            Department = "Ban Giám Đốc" // should be ignored
        };

        // Act
        var (isSuccess, _) = await service.UpdateProfileAsync(user.Id, dto);

        // Assert
        Assert.True(isSuccess);
        var updated = await db.Users.FindAsync(user.Id);
        Assert.NotNull(updated);
        Assert.Equal("long.le@noveratech.digital", updated.Email);
        Assert.Equal("Khối Công nghệ & Kỹ thuật", updated.Department);
        Assert.Equal("Recruiter", updated.Role);
        Assert.Equal("Lê Hoàng Long Mới", updated.FullName);
    }

    [Fact]
    public async Task UploadAvatarAsync_FileTooLarge_ReturnsError()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "User Avatar",
            Email = "avatar@noveratech.digital",
            Status = "ACTIVE"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new ProfileService(db, _mockLogger.Object);

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.Length).Returns(3 * 1024 * 1024); // 3MB > 2MB limit
        mockFile.Setup(f => f.FileName).Returns("avatar.jpg");

        // Act
        var (isSuccess, message, _) = await service.UploadAvatarAsync(user.Id, mockFile.Object, _testWebRoot);

        // Assert
        Assert.False(isSuccess);
        Assert.Contains("2MB", message);
    }

    [Fact]
    public async Task UploadAvatarAsync_InvalidExtension_ReturnsError()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "User Avatar",
            Email = "avatar2@noveratech.digital",
            Status = "ACTIVE"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new ProfileService(db, _mockLogger.Object);

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.Length).Returns(500 * 1024);
        mockFile.Setup(f => f.FileName).Returns("avatar.exe");

        // Act
        var (isSuccess, message, _) = await service.UploadAvatarAsync(user.Id, mockFile.Object, _testWebRoot);

        // Assert
        Assert.False(isSuccess);
        Assert.Contains("Định dạng file không hỗ trợ", message);
    }
}
