using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Ats.Web.Tests;

public class AuthTests
{
    private readonly Mock<IEmailService> _emailServiceMock = new();

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsGenericErrorMessage_AndRecordsFailedAttempt()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var rawPassword = "CorrectPassword123@";
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(rawPassword);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "nam.dang@noveratech.digital",
            FullName = "Đặng Hoàng Nam",
            Role = UserRoles.Admin,
            Status = "ACTIVE",
            PasswordHash = passwordHash,
            FailedLoginAttempts = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var authService = new AuthService(context, _emailServiceMock.Object);
        var loginDto = new LoginRequestDto("nam.dang@noveratech.digital", "WrongPassword123!");

        // Act
        var result = await authService.AuthenticateAsync(loginDto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        // S1-01: Generic error message, does not disclose specific reason
        result.Message.Should().Be("Email hoặc mật khẩu không chính xác.");

        var updatedUser = await context.Users.FindAsync(user.Id);
        updatedUser!.FailedLoginAttempts.Should().Be(1);
    }

    [Fact]
    public async Task LoginAsync_After5FailedAttempts_LocksAccountFor15Minutes()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "locked.user@noveratech.digital",
            FullName = "Locked User",
            Role = UserRoles.Recruiter,
            Status = "ACTIVE",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("RealPassword123!"),
            FailedLoginAttempts = 4, // Lần thứ 5 sẽ bị khóa
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var authService = new AuthService(context, _emailServiceMock.Object);
        var loginDto = new LoginRequestDto("locked.user@noveratech.digital", "WrongPass!");

        // Act (Lần thứ 5 thất bại)
        var result = await authService.AuthenticateAsync(loginDto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("15 phút");

        var updatedUser = await context.Users.FindAsync(user.Id);
        updatedUser!.FailedLoginAttempts.Should().Be(5);
        updatedUser.LockedUntil.Should().NotBeNull();
        updatedUser.LockedUntil!.Value.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(14));
    }

    [Fact]
    public async Task ForgotPasswordAsync_ForExistingAndNonExistingEmail_ReturnsSuccessUniformly()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "phuong.nguyen@noveratech.digital",
            FullName = "Nguyễn Mai Phương",
            Role = UserRoles.HRManager,
            Status = "ACTIVE",
            PasswordHash = "dummy",
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Users.Add(existingUser);
        await context.SaveChangesAsync();

        var authService = new AuthService(context, _emailServiceMock.Object);

        // Act - Existing email
        var result1 = await authService.ForgotPasswordAsync("phuong.nguyen@noveratech.digital", "http://localhost");
        // Act - Non-existing email
        var result2 = await authService.ForgotPasswordAsync("non.existing@noveratech.digital", "http://localhost");

        // Assert - Both should return success (S1-03: prevent email enumeration)
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();

        // Existing user received email with token
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            It.Is<SendEmailRequestDto>(r => r.ToEmail == "phuong.nguyen@noveratech.digital"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Non-existing user did not trigger email
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            It.Is<SendEmailRequestDto>(r => r.ToEmail == "non.existing@noveratech.digital"),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithValidToken_UpdatesPasswordAndInvalidatesToken()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var resetToken = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Id = userId,
            Email = "reset.user@noveratech.digital",
            FullName = "Reset User",
            Role = UserRoles.Interviewer,
            Status = "ACTIVE",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassword123!"),
            PasswordResetToken = resetToken,
            PasswordResetTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20), // còn hạn
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var authService = new AuthService(context, _emailServiceMock.Object);
        var resetDto = new ResetPasswordRequestDto(
            "reset.user@noveratech.digital",
            resetToken,
            "BrandNewPassword123@"
        );

        // Act
        var result = await authService.ResetPasswordAsync(resetDto);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updatedUser = await context.Users.FindAsync(userId);
        // Token must be invalidated immediately (Single-use token - S1-03)
        updatedUser!.PasswordResetToken.Should().BeNull();
        updatedUser.PasswordResetTokenExpiresAt.Should().BeNull();
        // Password hash must verify with the new password
        BCrypt.Net.BCrypt.Verify("BrandNewPassword123@", updatedUser.PasswordHash).Should().BeTrue();
    }
}
